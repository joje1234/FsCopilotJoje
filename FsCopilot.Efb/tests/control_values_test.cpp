#include <cassert>
#include <limits>
#include <iostream>
#include "../Wasm/control_pickup.h"
using namespace fsc_controls;

double raw_target(size_t axis, double normalized) {
    double raw = axes[axis].signed_target ? normalized * 2 - 1 : normalized;
    if (axis < 3) raw *= 16384;
    if (axis >= steering_axis_count) raw *= 100;
    return raw;
}
int main() {
    const double bad[] = {std::numeric_limits<double>::quiet_NaN(), std::numeric_limits<double>::infinity(),
        -std::numeric_limits<double>::infinity(), -100, 100, -1e200, 1e200};
    for (unsigned engines = 1; engines <= 4; ++engines) for (size_t axis = 0; axis < axis_count; ++axis) for (auto value : bad) {
        control_pickup pickup; double values[target_count]{};
        values[axis_count] = engines;
        for (size_t i = 0; i < axis_count; ++i) values[i] = raw_target(i, .5);
        pickup.update(values, 1, false, false, true);
        uint32_t schema = 0; for (size_t i = 0; i < axis_count; ++i) if (pickup.required_axis(i)) schema |= 1u << i;
        values[axis] = raw_target(axis, value); pickup.update(values, 2, false, false, true);
        assert(pickup.targets[axis] == -1 && !pickup.valid_target(axis));
        for (size_t i = 0; i < axis_count; ++i) if (i != axis) assert(pickup.targets[i] == .5);
        uint32_t after = 0; for (size_t i = 0; i < axis_count; ++i) if (pickup.required_axis(i)) after |= 1u << i;
        assert(schema == after && pickup.engines == engines);
        values[axis] = raw_target(axis, .75); pickup.update(values, 3, false, false, true);
        assert(pickup.valid_target(axis) && pickup.targets[axis] == .75);
    }
    for (size_t axis = 0; axis < axis_count; ++axis) {
        control_pickup pickup; pickup.engines = 4;
        auto& input = pickup.inputs[axis]; input.calibrated = true; input.candidates = input.saved_candidates = 1;
        pickup.sample(axis, INT32_MAX, 1, "AXIS_TEST");
        assert(input.calibrated && input.saved_candidates == 1 && pickup.input_position(axis) < 0);
        pickup.sample(axis, 0, 2, "AXIS_TEST");
        assert(input.calibrated && !input.saved_candidates && pickup.input_position(axis) == .5);
        pickup.sample(axis, 16384, 3, "OTHER_SET");
        assert(input.calibrated && input.raw == 0 && input.observed == 2);
    }
    control_pickup layout; double values[target_count]{}; values[axis_count] = 2;
    layout.update(values, 1, false, false, true);
    for (auto invalid : {0., 1.5, 5., std::numeric_limits<double>::quiet_NaN()}) {
        values[axis_count] = invalid; layout.update(values, 2, false, false, true); assert(layout.engines == 2);
    }
    std::cout << "PASS all 21 control slots / 1-4 engines: invalid readings isolated, recovery current, schema and learned hardware retained.\n";
}
