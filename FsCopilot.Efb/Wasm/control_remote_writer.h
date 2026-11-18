#pragma once
#include "control_session_timing.h"
#include "control_bridge_protocol.h"
#include "control_output.h"
#include "control_runtime.h"

namespace fsc_controls {
enum class writer_owner : uint32_t { none, local, remote };
enum class writer_fault { none, ended, lease_expired, native_unavailable, schema_changed, authority_invalid, write_failed };
struct writer_binding {
    char native[24]{}, backend[40]{}, session[40]{};
    uint64_t epoch = 0;
};
struct writer_state {
    uint64_t revision = 0, generation[4]{};
    writer_owner owner[4]{};
    uint32_t attached = 0, pending = 0, engines = 0, axes = 0;
};
struct writer_frame {
    writer_binding binding{};
    uint64_t revision = 0, generation = 0, sequence = 0;
    group target = group::steering;
    uint32_t axes = 0;
    double positions[axis_count]{};
};

// The backend must first authorize a local join, confirm all four masks, and
// bind a fresh session. This executor has no IPC handler: discovery cannot bind
// it. It accepts only the physics host's current remote owner, rechecking the
// binding, authority revision, group generation, sequence and confirmed native
// mask at the point of execution. It never changes masks itself.
template<class Runtime, class Output> class control_remote_writer {
public:
    control_remote_writer(control_pickup& pickup, Runtime& runtime, Output& output) : pickup_(pickup), runtime_(runtime), output_(output) {}
    void initialize(const char* native) {
        native_[0] = 0;
        if (native && bridge_token(native, sizeof(native_))) std::snprintf(native_, sizeof(native_), "%s", native);
        active_ = used_ = holding_ = false; fault_ = writer_fault::none; renewed_ = -1;
        binding_ = writer_binding{}; state_ = writer_state{};
        for (auto& sequence : sequence_) sequence = 0;
    }
    bool active() const { return active_; }
    bool holding() const { return holding_; }
    bool recover_local() { if (active_ || !holding_) return false; holding_ = false; return true; }
    writer_fault fault() const { return fault_; }
    static bool valid_binding_data(const writer_binding& value) { return valid_binding(value); }
    static bool valid_state_data(const writer_state& value) { return valid_state(value); }
    void end(writer_fault fault = writer_fault::ended) { active_ = false; used_ = true; fault_ = fault; }
    bool begin(const writer_binding& binding, const writer_state& state, double now) {
        if (active_ || used_ || !native_[0] || !valid_binding(binding) || std::strcmp(native_, binding.native) ||
            !valid_state(state) || state.attached || state.pending || !std::isfinite(now) || now < 0 ||
            !native_ready(now) || runtime_.blocked != select_all || runtime_.waiting() ||
            !schema_matches(state)) return false;
        binding_ = binding; state_ = state; renewed_ = now; active_ = used_ = holding_ = true; return true;
    }
    bool update(const writer_binding& binding, const writer_state& state, double now) {
        if (!live(now) || !same_binding(binding) || state.revision < state_.revision) return false;
        if (!valid_state(state)) { end(writer_fault::authority_invalid); return false; }
        if (state.engines != state_.engines || state.axes != state_.axes) { end(writer_fault::schema_changed); return false; }
        if (state.revision == state_.revision) {
            if (!same_state(state)) { end(writer_fault::authority_invalid); return false; } // Exact repeats are heartbeats, never mutations.
        } else {
            for (unsigned index = 0; index < 4; ++index) if (state.generation[index] < state_.generation[index] ||
                (state.owner[index] != state_.owner[index] && state.generation[index] == state_.generation[index])) {
                end(writer_fault::authority_invalid); return false;
            }
            for (unsigned index = 0; index < 4; ++index)
                if (state.generation[index] != state_.generation[index]) sequence_[index] = 0;
            state_ = state;
        }
        renewed_ = now; return true;
    }
    void tick(double now) { (void)live(now); }
    bool apply(const writer_frame& source, double now) { return apply_frame(source, now, false); }
    // Only the guest's bound world handler calls this. Canonical cockpit
    // targets must remain current during initial/pending aligned pickup too.
    bool mirror(const writer_frame& source, double now) { return apply_frame(source, now, true); }
private:
    bool apply_frame(const writer_frame& source, double now, bool mirror) {
        // Copy the accepted values before calling out; partial/throwing adapters
        // must never make the same sequence eligible for replay.
        const writer_frame frame = source;
        if (!live(now) || !same_binding(frame.binding) || frame.revision != state_.revision ||
            !frame.sequence || !frame.generation) return false;
        const auto selection = group_selection(frame.target);
        const unsigned index = selection_index(selection);
        if (index >= 4 || (!mirror && (state_.owner[index] != writer_owner::remote || !(state_.attached & selection) || state_.pending & selection)) ||
            (mirror && state_.owner[index] == writer_owner::local && (state_.attached & selection) && !(state_.pending & selection)) ||
            (selection == select_steering && pickup_.autopilot) ||
            frame.generation != state_.generation[index] || frame.sequence <= sequence_[index] ||
            !native_ready(now) || !(runtime_.blocked & selection)) return false;
        uint32_t required = 0;
        for (size_t axis = 0; axis < axis_count; ++axis)
            if ((state_.axes & (1u << axis)) && control_pickup::matches_group(axis, frame.target)) required |= 1u << axis;
        if (!required || frame.axes != required) return false;
        output_action plan[axis_count]{}; size_t count = 0;
        if (!group_output(pickup_, frame.target, frame.axes, frame.positions, plan, count)) return false;
        sequence_[index] = frame.sequence;
        if (!output_.write(plan, count)) { end(writer_fault::write_failed); return false; }
        return true;
    }
private:
    control_pickup& pickup_;
    Runtime& runtime_;
    Output& output_;
    char native_[24]{};
    writer_binding binding_{};
    writer_state state_{};
    uint64_t sequence_[4]{};
    double renewed_ = -1;
    bool active_ = false, used_ = false, holding_ = false;
    writer_fault fault_ = writer_fault::none;
    static bool valid_binding(const writer_binding& value) {
        return value.epoch && bridge_token(value.native, sizeof(value.native)) &&
            bridge_token(value.backend, sizeof(value.backend), 32) && bridge_token(value.session, sizeof(value.session), 32);
    }
    bool same_binding(const writer_binding& value) const {
        return valid_binding(value) && value.epoch == binding_.epoch && !std::strcmp(value.native, binding_.native) &&
            !std::strcmp(value.backend, binding_.backend) && !std::strcmp(value.session, binding_.session);
    }
    static bool valid_state(const writer_state& value) {
        if (!value.revision || value.engines < 1 || value.engines > 4 || value.axes >> axis_count ||
            (value.axes & 3u) != 3u || value.attached & ~select_all || value.pending & ~select_all || value.attached & value.pending) return false;
        uint32_t engine_axes = 0;
        for (size_t axis = steering_axis_count; axis < steering_axis_count + value.engines * 3; ++axis) engine_axes |= 1u << axis;
        if ((value.axes & ~((1u << steering_axis_count) - 1)) != engine_axes) return false;
        for (unsigned index = 0; index < 4; ++index) if (!value.generation[index] ||
            static_cast<uint32_t>(value.owner[index]) > static_cast<uint32_t>(writer_owner::remote) ||
            (value.owner[index] == writer_owner::none && (value.attached & (1u << index)))) return false;
        return true;
    }
    bool same_state(const writer_state& value) const {
        if (value.attached != state_.attached || value.pending != state_.pending) return false;
        for (unsigned index = 0; index < 4; ++index)
            if (value.generation[index] != state_.generation[index] || value.owner[index] != state_.owner[index]) return false;
        return true;
    }
    bool schema_matches(const writer_state& value) const {
        if (value.engines != pickup_.engines) return false;
        for (size_t axis = 0; axis < axis_count; ++axis) if (pickup_.required_axis(axis) && !(value.axes & (1u << axis))) return false;
        return true;
    }
    bool native_ready(double now) const {
        return output_.available && runtime_.ready && !runtime_.applying && pickup_.target_time >= 0 &&
            now >= pickup_.target_time && now - pickup_.target_time <= 1;
    }
    bool live(double now) {
        if (!active_) return false;
        if (!std::isfinite(now) || now < renewed_ || now - renewed_ >= session_recovery_seconds) { end(writer_fault::lease_expired); return false; }
        if (!output_.available || !runtime_.ready) { end(writer_fault::native_unavailable); return false; }
        if (!schema_matches(state_)) { end(writer_fault::schema_changed); return false; }
        return true;
    }
};
}
