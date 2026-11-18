#pragma once
#include "../Wasm/control_bridge_protocol.h"
#include "../Wasm/control_profile.h"
#include "../Wasm/control_output.h"

namespace fsc_controls {
struct output_check_guard {
    double at = -1, ground = 0, knots = 999, autopilot = 0, parking_brake = 0;
    bool unbound_session_fresh = false; // Verified by the SDK observer, never inferred from Me ownership.
};
// Planning/verification for a bounded SDK diagnostic, never a session authority.
// It accepts only the installed solo bridge and measured, stationary Baron data.
class same_state_control_check {
public:
    static constexpr double maximum_error = 0.0051;
    // Below the 0.49% minimum movement accepted as a distinct output readback.
    static constexpr double hardware_noise_limit = 0.0045;
    output_action actions[axis_count]{};
    size_t count = 0;
    uint32_t axes = 0;
    bool prepare(const bridge_packet& observed, const control_profile& saved, group target,
        double now, double observed_at, const output_check_guard& guard) {
        if (prepared_ || !fresh(observed, now, observed_at, guard) ||
            (target != group::steering && target != group::throttle && target != group::propeller && target != group::mixture) ||
            (target == group::steering && guard.autopilot != 0)) return false;
        char identity[1024]{};
        if (!profile_identity(observed.aircraft, sizeof(observed.aircraft), identity) ||
            std::strcmp(identity, baron) || std::strcmp(observed.adapter, "bksq-baron58p-cockpit-v1") ||
            !valid_profile(saved, identity, observed.engines)) return false;
        uint32_t needed = 0;
        if (target == group::steering) {
            needed = observed.required & 0x1ffu;
            if ((needed & 3u) != 3u) return false;
        } else for (size_t axis = steering_axis_count; axis < steering_axis_count + 3 * observed.engines; ++axis)
            if (control_pickup::matches_group(axis, target)) needed |= 1u << axis;
        if (!needed || (observed.required & observed.learned & needed) != needed) return false;
        control_pickup model; model.engines = observed.engines;
        restore_profile(saved, model, uint32_t{0});
        // This copies the observed axis schema, not any invented hardware pose.
        for (size_t axis = 0; axis < axis_count; ++axis) model.inputs[axis].detected = !!(observed.required & (1u << axis));
        output_action plan[axis_count]{}; size_t planned = 0;
        if (!group_output(model, target, needed, observed.targets, plan, planned)) return false;
        baseline_ = observed; target_ = target; axes = needed; count = planned;
        model_ = model; profile_checksum_ = saved.checksum;
        std::memcpy(actions, plan, sizeof(actions)); prepared_ = true; return true;
    }
    bool current(const bridge_packet& observed, double now, double observed_at, const output_check_guard& guard,
        bool after_write = false, bool nudged = false) const {
        if (!prepared_ || !fresh(observed, now, observed_at, guard) ||
            (target_ == group::steering && guard.autopilot != 0) || std::strcmp(observed.native, baseline_.native) ||
            std::strcmp(observed.aircraft, baseline_.aircraft) || std::strcmp(observed.adapter, baseline_.adapter) ||
            observed.engines != baseline_.engines || observed.required != baseline_.required || observed.serial < baseline_.serial ||
            (observed.learned & axes) != axes) return false;
        for (size_t axis = 0; axis < axis_count; ++axis) if (axes & (1u << axis)) {
            const auto expected = nudged ? nudge_position(axis) : baseline_.targets[axis];
            if (observed.targets[axis] < 0 || std::abs(observed.targets[axis] - expected) > maximum_error ||
                (nudged && std::abs(observed.targets[axis] - baseline_.targets[axis]) < 0.0049)) return false;
            // DEFAULT-priority output must not echo back as hardware input.
            if (after_write && observed.input[axis] != baseline_.input[axis] &&
                (!(noise_axes_ & (1u << axis)) || !std::isfinite(observed.input[axis]) ||
                    std::abs(observed.input[axis] - baseline_.input[axis]) > hardware_noise_limit)) return false;
        }
        return true;
    }
    bool matches_profile(const control_profile& value) const { return prepared_ && value.checksum == profile_checksum_; }
    bool use_mixture_set_route() {
        if (!prepared_ || target_ != group::mixture) return false;
        mixture_set_route_ = true; map_mixture_set(baseline_.targets, actions); return true;
    }
    bool hardware_noise(const char* event, int32_t raw) {
        if (!prepared_ || !event) return false;
        // Output routes are never noise evidence, even at the same position.
        for (size_t index = 0; index < count; ++index) if (!std::strcmp(event, actions[index].event)) return false;
        uint32_t matched = 0;
        for (size_t axis = 0; axis < axis_count; ++axis) if ((axes & (1u << axis)) && !std::strcmp(event, model_.inputs[axis].event)) {
            unsigned candidate = 0; while (candidate < 10 && !(model_.inputs[axis].candidates & (1u << candidate))) ++candidate;
            if (candidate == 10 || baseline_.input[axis] < 0 || baseline_.input[axis] > 1) return false;
            const auto position = control_pickup::prediction(raw, candidate);
            if (!std::isfinite(position) || position < 0 || position > 1 ||
                std::abs(position - baseline_.input[axis]) > hardware_noise_limit) return false;
            matched |= 1u << axis;
        }
        if (!matched) return false;
        noise_axes_ |= matched; return true;
    }
    bool make_nudge(output_action (&plan)[axis_count], size_t& planned) const {
        planned = 0; if (!prepared_) return false;
        double positions[axis_count]{}; std::memcpy(positions, baseline_.targets, sizeof(positions));
        for (size_t axis = 0; axis < axis_count; ++axis) if (axes & (1u << axis)) positions[axis] = nudge_position(axis);
        if (!group_output(model_, target_, axes, positions, plan, planned)) return false;
        if (mixture_set_route_) map_mixture_set(positions, plan);
        return true;
    }
private:
    static constexpr const char* baron = "simobjects/airplanes/bksq-aircraft-baronpropress/aircraft.cfg";
    bool prepared_ = false;
    group target_ = group::steering;
    bridge_packet baseline_{};
    control_pickup model_{};
    uint32_t profile_checksum_ = 0;
    uint32_t noise_axes_ = 0;
    bool mixture_set_route_ = false;
    void map_mixture_set(const double* positions, output_action (&plan)[axis_count]) const {
        size_t index = 0;
        for (size_t axis = steering_axis_count; axis < axis_count; ++axis) if (axes & (1u << axis)) {
            const auto engine = static_cast<unsigned>((axis - steering_axis_count) / 3 + 1);
            std::snprintf(plan[index].event, sizeof(plan[index].event), "MIXTURE%u_SET", engine);
            plan[index++].raw = static_cast<int32_t>(std::lround(positions[axis] * 16384));
        }
    }
    double nudge_position(size_t axis) const { return baseline_.targets[axis] >= 0.99 ? baseline_.targets[axis] - 0.01 : baseline_.targets[axis] + 0.01; }
    static bool fresh(const bridge_packet& observed, double now, double observed_at, const output_check_guard& guard) {
        return bridge_valid(observed) && (observed.kind == bridge_kind::status || observed.kind == bridge_kind::reply) &&
            ((observed.connection_api == 0 && observed.capabilities == 11) ||
                (observed.connection_api == 1 && (observed.capabilities & ~16u) == 11 && guard.unbound_session_fresh)) &&
            observed.engines == 2 && observed.blocked == 0 && observed.automatic == 0 &&
            (observed.flags & (bridge_ready | bridge_targets_fresh | bridge_applying | bridge_storage_failed)) == (bridge_ready | bridge_targets_fresh) &&
            std::isfinite(now) && now >= 0 && observed_at >= 0 && now >= observed_at && now - observed_at <= 0.35 &&
            guard.at >= 0 && now >= guard.at && now - guard.at <= 0.25 && guard.ground == 1 && guard.parking_brake == 1 &&
            std::isfinite(guard.knots) && std::abs(guard.knots) <= 0.1 && std::isfinite(guard.autopilot) && (guard.autopilot == 0 || guard.autopilot == 1);
    }
};
}
