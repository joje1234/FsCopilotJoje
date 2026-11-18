#include <cassert>
#include <iostream>
#include <limits>
#include "../Wasm/control_output.h"
#include "../Wasm/control_events.h"
using namespace fsc_controls;
int main()
{
    for (unsigned candidate = 0; candidate < 10; ++candidate) for (unsigned point = 0; point <= 100; ++point) {
        int32_t raw = 0; const double desired = point / 100.0;
        assert(output_raw(desired, 1u << candidate, raw));
        assert(std::abs(control_pickup::prediction(raw, candidate) - desired) <= 0.0051);
    }
    int32_t raw = 0;
    assert(!output_raw(std::numeric_limits<double>::quiet_NaN(), 1, raw));
    assert(!output_raw(1.01, 1, raw) && !output_raw(-0.01, 1, raw));
    assert(!output_raw(0.5, 3, raw) && !output_raw(0.5, 0, raw) && !output_raw(0.5, 1024, raw));
    control_pickup pickup; pickup.engines = 2;
    const char* names[] = {"AXIS_AILERONS_SET", "AXIS_ELEVATOR_SET", "AXIS_RUDDER_SET", "AXIS_STEERING_SET", "AXIS_LEFT_BRAKE_SET", "AXIS_RIGHT_BRAKE_SET",
        "AXIS_ELEV_TRIM_SET", "AILERON_TRIM_SET", "RUDDER_TRIM_SET", "THROTTLE_AXIS_SET_EX1", "PROP_PITCH_AXIS_SET_EX1", "AXIS_MIXTURE_SET",
        "THROTTLE_AXIS_SET_EX1", "PROP_PITCH_AXIS_SET_EX1", "AXIS_MIXTURE_SET"};
    for (size_t axis = 0; axis < 15; ++axis) {
        pickup.inputs[axis].calibrated = true; pickup.inputs[axis].candidates = axis < 2 ? 32 : 1;
        pickup.inputs[axis].detected = axis < steering_axis_count;
        std::snprintf(pickup.inputs[axis].event, sizeof(pickup.inputs[axis].event), "%s", names[axis]);
    }
    double positions[axis_count]{};
    for (size_t axis = 0; axis < axis_count; ++axis) positions[axis] = axis / 21.0;
    output_action actions[axis_count]{}; size_t count = 0;
    const uint32_t steering = (1u << steering_axis_count) - 1;
    const uint32_t quadrant = ((1u << 6) - 1) << steering_axis_count;
    assert(group_output(pickup, group::steering, steering, positions, actions, count) && count == 9);
    assert(actions[0].raw == 16384); // Captured inverted yoke mapping; never guess its direction.
    assert(!group_output(pickup, group::steering, steering & ~(1u << 2), positions, actions, count) && !count);
    assert(group_output(pickup, group::quadrant, quadrant, positions, actions, count) && count == 6);
    assert(!std::strcmp(actions[0].event, "THROTTLE1_AXIS_SET_EX1"));
    assert(!std::strcmp(actions[2].event, "AXIS_MIXTURE1_SET") && !std::strcmp(actions[5].event, "AXIS_MIXTURE2_SET"));
    assert(actions[2].raw != actions[5].raw); // Engine values remain independent.
    for (const auto target : {group::throttle, group::propeller, group::mixture}) {
        const unsigned offset = static_cast<unsigned>(target) - static_cast<unsigned>(group::throttle);
        const uint32_t pair = (1u << (9 + offset)) | (1u << (12 + offset));
        assert(group_output(pickup, target, pair, positions, actions, count) && count == 2);
        assert(actions[0].raw != actions[1].raw);
        assert(!group_output(pickup, target, pair | (1u << (9 + (offset + 1) % 3)), positions, actions, count) && !count);
        assert(!group_output(pickup, target, pair & ~(1u << (12 + offset)), positions, actions, count) && !count);
    }
    assert(!group_output(pickup, group::all, steering | quadrant, positions, actions, count) && !count);
    assert(group_output(pickup, group::quadrant, quadrant, positions, actions, count) && count == 6);
    for (size_t item = 0; item < count; ++item) {
        bool registered = false;
        for (const auto& event : events) if (!std::strcmp(actions[item].event, event.name)) registered = true;
        assert(registered);
    }
    assert(!group_output(pickup, group::quadrant, quadrant | 1u, positions, actions, count) && !count);
    assert(!group_output(pickup, group::quadrant, quadrant & ~(1u << 14), positions, actions, count) && !count);
    pickup.inputs[14].calibrated = false;
    assert(!group_output(pickup, group::quadrant, quadrant, positions, actions, count) && !count);
    pickup.inputs[14].calibrated = true;
    std::snprintf(pickup.inputs[14].event, sizeof(pickup.inputs[14].event), "%s", "AXIS_MIXTURE1_SET");
    assert(!group_output(pickup, group::quadrant, quadrant, positions, actions, count));
    std::cout << "Output mapping: 1,010 inverse-scale/curve samples, captured yoke direction, rudder/brakes, independent engine aliases, complete frames and atomic invalid-frame rejection passed. No simulator writes.\n";
}
