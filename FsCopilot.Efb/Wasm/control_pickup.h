#pragma once
#include <cmath>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include "control_protocol.h"
#include "control_definition_profile.h"

namespace fsc_controls {
struct axis_definition { const char* id; const char* label; const char* variable; const char* units; bool signed_target; };
constexpr axis_definition axes[] = {
    {"roll", "Yoke roll", "AILERON POSITION", "Position 16k", true},
    {"pitch", "Yoke pitch", "ELEVATOR POSITION", "Position 16k", true},
    {"rudder", "Rudder", "RUDDER POSITION", "Position 16k", true},
    {"steering", "Nosewheel", "STEER INPUT CONTROL", "Percent Over 100", true},
    {"leftBrake", "Left brake", "BRAKE LEFT POSITION", "Percent Over 100", false},
    {"rightBrake", "Right brake", "BRAKE RIGHT POSITION", "Percent Over 100", false},
    {"elevatorTrim", "Elevator trim", "ELEVATOR TRIM PCT EX1", "Percent Over 100", false},
    {"aileronTrim", "Aileron trim", "AILERON TRIM PCT", "Number", true},
    {"rudderTrim", "Rudder trim", "RUDDER TRIM PCT", "Number", true},
    {"throttle1", "Throttle 1", "GENERAL ENG THROTTLE LEVER POSITION:1", "Percent", false},
    {"propeller1", "Propeller 1", "GENERAL ENG PROPELLER LEVER POSITION:1", "Percent", false},
    {"mixture1", "Mixture 1", "GENERAL ENG MIXTURE LEVER POSITION:1", "Percent", false},
    {"throttle2", "Throttle 2", "GENERAL ENG THROTTLE LEVER POSITION:2", "Percent", false},
    {"propeller2", "Propeller 2", "GENERAL ENG PROPELLER LEVER POSITION:2", "Percent", false},
    {"mixture2", "Mixture 2", "GENERAL ENG MIXTURE LEVER POSITION:2", "Percent", false},
    {"throttle3", "Throttle 3", "GENERAL ENG THROTTLE LEVER POSITION:3", "Percent", false},
    {"propeller3", "Propeller 3", "GENERAL ENG PROPELLER LEVER POSITION:3", "Percent", false},
    {"mixture3", "Mixture 3", "GENERAL ENG MIXTURE LEVER POSITION:3", "Percent", false},
    {"throttle4", "Throttle 4", "GENERAL ENG THROTTLE LEVER POSITION:4", "Percent", false},
    {"propeller4", "Propeller 4", "GENERAL ENG PROPELLER LEVER POSITION:4", "Percent", false},
    {"mixture4", "Mixture 4", "GENERAL ENG MIXTURE LEVER POSITION:4", "Percent", false}
};
constexpr size_t axis_count = sizeof(axes) / sizeof(axes[0]);
constexpr size_t steering_axis_count = 9;
constexpr size_t target_count = axis_count + 4; // Engine count, AP master, two optional cockpit-mixture references.

struct pickup_axis {
    bool detected = false;
    int32_t raw = 0;
    int32_t anchor = 0;
    double changed = 0;
    double observed = -1;
    unsigned candidates = 0x3ff; // Five conventional scales and their inversions.
    bool calibrated = false;
    unsigned saved_candidates = 0; // Learned mapping awaiting the first current hardware sample.
    char event[48]{};
    unsigned confirming = 0;
    unsigned confirmations = 0;
    bool has_evidence = false;
    int32_t evidence_low = 0;
    int32_t evidence_high = 0;
};

struct pickup_request {
    bool waiting = false;
    command request{};
    double detached = -1;
    double stable_since = -1;
    double tolerance = default_tolerance_percent / 100.0;
};

class control_pickup {
public:
    pickup_axis inputs[axis_count]{};
    pickup_request requests[2]{};
    double targets[axis_count]{};
    double target_time = -1;
    unsigned engines = 0;
    bool autopilot = false;
    const definition_profile* definitions = nullptr;
    bool brakes_blocked = false;
    uint32_t output_response_axes = 0; // Verified output route, independent of discovered hardware.
    double relative_observed[axis_count]{};

    bool canonical_axis(size_t axis) const {
        return axis >= 2 && axis < steering_axis_count && definition_for_axis(definitions, axis);
    }
    bool relative_brake(size_t axis) const { return (axis == 4 || axis == 5) && canonical_axis(axis) && !inputs[axis].event[0]; }
    bool relative_input(size_t axis) const { return canonical_axis(axis); }
    static double neutral(size_t axis) { return axis == 2 ? .5 : 0; }
    bool axis_known(size_t axis) const { return canonical_axis(axis) || inputs[axis].calibrated; }
    bool valid_target(size_t axis) const { return target_time >= 0 && std::isfinite(targets[axis]) && targets[axis] >= 0 && targets[axis] <= 1; }

