#pragma once
#include "control_pickup.h"

namespace fsc_controls {
enum control_selection : uint32_t { select_none = 0, select_steering = 1, select_throttle = 2, select_propeller = 4,
    select_mixture = 8, select_quadrant = 14, select_all = 15 };
inline bool valid_selection(uint32_t selection) { return selection && !(selection & ~select_all); }
inline uint32_t axis_selection(size_t axis) { return axis < steering_axis_count ? select_steering : 1u << (1 + (axis - steering_axis_count) % 3); }
inline unsigned selection_index(uint32_t selection) { unsigned index = 0; while (index < 4 && selection != (1u << index)) ++index; return index; }
inline uint32_t event_selection(group target, const char* name) {
    if (target == group::steering) return select_steering;
    if (std::strstr(name, "THROTTLE")) return select_throttle;
    if (std::strstr(name, "PROPELLER") || std::strstr(name, "PROP_PITCH")) return select_propeller;
    if (std::strstr(name, "MIXTURE")) return select_mixture;
    return select_none;
}

struct runtime_takeover {
    uint32_t selection = 0;
    bool automatic = false;
    double stable_since = -1;
    command request{};
};

// The adapter configures four native event masks and confirms its matching
// SimConnect barrier. This class never treats a submitted change as applied.
template<class Adapter> class control_runtime
{
public:
    explicit control_runtime(control_pickup& inputs, Adapter& adapter) : inputs_(inputs), adapter_(adapter) {}
    uint32_t blocked = 0, barrier = 0, revision = 0;
    bool ready = false, applying = false, advanced = false;
    bool steering_blocked = false, quadrant_blocked = false;
    command pending{};
    uint32_t automatic = 0;
    runtime_takeover takeovers[4]{};
    double tolerance[4] = {0.1, 0.1, 0.1, 0.1};

    bool initialize(bool advanced_default = false, bool automatic_default = false) {
        ready = false; automatic = automatic_default ? select_quadrant : 0; advanced = advanced_default; pending = command{};
        for (auto& takeover : takeovers) takeover = runtime_takeover{};
        for (unsigned index = 0; index < 4; ++index) { detached_[index] = -1; tolerance[index] = 0.1; }
        reset_anchors(select_all); return configure(0, initial, 0);
    }
    bool confirm(uint32_t id, double now)
    {
        if (!applying || id != barrier) return false;
        const uint32_t newly_blocked = next_ & ~blocked;
        blocked = next_; ready = true; applying = false;
        steering_blocked = !!(blocked & select_steering); quadrant_blocked = (blocked & select_quadrant) == select_quadrant;
        inputs_.brakes_blocked = steering_blocked;
        for (unsigned index = 0; index < 4; ++index) if (newly_blocked & (1u << index)) detached_[index] = now;
        if (pending_ == assigning_mock) { seed_anchors(pending_selection_, now); }
        if (pending_ == attaching) cancel_overlapping(pending_selection_);
        ++revision; return true;
    }
    void fail()
    {
        adapter_.release(); blocked = automatic = 0; ready = applying = false;
        steering_blocked = quadrant_blocked = false;
        for (auto& takeover : takeovers) takeover = runtime_takeover{};
        for (auto& known : anchored_) known = false;
        ++revision;
    }
    uint32_t waiting() const { uint32_t result = 0; for (const auto& takeover : takeovers) result |= takeover.selection; return result; }
    bool set_advanced(bool value)
    {
        if (!ready || applying) return false;
        // Presentation only: a mixed quadrant stays mixed in Simple view, and
        // the single Auto switch remains independent of the Advanced view.
        advanced = value; ++revision; return true;
    }
    bool set_auto(uint32_t selection, bool enabled, double now)
    {
        if (!ready || applying || selection != select_quadrant) return false;
        automatic = enabled ? select_quadrant : 0;
        if (!enabled) {
            for (auto& takeover : takeovers) if (takeover.automatic) takeover = runtime_takeover{};
        }
        seed_anchors(selection, now); ++revision; return true;
    }
    bool set_tolerance(uint32_t selection, unsigned percent)
    {
        if (!ready || applying || !valid_selection(selection) || (percent != 1 && percent != 2 && percent != 3 && percent != 5 && percent != 10 && percent != 20 && percent != 30)) return false;
        for (unsigned index = 0; index < 4; ++index) if (selection & (1u << index)) tolerance[index] = percent / 100.0;
        for (auto& takeover : takeovers) if (takeover.selection & selection) takeover.stable_since = -1;
        ++revision; return true;
    }
    bool assign_mock(uint32_t selection, double now)
    {
        if (!ready || applying || !valid_selection(selection)) return false;
        cancel_overlapping(selection); seed_anchors(selection, now);
        if ((blocked & selection) == selection) { ++revision; return true; }
        return configure(blocked | selection, assigning_mock, selection);
    }
    bool request_me(uint32_t selection, bool is_automatic = false, const command& request = command{})
    {
        if (!ready || applying || !valid_selection(selection) || (is_automatic &&
            (selection_index(selection) < 1 || selection_index(selection) > 3 || !(automatic & selection)))) return false;
        cancel_overlapping(selection);
        if (!(blocked & selection)) { ++revision; return true; }
        runtime_takeover* free = nullptr;
        for (auto& takeover : takeovers) if (!takeover.selection) { free = &takeover; break; }
        if (!free) return false;
        *free = {selection, is_automatic, -1, request}; ++revision;
        pending = request;
        if ((blocked & selection) != selection && !configure(blocked | selection, preparing, selection)) return false;
        return true;
    }
    bool force_me(uint32_t selection)
    {
        if (!ready || applying || !valid_selection(selection)) return false;
        cancel_overlapping(selection); reset_anchors(selection);
        if (!(blocked & selection)) { ++revision; return true; }
        return configure(blocked & ~selection, attaching, selection);
    }
    bool cancel(uint32_t selection, double now)
    {
        if (!ready || applying || !valid_selection(selection)) return false;
        cancel_overlapping(selection); seed_anchors(selection, now); ++revision; return true;
    }
    bool managed_mask(uint32_t mask) {
        if (!ready || applying || mask & ~select_all) return false;
        cancel_overlapping(select_all); reset_anchors(select_all);
        // Always fence preceding SDK transmissions, even if the mask is already
        // correct. A cached mask alone cannot prove an outgoing owner drained.
        return configure(mask, preparing, select_all);
    }
    void update_targets(const double* values, double now, bool pause_learning = false)
    {
        uint32_t axes_blocked = 0;
        for (size_t axis = 0; axis < axis_count; ++axis) if (blocked & axis_selection(axis)) axes_blocked |= 1u << axis;
        inputs_.update(values, now, false, false, applying || !ready || pause_learning, axes_blocked);
    }
    void tick(double now)
    {
        if (!ready || applying) return;
        for (unsigned index = 1; index < 4; ++index) {
            const uint32_t selection = 1u << index;
            if (!(automatic & selection) || !(blocked & selection) || (waiting() & selection)) { reset_anchors(selection); continue; }
            bool moved = false;
            for (size_t axis = steering_axis_count; axis < axis_count; ++axis) if (axis_selection(axis) == selection && inputs_.active_axis(axis)) {
                const double position = inputs_.input_position(axis);
                const auto& input = inputs_.inputs[axis];
                if (position < 0 || now - input.observed > 30) { anchored_[axis] = false; continue; }
                if (input.observed < detached_[index]) continue;
                if (!anchored_[axis]) { anchors_[axis] = position; anchored_[axis] = true; continue; }
                if (std::abs(position - anchors_[axis]) > 0.02 + 1e-9) moved = true;
            }
            if (moved) request_me(selection, true);
        }
        for (auto& takeover : takeovers) if (takeover.selection) {
            if (!aligned(takeover.selection, now)) { takeover.stable_since = -1; continue; }
            if (takeover.stable_since < 0) takeover.stable_since = now;
            if (now - takeover.stable_since >= 0.3) {
                pending = takeover.request;
                configure(blocked & ~takeover.selection, attaching, takeover.selection); break;
            }
        }
    }
    bool aligned(uint32_t selection, double now) const
    {
        if (!valid_selection(selection) || inputs_.target_time < 0 || now - inputs_.target_time > 1 ||
            ((selection & select_steering) && inputs_.autopilot) || ((selection & select_quadrant) && !inputs_.engines)) return false;
        bool any = false;
        for (size_t axis = 0; axis < axis_count; ++axis) if ((selection & axis_selection(axis)) && inputs_.required_axis(axis) && control_pickup::alignment_axis(axis)) {
            any = true; if (!axis_aligned(axis, now)) return false;
        }
        return any;
    }
    bool axis_fresh(size_t axis, double now) const {
        if (inputs_.relative_input(axis)) return inputs_.valid_target(axis) && inputs_.target_time >= detached_[0] && now >= inputs_.target_time && now - inputs_.target_time <= 1;
        return inputs_.inputs[axis].detected && inputs_.inputs[axis].observed >= detached_[selection_index(axis_selection(axis))] && now - inputs_.inputs[axis].observed <= 30;
    }
    bool axis_aligned(size_t axis, double now) const {
        if (inputs_.relative_input(axis)) return axis_fresh(axis, now) && std::abs(inputs_.targets[axis] - control_pickup::neutral(axis)) <= tolerance[0] + 1e-9;
        const double position = inputs_.input_position(axis);
        return inputs_.target_time >= 0 && now - inputs_.target_time <= 1 && axis_fresh(axis, now) && position >= 0 &&
            std::isfinite(inputs_.targets[axis]) && inputs_.targets[axis] >= 0 && inputs_.targets[axis] <= 1 &&
            std::abs(position - inputs_.targets[axis]) <= tolerance[selection_index(axis_selection(axis))] + 1e-9;
    }
private:
    enum operation { initial, assigning_mock, preparing, attaching } pending_ = initial;
    control_pickup& inputs_;
    Adapter& adapter_;
    uint32_t next_ = 0, pending_selection_ = 0;
    double detached_[4] = {-1, -1, -1, -1};
    double anchors_[axis_count]{};
    bool anchored_[axis_count]{};
    void cancel_overlapping(uint32_t selection) { for (auto& takeover : takeovers) if (takeover.selection & selection) takeover = runtime_takeover{}; }
    void reset_anchors(uint32_t selection) {
        for (size_t axis = 0; axis < axis_count; ++axis) if (axis_selection(axis) & selection) anchored_[axis] = false;
    }
    void seed_anchors(uint32_t selection, double now) {
        reset_anchors(selection);
        for (size_t axis = 0; axis < axis_count; ++axis) if (axis_selection(axis) & selection) {
            const double position = inputs_.input_position(axis);
            if (position >= 0 && now - inputs_.inputs[axis].observed <= 30) { anchors_[axis] = position; anchored_[axis] = true; }
        }
    }
    bool configure(uint32_t mask, operation operation, uint32_t selection)
    {
        next_ = mask; pending_ = operation; pending_selection_ = selection; applying = true; ++barrier;
        if (!adapter_.configure_mask(mask, barrier)) { fail(); return false; }
        return true;
    }
};
}
