// Binary subprocess boundary for the real C# network/coordinator integration
// test. Includes the production WASM controller and output adapter; the SDK fake
// captures hardware masking and transmissions. Never connects to MSFS.
#define _CRT_SECURE_NO_WARNINGS
#define FSC_TEST_SILENT_TRACE
#define FSC_CONNECTION_EPHEMERAL
#define FSC_PROFILE_ROOT "artifacts/native-coordinator/no-profile/"
#include <cassert>
#ifdef _WIN32
#include <io.h>
#include <fcntl.h>
#endif
#include "../Wasm/control_test.cpp"

namespace {
constexpr uint32_t harness_magic = 0x31544846;
#pragma pack(push, 1)
struct request {
    uint32_t magic = harness_magic, kind = 0;
    double unused = 0;
    uint32_t selection = 0;
    double positions[axis_count]{};
    session_packet command{};
    char text[512]{};
};
#pragma pack(pop)
static_assert(sizeof(request) == 1724, "Harness request layout changed");
size_t confirmed = 0, applied = 0, written = 0;
double world_model[world_value_count] = {0.8, 0.15, 2500, 0.05, -0.04, 2.5, 12, 3, 160, 0.1, 0.2, 0.3};
uint32_t readback_fault_axes = 0;
double readback_fault_values[axis_count]{};
uint32_t freeze_model = 0;
double pressure_model[4] = {999.125, 1, 1012.5, 0};
struct simulated_instrument_input : altimeter_input_gate {
    altimeter_input_proof state{0, 0, 0, 3, false};
    uint32_t requested = 0;
    altimeter_input_proof proof() const override { return state; }
    bool request(uint32_t mask, uint64_t epoch, uint64_t preparation, double) override {
        requested = mask; state.epoch = epoch; state.preparation = preparation; state.ready = false; return true;
    }
    void hold(double) override { requested = state.blocked = 3; state.ready = true; }
    void confirm() { state.blocked = requested; state.ready = true; }
} instrument_input;
bool instrument_fixture_enabled = false;
struct simulated_guidance_input : guidance_input_gate {
    guidance_input_proof state{};
    guidance_values logical{};
    std::vector<guidance_captured_input> inputs;
    simulated_guidance_input() {
        state.verified_routes = 8191; state.single_host_engine = state.navigation_ready = true;
        std::snprintf(state.adapter, sizeof(state.adapter), "test-absolute-ap-v1"); std::memset(state.navigation_fingerprint, 'a', 64);
    }
    guidance_input_proof proof() const override { return state; }
    bool request(bool capture, bool, uint64_t command_epoch, uint64_t navigation_epoch, uint64_t preparation, double) override {
        state.capture = capture; state.command_epoch = command_epoch; state.navigation_epoch = navigation_epoch; state.preparation = preparation;
        state.blocked = 8191; state.ready = false; return true;
    }
    void hold(double) override { state.capture = false; state.blocked = 8191; state.ready = true; inputs.clear(); }
    bool release(double) override { state.capture = false; state.blocked = 0; state.ready = true; return true; }
    bool reference(const guidance_values& values, uint64_t) override { logical = values; return true; }
    bool poll(guidance_captured_input& value) override {
        if (inputs.empty()) return false; value = inputs.front(); inputs.erase(inputs.begin()); return true;
    }
    void capture(guidance_setting setting, double value) {
        if (std::isnan(value)) value = guidance_matches(logical, setting, 1) ? 0 : 1;
        inputs.push_back({state.command_epoch, state.navigation_epoch, state.preparation, setting, value});
        guidance_assign(logical, setting, value);
    }
} guidance_input;
bool guidance_fixture_enabled = false;
double guidance_model[control_guidance::value_count]{};
uint32_t guidance_outputs = 0;
double physical[axis_count]{};
bool definition_fixture_enabled = false;
bool canonical_fixture_enabled = false, relative_controls_fixture = false;
unsigned definition_outputs = 0;
const char* names[] = {"AXIS_AILERONS_SET", "AXIS_ELEVATOR_SET", "AXIS_RUDDER_SET", "AXIS_STEERING_SET", "AXIS_LEFT_BRAKE_SET", "AXIS_RIGHT_BRAKE_SET",
    "AXIS_ELEV_TRIM_SET", "AILERON_TRIM_SET", "RUDDER_TRIM_SET", "AXIS_THROTTLE1_SET", "AXIS_PROPELLER1_SET", "AXIS_MIXTURE1_SET",
    "AXIS_THROTTLE2_SET", "AXIS_PROPELLER2_SET", "AXIS_MIXTURE2_SET"};
void drain() {
    unsigned limit = 0;
    while (confirmed < fake_sim::system_requests.size()) {
        assert(++limit < 1000); const auto id = fake_sim::system_requests[confirmed++];
        if (id == identity_request) continue; // This fixture supplies a fixed aircraft identity below.
        SIMCONNECT_RECV_SYSTEM_STATE state{}; state.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE; state.dwRequestID = id; state.dwInteger = 1;
        dispatch(&state, sizeof(state), nullptr);
    }
    if (instrument_fixture_enabled) instrument_input.confirm();
    if (guidance_fixture_enabled) guidance_input.state.ready = true;
}
void hardware(size_t axis) {
    const char* source = names[axis];
    if (relative_controls_fixture && (axis == 2 || axis == 4 || axis == 5) && pickup.canonical_axis(axis)) source = axis == 2 ?
        physical[axis] > .5 ? "RUDDER_AXIS_MINUS" : physical[axis] < .5 ? "RUDDER_AXIS_PLUS" : "RUDDER_CENTER" :
        physical[4] == physical[5] ? "BRAKES" : axis == 4 ? "BRAKES_LEFT" : "BRAKES_RIGHT";
    for (size_t index = 0; index < event_count; ++index) if (!std::strcmp(source, events[index].name)) {
        const auto event_id = first_event + static_cast<DWORD>(index);
        SIMCONNECT_RECV_EVENT event{}; event.dwID = SIMCONNECT_RECV_ID_EVENT; event.uEventID = event_id;
        const auto* mapping = pickup.canonical_axis(axis) ? definition_for_axis(pickup.definitions, axis) : nullptr;
        event.dwData = static_cast<DWORD>(static_cast<int32_t>(std::lround(mapping && !relative_controls_fixture ?
            definition_output_value(*mapping, physical[axis], true) : physical[axis] * 32768 - 16384)));
        dispatch(&event, sizeof(event), nullptr);
        bool found = false, masked = false;
        for (const auto& group : fake_sim::groups) {
            const auto item = group.second.find(event_id);
            if (item != group.second.end()) { found = true; masked = item->second; }
        }
        assert(found); if (!masked) pickup.targets[axis] = physical[axis]; return;
    }
    assert(false);
}
void pump() {
    const auto now = seconds(); pickup.target_time = now;
    for (size_t axis = 0; axis < 15; ++axis) if (pickup.inputs[axis].detected ||
        (canonical_fixture_enabled && (axis == 2 || axis == 4 || axis == 5))) hardware(axis);
    // Model only the SDK event's cockpit read-back. A remote DEFAULT-priority
    // transmission is never delivered to the hardware observer or learner.
    while (applied < fake_sim::transmissions.size()) {
        const auto& sent = fake_sim::transmissions[applied++]; assert(sent.priority == SIMCONNECT_GROUP_PRIORITY_DEFAULT);
        if (sent.event >= control_guidance::first_output && sent.event < control_guidance::first_output + 26) {
            assert(guidance_fixture_enabled); ++guidance_outputs;
            const auto setting = static_cast<guidance_setting>((sent.event - control_guidance::first_output) / 2);
            if (setting == guidance_setting::heading) guidance_model[13] = sent.raw * 360.0 / 16384;
            else if (setting == guidance_setting::altitude) guidance_model[14] = static_cast<int32_t>(sent.raw);
            else if (setting == guidance_setting::vertical_speed) guidance_model[15] = static_cast<int32_t>(sent.raw);
            else {
                const unsigned indices[] = {0, 0, 0, 0, 1, 2, 3, 4, 5, 9, 10, 11, 12};
                guidance_model[indices[static_cast<unsigned>(setting)]] = (sent.event - control_guidance::first_output) & 1u;
                if (setting == guidance_setting::approach_hold && !guidance_model[5]) guidance_model[6] = guidance_model[7] = guidance_model[8] = 0;
            }
            if (guidance_fixture_enabled) pickup.autopilot = guidance_model[0] != 0;
            continue;
        }
        if (sent.event == control_altimeters::set_event || sent.event == control_altimeters::standard_event) {
            assert(instrument_fixture_enabled);
            const auto index = sent.event == control_altimeters::set_event ? sent.extra[0] : sent.raw;
            assert(index >= 1 && index <= 2);
            if (sent.event == control_altimeters::set_event) { pressure_model[(index - 1) * 2] = sent.raw / 16.0; pressure_model[(index - 1) * 2 + 1] = 0; }
            else pressure_model[(index - 1) * 2 + 1] = 1;
            continue;
        }
        if (sent.event >= control_world::first_freeze_event && sent.event < control_world::first_freeze_event + 3) {
            const auto bit = 1u << (sent.event - control_world::first_freeze_event);
            if (sent.raw) freeze_model |= bit; else freeze_model &= ~bit;
            continue;
        }
        assert(sent.event >= first_event && sent.event < first_event + event_count);
        const auto* name = events[sent.event - first_event].name;
        if (definition_fixture_enabled) {
            bool matched = false;
            for (size_t index = 0; index < aircraft_definitions->count; ++index) {
                const auto& axis = aircraft_definitions->axes[index];
                if (axis.output_kind == 3 && !std::strcmp(name, axis.write + 2)) {
                    pickup.targets[axis.axis] = axis.axis == 3 ? .5 - static_cast<int32_t>(sent.raw) / 32768. :
                        axis.axis == 5 || (axis.axis == 4 && !relative_controls_fixture) ? control_pickup::prediction(static_cast<int32_t>(sent.raw), 4) :
                        (static_cast<int32_t>(sent.raw) - axis.output_minimum) / (axis.output_maximum - axis.output_minimum); matched = true;
                }
            }
            if (matched) continue;
        }
        for (size_t axis = 0; axis < 15; ++axis) {
            output_action action{};
            if (output_for_axis(pickup, axis, 0.5, action) && !std::strcmp(name, action.event))
                pickup.targets[axis] = control_pickup::prediction(static_cast<int32_t>(sent.raw), 0);
        }
    }
    while (written < fake_sim::object_writes.size()) {
        const auto& write = fake_sim::object_writes[written++];
        if (definition_fixture_enabled && write.definition >= control_definition_output::data_definition &&
            write.definition < control_definition_output::data_definition + axis_count) {
            const auto axis = write.definition - control_definition_output::data_definition;
            const auto* mapping = definition_for_axis(aircraft_definitions, axis);
            assert(mapping && mapping->output_kind == 1 && write.bytes.size() == sizeof(double));
            double raw = 0; std::memcpy(&raw, write.bytes.data(), sizeof(raw));
            pickup.targets[axis] = (raw - mapping->minimum) / (mapping->maximum - mapping->minimum);
            ++definition_outputs; continue;
        }
        assert(write.definition == control_world::write_definition && write.bytes.size() == sizeof(world_model));
        std::memcpy(world_model, write.bytes.data(), sizeof(world_model));
    }
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * (world_value_count + 3));
    auto* observed = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
    observed->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA; observed->dwRequestID = control_world::read_request; observed->dwDefineCount = world_value_count + 3;
    double values[world_value_count + 3]{}; std::memcpy(values, world_model, sizeof(world_model));
    for (unsigned index = 0; index < 3; ++index) values[world_value_count + index] = (freeze_model >> index) & 1u;
    std::memcpy(&observed->dwData, values, sizeof(values));
    const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&observed->dwData) - reinterpret_cast<char*>(observed) + sizeof(values));
    dispatch(observed, size, nullptr);
    observed->dwRequestID = control_altimeters::read_request; observed->dwDefineCount = 4;
    std::memcpy(&observed->dwData, pressure_model, sizeof(pressure_model));
    const auto pressure_size = static_cast<DWORD>(reinterpret_cast<char*>(&observed->dwData) - reinterpret_cast<char*>(observed) + sizeof(pressure_model));
    dispatch(observed, pressure_size, nullptr);
    {
        std::vector<unsigned char> ap_bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(guidance_model));
        auto* ap = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(ap_bytes.data());
        ap->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA; ap->dwRequestID = control_guidance::read_request; ap->dwDefineCount = control_guidance::value_count;
        std::memcpy(&ap->dwData, guidance_model, sizeof(guidance_model)); dispatch(ap, static_cast<DWORD>(ap_bytes.size()), nullptr);
    }
    for (size_t axis = 0; axis < axis_count; ++axis)
        if (readback_fault_axes & (1u << axis)) pickup.targets[axis] = readback_fault_values[axis];
    remote_writer.tick(seconds()); shared_session.tick(seconds(), native_bridge.available); drain();
    Update_StandAlone(0); // Includes deferred explicit world recovery.
}
void reply() {
    pump(); auto managed = shared_status(); auto observed = bridge_status();
    auto ap = shared_session.guidance_status(seconds()); assert(guidance_frame_valid(ap));
    const uint32_t outputs[] = {static_cast<uint32_t>(fake_sim::transmissions.size()) + definition_outputs, guidance_outputs};
    assert(session_valid(managed) && bridge_valid(observed));
    assert(std::fwrite(&managed, 1, sizeof(managed), stdout) == sizeof(managed));
    assert(std::fwrite(&observed, 1, sizeof(observed), stdout) == sizeof(observed));
    assert(std::fwrite(outputs, 1, sizeof(outputs), stdout) == sizeof(outputs)); std::fflush(stdout);
    assert(std::fwrite(&ap, 1, sizeof(ap), stdout) == sizeof(ap)); std::fflush(stdout);
}
}
int main(int argc, char** argv) {
#ifdef _WIN32
    _set_error_mode(_OUT_TO_STDERR);
    _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
    _setmode(_fileno(stdin), _O_BINARY); _setmode(_fileno(stdout), _O_BINARY);
#endif
    module_init();
    for (int index = 1; index < argc; ++index) {
        if (!std::strcmp(argv[index], "--verified-altimeter-fixture")) { instrument_fixture_enabled = true; assert(shared_session.configure_instrument_adapter(&instrument_input)); }
        else if (!std::strcmp(argv[index], "--definition-fixture")) definition_fixture_enabled = true;
        else if (!std::strcmp(argv[index], "--canonical-controls-fixture")) canonical_fixture_enabled = definition_fixture_enabled = true;
        else if (!std::strcmp(argv[index], "--relative-controls-fixture")) relative_controls_fixture = true;
        else { assert(!std::strcmp(argv[index], "--verified-guidance-fixture")); guidance_fixture_enabled = true; assert(shared_session.configure_guidance_adapter(&guidance_input)); }
    }
    guidance_model[13] = 90; guidance_model[14] = 5000; guidance_model[15] = 500;
    guidance_model[16] = guidance_model[17] = guidance_model[18] = guidance_model[19] = guidance_model[20] = 1;
    SIMCONNECT_RECV opened{}; opened.dwID = SIMCONNECT_RECV_ID_OPEN; dispatch(&opened, sizeof(opened), nullptr);
    const char* read = "5|harness|1|read"; receive_command(read, static_cast<unsigned>(std::strlen(read)), nullptr); drain();
    std::snprintf(aircraft_identity, sizeof(aircraft_identity), "%s", "simobjects/airplanes/bksq-aircraft-baronpropress/aircraft.cfg");
    aircraft_definitions = find_definition_profile(aircraft_identity); pickup.engines = 2;
    if (canonical_fixture_enabled) pickup.definitions = aircraft_definitions;
    // This fixture advertises output already verified against its independent
    // SDK model: button user's left route is linear, axis user's is curved.
    if (canonical_fixture_enabled && !relative_controls_fixture) pickup.output_response_axes = 1u << 4;
    if (definition_fixture_enabled) {
        assert(remote_output.select_profile(aircraft_definitions));
        fake_vars::state().on_set = [](unsigned engine, double position) { pickup.targets[11 + engine * 3] = position; ++definition_outputs; };
    }
    for (size_t axis = 0; axis < axis_count; ++axis) { physical[axis] = pickup.targets[axis] = 0.5; }
    if (canonical_fixture_enabled) for (const auto axis : {4, 5}) physical[axis] = pickup.targets[axis] = 0;
    for (size_t axis = 0; axis < 15; ++axis) if (axis < 3 || axis >= steering_axis_count) {
        if (relative_controls_fixture && pickup.canonical_axis(axis)) continue;
        pickup.inputs[axis].calibrated = true; pickup.inputs[axis].candidates = 1;
        std::snprintf(pickup.inputs[axis].event, sizeof(pickup.inputs[axis].event), "%s", names[axis]);
        pickup.sample(axis, 0, seconds());
    }
    pickup.target_time = seconds(); assert(gate.set_advanced(true));
    const char* join = "5|harness|2|join"; receive_command(join, static_cast<unsigned>(std::strlen(join)), nullptr);
    request value{};
    while (std::fread(&value, 1, sizeof(value), stdin) == sizeof(value)) {
        assert(value.magic == harness_magic);
        if (value.kind == 1) {
            assert(session_valid(value.command));
            std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_CLIENT_DATA) + sizeof(value.command));
            auto* packet = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(bytes.data()); packet->dwID = SIMCONNECT_RECV_ID_CLIENT_DATA;
            packet->dwRequestID = packet->dwDefineID = control_bridge_transport::session_command_id;
            std::memcpy(&packet->dwData, &value.command, sizeof(value.command));
            const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&packet->dwData) - reinterpret_cast<char*>(packet) + sizeof(value.command));
            bridge_dispatch(packet, size, nullptr);
        } else if (value.kind == 3) {
            for (size_t axis = 0; axis < 15; ++axis) if (value.selection & (1u << axis)) {
                assert(value.positions[axis] >= 0 && value.positions[axis] <= 1);
                physical[axis] = value.positions[axis];
                hardware(axis); // First use must deliver the real observed event, including previously untouched trim.
            }
        } else if (value.kind == 6) {
            const auto target = value.selection == 1 ? "steering" : value.selection == 2 ? "throttle" : value.selection == 4 ? "propeller" : value.selection == 8 ? "mixture" : "quadrant";
            char force[96]{}; std::snprintf(force, sizeof(force), "5|harness|2|force|%s|me", target);
            receive_command(force, static_cast<unsigned>(std::strlen(force)), nullptr);
        } else if (value.kind == 7) pickup.autopilot = value.selection != 0;
        else if (value.kind == 8) {
            const auto target = value.selection == 1 ? "steering" : value.selection == 2 ? "throttle" : value.selection == 4 ? "propeller" : value.selection == 8 ? "mixture" : "quadrant";
            const auto intent = static_cast<session_intent>(static_cast<uint32_t>(value.positions[0]));
            const auto operation = intent == session_intent::auto_on || intent == session_intent::auto_off ? "auto" : intent == session_intent::force ? "force" : intent == session_intent::cancel ? "cancel" : "set";
            const auto owner = intent == session_intent::auto_on ? "on" : intent == session_intent::auto_off ? "off" : intent == session_intent::other ? "mock" : intent == session_intent::cancel ? "pending" : "me";
            char command[96]{}; std::snprintf(command, sizeof(command), "5|harness|3|%s|%s|%s", operation, target, owner);
            receive_command(command, static_cast<unsigned>(std::strlen(command)), nullptr);
        }
        else if (value.kind == 9 || value.kind == 10) {
            const auto text = value.kind == 9 ? "5|harness|4|leave" : "5|harness|5|join";
            receive_command(text, static_cast<unsigned>(std::strlen(text)), nullptr);
        }
        else if (value.kind == 11) { assert(world_valid(value.positions)); std::memcpy(world_model, value.positions, sizeof(world_model)); }
        else if (value.kind == 19) {
            // Raw read-back fault injection only. Production bridge_status must
            // convert each invalid slot to unknown; no hardware command is sent.
            readback_fault_axes |= value.selection;
            for (size_t axis = 0; axis < axis_count; ++axis)
                if (value.selection & (1u << axis)) readback_fault_values[axis] = value.positions[axis];
        }
        else if (value.kind == 20) {
            readback_fault_axes &= ~value.selection;
            for (size_t axis = 0; axis < axis_count; ++axis)
                if (value.selection & (1u << axis)) pickup.targets[axis] = physical[axis];
        }
        else if (value.kind == 12) { assert(std::memchr(value.text, 0, sizeof(value.text))); receive_command(value.text, static_cast<unsigned>(std::strlen(value.text)), nullptr); }
        else if (value.kind == 13) {
            bridge_packet report{}; std::memcpy(&report, &value.command, sizeof(report)); assert(bridge_valid(report) && report.kind == bridge_kind::backend);
            std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_CLIENT_DATA) + sizeof(report));
            auto* packet = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(bytes.data()); packet->dwID = SIMCONNECT_RECV_ID_CLIENT_DATA;
            packet->dwRequestID = packet->dwDefineID = control_bridge_transport::command_id;
            std::memcpy(&packet->dwData, &report, sizeof(report));
            const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&packet->dwData) - reinterpret_cast<char*>(packet) + sizeof(report));
            bridge_dispatch(packet, size, nullptr);
        }
        else if (value.kind == 14) {
            for (unsigned index = 0; index < 2; ++index) if (!instrument_fixture_enabled || !(instrument_input.state.blocked & (1u << index))) {
                pressure_model[index * 2] = value.positions[index * 2]; pressure_model[index * 2 + 1] = value.positions[index * 2 + 1];
            }
            std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * 4);
            auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data()); data->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA;
            data->dwRequestID = control_altimeters::read_request; data->dwDefineCount = 4;
            std::memcpy(&data->dwData, pressure_model, sizeof(double) * 4);
            const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&data->dwData) - reinterpret_cast<char*>(data) + sizeof(double) * 4);
            dispatch(data, size, nullptr);
        }
        else if (value.kind == 15) {
            assert(instrument_fixture_enabled); instrument_input.state.verified_routes = value.selection ? 3u : 0u;
        }
        else if (value.kind == 16) {
            guidance_packet command{}; std::memcpy(&command, &value.command, sizeof(command)); assert(guidance_frame_valid(command));
            std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_CLIENT_DATA) + sizeof(command));
            auto* data = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(bytes.data()); data->dwID = SIMCONNECT_RECV_ID_CLIENT_DATA;
            data->dwRequestID = data->dwDefineID = control_guidance_transport::command_id; std::memcpy(&data->dwData, &command, sizeof(command));
            bridge_dispatch(data, static_cast<DWORD>(bytes.size()), nullptr);
        }
        else if (value.kind == 17) { assert(guidance_fixture_enabled); guidance_input.capture(static_cast<guidance_setting>(value.selection), value.positions[0]); }
        else if (value.kind == 18) { assert(guidance_fixture_enabled); guidance_input.state.verified_routes = value.selection ? 8191u : 0u; }
        else assert(value.kind == 2 || value.kind == 5);
        reply(); if (value.kind == 5) break;
    }
    adapter.release(); return 0;
}