    static unsigned index(group value) { return value == group::steering ? 0 : 1; }
    static bool axis_source(const char* name) { return !std::strncmp(name, "AXIS_", 5) || std::strstr(name, "_AXIS_SET_EX1"); }
    static group axis_group(size_t axis) { return axis < steering_axis_count ? group::steering : group::quadrant; }
    static bool matches_group(size_t axis, group value) {
        const auto selection = axis < steering_axis_count ? 1u : 1u << (1 + (axis - steering_axis_count) % 3);
        return !!(group_selection(value) & selection);
    }
    bool active_axis(size_t axis) const { return axis < steering_axis_count || (axis - steering_axis_count) / 3 < engines; }
    // A supported aircraft's complete layout is fixed at load, not discovered
    // from hardware. Keep the legacy fallback for unsupported local profiles.
    // Slots 6..8 stay reserved in the wire format; trim uses ordinary shared instruments.
    bool required_axis(size_t axis) const { return (axis < 6 || axis >= steering_axis_count) && active_axis(axis) && (axis < 2 || axis >= steering_axis_count ||
        (definitions ? definition_for_axis(definitions, axis) != nullptr : inputs[axis].detected || inputs[axis].saved_candidates)); }
    static bool alignment_axis(size_t axis) { return axis < 2 || axis >= steering_axis_count; }

    static double prediction(int32_t raw, unsigned candidate)
    {
        const unsigned scale = candidate % 5;
        double value = scale == 0 || scale == 4 ? (static_cast<double>(raw) + 16384) / 32768 :
            scale == 1 ? raw / 16384.0 : scale == 2 ? raw / 100.0 : (raw + 100.0) / 200.0;
        if (value < -0.001 || value > 1.001) return -10;
        // Simulator integer conversions can land one count beyond an endpoint.
        if (value < 0) value = 0;
        if (value > 1) value = 1;
        if (scale == 4) {
            constexpr double curve[] = {0, 0.08, 0.27, 0.53, 1};
            const double segment = value * 4;
            const unsigned part = segment >= 4 ? 3 : static_cast<unsigned>(segment);
            value = curve[part] + (curve[part + 1] - curve[part]) * (segment - part);
        }
        return candidate < 5 ? value : 1 - value;
    }

    // A mapping is usable only after validation at distinct settled positions.
    // Once learned, the selected scale is retained through all handovers.
    double input_position(size_t axis) const
    {
        // Fixed pedal/trim routes share the actual unmasked simulator demand,
        // exactly like vanilla. Their hardware pose is not an alignment input.
        if (relative_input(axis)) return !brakes_blocked && valid_target(axis) ? targets[axis] : -10;
        const auto& input = inputs[axis];
        if (!input.detected || !input.calibrated || !input.candidates) return -10;
        double low = 2, high = -1;
        for (unsigned candidate = 0; candidate < 10; ++candidate) if (input.candidates & (1u << candidate)) {
            const double value = prediction(input.raw, candidate);
            if (value < 0 || value > 1) return -10;
            if (value < low) low = value;
            if (value > high) high = value;
        }
        return high - low <= 0.01 ? (low + high) / 2 : -10;
    }

    void sample(size_t axis, int32_t raw, double now, const char* event = nullptr)
    {
        auto& input = inputs[axis];
        // Aircraft can emit ordinary SET events in response to an AXIS input.
        // They are not a second physical axis and may have a different scale.
        // Keep the axis source once observed; feedback must neither erase its
        // learning nor refresh its hardware-observation timestamp.
        if (event && axis_source(input.event) && !axis_source(event)) return;
        // A different absolute-event binding may use a different scale. Never
        // silently apply the previous event's mapping to the new event.
        if (event && input.event[0] && std::strcmp(input.event, event)) {
            // Cockpit/aircraft SET echoes must not replace a learned physical
            // binding mid-session. A changed binding requires explicit Relearn.
            if (input.calibrated || input.saved_candidates) return;
            input = pickup_axis{};
        }
        if (event) std::snprintf(input.event, sizeof(input.event), "%s", event);
        const double chatter = std::abs(static_cast<double>(raw)) > 200 || std::abs(static_cast<double>(input.anchor)) > 200 ? 256 : 1;
        if (!input.detected || std::abs(static_cast<double>(raw) - input.anchor) > chatter) {
            input.anchor = raw;
            input.changed = now;
        }
        input.detected = true;
        input.raw = raw;
        input.observed = now;
        if (canonical_axis(axis)) {
            // Fixed SDK event domain, with sign/range from the same vanilla
            // absolute setter used for output. No two-position calibration.
            input.calibrated = true; input.candidates = axis == 2 ? 32 : 1; input.saved_candidates = 0;
            return;
        }
        if (input.saved_candidates) {
            // The persisted scale was already learned. Read the current raw
            // position, even at a centre/endpoint where scales coincide; do
            // not repeat learning against potentially delayed cockpit data.
            // An outlier is unavailable input, not evidence to erase calibration.
            // Preserve the mapping and wait for a valid current sample.
            if (input_position(axis) >= 0) input.saved_candidates = 0;
        }
    }

