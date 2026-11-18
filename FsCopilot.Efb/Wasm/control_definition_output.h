#pragma once
#include <MSFS/MSFS_Vars.h>
#include "control_sim_output.h"
#include "control_definition_profile.h"

namespace fsc_controls {
// Invoked only after control_remote_writer's existing session, owner, generation,
// sequence and hardware-mask checks. Profile selection never writes a control.
class control_definition_output : public control_sim_output {
public:
    static constexpr DWORD data_definition = 0x6f00;
    void initialize(HANDLE connection, DWORD first_event) {
        control_sim_output::initialize(connection, first_event); profile_ = nullptr;
        for (auto& mapped : data_mapped_) mapped = false;
    }
    bool select_profile(const definition_profile* profile) {
        if (profile_ == profile) return true;
        for (size_t axis = 0; axis < axis_count; ++axis) if (data_mapped_[axis]) {
            if (!record(SimConnect_ClearDataDefinition(connection_, data_definition + static_cast<DWORD>(axis)))) return false;
            data_mapped_[axis] = false;
        }
        profile_ = profile; return true;
    }
    bool write(const output_action* actions, size_t count) {
        if (!profile_) return control_sim_output::write(actions, count);
        if (!available || !actions || !count || count > axis_count || calls_ + count * 2 + 1 > capacity) return false;
        const definition_axis* mappings[axis_count]{};
        FsLVarId locals[axis_count]{}; FsUnitId units[axis_count]{};
        double values[axis_count]{}; output_action keys[axis_count]{}; size_t key_count = 0;
        uint32_t seen = 0; unsigned pair_type = 3, pair_count = 0;
        // Validate the complete group and resolve every existing LVar before
        // the first write. A setter failure stops output; there is no raw fallback.
        for (size_t item = 0; item < count; ++item) {
            const auto& action = actions[item];
            if (action.axis >= axis_count || seen & (1u << action.axis) || !std::isfinite(action.position) || action.position < 0 || action.position > 1) return false;
            seen |= 1u << action.axis;
            const auto* mapping = definition_for_axis(profile_, action.axis); mappings[item] = mapping;
            if (action.axis >= steering_axis_count) {
                const unsigned type = static_cast<unsigned>((action.axis - steering_axis_count) % 3);
                if (!mapping || (pair_type != 3 && pair_type != type)) return false;
                pair_type = type; ++pair_count;
            }
            if (!mapping) { keys[key_count++] = action; continue; }
            values[item] = definition_output_value(*mapping, action.position, action.axis_response);
            if (mapping->output_kind == 2) {
                units[item] = fsVarsGetUnitId(mapping->write_units);
                locals[item] = fsVarsGetLVarId(mapping->write + 2);
                double current = 0;
                if (units[item] == FS_INVALID_UNIT || locals[item] == FS_VAR_INVALID_ID ||
                    fsVarsLVarGet(locals[item], units[item], &current) != FS_VAR_ERROR_NONE || !std::isfinite(current) ||
                    current < mapping->minimum - .001 * (mapping->maximum - mapping->minimum) ||
                    current > mapping->maximum + .001 * (mapping->maximum - mapping->minimum)) { available = false; return false; }
            } else if (mapping->output_kind == 3) {
                auto& key = keys[key_count++]; key = action;
                std::snprintf(key.event, sizeof(key.event), "%s", mapping->write + 2);
                key.raw = static_cast<int32_t>(std::lround(values[item]));
            } else if (mapping->output_kind != 1) return false;
        }
        if (pair_count && (pair_count != profile_->engines || pair_count != count)) return false;
        for (size_t item = 0; item < key_count; ++item) {
            if (!std::memchr(keys[item].event, 0, sizeof(keys[item].event))) return false;
            bool known = false;
            for (const auto& event : events) if (!std::strcmp(event.name, keys[item].event)) { known = true; break; }
            if (!known) return false;
        }
        for (size_t item = 0; item < count; ++item) {
            const auto* mapping = mappings[item];
            if (mapping && mapping->output_kind == 1 && !data_mapped_[actions[item].axis]) {
                const auto id = data_definition + static_cast<DWORD>(actions[item].axis);
                if (!record(SimConnect_AddToDataDefinition(connection_, id, mapping->write + 2, mapping->write_units, SIMCONNECT_DATATYPE_FLOAT64), actions[item].axis, mapping->write, "Define SimVar")) return false;
                data_mapped_[actions[item].axis] = true;
            }
        }
        if (key_count && !control_sim_output::write(keys, key_count)) return false;
        for (size_t item = 0; item < count; ++item) {
            const auto* mapping = mappings[item];
            if (!mapping) continue;
            if (mapping->output_kind == 1) {
                if (!record(SimConnect_SetDataOnSimObject(connection_, data_definition + static_cast<DWORD>(actions[item].axis), SIMCONNECT_OBJECT_ID_USER, 0, 0, sizeof(double), &values[item]), actions[item].axis, mapping->write, "Set SimVar")) return false;
            } else if (mapping->output_kind == 2 && fsVarsLVarSet(locals[item], units[item], values[item]) != FS_VAR_ERROR_NONE) {
                available = false; return false;
            }
        }
        return checkpoint_if_needed();
    }
private:
    const definition_profile* profile_ = nullptr;
    bool data_mapped_[axis_count]{};
};
}
