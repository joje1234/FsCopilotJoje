#pragma once
#include <MSFS/MSFS_Vars.h>
#include "control_targets.h"
#include "control_definition_profile.h"

namespace fsc_controls {
// Resolve existing variables only. Registering an LVar through a SimConnect
// definition can redefine its units; this getter never registers or writes one.
inline void read_cockpit_references(double* values, const definition_profile* profile) {
    if (!profile) return;
    for (size_t index = 0; index < profile->count; ++index) {
        const auto& axis = profile->axes[index];
        if (!axis.cockpit_reference || axis.axis >= axis_count) continue;
        double value = std::numeric_limits<double>::quiet_NaN();
        const auto unit = fsVarsGetUnitId(axis.read_units);
        if (unit != FS_INVALID_UNIT && !std::strncmp(axis.read, "L:", 2)) {
            const auto variable = fsVarsGetLVarId(axis.read + 2);
            if (variable != FS_VAR_INVALID_ID && fsVarsLVarGet(variable, unit, &value) != FS_VAR_ERROR_NONE) value = std::numeric_limits<double>::quiet_NaN();
        } else if (unit != FS_INVALID_UNIT && !std::strncmp(axis.read, "A:", 2)) {
            const auto variable = fsVarsGetAVarId(axis.read + 2);
            if (variable != FS_VAR_INVALID_ID && fsVarsAVarGet(variable, unit, FsVarParamArray{}, &value) != FS_VAR_ERROR_NONE) value = std::numeric_limits<double>::quiet_NaN();
        }
        const double normalized = (value - axis.minimum) / (axis.maximum - axis.minimum);
        values[axis.axis] = std::isfinite(normalized) && normalized >= -.001 && normalized <= 1.001 ?
            axis.axis >= steering_axis_count ? normalized * 100 : axis.axis < 3 ? (normalized * 2 - 1) * 16384 :
            axes[axis.axis].signed_target ? normalized * 2 - 1 : normalized : std::numeric_limits<double>::quiet_NaN();
    }
}
}