    void observe(const char* event, int32_t raw, double now)
    {
        const bool rudder_relative = !std::strcmp(event, "RUDDER_AXIS_MINUS") || !std::strcmp(event, "RUDDER_AXIS_PLUS") ||
            !std::strcmp(event, "RUDDER_LEFT") || !std::strcmp(event, "RUDDER_RIGHT") || !std::strcmp(event, "RUDDER_CENTER") || !std::strcmp(event, "CENTER_AILER_RUDDER");
        const bool both_brakes = !std::strcmp(event, "BRAKES");
        if (rudder_relative) relative_observed[2] = now;
        if (both_brakes || !std::strcmp(event, "BRAKES_LEFT")) relative_observed[4] = now;
        if (both_brakes || !std::strcmp(event, "BRAKES_RIGHT")) relative_observed[5] = now;
        for (const auto axis : {2u, 4u, 5u}) if (canonical_axis(axis) && !inputs[axis].detected &&
            ((axis == 2 && rudder_relative) || (axis == 4 && (both_brakes || !std::strcmp(event, "BRAKES_LEFT"))) ||
             (axis == 5 && (both_brakes || !std::strcmp(event, "BRAKES_RIGHT"))))) {
            // A cached absolute binding without a current sample is not proof
            // of today's hardware. Recognize an actual relative command.
            inputs[axis] = pickup_axis{};
        }
        const char* names[][4] = {
            {"AXIS_AILERONS_SET", "AILERON_SET", nullptr, nullptr},
            {"AXIS_ELEVATOR_SET", "ELEVATOR_SET", nullptr, nullptr},
            {"AXIS_RUDDER_SET", "RUDDER_SET", "AXIS_SENSOR_RUDDER_SET", nullptr},
            {"AXIS_STEERING_SET", "STEERING_SET", nullptr, nullptr},
            {"AXIS_LEFT_BRAKE_SET", nullptr, nullptr, nullptr},
            {"AXIS_RIGHT_BRAKE_SET", nullptr, nullptr, nullptr},
            {"AXIS_ELEV_TRIM_SET", "ELEVATOR_TRIM_SET", nullptr, nullptr},
            {"AILERON_TRIM_SET", "AILERON_TRIM_SET_EX1", nullptr, nullptr},
            {"RUDDER_TRIM_SET", "RUDDER_TRIM_SET_EX1", nullptr, nullptr}
        };
        for (size_t axis = 0; axis < steering_axis_count; ++axis)
            for (const char* name : names[axis]) if (name && !std::strcmp(event, name)) { sample(axis, raw, now, event); return; }
        const char* templates[][3] = {
            {"AXIS_THROTTLE%s_SET", "THROTTLE%s_SET", "THROTTLE%s_AXIS_SET_EX1"},
            {"AXIS_PROPELLER%s_SET", "PROP_PITCH%s_SET", "PROP_PITCH%s_AXIS_SET_EX1"},
            {"AXIS_MIXTURE%s_SET", "MIXTURE%s_SET", nullptr}
        };
        for (unsigned type = 0; type < 3; ++type) for (unsigned engine = 0; engine <= 4; ++engine) {
            char number[2]{};
            if (engine) number[0] = static_cast<char>('0' + engine);
            for (const char* format : templates[type]) if (format) {
                char name[48]{};
                std::snprintf(name, sizeof(name), format, number);
                if (std::strcmp(event, name)) continue;
                for (unsigned target = 1; target <= 4; ++target)
                    if (!engine || target == engine) sample(steering_axis_count + (target - 1) * 3 + type, raw, now, event);
                return;
            }
        }
    }

