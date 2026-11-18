#pragma once
#include "control_output_check.h"

namespace fsc_controls {
enum class wizard_phase : unsigned { idle, prepare, input, mask, check, takeover, complete, failed, cancelled };

// Native orchestration survives an EFB view reload. No forced takeover, altered
// calibration, diagnostic thresholds or independent persistence flag.
class control_setup_wizard {
public:
    wizard_phase phase = wizard_phase::idle;
    group target = group::steering;
    uint32_t needed_axes = 0;
    size_t failed_axis = axis_count;
    const char* message = "";
    bool active() const { return phase >= wizard_phase::prepare && phase <= wizard_phase::takeover; }
    void reset() { *this = control_setup_wizard{}; }
    void interrupt(const char* reason) { if (active()) { phase = wizard_phase::failed; message = reason; } }
    static const char* prerequisite(const output_check_context& c, bool engines_off, bool definitions, bool writable, double now) {
        if (!c.local) return "Leave shared controls and recover them locally before setup.";
        if (!c.ready || c.applying || c.waiting) return "Wait for the current control change.";
        if (!definitions || !writable) return "Aircraft output definitions or simulator output are unavailable.";
        if (c.observed < 0 || now < c.observed || now - c.observed > .25) return "Waiting for fresh aircraft observations.";
        if (c.ground != 1 || !std::isfinite(c.knots) || std::abs(c.knots) > .1) return "Park the aircraft before setup.";
        if (c.parking_brake != 1) return "Set the parking brake.";
        if (c.ap != 0) return "Turn the autopilot off.";
        if (!engines_off) return "Stop the engines before guided hardware setup.";
        return "";
    }
    template<class Runtime, class Check, class Output>
    bool start(Runtime& gate, control_pickup& pickup, Check& check, Output& output,
        const definition_profile* profile, const output_check_context& c, bool engines_off, double now) {
        if (active()) return true;
        const auto* reason = prerequisite(c, engines_off, profile != nullptr, output.available, now);
        if (reason[0] || check.running()) { message = reason[0] ? reason : "Wait for the current output check."; return false; }
        reset(); remaining_ = select_all & ~check.valid_passes(pickup, profile);
        if (!remaining_) { phase = wizard_phase::complete; return true; }
        original_auto_ = gate.automatic != 0;
        if (!gate.set_auto(select_quadrant, false, now)) { message = "Could not pause Auto."; return false; }
        next(now); return true;
    }
    template<class Check> void cancel(Check& check, double now) {
        if (!active()) return;
        cancelling_ = true; message = "Cancelling: returning this group through alignment.";
        if (phase == wizard_phase::check) check.restore_and_cancel(now);
    }
    template<class Runtime, class Check, class Output, class Save>
    void tick(Runtime& gate, control_pickup& pickup, Check& check, Output& output,
        const definition_profile* profile, const output_check_context& c, bool engines_off, double now, Save save) {
        if (!active()) return;
        // Applying/waiting are expected during this wizard's own handovers.
        auto guard = c; guard.applying = false; guard.waiting = 0;
        const auto* reason = prerequisite(guard, engines_off, profile != nullptr, output.available, now);
        if (reason[0]) { interrupt(reason); return; }
        if (gate.applying) return;
        const auto selection = group_selection(target);
        if (cancelling_ && phase != wizard_phase::check && phase != wizard_phase::takeover) phase = wizard_phase::takeover;
        if (phase == wizard_phase::prepare || phase == wizard_phase::takeover) {
            if (gate.blocked & selection) {
                if (!(gate.waiting() & selection) && !gate.request_me(selection)) interrupt("Aligned takeover could not be requested. Auto remains paused.");
                return;
            }
            if (phase == wizard_phase::prepare) { phase = wizard_phase::input; input_at_ = now; stable_at_ = -1; return; }
            if (failed_) { phase = wizard_phase::failed; return; }
            if (cancelling_) { finish(gate, now, wizard_phase::cancelled); return; }
            remaining_ &= ~selection;
            if (!remaining_) finish(gate, now, wizard_phase::complete); else next(now);
            return;
        }
        if (phase == wizard_phase::input) {
            needed_axes = 0;
            for (size_t axis = 0; axis < axis_count; ++axis) if (pickup.required_axis(axis) && control_pickup::matches_group(axis, target)) {
                if (target == group::steering && !control_pickup::alignment_axis(axis)) continue; // Fixed definition states need no manual discovery/calibration.
                const auto& input = pickup.inputs[axis];
                bool ready = pickup.axis_known(axis) && pickup.valid_target(axis);
                if (pickup.relative_input(axis)) ready = ready && pickup.relative_observed[axis] >= input_at_;
                else ready = ready && input.detected && input.observed >= input_at_ && pickup.input_position(axis) >= 0 && now - input.changed >= .6;
                if (target == group::steering && axis < 2) ready = ready && std::abs(pickup.targets[axis] - .5) <= .02;
                if (!ready) needed_axes |= 1u << axis;
            }
            if (needed_axes) { stable_at_ = -1; return; }
            if (stable_at_ < 0) stable_at_ = now;
            if (now - stable_at_ < .6) return;
            if (!gate.assign_mock(selection, now)) { interrupt("Could not hold this group for its output check."); return; }
            phase = wizard_phase::mask; return;
        }
        if (phase == wizard_phase::mask) {
            if ((gate.blocked & selection) != selection || !check.start(target, pickup, profile, c, output, now)) {
                interrupt("The output check could not start. Check hardware positions and aircraft readiness."); return;
            }
            if (!save()) {
                check.restore_and_cancel(now); failed_ = true; message = "Cannot save validation. No diagnostic movement started.";
            }
            phase = wizard_phase::check; return;
        }
        if (phase == wizard_phase::check && !check.running()) {
            if (!check.restored) { interrupt("Original cockpit positions were not confirmed. Auto remains paused; recover this group explicitly."); return; }
            if (!cancelling_ && check.phase != output_check_phase::passed && !failed_) {
                failed_ = true; message = check.issue; failed_axis = check.failed_axis;
            }
            if (!failed_ && !cancelling_ && !save()) { failed_ = true; message = "Output passed but saving failed. Auto remains paused."; }
            phase = wizard_phase::takeover;
        }
    }
private:
    uint32_t remaining_ = 0;
    double input_at_ = 0, stable_at_ = -1;
    bool original_auto_ = false, cancelling_ = false, failed_ = false;
    void next(double now) {
        target = remaining_ & 1 ? group::steering : remaining_ & 2 ? group::throttle : remaining_ & 4 ? group::propeller : group::mixture;
        phase = wizard_phase::prepare; needed_axes = 0; input_at_ = now; message = "";
    }
    template<class Runtime> void finish(Runtime& gate, double now, wizard_phase terminal) {
        if (!gate.set_auto(select_quadrant, original_auto_, now)) { interrupt("Could not restore the original Auto setting."); return; }
        phase = terminal; message = "";
    }
};
}
