#pragma once
#include "control_definition_output.h"
#include "control_runtime.h"

namespace fsc_controls {
struct output_check_context {
    bool local = false, ready = false, applying = false;
    uint32_t blocked = 0, automatic = 0, waiting = 0;
    double ground = 0, knots = 0, parking_brake = 0, ap = 0, observed = -1;
};
enum class output_check_phase : unsigned { idle, same, movement, restore, passed, failed };

// Explicit local parked-aircraft check. It never changes ownership/masks and can
// write only a complete group already assigned to Mock. Restoration is attempted
// only while that same local mask/context is still valid.
template<class Output> class control_output_check {
public:
    output_check_phase phase = output_check_phase::idle;
    group target = group::steering;
    uint32_t passed = 0;
    bool restored = false;
    const char* issue = "";
    size_t failed_axis = axis_count;
    double failed_expected = 0, failed_observed = 0;
    bool running() const { return phase == output_check_phase::same || phase == output_check_phase::movement || phase == output_check_phase::restore; }
    void reset() { phase = output_check_phase::idle; passed = 0; restored = false; issue = ""; failed_axis = axis_count; for (auto& proof : proofs_) proof = 0; }
    void copy_proofs(uint64_t (&destination)[4]) const { std::memcpy(destination, proofs_, sizeof(proofs_)); }
    // Only disk-validated records for the current aircraft may enter here.
    // valid_passes still checks each effective output route before use.
    void restore_proofs(const uint64_t (&source)[4]) {
        if (running()) return;
        for (unsigned index = 0; index < 4; ++index) {
            proofs_[index] = source[index];
            if (source[index]) passed |= 1u << index;
            else passed &= ~(1u << index);
        }
    }
    void invalidate() { for (auto& proof : proofs_) proof = 0; passed = 0; }
    void cancel() { if (running()) { phase = output_check_phase::failed; issue = "Check cancelled by local control change."; } }
    void restore_and_cancel(double now) {
        if (!running()) return;
        failed_ = true; issue = "Check cancelled.";
        if (phase == output_check_phase::same && !sent_) { restored = true; phase = output_check_phase::failed; }
        else if (phase != output_check_phase::restore) begin_restore(now);
    }
    uint32_t valid_passes(const control_pickup& pickup, const definition_profile* profile) {
        for (unsigned index = 0; index < 4; ++index) if (proofs_[index] && proofs_[index] != fingerprint(pickup, profile, atomic_group(index))) {
            proofs_[index] = 0; passed &= ~(1u << index);
        }
        return profile && profile->engines == pickup.engines ? passed : 0;
    }
    bool start(group selected, const control_pickup& pickup, const definition_profile* profile, const output_check_context& context, Output& output, double now) {
        const auto selection = group_selection(selected);
        if (running() || !profile || profile->engines != pickup.engines || !single(selection) || !safe(context, selection, now) || !output.available) return false;
        uint32_t required = 0;
        for (size_t axis = 0; axis < axis_count; ++axis) if (pickup.required_axis(axis) && control_pickup::matches_group(axis, selected)) {
            if (!pickup.axis_known(axis) || (!pickup.relative_input(axis) && pickup.input_position(axis) < 0) || !position(pickup.targets[axis])) return false;
            required |= 1u << axis;
        }
        if (!required || pickup.target_time < 0 || now < pickup.target_time || now - pickup.target_time > .25) return false;
        target = selected; profile_ = profile; axes_ = required; started_ = step_at_ = now;
        fingerprint_ = fingerprint(pickup, profile, selected); phase = output_check_phase::same;
        restored = false; failed_ = false; issue = ""; failed_axis = axis_count; sent_ = false; matches_ = 0; request_ = 0; tried_response_axes_ = 0;
        passed &= ~selection; proofs_[selection_index(selection)] = 0;
        for (size_t axis = 0; axis < axis_count; ++axis) {
            baseline_[axis] = pickup.targets[axis]; hardware_[axis] = pickup.input_position(axis);
            desired_[axis] = baseline_[axis];
        }
        return true;
    }
    void confirm(DWORD request) { if (request && request == request_) barrier_ = true; }
    void tick(control_pickup& pickup, const definition_profile* profile, const output_check_context& context, Output& output, double now) {
        if (!running()) return;
        const auto selection = group_selection(target);
        const bool context_ok = profile == profile_ && fingerprint(pickup, profile, target) == fingerprint_ && safe(context, selection, now) &&
            output.available && pickup.target_time >= 0 && now >= pickup.target_time && now - pickup.target_time <= .25;
        if (!context_ok) { phase = output_check_phase::failed; issue = "Check stopped: aircraft, input mask or parked-state confirmation changed."; return; }
        bool interference = false;
        for (size_t axis = 0; axis < axis_count; ++axis) if (axes_ & (1u << axis)) {
            if (pickup.relative_input(axis)) continue;
            const auto input = pickup.input_position(axis);
            if (!position(input) || std::abs(input - hardware_[axis]) > .0045) {
                if (!interference) { failed_axis = axis; failed_expected = hardware_[axis]; failed_observed = input; }
                interference = true;
            }
        }
        if ((interference || now - step_at_ > 1.1) && phase != output_check_phase::restore) {
            // A declared alternate event response is selected by actual cockpit
            // readback, never by discovering a button/axis later in the flight.
            if (!interference && retry_response(pickup, profile, now)) return;
            failed_ = true; issue = interference ? "Keep the hardware still during the check." : "The requested cockpit movement was not confirmed.";
            begin_restore(now);
        }
        if (now - started_ > (tried_response_axes_ ? 8 : 4) || (phase == output_check_phase::restore && now - step_at_ > 1.1)) {
            phase = output_check_phase::failed; issue = "Restoration was not confirmed. Use this control's Force takeover if needed."; return;
        }
        if (!sent_) {
            output_action actions[axis_count]{}; size_t count = 0;
            if (!group_output(pickup, target, axes_, desired_, actions, count) || !output.write(actions, count)) {
                phase = output_check_phase::failed; issue = "Simulator output failed; this group remains with Mock."; return;
            }
            request_ = output.request_checkpoint();
            if (!request_) { phase = output_check_phase::failed; issue = "Could not confirm the output request."; return; }
            sent_ = true; barrier_ = false; observed_ = pickup.target_time; matches_ = 0; return;
        }
        if (pickup.target_time == observed_) return;
        observed_ = pickup.target_time;
        bool matched = barrier_;
        size_t mismatch = axis_count;
        for (size_t axis = 0; axis < axis_count; ++axis) if (axes_ & (1u << axis)) {
            if (!position(pickup.targets[axis]) || std::abs(pickup.targets[axis] - desired_[axis]) > .0051 ||
                (phase == output_check_phase::movement && std::abs(pickup.targets[axis] - baseline_[axis]) < .0049)) {
                matched = false; if (mismatch == axis_count) mismatch = axis;
            }
        }
        if (phase != output_check_phase::restore && mismatch != axis_count) {
            failed_axis = mismatch; failed_expected = desired_[mismatch]; failed_observed = pickup.targets[mismatch];
        }
        matches_ = matched ? matches_ + 1 : 0;
        if (matches_ < 3) return;
        if (phase == output_check_phase::same) {
            phase = output_check_phase::movement; step_at_ = now; sent_ = false;
            for (size_t axis = 0; axis < axis_count; ++axis) if (axes_ & (1u << axis)) desired_[axis] = baseline_[axis] <= .99 ? baseline_[axis] + .01 : baseline_[axis] - .01;
        } else if (phase == output_check_phase::movement) begin_restore(now);
        else {
            restored = true; phase = failed_ ? output_check_phase::failed : output_check_phase::passed;
            if (!failed_) { passed |= selection; proofs_[selection_index(selection)] = fingerprint_; }
        }
    }
private:
    const definition_profile* profile_ = nullptr;
    uint32_t axes_ = 0, tried_response_axes_ = 0; uint64_t fingerprint_ = 0, proofs_[4]{};
    double baseline_[axis_count]{}, hardware_[axis_count]{}, desired_[axis_count]{};
    double started_ = 0, step_at_ = 0, observed_ = -1;
    unsigned matches_ = 0; DWORD request_ = 0;
    bool failed_ = false, sent_ = false, barrier_ = false;
    static bool single(uint32_t value) { return value && !(value & (value - 1)) && value <= 8; }
    static bool position(double value) { return std::isfinite(value) && value >= 0 && value <= 1; }
    bool retry_response(control_pickup& pickup, const definition_profile* profile, double now) {
        for (size_t axis = 0; axis < axis_count; ++axis) {
            const auto bit = 1u << axis;
            const auto* mapping = definition_for_axis(profile, axis);
            if (!(axes_ & bit) || (tried_response_axes_ & bit) || !mapping || !mapping->axis_input_response_count ||
                !position(pickup.targets[axis]) || std::abs(pickup.targets[axis]-desired_[axis]) <= .0051) continue;
            const bool current = !!(pickup.output_response_axes & bit);
            const auto raw = definition_output_value(*mapping, desired_[axis], current);
            const auto alternative = definition_input_position(*mapping, raw, !current);
            if (!position(alternative) || std::abs(alternative-pickup.targets[axis]) > .0025) continue;
            pickup.output_response_axes ^= bit; tried_response_axes_ |= bit;
            fingerprint_ = fingerprint(pickup, profile, target);
            phase = output_check_phase::same; step_at_ = now; sent_ = false; matches_ = 0; request_ = 0;
            for (size_t item = 0; item < axis_count; ++item) desired_[item] = baseline_[item];
            return true;
        }
        return false;
    }
    static group atomic_group(unsigned index) { return index == 0 ? group::steering : index == 1 ? group::throttle : index == 2 ? group::propeller : group::mixture; }
    static bool safe(const output_check_context& c, uint32_t selection, double now) {
        return std::isfinite(now) && c.local && c.ready && !c.applying && (c.blocked & selection) == selection && !c.automatic && !c.waiting &&
            c.ground == 1 && c.parking_brake == 1 && c.ap == 0 && std::isfinite(c.knots) && std::abs(c.knots) <= .1 &&
            c.observed >= 0 && now >= c.observed && now - c.observed <= .25;
    }
    static uint64_t fingerprint(const control_pickup& p, const definition_profile* profile, group target) {
        if (!profile || profile->engines != p.engines) return 0;
        uint64_t hash = 14695981039346656037ull;
        auto byte = [&](unsigned char value) { hash ^= value; hash *= 1099511628211ull; };
        auto text = [&](const char* value) { do { byte(static_cast<unsigned char>(*value)); } while (*value++); };
        auto number = [&](uint64_t value) { for (unsigned i = 0; i < 8; ++i) { byte(static_cast<unsigned char>(value)); value >>= 8; } };
        auto scalar = [&](double value) { uint64_t bits = 0; std::memcpy(&bits, &value, sizeof(bits)); number(bits); };
        // Bump this route revision when native output/target interpretation
        // changes. UI, helper and unrelated definition edits do not revoke it.
        text("shared-output-routes-v1"); text(profile->aircraft);
        byte(static_cast<unsigned char>(group_selection(target)));
        byte(static_cast<unsigned char>(p.engines));
        for (size_t axis = 0; axis < axis_count; ++axis) if (p.required_axis(axis) && control_pickup::matches_group(axis, target)) {
            byte(static_cast<unsigned char>(axis));
            text(axes[axis].variable); text(axes[axis].units); byte(axes[axis].signed_target ? 1 : 0);
            const auto* mapping = definition_for_axis(profile, axis);
            byte(mapping ? 1 : 0);
            if (mapping) {
                text(mapping->read); text(mapping->read_units); scalar(mapping->minimum); scalar(mapping->maximum);
                byte(mapping->cockpit_reference ? 1 : 0); number(mapping->output_kind);
                text(mapping->write); text(mapping->write_units);
                if(mapping->axis_input_response_count && (p.output_response_axes & (1u << axis))) {
                    text("absolute-axis-response-v1");number(mapping->axis_input_response_count);
                    for(size_t point=0;point<mapping->axis_input_response_count;++point)scalar(mapping->axis_input_response[point]);
                }
                // Preserve existing proof hashes for unchanged direct routes.
                if (mapping->output_minimum != mapping->minimum || mapping->output_maximum != mapping->maximum) {
                    text("affine-output-v1"); scalar(mapping->output_minimum); scalar(mapping->output_maximum);
                }
                if (mapping->output_count) {
                    text("piecewise-output-v1"); number(mapping->output_count);
                    for (size_t point = 0; point < mapping->output_count; ++point) {
                        scalar(mapping->output_positions[point]); scalar(mapping->output_values[point]);
                    }
                }
            } else {
                // The fallback writer inverts this hardware calibration. A
                // definition's absolute setter uses normalized positions and
                // does not depend on the sender's physical axis scale.
                if (!p.inputs[axis].calibrated || !p.inputs[axis].event[0]) return 0;
                number(p.inputs[axis].candidates); text(p.inputs[axis].event);
            }
        }
        return hash;
    }
    void begin_restore(double now) { phase = output_check_phase::restore; step_at_ = now; sent_ = false; matches_ = 0; for (size_t axis = 0; axis < axis_count; ++axis) desired_[axis] = baseline_[axis]; }
};
}