    void update(const double* values, double now, bool steering_blocked, bool quadrant_blocked, bool applying, uint32_t blocked_axes = UINT32_MAX)
    {
        // An invalid count is no evidence of an aircraft/schema change. Retain
        // the last verified layout; startup still waits for a valid first count.
        const double count = values[axis_count];
        if (std::isfinite(count) && count >= 1 && count <= 4 && std::floor(count) == count) engines = static_cast<unsigned>(count);
        // Unknown AP state cannot grant manual steering, but must not stop engines.
        autopilot = values[axis_count + 1] != 0;
        brakes_blocked = blocked_axes != UINT32_MAX ? !!(blocked_axes & ((1u << 4) | (1u << 5))) : steering_blocked;
        target_time = now;
        for (size_t axis = 0; axis < axis_count; ++axis) {
            double value = values[axis];
            if (axis < 3) value /= 16384;
            if (axis >= steering_axis_count) value /= 100;
            targets[axis] = axes[axis].signed_target ? (value + 1) / 2 : value;
            if (targets[axis] < 0 && targets[axis] >= -0.001) targets[axis] = 0;
            if (targets[axis] > 1 && targets[axis] <= 1.001) targets[axis] = 1;
            if (!std::isfinite(targets[axis]) || targets[axis] < 0 || targets[axis] > 1) targets[axis] = -1;
            auto& input = inputs[axis];
            const bool blocked = blocked_axes != UINT32_MAX ? !!(blocked_axes & (1u << axis)) : axis_group(axis) == group::steering ? steering_blocked : quadrant_blocked;
            // Returning to Me resumes input; it must never resume destructive
            // model elimination against delayed read-back. Only explicit
            // Relearn / aircraft reset may discard a validated mapping.
            if (canonical_axis(axis) || input.calibrated || applying || blocked || (axis < steering_axis_count && autopilot) || !input.detected ||
                now - input.changed < 0.15 || now - input.observed > 2 || !valid_target(axis)) continue;
            unsigned matching = 0;
            for (unsigned candidate = 0; candidate < 10; ++candidate)
                if (std::abs(prediction(input.raw, candidate) - targets[axis]) <= 0.015) matching |= 1u << candidate;
            if (!matching) {
                // A stale or aircraft-filtered sample is not evidence that
                // every scale is invalid. A later valid pair can still learn.
                input.confirmations = 0;
                continue;
            }
            unsigned candidates = input.candidates & matching;
            if (!candidates) { candidates = matching; input.has_evidence = false; }
            input.candidates = candidates;
            if (!input.has_evidence) {
                input.evidence_low = input.evidence_high = input.raw;
                input.has_evidence = true;
            }
            if (input.raw < input.evidence_low) input.evidence_low = input.raw;
            if (input.raw > input.evidence_high) input.evidence_high = input.raw;
            if ((candidates & (candidates - 1)) != 0) { input.confirmations = 0; continue; }
            if (input.confirming != candidates) { input.confirming = candidates; input.confirmations = 0; }
            ++input.confirmations;
            unsigned selected = 0;
            while (!(candidates & (1u << selected))) ++selected;
            const double travel = std::abs(prediction(input.evidence_high, selected) - prediction(input.evidence_low, selected));
            input.calibrated = input.confirmations >= 3 && travel >= 0.10;
        }
    }

    void detach(group value, double now) { auto& request = requests[index(value)]; request.waiting = false; request.detached = now; request.stable_since = -1; }
    void cancel(group value) { auto& request = requests[index(value)]; request.waiting = false; request.stable_since = -1; }
    void arm(const command& cmd) { auto& request = requests[index(cmd.target)]; request.waiting = true; request.request = cmd; request.stable_since = -1; }

    bool aligned(size_t axis, double now) const
    {
        const auto& input = inputs[axis];
        const auto& request = requests[index(axis_group(axis))];
        if (relative_input(axis)) return valid_target(axis) && target_time >= request.detached && now - target_time <= 1 && std::abs(targets[axis] - neutral(axis)) <= request.tolerance;
        const double position = input_position(axis);
        return target_time >= 0 && now - target_time <= 1 && input.observed >= request.detached &&
            now - input.observed <= 30 && std::isfinite(targets[axis]) && targets[axis] >= 0 && targets[axis] <= 1 &&
            position >= 0 && std::abs(position - targets[axis]) <= request.tolerance;
    }

    bool ready(group value, double now)
    {
        auto& request = requests[index(value)];
        bool any = false, all = !(value == group::steering && autopilot);
        for (size_t axis = 0; axis < axis_count; ++axis) if (required_axis(axis) && alignment_axis(axis) && axis_group(axis) == value) {
            any = true;
            if (!aligned(axis, now)) all = false;
        }
        if (!any || !all || (value == group::quadrant && !engines)) { request.stable_since = -1; return false; }
        if (request.stable_since < 0) request.stable_since = now;
        return now - request.stable_since >= 0.3;
    }
};
}
