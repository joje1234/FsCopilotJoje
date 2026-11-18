#pragma once
#include <cstring>
#include "control_definition_catalog.h"

namespace fsc_controls {
inline const definition_profile* find_definition_profile(const char* normalized_path) {
    if (!normalized_path) return nullptr;
    const char prefix[] = "simobjects/airplanes/";
    const auto* start = std::strstr(normalized_path, prefix);
    if (!start || (start != normalized_path && start[-1] != '/')) return nullptr;
    start += sizeof(prefix) - 1;
    const auto* end = std::strchr(start, '/');
    if (!end) return nullptr;
    for (const auto& profile : definition_profiles)
        if (std::strlen(profile.aircraft) == static_cast<size_t>(end - start) && !std::strncmp(start, profile.aircraft, end - start)) return &profile;
    return nullptr;
}
inline const definition_axis* definition_for_axis(const definition_profile* profile, size_t axis) {
    if (profile) for (size_t index = 0; index < profile->count; ++index) if (profile->axes[index].axis == axis) return &profile->axes[index];
    return nullptr;
}
inline double definition_output_value(const definition_axis& mapping, double position, bool axis_source = false) {
    if(axis_source && mapping.axis_input_response_count) {
        size_t part=0;
        while(part+2<mapping.axis_input_response_count && position>mapping.axis_input_response[part+1])++part;
        position=(part+(position-mapping.axis_input_response[part])/(mapping.axis_input_response[part+1]-mapping.axis_input_response[part]))/(mapping.axis_input_response_count-1);
    }
    if (!mapping.output_count) return mapping.output_minimum + position * (mapping.output_maximum - mapping.output_minimum);
    size_t part = 0;
    while (part + 2 < mapping.output_count && position > mapping.output_positions[part + 1]) ++part;
    const auto fraction = (position - mapping.output_positions[part]) / (mapping.output_positions[part + 1] - mapping.output_positions[part]);
    return mapping.output_values[part] + fraction * (mapping.output_values[part + 1] - mapping.output_values[part]);
}
inline double definition_input_position(const definition_axis& mapping, double raw, bool axis_source = false) {
    if (!mapping.output_count) {
        double position=(raw-mapping.output_minimum)/(mapping.output_maximum-mapping.output_minimum);
        if(axis_source && mapping.axis_input_response_count && position>=0 && position<=1) {
            const double segment=position*(mapping.axis_input_response_count-1);
            const size_t part=segment>=mapping.axis_input_response_count-1?mapping.axis_input_response_count-2:static_cast<size_t>(segment);
            position=mapping.axis_input_response[part]+(segment-part)*(mapping.axis_input_response[part+1]-mapping.axis_input_response[part]);
        }
        return position;
    }
    size_t part = 0;
    const bool increasing = mapping.output_maximum > mapping.output_minimum;
    while (part + 2 < mapping.output_count && (increasing ? raw > mapping.output_values[part + 1] : raw < mapping.output_values[part + 1])) ++part;
    const auto fraction = (raw - mapping.output_values[part]) / (mapping.output_values[part + 1] - mapping.output_values[part]);
    return mapping.output_positions[part] + fraction * (mapping.output_positions[part + 1] - mapping.output_positions[part]);
}
inline bool has_cockpit_references(const definition_profile* profile) {
    if (profile) for (size_t index = 0; index < profile->count; ++index) if (profile->axes[index].cockpit_reference) return true;
    return false;
}
}
