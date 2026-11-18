#include <cassert>
#include <iostream>
#include "../Wasm/control_cockpit_refs.h"
using namespace fsc_controls;
void apply_cockpit_references(double* values, bool selected) {
    fake_vars::state().value[0] = values[axis_count + 2];
    fake_vars::state().value[1] = values[axis_count + 3];
    read_cockpit_references(values, selected ? &definition_profiles[0] : nullptr);
}

int main()
{
    assert(find_definition_profile("simobjects/airplanes/bksq-aircraft-baronpropress/aircraft.cfg"));
    assert(!find_definition_profile("simobjects/airplanes/other-baron/aircraft.cfg"));
    double values[target_count]{};
    values[axis_count] = 2;
    values[11] = values[14] = 75.51088; // Actual full-rich capture from the generic engine SimVars.
    values[axis_count + 2] = values[axis_count + 3] = 100; // Live 58P full-rich readback, 2026-11-16.
    apply_cockpit_references(values, false);
    assert(values[11] == 75.51088); // No global change for other aircraft.
    apply_cockpit_references(values, true);
    assert(values[11] == 100 && values[14] == 100);
    values[11] = values[14] = 40.11333; // Actual midpoint capture.
    values[axis_count + 2] = values[axis_count + 3] = 53.125;
    apply_cockpit_references(values, true);
    assert(values[11] == 53.125 && values[14] == 53.125);

    control_pickup pickup;
    pickup.observe("AXIS_MIXTURE_SET", 1024, 1); // Signed 16k position corresponding to 53.125%.
    for (unsigned frame = 0; frame < 3; ++frame) pickup.update(values, 1.2 + frame * 0.05, false, false, false);
    assert(!pickup.inputs[11].calibrated); // Still requires separated evidence for a new scale.
    pickup.observe("AXIS_MIXTURE_SET", 8192, 2);
    values[11] = values[14] = 56.63316; // Engine correction must not affect pickup.
    values[axis_count + 2] = values[axis_count + 3] = 75;
    apply_cockpit_references(values, true);
    for (unsigned frame = 0; frame < 3; ++frame) pickup.update(values, 2.2 + frame * 0.05, false, false, false);
    assert(pickup.inputs[11].calibrated && pickup.inputs[14].calibrated);
    pickup.detach(group::quadrant, 3);
    pickup.observe("AXIS_MIXTURE_SET", 16384, 3.1);
    values[11] = values[14] = 75.51088;
    values[axis_count + 2] = values[axis_count + 3] = 100;
    apply_cockpit_references(values, true);
    pickup.update(values, 3.2, false, true, false);
    assert(pickup.aligned(11, 3.2) && pickup.aligned(14, 3.2));
    values[axis_count + 2] = 150; // Invalid adapter data must not approve pickup.
    apply_cockpit_references(values, true);
    pickup.update(values, 3.3, false, true, false);
    assert(!pickup.aligned(11, 3.3) && pickup.aligned(14, 3.3));
    values[axis_count + 2] = 0.5; // Half a percent is not half travel.
    apply_cockpit_references(values, true);
    assert(values[11] == 0.5);
    values[axis_count + 2] = 0;
    apply_cockpit_references(values, true);
    assert(values[11] == 0);
    std::cout << "Baron cockpit-mixture references, captured values, learning, pickup and invalid-reference checks passed.\n";
}
