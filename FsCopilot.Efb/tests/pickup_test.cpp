#include <cassert>
#include <iostream>
#include "../Wasm/control_pickup.h"
using namespace fsc_controls;

int main()
{
    control_pickup pickup;
    assert(pickup.requests[0].tolerance == 0.10 && pickup.requests[1].tolerance == 0.10);
    // Alignment uses the requested ten-percent band, independently of the
    // learner's scale-validation precision.
    pickup.inputs[0].calibrated = true;
    pickup.inputs[0].candidates = 1;
    pickup.sample(0, 2949, 1); // About 59%, target 50%: within 10%, outside 3%.
    pickup.target_time = 1;
    pickup.targets[0] = 0.5;
    assert(pickup.aligned(0, 1));
    pickup.sample(0, 3604, 1); // About 61%, outside 10%.
    assert(!pickup.aligned(0, 1));
    pickup.inputs[0] = pickup_axis{};
    double values[target_count]{};
    values[axis_count] = 2;
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) {
        values[axis] = 50;
        pickup.sample(axis, 0, 9);
    }
    pickup.update(values, 9.2, false, false, false);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) {
        values[axis] = 75;
        pickup.sample(axis, 8192, 10);
    }
    for (unsigned frame = 0; frame < 3; ++frame) pickup.update(values, 10.2 + frame * 0.05, false, false, false);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) assert(pickup.inputs[axis].calibrated);
    command request{};
    request.operation = action::set;
    request.target = group::quadrant;
    pickup.detach(group::quadrant, 11);
    pickup.arm(request);
    assert(!pickup.ready(group::quadrant, 11)); // Pre-detach samples are insufficient.
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 5; ++axis) pickup.sample(axis, 8192, 11.1);
    pickup.sample(steering_axis_count + 5, 0, 11.1);
    pickup.update(values, 11.2, true, true, false);
    assert(!pickup.ready(group::quadrant, 11.2)); // One unmatched mixture blocks the whole group.
    pickup.sample(steering_axis_count + 5, 8192, 11.3);
    assert(!pickup.ready(group::quadrant, 11.3));
    pickup.update(values, 11.61, true, true, false);
    assert(pickup.ready(group::quadrant, 11.61));
    values[steering_axis_count] = 25;
    pickup.update(values, 11.62, true, true, false);
    assert(!pickup.ready(group::quadrant, 11.62)); // Moving target revokes readiness.
    pickup.observe("AXIS_THROTTLE_SET", 8192, 11.7);
    assert(!pickup.ready(group::quadrant, 11.7)); // A combined lever cannot match split targets.
    assert(values[steering_axis_count] == 25 && values[steering_axis_count + 3] == 75);
    values[steering_axis_count] = 75;
    pickup.update(values, 12, true, true, false);
    assert(!pickup.ready(group::quadrant, 12));
    pickup.update(values, 12.31, true, true, false);
    assert(pickup.ready(group::quadrant, 12.31));
    assert(!pickup.ready(group::quadrant, 14)); // Missing target stream.
    pickup.update(values, 50, true, true, false);
    assert(!pickup.ready(group::quadrant, 50)); // Old input stream.
    pickup.cancel(group::quadrant);
    assert(!pickup.requests[1].waiting);

    // Returning to Me must not erase calibration when read-back lags behind the
    // newest hardware event. Learned means learned until explicit reset.
    const unsigned learned_scale = pickup.inputs[steering_axis_count].candidates;
    for (unsigned handover = 0; handover < 5; ++handover) {
        const double now = 51 + handover;
        pickup.sample(steering_axis_count, 8192, now);
        values[steering_axis_count] = 10; // Delayed/inconsistent target frame.
        pickup.update(values, now + 0.2, false, false, false);
        assert(pickup.inputs[steering_axis_count].calibrated);
        assert(pickup.inputs[steering_axis_count].candidates == learned_scale);
        assert(pickup.input_position(steering_axis_count) == 0.75);
        pickup.detach(group::quadrant, now + 0.3);
        pickup.sample(steering_axis_count, 0, now + 0.4);
        pickup.update(values, now + 0.5, false, true, false);
        assert(pickup.inputs[steering_axis_count].calibrated);
    }

    // Actual captured mixture pair: AXIS_MIXTURE_SET=2432, cockpit=57.03125%.
    // A bad initial pair and small ADC chatter must not poison future learning.
    control_pickup mixture;
    double mixture_values[target_count]{};
    mixture_values[axis_count] = 2;
    mixture.observe("AXIS_MIXTURE_SET", 2432, 1);
    mixture.update(mixture_values, 1.2, false, false, false); // No scale fits yet.
    assert(!mixture.inputs[11].calibrated && mixture.inputs[11].candidates != 0);
    mixture_values[11] = mixture_values[14] = 57.03125;
    for (unsigned frame = 0; frame < 3; ++frame) mixture.update(mixture_values, 1.3 + frame * 0.05, false, false, false);
    assert(!mixture.inputs[11].calibrated); // One position cannot establish a scale.
    mixture.observe("AXIS_MIXTURE_SET", 8192, 2);
    mixture_values[11] = mixture_values[14] = 75;
    for (unsigned frame = 0; frame < 3; ++frame) {
        mixture.observe("AXIS_MIXTURE_SET", frame % 2 ? 8064 : 8192, 2.2 + frame * 0.05);
        mixture.update(mixture_values, 2.2 + frame * 0.05, false, false, false);
    }
    assert(mixture.inputs[11].calibrated && mixture.inputs[14].calibrated);
    assert(std::abs(mixture.input_position(11) - 0.75) < 0.01);
    mixture_values[11] = mixture_values[14] = 0; // Late read-back after handover.
    mixture.update(mixture_values, 2.4, false, false, false);
    assert(mixture.inputs[11].calibrated && mixture.inputs[14].calibrated);
    assert(control_pickup::prediction(16385, 0) == 1); // Integer endpoint overshoot.
    mixture_values[2] = -16384.00031; // Actual captured rudder conversion error.
    mixture.update(mixture_values, 2.5, false, false, false);
    assert(mixture.targets[2] == 0);

    // Paired streams for one throttle: an axis command uses signed 16k, then an
    // aircraft SET echo reports unsigned 16k. Echoes must not reset the learner
    // on every movement or replace a learned physical input while masked.
    control_pickup throttle;
    double throttle_values[target_count]{};
    throttle_values[axis_count] = 2;
    auto throttle_position = [&](int32_t raw, int32_t echo, double percent, double now) {
        throttle.observe("AXIS_THROTTLE_SET", raw, now);
        throttle.observe("THROTTLE1_SET", echo, now + 0.01);
        throttle.observe("THROTTLE2_SET", echo, now + 0.01);
        throttle_values[9] = throttle_values[12] = percent;
        for (unsigned frame = 0; frame < 3; ++frame) throttle.update(throttle_values, now + 0.2 + frame * 0.05, false, false, false);
    };
    throttle_position(-8192, 4096, 25, 1);
    throttle_position(4864, 10624, 64.84375, 2); // Captured SET echo value.
    assert(throttle.inputs[9].calibrated && throttle.inputs[12].calibrated);
    assert(!std::strcmp(throttle.inputs[9].event, "AXIS_THROTTLE_SET"));
    assert(throttle.inputs[9].raw == 4864 && throttle.inputs[9].observed == 2);
    throttle.detach(group::quadrant, 3);
    throttle.observe("THROTTLE1_SET", 10624, 3.1);
    assert(!throttle.aligned(9, 3.1)); // A cockpit echo cannot masquerade as new hardware input.
    throttle.observe("AXIS_THROTTLE_SET", 4864, 3.2);
    assert(throttle.aligned(9, 3.2));
    throttle.observe("THROTTLE1_SET", 4096, 34);
    assert(!throttle.aligned(9, 34)); // Nor can echoes keep old hardware readings fresh.
    assert(throttle.inputs[9].calibrated && throttle.inputs[9].raw == 4864);
    control_pickup unsigned_throttle;
    unsigned_throttle.observe("THROTTLE1_SET", 4096, 1);
    throttle_values[9] = 25;
    unsigned_throttle.update(throttle_values, 1.2, false, false, false);
    unsigned_throttle.observe("THROTTLE1_SET", 12288, 2);
    throttle_values[9] = 75;
    for (unsigned frame = 0; frame < 3; ++frame) unsigned_throttle.update(throttle_values, 2.2 + frame * 0.05, false, false, false);
    assert(unsigned_throttle.inputs[9].calibrated && unsigned_throttle.inputs[9].candidates == 2);

    // Live capture: EX1 alternates 9856/9984, followed by unsigned SET echoes
    // 13120/13184; cockpit read-back alternates 80.4688/80.0781%. The old
    // source-switch reset kept the settle timer and all evidence at zero.
    control_pickup captured_throttle;
    for (unsigned frame = 0; frame < 20; ++frame) {
        const double now = 1 + frame * 0.05;
        captured_throttle.observe("THROTTLE_AXIS_SET_EX1", frame % 2 ? 9984 : 9856, now);
        captured_throttle.observe("THROTTLE1_SET", frame % 2 ? 13184 : 13120, now);
        captured_throttle.observe("THROTTLE2_SET", frame % 2 ? 13184 : 13120, now);
        throttle_values[9] = throttle_values[12] = frame % 2 ? 80.0781 : 80.4688;
        captured_throttle.update(throttle_values, now, false, false, false);
    }
    assert(captured_throttle.inputs[9].confirmations >= 3 && captured_throttle.inputs[9].has_evidence);
    assert(!captured_throttle.inputs[9].calibrated); // Only one captured position, so still needs travel.
    captured_throttle.observe("THROTTLE_AXIS_SET_EX1", 0, 3);
    captured_throttle.observe("THROTTLE1_SET", 8192, 3);
    captured_throttle.observe("THROTTLE2_SET", 8192, 3);
    throttle_values[9] = throttle_values[12] = 50;
    for (unsigned frame = 0; frame < 3; ++frame) captured_throttle.update(throttle_values, 3.2 + frame * 0.05, false, false, false);
    assert(captured_throttle.inputs[9].calibrated && captured_throttle.inputs[12].calibrated);

    // At centre, signed 16k and signed percent scales agree. That is not yet
    // a learned mapping, even though both currently produce the right value.
    control_pickup roll;
    double roll_values[target_count]{};
    roll.sample(0, 0, 1);
    for (unsigned frame = 0; frame < 3; ++frame) roll.update(roll_values, 1.2 + frame * 0.05, false, false, false);
    assert(!roll.inputs[0].calibrated && roll.input_position(0) < 0);
    roll.sample(0, 8192, 2);
    roll_values[0] = 8192;
    for (unsigned frame = 0; frame < 3; ++frame) roll.update(roll_values, 2.2 + frame * 0.05, false, false, false);
    assert(roll.inputs[0].calibrated);
    roll.sample(0, 100, 3);
    roll_values[0] = -8192; // A mismatched read-back cannot undo learning.
    roll.update(roll_values, 3.2, false, false, false);
    assert(roll.inputs[0].calibrated);
    assert(std::abs(roll.input_position(0) - (0.5 + 100.0 / 32768)) < 0.00001);

    control_pickup unknown;
    unknown.update(values, 1, true, true, false);
    assert(!unknown.ready(group::quadrant, 1)); // No bound axes is not automatic success.
    unknown.sample(0, 8192, 1);
    values[0] = 8192;
    unknown.update(values, 1.2, false, false, false);
    unknown.detach(group::steering, 2);
    unknown.sample(0, 8192, 2.1);
    values[axis_count + 1] = 1;
    unknown.update(values, 2.2, true, true, false);
    assert(!unknown.ready(group::steering, 3)); // Autopilot is not a handover target.
    values[axis_count + 1] = 0;
    unknown.update(values, 3, true, true, false);
    unknown.inputs[0].candidates = 0;
    assert(!unknown.ready(group::steering, 3)); // Unsupported mapping stays detached.
    // Rudder and both independent toe brakes are part of the same Steering
    // group. Only roll/pitch gate pickup; pedals still transfer, no quadrant transfer.
    control_pickup pedals;
    double pedal_values[target_count]{}; pedal_values[axis_count] = 2;
    const char* pedal_events[] = {"AXIS_AILERONS_SET", "AXIS_ELEVATOR_SET", "AXIS_RUDDER_SET", "AXIS_LEFT_BRAKE_SET", "AXIS_RIGHT_BRAKE_SET"};
    const size_t pedal_axes[] = {0, 1, 2, 4, 5};
    for (unsigned position = 0; position < 2; ++position) {
        const double now = 1 + position;
        for (unsigned item = 0; item < 5; ++item) {
            const bool brake = item >= 3;
            const int raw = brake ? (position ? 12288 : 4096) : (position ? 8192 : -8192);
            pedals.observe(pedal_events[item], raw, now);
            pedal_values[pedal_axes[item]] = brake ? raw / 16384.0 : raw;
        }
        for (unsigned sample = 0; sample < 3; ++sample) pedals.update(pedal_values, now + 0.2 + sample * 0.05, false, false, false);
    }
    for (const auto axis : pedal_axes) assert(pedals.required_axis(axis) && pedals.inputs[axis].calibrated);
    request.target = group::steering;
    pedals.detach(group::steering, 3); pedals.arm(request);
    pedals.requests[1].waiting = true;
    for (const auto axis : pedal_axes) pedal_values[axis] = 0;
    pedals.observe("AXIS_AILERONS_SET", 8192, 3.1); pedals.observe("AXIS_ELEVATOR_SET", 0, 3.1);
    pedals.observe("AXIS_RUDDER_SET", 8192, 3.1);
    pedals.observe("AXIS_LEFT_BRAKE_SET", 0, 3.1); pedals.observe("AXIS_RIGHT_BRAKE_SET", 0, 3.1);
    pedals.update(pedal_values, 3.2, true, false, false);
    assert(!pedals.ready(group::steering, 3.2)); // Off-centre roll still blocks the group.
    pedals.observe("AXIS_AILERONS_SET", 0, 3.3);
    assert(!pedals.ready(group::steering, 3.3)); // Starts, rather than bypasses, the hold.
    pedals.observe("AXIS_LEFT_BRAKE_SET", 8192, 3.4);
    assert(!pedals.ready(group::steering, 3.4)); // Yoke alignment hold still applies.
    pedals.observe("AXIS_RIGHT_BRAKE_SET", 8192, 3.5);
    assert(!pedals.ready(group::steering, 3.5));
    assert(pedals.ready(group::steering, 3.7)); // Mismatched rudder and both brakes never reset the yoke hold.
    pedals.update(pedal_values, 4.01, true, false, false);
    assert(pedals.ready(group::steering, 4.01) && pedals.requests[1].waiting);
    control_pickup variants;
    variants.observe("AXIS_SENSOR_RUDDER_SET", -8192, 1);
    variants.observe("AXIS_LEFT_BRAKE_SET", 4096, 1);
    variants.observe("AXIS_RIGHT_BRAKE_SET", 8192, 1);
    assert(variants.inputs[2].detected && variants.inputs[2].raw == -8192);
    assert(variants.inputs[4].detected && variants.inputs[5].detected);
    std::cout << "Pickup steering/pedal/brake hold, throttle stream isolation, ten-percent tolerance, learning persistence/recovery, mixtures, chatter, endpoints and stale-data checks passed.\n";
}
