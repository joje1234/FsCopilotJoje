#include <cassert>
#include <iostream>
#include "../Wasm/control_definition_output.h"
using namespace fsc_controls;

int main() {
    control_pickup pickup; pickup.engines = 2;
    const char* source[] = {"THROTTLE_AXIS_SET_EX1", "PROP_PITCH_AXIS_SET_EX1", "AXIS_MIXTURE_SET"};
    for (size_t axis = 9; axis < 15; ++axis) {
        pickup.inputs[axis].calibrated = true; pickup.inputs[axis].candidates = 1;
        std::snprintf(pickup.inputs[axis].event, sizeof(pickup.inputs[axis].event), "%s", source[(axis - 9) % 3]);
    }
    constexpr DWORD first = 0x100;
    for (size_t event = 0; event < event_count; ++event) SimConnect_MapClientEventToSimEvent(1, first + static_cast<DWORD>(event), events[event].name);
    control_definition_output output; output.initialize(1, first);
    assert(output.select_profile(&definition_profiles[0]));
    double positions[axis_count]{};
    for (size_t axis = 9; axis < 12; ++axis) positions[axis] = .25;
    for (size_t axis = 12; axis < 15; ++axis) positions[axis] = .75;
    output_action plan[axis_count]{}; size_t count = 0;
    fake_vars::state() = fake_vars::registry{};
    fake_vars::state().percent_storage[1] = false;
    assert(group_output(pickup, group::mixture, (1u << 11) | (1u << 14), positions, plan, count));
    assert(output.write(plan, count));
    assert(fake_vars::state().sets == 2 && fake_vars::state().value[0] == 25 && fake_vars::state().value[1] == .75);
    assert(fake_sim::transmissions.empty() && fake_sim::object_writes.empty());
    // Output uses normalized cockpit positions, even with inverse hardware.
    pickup.inputs[11].candidates = pickup.inputs[14].candidates = 32;
    assert(group_output(pickup, group::mixture, (1u << 11) | (1u << 14), positions, plan, count) && output.write(plan, count));
    assert(fake_vars::state().value[0] == 25 && fake_vars::state().value[1] == .75);
    auto invalid = plan[1]; plan[1].position = 1.01; const auto before = fake_vars::state().sets;
    assert(!output.write(plan, count) && fake_vars::state().sets == before); plan[1] = invalid;
    assert(!output.write(plan, 1) && fake_vars::state().sets == before); // Complete pair required.
    fake_vars::state().available[1] = false;
    assert(!output.write(plan, count) && fake_vars::state().sets == before && !output.available);
    fake_vars::state().available[1] = true; output.initialize(1, first); output.select_profile(&definition_profiles[0]);
    fake_vars::state().set_failed[1] = true;
    assert(!output.write(plan, count) && !output.available); // Partial API failure is a failure, never a fallback.
    fake_vars::state().set_failed[1] = false;
    output.initialize(1, first); output.select_profile(&definition_profiles[0]);
    assert(group_output(pickup, group::propeller, (1u << 10) | (1u << 13), positions, plan, count) && output.write(plan, count));
    assert(fake_sim::transmissions.size() == 2);
    assert(fake_sim::names[fake_sim::transmissions[0].event] == "PROP_PITCH1_SET" && fake_sim::transmissions[0].raw == 4096);
    assert(fake_sim::names[fake_sim::transmissions[1].event] == "PROP_PITCH2_SET" && fake_sim::transmissions[1].raw == 12288);
    assert(group_output(pickup, group::throttle, (1u << 9) | (1u << 12), positions, plan, count) && output.write(plan, count));
    assert(fake_sim::object_writes.size() == 2 && fake_sim::transmissions.size() == 2);
    double raw = 0; std::memcpy(&raw, fake_sim::object_writes[0].bytes.data(), sizeof(raw)); assert(raw == 4096);
    assert(fake_sim::definitions[control_definition_output::data_definition + 9][0].second == "position 16k");
    for (unsigned frame = 0; frame < 300; ++frame) {
        assert(output.write(plan, count));
        if (output.confirm(fake_sim::request_id)) fake_sim::request_id = 0;
    }
    assert(output.available); // SimVar-only output also retires packet checkpoints.
    // Verify the actual SDK event path, not merely the planned normalized value.
    pickup.definitions = &definition_profiles[0];
    for (const auto demand : {.011895, .014863, .013809, .27, .5, 1.}) {
        output_action brake{};
        assert(output_for_axis(pickup, 4, demand, brake) && !brake.axis_response && output.write(&brake, 1));
        const auto linear = fake_sim::transmissions.back();
        assert(fake_sim::names[linear.event] == "AXIS_LEFT_BRAKE_SET" && static_cast<int32_t>(linear.raw) == brake.raw);
        assert(std::abs((static_cast<int32_t>(linear.raw) + 16384) / 32768. - demand) < .00004);
        pickup.observe("AXIS_LEFT_BRAKE_SET", 0, 1);
        assert(output_for_axis(pickup, 4, demand, brake) && !brake.axis_response); // Hardware discovery never changes the output route.
        pickup.output_response_axes = 1u << 4;
        assert(output_for_axis(pickup, 4, demand, brake) && brake.axis_response && output.write(&brake, 1));
        const auto curved = static_cast<int32_t>(fake_sim::transmissions.back().raw);
        assert(std::abs(control_pickup::prediction(curved, 4) - demand) < .0001);
        pickup.inputs[4] = pickup_axis{};
        pickup.output_response_axes = 0;
    }
    const auto simvar_calls=fake_sim::object_writes.size();
    for(const auto position:{0.,.25,.49,.5,.51,.75,1.}) {
        output_action control{};assert(output_for_axis(pickup,3,position,control));
        assert(std::strcmp(control.event,"AXIS_STEERING_SET")==0);
        assert(std::abs(.5-control.raw/32768.-position)<.00002);
        assert(output.write(&control,1) && fake_sim::object_writes.size()==simvar_calls);
        assert(std::abs(.5-static_cast<int32_t>(fake_sim::transmissions.back().raw)/32768.-position)<.00002);
    }
    control_sim_output diagnostics;diagnostics.initialize(1,first);
    output_action tagged{};tagged.axis=3;std::snprintf(tagged.event,sizeof(tagged.event),"%s","AXIS_STEERING_SET");
    assert(diagnostics.write(&tagged,1));const auto old_packet=fake_sim::last_packet;
    const auto checkpoint=diagnostics.request_checkpoint();assert(checkpoint);
    tagged.axis=2;std::snprintf(tagged.event,sizeof(tagged.event),"%s","AXIS_RUDDER_SET");assert(diagnostics.write(&tagged,1));
    const auto tagged_packet=fake_sim::last_packet;assert(diagnostics.confirm(checkpoint));
    SIMCONNECT_RECV_EXCEPTION error{};error.dwSendID=old_packet;assert(!diagnostics.exception(error));
    error.dwSendID=tagged_packet;assert(diagnostics.exception(error) && diagnostics.failed_axis()==2);
    assert(!std::strcmp(diagnostics.failed_target(),"AXIS_RUDDER_SET") && !std::strcmp(diagnostics.failed_operation(),"Transmit event"));
    assert(output.select_profile(nullptr));
    assert(!fake_sim::definitions.count(control_definition_output::data_definition + 9));
    const auto events_before = fake_sim::transmissions.size();
    assert(output.write(plan, count) && fake_sim::transmissions.size() == events_before + 2);
    output.stop(); assert(!output.write(plan, count));
    std::cout << "Definition output passed: normalized typed LVars, inverse hardware, exact native setter routing, separate engine values, whole-pair validation, no duplicate output, preflight/partial failures, legacy fallback and sustained SimVar checkpoints. No simulator access.\n";
}
