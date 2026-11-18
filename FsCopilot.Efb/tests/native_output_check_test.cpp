#define _CRT_SECURE_NO_WARNINGS
#define FSC_PROFILE_ROOT "artifacts/efb-control-test/tests/native-output-"
#include <cassert>
#include <vector>
#include <iostream>
#include <thread>
#include "../Wasm/control_test.cpp"

namespace {
double cockpit[axis_count]{};
size_t events_applied = 0, objects_applied = 0;
bool left_axis_curve = false;
void confirm_mask();
const char aircraft[] = "SimObjects\\Airplanes\\bksq-aircraft-baronpropress\\aircraft.CFG";
void clean_profiles() {
    char identity[1024]{}; assert(profile_identity(aircraft, sizeof(aircraft), identity));
    for (const char* suffix : {"", "-output"}) for (unsigned slot = 0; slot < 2; ++slot) {
        char path[600]{};
        std::snprintf(path, sizeof(path), FSC_PROFILE_ROOT "pickup-%08x-2%s.%u.bin", profile_hash(identity, std::strlen(identity)), suffix, slot);
        std::remove(path);
    }
}
void open_module() {
    module_init(); SIMCONNECT_RECV opened{}; opened.dwID = SIMCONNECT_RECV_ID_OPEN; dispatch(&opened, sizeof(opened), nullptr);
    const char read[] = "5|native-check|1|read"; receive_command(read, sizeof(read), nullptr); confirm_mask();
    SIMCONNECT_RECV_SYSTEM_STATE identity{}; identity.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE; identity.dwRequestID = identity_request;
    std::snprintf(identity.szString, sizeof(identity.szString), "%s", aircraft);
    dispatch(&identity, sizeof(identity), nullptr); assert(aircraft_definitions);
}
void data(DWORD request, const double* values, size_t count) {
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + count * sizeof(double));
    auto* packet = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
    packet->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA; packet->dwRequestID = request; packet->dwDefineCount = static_cast<DWORD>(count);
    std::memcpy(&packet->dwData, values, count * sizeof(double)); dispatch(packet, static_cast<DWORD>(bytes.size()), nullptr);
}
void confirm_mask() {
    SIMCONNECT_RECV_SYSTEM_STATE packet{}; packet.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    packet.dwRequestID = first_barrier + gate.barrier; dispatch(&packet, sizeof(packet), nullptr);
}
void frame() {
    while (events_applied < fake_sim::transmissions.size()) {
        const auto& sent = fake_sim::transmissions[events_applied++];
        assert(sent.event >= first_event && sent.event < first_event + event_count);
        const auto* name = events[sent.event - first_event].name;
        if (!std::strcmp(name, "AXIS_AILERONS_SET")) cockpit[0] = control_pickup::prediction(static_cast<int32_t>(sent.raw), 0);
        else if (!std::strcmp(name, "AXIS_ELEVATOR_SET")) cockpit[1] = control_pickup::prediction(static_cast<int32_t>(sent.raw), 0);
        // Observed in MSFS 0.9.7: +328 yields 48.9990%, not 51.0010%.
        // Keep this SDK convention independent of the definition under test.
        else if (!std::strcmp(name, "AXIS_STEERING_SET")) cockpit[3] = .5 - static_cast<int32_t>(sent.raw) / 32768.;
        else {
            bool applied = false;
            for (const auto& axis : definition_axes_0) if (axis.output_kind == 3 && !std::strcmp(name, axis.write + 2)) {
                cockpit[axis.axis] = axis.axis == 5 || (axis.axis == 4 && left_axis_curve) ? control_pickup::prediction(static_cast<int32_t>(sent.raw), 4) :
                    (static_cast<int32_t>(sent.raw) - axis.output_minimum) / (axis.output_maximum - axis.output_minimum); applied = true;
            }
            assert(applied);
        }
    }
    while (objects_applied < fake_sim::object_writes.size()) {
        const auto& sent = fake_sim::object_writes[objects_applied++];
        const auto axis = sent.definition - control_definition_output::data_definition;
        const auto* mapping = definition_for_axis(aircraft_definitions, axis); assert(mapping && sent.bytes.size() == sizeof(double));
        double raw = 0; std::memcpy(&raw, sent.bytes.data(), sizeof(raw)); cockpit[axis] = (raw - mapping->minimum) / (mapping->maximum - mapping->minimum);
    }
    fake_vars::state().brakes[0] = cockpit[4]; fake_vars::state().brakes[1] = cockpit[5];
    const unsigned reference_axes[] = {3,6,7,8};
    for (unsigned i = 0; i < 4; ++i) fake_vars::state().steering[i] = cockpit[reference_axes[i]];
    const double guard[] = {1, 0, 1, 0}; data(control_output_guard::request, guard, 4);
    double positions[sim_target_count]{};
    for (size_t axis = 0; axis < axis_count; ++axis) positions[axis] = axis < 9 ? axes[axis].signed_target ? (cockpit[axis] * 2 - 1) * 16384 : cockpit[axis] : cockpit[axis] * 100;
    positions[axis_count] = 2;
    data(target_request, positions, sim_target_count);
    if (fake_sim::request_id >= 0x75000000u && fake_sim::request_id < 0x76000000u) {
        SIMCONNECT_RECV_SYSTEM_STATE reply{}; reply.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
        reply.dwRequestID = fake_sim::request_id; fake_sim::request_id = 0; dispatch(&reply, sizeof(reply), nullptr);
    }
    Update_StandAlone(0);
}
}
int main() {
    std::setbuf(stdout, nullptr);
    clean_profiles(); open_module();
    fake_vars::state() = fake_vars::registry{};
    for (size_t axis = 0; axis < axis_count; ++axis) cockpit[axis] = .5;
    fake_vars::state().value[0] = fake_vars::state().value[1] = 50;
    frame(); assert(gate.set_advanced(true));
    const double stopped_engines[] = {0,0,0,0}; data(control_output_guard::engine_request, stopped_engines, 4);
    const auto untouched_keys = fake_sim::transmissions.size();
    const char wizard_start[] = "5|wizard-native|1|wizard-start";
    receive_command(wizard_start, sizeof(wizard_start), nullptr);
    assert(setup_wizard.phase == wizard_phase::prepare && !gate.automatic);
    const char competing_auto[] = "5|wizard-native|2|auto|quadrant|on";
    receive_command(competing_auto, sizeof(competing_auto), nullptr); assert(!gate.automatic);
    const char wizard_cancel[] = "5|wizard-native|3|wizard-cancel";
    receive_command(wizard_cancel, sizeof(wizard_cancel), nullptr); frame();
    assert(setup_wizard.phase == wizard_phase::cancelled && gate.automatic && !gate.blocked);
    receive_command(wizard_start, sizeof(wizard_start), nullptr);
    assert(setup_wizard.phase == wizard_phase::cancelled && fake_sim::transmissions.size() == untouched_keys);
    for (const auto axis : {0, 1, 9, 10, 11, 12, 13, 14}) {
        auto& input = pickup.inputs[axis]; input.calibrated = true; input.candidates = 1;
        std::snprintf(input.event, sizeof(input.event), "%s", axis == 0 ? "AXIS_AILERONS_SET" : axis == 1 ? "AXIS_ELEVATOR_SET" :
            (axis - 9) % 3 == 0 ? "THROTTLE_AXIS_SET_EX1" : (axis - 9) % 3 == 1 ? "PROP_PITCH_AXIS_SET_EX1" : "AXIS_MIXTURE_SET");
        pickup.sample(axis, 0, seconds());
    }
    assert(!(bridge_status().capabilities & 4));
    assert(gate.advanced && gate.automatic == select_quadrant);
    unsigned request = 1;
    for (const auto target : {group::steering, group::throttle, group::propeller, group::mixture}) {
        assert(gate.assign_mock(group_selection(target), seconds())); confirm_mask(); frame();
        char command[128]{}; std::snprintf(command, sizeof(command), "5|native-check|%u|check-output|%s|start", ++request, group_name(target));
        receive_command(command, static_cast<unsigned>(std::strlen(command)), nullptr); assert(output_check.running());
        assert(!gate.automatic);
        for (unsigned sample = 0; sample < 40 && output_check.running(); ++sample) frame();
        assert(output_check.phase == output_check_phase::passed && output_check.restored);
        assert(gate.automatic == select_quadrant);
        for (size_t axis = 0; axis < axis_count; ++axis) if (pickup.required_axis(axis)) assert(std::abs(pickup.targets[axis] - .5) < .0001);
        assert((bridge_status().capabilities & 4) == (target == group::mixture ? 4u : 0u));
        assert(gate.force_me(group_selection(target))); confirm_mask();
    }
    assert(saved_output_validation() == 15);
    data(control_output_guard::engine_request, stopped_engines, 4);
    const char cached_wizard[] = "5|wizard-native|4|wizard-start";
    const auto cached_keys = fake_sim::transmissions.size();
    receive_command(cached_wizard, sizeof(cached_wizard), nullptr);
    assert(setup_wizard.phase == wizard_phase::complete && fake_sim::transmissions.size() == cached_keys);
    module_deinit();
    const auto keys_before = fake_sim::transmissions.size(), objects_before = fake_sim::object_writes.size();
    const auto locals_before = fake_vars::state().sets;
    open_module(); frame();
    std::cout << "Restart validation: loaded=" << output_validation_loaded << " saved=" << saved_output_validation()
        << " passed=" << output_check.passed << " capabilities=" << bridge_status().capabilities
        << " calibration=" << pickup.inputs[0].candidates << " event=" << pickup.inputs[0].event << "\n";
    assert(output_validation_loaded && saved_output_validation() == 15 && (bridge_status().capabilities & 4));
    assert(fake_sim::transmissions.size() == keys_before && fake_sim::object_writes.size() == objects_before && fake_vars::state().sets == locals_before);
    assert(pickup.input_position(0) < 0 && !pickup.inputs[0].detected); // No stored hardware pose or automatic movement.
    pickup.inputs[11].candidates = 32; assert(bridge_status().capabilities & 4); pickup.inputs[11].candidates = 1;
    pickup.inputs[0].candidates = 32; assert(!(bridge_status().capabilities & 4)); pickup.inputs[0].candidates = 1;
    for (size_t axis = 0; axis < axis_count; ++axis) if (pickup.required_axis(axis)) pickup.sample(axis, 0, seconds());
    assert(gate.set_advanced(true));
    assert(gate.assign_mock(select_mixture, seconds())); confirm_mask(); frame();
    char command[128]{}; std::snprintf(command, sizeof(command), "5|native-check|%u|check-output|mixture|start", ++request);
    receive_command(command, static_cast<unsigned>(std::strlen(command)), nullptr); assert(output_check.running());
    output_validation_record revoked{};
    assert(read_output_validation(output_validation_prefix, aircraft_identity, 2, revoked) && !revoked.proofs[3]);
    assert(fake_sim::transmissions.size() == keys_before && fake_sim::object_writes.size() == objects_before && fake_vars::state().sets == locals_before);
    output_check.tick(pickup, aircraft_definitions, output_context(), remote_output, seconds());
    SIMCONNECT_RECV_EXCEPTION exception{}; exception.dwID = SIMCONNECT_RECV_ID_EXCEPTION;
    exception.dwSendID = fake_sim::last_packet; exception.dwException = 3;
    dispatch(&exception, sizeof(exception), nullptr);
    assert(!remote_output.available && output_check.phase == output_check_phase::failed);
    assert(!gate.automatic); // Failed SDK/restoration cannot resume automatic takeover.
    assert(read_output_validation(output_validation_prefix, aircraft_identity, 2, revoked));
    for (const auto proof : revoked.proofs) assert(!proof);
    assert(gate.blocked == select_mixture && shared_session.status().phase == session_phase::idle);
    assert(gate.force_me(select_mixture)); confirm_mask(); assert(!gate.blocked);
    module_deinit();
    clean_profiles();
    // Reproduce the friend's nonlinear left-pedal response through the complete
    // real module/check/writer path. The SDK model uses the published curve,
    // independently of the definition planner under test.
    left_axis_curve = true; open_module(); frame();
    for (const auto axis : {0, 1}) {
        pickup.inputs[axis].calibrated = true; pickup.inputs[axis].candidates = 1;
        pickup.sample(axis, 0, seconds(), axis == 0 ? "AXIS_AILERONS_SET" : "AXIS_ELEVATOR_SET");
    }
    pickup.observe("AXIS_LEFT_BRAKE_SET", 0, seconds());
    cockpit[4] = .001895; // The logged failure requested baseline + 1% = 1.1895%.
    frame(); assert(gate.set_auto(select_quadrant, false, seconds()));
    assert(gate.assign_mock(select_steering, seconds())); confirm_mask(); frame();
    const char axis_check[] = "5|curved-brake|1|check-output|steering|start";
    receive_command(axis_check, sizeof(axis_check), nullptr); assert(output_check.running());
    for (unsigned sample = 0; sample < 100 && output_check.running(); ++sample) {
        frame(); std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    std::cout << "Automatic brake response: mask=" << pickup.output_response_axes << " phase=" << static_cast<unsigned>(output_check.phase)
        << " saved=" << saved_output_validation() << " issue=" << output_check.issue << "\n";
    assert(output_check.phase == output_check_phase::passed && output_check.restored && saved_output_validation() == 1);
    assert(std::abs(cockpit[4] - .001895) < .00004);
    output_validation_record response_saved{};
    assert(read_output_validation(output_validation_prefix, aircraft_identity, 2, response_saved) && response_saved.response_axes == (1u<<4));
    module_deinit();
    const auto restored_keys=fake_sim::transmissions.size(), restored_objects=fake_sim::object_writes.size();
    open_module(); frame();
    assert(pickup.output_response_axes==(1u<<4) && saved_output_validation()==1);
    assert(fake_sim::transmissions.size()==restored_keys && fake_sim::object_writes.size()==restored_objects);
    assert(pickup.inputs[0].calibrated && gate.blocked==0);
    assert(gate.assign_mock(select_steering,seconds()));confirm_mask();frame();
    const auto before_refusal=profile.generation;
    assert(reset_saved_setup()[0] && profile.generation==before_refusal && saved_output_validation()==1);
    assert(gate.force_me(select_steering));confirm_mask();frame();
    setup_wizard.phase=wizard_phase::input;
    assert(reset_saved_setup()[0] && profile.generation==before_refusal);
    setup_wizard.reset();
    assert(gate.set_tolerance(select_all,20));
    const auto kept_connection=connection_configuration;
    const auto kept_auto=gate.automatic;
    const auto keys_at_reset=fake_sim::transmissions.size(),objects_at_reset=fake_sim::object_writes.size();
    const auto locals_at_reset=fake_vars::state().sets;
    const char reset_command[]="5|reset-native|42|reset-setup";
    receive_command(reset_command,sizeof(reset_command),nullptr);
    assert(!saved_output_validation() && !output_check.passed && setup_wizard.phase==wizard_phase::idle);
    assert(!pickup.inputs[0].calibrated && !pickup.inputs[0].detected && !pickup.output_response_axes);
    assert(!gate.blocked && gate.automatic==kept_auto && gate.tolerance[0]==.20);
    assert(!std::memcmp(&kept_connection,&connection_configuration,sizeof(kept_connection)));
    assert(fake_sim::transmissions.size()==keys_at_reset && fake_sim::object_writes.size()==objects_at_reset && fake_vars::state().sets==locals_at_reset);
    control_profile blank{};assert(read_profile(profile_prefix,aircraft_identity,2,blank));
    for(const auto& mapping:blank.mapping)assert(!mapping.scale && !mapping.event[0]);
    output_validation_record blank_checks{};assert(read_output_validation(output_validation_prefix,aircraft_identity,2,blank_checks));
    for(auto proof:blank_checks.proofs)assert(!proof);
    pickup.inputs[0].calibrated=true;pickup.inputs[0].candidates=1;
    receive_command(reset_command,sizeof(reset_command),nullptr); // Retransmission cannot erase newly learned input.
    assert(pickup.inputs[0].calibrated && profile.generation==blank.generation);
    pickup.inputs[0]=pickup_axis{};
    char kept_prefix[600]{};std::snprintf(kept_prefix,sizeof(kept_prefix),"%s",profile_prefix);
    std::snprintf(profile_prefix,sizeof(profile_prefix),FSC_PROFILE_ROOT "missing-reset-storage/cache");
    assert(reset_saved_setup()[0] && profile_failed && !saved_output_validation());
    std::snprintf(profile_prefix,sizeof(profile_prefix),"%s",kept_prefix);
    assert(!reset_saved_setup()[0] && !profile_failed); // Storage can recover without restarting the module.
    module_deinit();open_module();frame();
    assert(!pickup.inputs[0].calibrated && !saved_output_validation() && !pickup.output_response_axes);
    module_deinit(); clean_profiles();
    std::cout<<"Live setup reset passed: durable empty caches survive restart, duplicate protection, no aircraft/ownership writes, preserved connection/tolerance/Auto, blocked/active-wizard refusal and retry after storage failure. SDK simulated.\n";
    std::cout << "Actual module parked checks passed: four explicit scoped checks, saved validation survives module restart with zero output calls and no saved hardware pose, per-route invalidation, durable revocation before recheck, SDK failure retires saved validation. SDK simulated; no simulator access.\n";
}
