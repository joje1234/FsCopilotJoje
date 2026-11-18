#pragma once
#include <cmath>
#include <limits>
#include "control_pickup.h"
#include "control_definition_profile.h"

namespace fsc_controls {
constexpr size_t sim_target_count = axis_count + 2; // Standard SimVars only; cockpit LVars use the native Vars API.
}
