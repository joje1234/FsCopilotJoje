#include <cassert>
#include <iostream>
#include "../Wasm/control_definition_output.h"
#include "../Wasm/control_cockpit_refs.h"
#include "../Wasm/control_runtime.h"
using namespace fsc_controls;
struct adapter { bool configure_mask(uint32_t, uint32_t) { return true; } void release() {} };
int main() {
    control_pickup p; p.definitions = &definition_profiles[0];
    adapter sdk; control_runtime<adapter> gate(p, sdk);
    assert(gate.initialize(true, true) && gate.confirm(gate.barrier, 1));
    double values[target_count]{}; values[axis_count] = 2;
    for (const auto axis : {0, 1, 9, 10, 11, 12, 13, 14}) {
        auto& input = p.inputs[axis]; input.calibrated = true; input.candidates = 1;
        std::snprintf(input.event, sizeof(input.event), "%s", axis == 0 ? "AXIS_AILERONS_SET" : axis == 1 ? "AXIS_ELEVATOR_SET" :
            (axis - 9) % 3 == 0 ? "THROTTLE_AXIS_SET_EX1" : (axis - 9) % 3 == 1 ? "PROP_PITCH_AXIS_SET_EX1" : "AXIS_MIXTURE_SET");
        p.sample(axis, 0, 1);
    }
    fake_vars::state() = fake_vars::registry{};
    values[4] = values[5] = 1; // Parking brake affects ordinary readings.
    read_cockpit_references(values, p.definitions); gate.update_targets(values, 1, false);
    assert(p.targets[4] == 0 && p.targets[5] == 0); // EX1 demand excludes parking.
    uint32_t required = 0, learned = 0;
    for (size_t axis = 0; axis < axis_count; ++axis) { if (p.required_axis(axis)) required |= 1u << axis; if (p.axis_known(axis)) learned |= 1u << axis; }
    assert(required == 0x7e3f && (learned & required) == required);
    for (size_t axis = 2; axis < 6; ++axis) assert(p.required_axis(axis) && p.axis_known(axis));
    for (size_t axis = 6; axis < 9; ++axis) assert(!p.required_axis(axis));
    for (const auto axis : {2, 4, 5}) assert(p.relative_input(axis));
    p.observe("RUDDER_AXIS_MINUS", 15602, 1.1); p.observe("BRAKES", 0, 1.1);
    assert(p.relative_input(2) && p.relative_input(4) && p.relative_input(5));
    assert(gate.assign_mock(select_steering, 1.2) && gate.confirm(gate.barrier, 1.2));
    for(const auto& binding:events)assert(!std::strstr(binding.name,"TRIM")); // No trim input is registered in a blockable group.
    unsigned shared_trims=0;
    for(size_t i=0;i<p.definitions->cockpit_count;++i) {
        const auto& d=p.definitions->cockpit[i];
        if(!std::strcmp(d.name,"A:AILERON TRIM PCT")||!std::strcmp(d.name,"A:ELEVATOR TRIM PCT")||!std::strcmp(d.name,"A:RUDDER TRIM PCT")) {assert(d.shared);++shared_trims;}
    }
    assert(shared_trims==3);
    p.sample(0, 0, 1.25); p.sample(1, 0, 1.25);
    gate.update_targets(values, 1.3, false);
    for (const auto axis : {2, 4, 5}) assert(p.input_position(axis) < 0); // No fabricated blocked hardware pose.
    assert(gate.aligned(select_steering, 1.3));
    values[2] = 8192; gate.update_targets(values, 1.4, false);
    assert(!gate.axis_aligned(2, 1.4) && gate.aligned(select_steering, 1.4)); // Rudder pose is informational, never a takeover gate.
    values[2] = 0; values[4] = .5; gate.update_targets(values, 1.5, false);
    assert(!gate.axis_aligned(4, 1.5) && gate.aligned(select_steering, 1.5)); // Brake demand need not match for takeover.
    p.observe("AXIS_LEFT_BRAKE_SET", 0, 1.6);
    assert(p.relative_input(4) && p.input_position(4) < 0 && p.axis_known(4)); // Masked hardware is not a fabricated absolute demand.
    gate.update_targets(values, 1.7, false); assert(!gate.axis_aligned(4, 1.7) && gate.aligned(select_steering, 1.7));
    p.observe("BRAKES", 0, 1.8); assert(p.relative_input(4) && p.relative_input(5)); // Both use the unmasked cockpit demand when owned.
    p.observe("AXIS_RUDDER_SET", 8192, 1.8); assert(p.axis_known(2));
    p.observe("AXIS_RIGHT_BRAKE_SET", -16384, 1.8); assert(p.axis_known(5));
    p.observe("AXIS_RIGHT_BRAKE_SET", 0, 1.9); assert(!p.output_response_axes);
    p.observe("AXIS_ELEV_TRIM_SET", 12000, 1.9); p.observe("AILERON_TRIM_SET", 40, 1.9); p.observe("RUDDER_TRIM_SET_EX1", 8192, 1.9);
    output_action brake{}; assert(output_for_axis(p, 5, .01, brake) && brake.raw == -15360);
    assert(std::abs(control_pickup::prediction(brake.raw, 4) - .01) < .000001);
    uint32_t after = 0; for (size_t axis = 0; axis < axis_count; ++axis) if (p.required_axis(axis)) after |= 1u << axis;
    assert(after == required); // Late first-axis discovery cannot change the shared schema.
    double positions[axis_count]{}; positions[0] = positions[1] = .5; positions[2] = .25; positions[4] = .5; positions[5] = 1;
    output_action plan[axis_count]{}; size_t count = 0;
    positions[3] = positions[6] = positions[7] = positions[8] = .5;
    assert(group_output(p, group::steering, 0x3f, positions, plan, count) && count == 6);
    assert(plan[2].raw == 8192 && plan[4].raw == 0 && plan[5].raw == 16384);
    p.output_response_axes = 1u << 4;
    assert(output_for_axis(p, 4, .5, brake) && std::abs(control_pickup::prediction(brake.raw, 4) - .5) < .0001);
    // Neither absolute pedals nor undetected optional trim may stop a yoke-aligned pickup.
    p.sample(0, 16384, 2); p.sample(1, 16384, 2);
    gate.update_targets(values, 2, false); assert(!gate.aligned(select_steering, 2));
    p.sample(0, 0, 2.1); p.sample(1, 0, 2.1); gate.update_targets(values, 2.1, false);
    assert(gate.request_me(select_steering)); gate.tick(2.1);
    gate.update_targets(values, 2.5, false); gate.tick(2.5);
    assert(gate.applying && gate.confirm(gate.barrier, 2.51) && !(gate.blocked & select_steering));
    for (const auto* name : {"BRAKES", "BRAKES_LEFT", "BRAKES_RIGHT", "AXIS_LEFT_BRAKE_SET", "AXIS_RIGHT_BRAKE_SET", "RUDDER_AXIS_MINUS", "RUDDER_AXIS_PLUS"}) {
        bool found = false; for (const auto& binding : events) if (!std::strcmp(name, binding.name)) found = binding.target == group::steering;
        assert(found); // Both button forms, axes and relative rudder belong to the mask.
    }
    std::cout << "Mixed brake/rudder contracts passed: fixed SDK routes, vanilla affine output, parking-independent demand, both/individual buttons, absolute pedals without learning, neutral relative takeover, no reflected hardware pose and stable shared schema. SDK simulated.\n";
}
