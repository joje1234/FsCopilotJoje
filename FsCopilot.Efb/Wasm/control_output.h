#pragma once
#include "control_pickup.h"
#include <limits>

namespace fsc_controls {
struct output_action {
    char event[48]{}; int32_t raw = 0;
    size_t axis = axis_count;
    double position = std::numeric_limits<double>::quiet_NaN();
    bool axis_response = false;
};

// Invert a scale already learned from this aircraft's actual cockpit read-back.
// No default direction/scale is guessed for an unknown host-side axis mapping.
inline bool output_raw(double position, unsigned mapping, int32_t& raw)
{
    if (!std::isfinite(position) || position < 0 || position > 1 || !mapping || mapping > 512 || (mapping & (mapping - 1))) return false;
    unsigned candidate = 0; while (!(mapping & (1u << candidate))) ++candidate;
    double value = candidate < 5 ? position : 1 - position;
    const unsigned scale = candidate % 5;
    if (scale == 4) {
        constexpr double curve[] = {0, 0.08, 0.27, 0.53, 1};
        unsigned part = 0; while (part < 3 && value > curve[part + 1]) ++part;
        value = (part + (value - curve[part]) / (curve[part + 1] - curve[part])) / 4;
    }
    raw = static_cast<int32_t>(std::lround(scale == 0 || scale == 4 ? value * 32768 - 16384 :
        scale == 1 ? value * 16384 : scale == 2 ? value * 100 : value * 200 - 100));
    return std::abs(control_pickup::prediction(raw, candidate) - position) <= 0.0051;
}

inline bool output_for_axis(const control_pickup& pickup, size_t axis, double position, output_action& output)
{
    if (axis < axis_count && pickup.active_axis(axis) && std::isfinite(position) && position >= 0 && position <= 1) {
        if (const auto* mapping = definition_for_axis(pickup.definitions, axis)) {
            output.axis = axis; output.position = position;
            output.axis_response = !!(pickup.output_response_axes & (1u << axis));
            if (mapping->output_kind == 3) {
                std::snprintf(output.event, sizeof(output.event), "%s", mapping->write + 2);
                output.raw = static_cast<int32_t>(std::lround(definition_output_value(*mapping, position, output.axis_response)));
            }
            return true; // Definition data selects the verified local absolute-output route.
        }
    }
    if (axis >= axis_count || !pickup.active_axis(axis) || !pickup.inputs[axis].calibrated ||
        !output_raw(position, pickup.inputs[axis].candidates, output.raw)) return false;
    output.axis = axis; output.position = position;
    const auto* source = pickup.inputs[axis].event;
    const char* steering[][3] = {
        {"AXIS_AILERONS_SET", "AILERON_SET", nullptr}, {"AXIS_ELEVATOR_SET", "ELEVATOR_SET", nullptr},
        {"AXIS_RUDDER_SET", "RUDDER_SET", "AXIS_SENSOR_RUDDER_SET"}, {"AXIS_STEERING_SET", "STEERING_SET", nullptr},
        {"AXIS_LEFT_BRAKE_SET", nullptr, nullptr}, {"AXIS_RIGHT_BRAKE_SET", nullptr, nullptr},
        {"AXIS_ELEV_TRIM_SET", "ELEVATOR_TRIM_SET", nullptr}, {"AILERON_TRIM_SET", "AILERON_TRIM_SET_EX1", nullptr},
        {"RUDDER_TRIM_SET", "RUDDER_TRIM_SET_EX1", nullptr}
    };
    if (axis < steering_axis_count) {
        for (const auto* name : steering[axis]) if (name && !std::strcmp(source, name)) {
            std::snprintf(output.event, sizeof(output.event), "%s", name); return true;
        }
        return false;
    }
    const unsigned type = static_cast<unsigned>((axis - steering_axis_count) % 3);
    const unsigned engine = static_cast<unsigned>((axis - steering_axis_count) / 3 + 1);
    const char* formats[][3] = {
        {"AXIS_THROTTLE%s_SET", "THROTTLE%s_SET", "THROTTLE%s_AXIS_SET_EX1"},
        {"AXIS_PROPELLER%s_SET", "PROP_PITCH%s_SET", "PROP_PITCH%s_AXIS_SET_EX1"},
        {"AXIS_MIXTURE%s_SET", "MIXTURE%s_SET", nullptr}
    };
    for (const auto* format : formats[type]) if (format) {
        for (unsigned input_engine = 0; input_engine <= 4; ++input_engine) {
            char number[2]{}; if (input_engine) number[0] = static_cast<char>('0' + input_engine);
            char name[48]{}; std::snprintf(name, sizeof(name), format, number);
            if (std::strcmp(name, source)) continue;
            if (input_engine && input_engine != engine) return false;
            // A combined hardware binding can still receive independent remote
            // engine values; never transmit a shared event twice for two levers.
            number[0] = static_cast<char>('0' + engine);
            std::snprintf(output.event, sizeof(output.event), format, number); return true;
        }
    }
    return false;
}

inline bool group_output(const control_pickup& pickup, group target, uint32_t mask, const double* positions,
    output_action (&output)[axis_count], size_t& count)
{
    count = 0;
    if (!positions || !mask || mask >> axis_count || target == group::all) return false;
    uint32_t required = 0;
    for (size_t axis = 0; axis < axis_count; ++axis)
        if (pickup.required_axis(axis) && control_pickup::matches_group(axis, target)) required |= 1u << axis;
    if (!required || (mask & required) != required || (target != group::steering && !pickup.engines)) return false;
    output_action plan[axis_count]{}; size_t planned = 0;
    for (size_t axis = 0; axis < axis_count; ++axis) if (mask & (1u << axis)) {
        if (!control_pickup::matches_group(axis, target) || !output_for_axis(pickup, axis, positions[axis], plan[planned])) return false;
        ++planned;
    }
    // Validate the whole frame before exposing any action to the simulator.
    std::memcpy(output, plan, sizeof(plan)); count = planned; return true;
}
}
