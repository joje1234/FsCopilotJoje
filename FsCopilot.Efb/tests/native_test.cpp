// Exercises the actual WASM module against a small SimConnect contract fake.
// A pass verifies our logic, not the simulator's implementation of masking.
#define _CRT_SECURE_NO_WARNINGS // Exercise the same portable stdio used by WASM.
#include <cassert>
#include <iostream>
#define FSC_PROFILE_ROOT "artifacts/efb-control-test/tests/"
#include "../Wasm/control_test.cpp"

void confirm_configuration()
{
    SIMCONNECT_RECV_SYSTEM_STATE state{};
    state.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    state.dwRequestID = first_barrier + gate.barrier;
    dispatch(&state, sizeof(state), nullptr);
}

void send(const char* packet) { receive_command(packet, static_cast<unsigned int>(std::strlen(packet)), nullptr); }

void open_connection()
{
    SIMCONNECT_RECV opened{};
    opened.dwID = SIMCONNECT_RECV_ID_OPEN;
    dispatch(&opened, sizeof(opened), nullptr);
}

void hardware(const char* name, int32_t value)
{
    for (size_t i = 0; i < event_count; ++i) if (!std::strcmp(events[i].name, name)) {
        SIMCONNECT_RECV_EVENT event{};
        event.dwID = SIMCONNECT_RECV_ID_EVENT;
        event.uEventID = first_event + static_cast<DWORD>(i);
        event.dwData = static_cast<DWORD>(value);
        dispatch(&event, sizeof(event), nullptr);
        return;
    }
    assert(false);
}

void positions(const double* values)
{
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * sim_target_count);
    auto* packet = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
    packet->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA;
    packet->dwRequestID = target_request;
    packet->dwDefineCount = static_cast<DWORD>(sim_target_count);
    std::memcpy(&packet->dwData, values, sizeof(double) * sim_target_count);
    dispatch(packet, static_cast<DWORD>(bytes.size()), nullptr);
}

int main()
{
    command parsed{};
    const char* good = "2|test-client|4294967295|set|quadrant|mock";
    assert(parse_command(good, std::strlen(good), parsed));
    assert(parsed.request == UINT32_MAX && parsed.target == group::quadrant && parsed.blocked);
    const char* tolerance = "2|client|1|tolerance|quadrant|5";
    assert(parse_command(tolerance, std::strlen(tolerance), parsed) && parsed.tolerance == 5);
    const char* unsafe_tolerance = "2|client|1|tolerance|quadrant|99";
    assert(!parse_command(unsafe_tolerance, std::strlen(unsafe_tolerance), parsed));
    for (const char* packet : {"5|client|1|set|all|mock", "5|client|1|force|all|me", "4|client|1|tolerance|all|20"})
        assert(!parse_command(packet, std::strlen(packet), parsed)); // Global scope is tolerance-only in v5.
    for (const char* packet : {"3|client|1|advanced|quadrant|on", "4|client|1|auto|quadrant|on",
        "3|client|1|set|propeller|mock", "3|client|1|force|mixture|me"}) assert(parse_command(packet, std::strlen(packet), parsed));
    for (const char* packet : {"2|client|1|auto|throttle|on", "2|client|1|set|mixture|me",
        "3|client|1|auto|steering|on", "3|client|1|auto|quadrant|on", "3|client|1|advanced|mixture|on",
        "3|client|1|auto|throttle|maybe"}) assert(!parse_command(packet, std::strlen(packet), parsed));
    assert(parse_command("2|client|1|read", 16, parsed)); // Includes terminating NUL.
    const char* bad[] = { "", "6|client|1|read", "2|client|0|read", "2|client|4294967296|read", "2|client|1x|read",
        "2|client|1|set|quadrant|maybe", "2|client|1|set|unknown|mock", "2|client|1|read|extra", "2|bad\"id|1|read",
        "2||1|read", "2|client|1|restore|extra|mock", "2|client|1|set|quadrant|mock|extra",
        "2|client|1|force|unknown|me", "2|client|1|force|quadrant|mock", "2|client|1|force", "2|client|1|force|steering|me|extra" };
    for (const auto* packet : bad) assert(!parse_command(packet, std::strlen(packet), parsed));
    const char embedded[] = "2|client|1|read\0hidden";
    assert(!parse_command(embedded, sizeof(embedded) - 1, parsed));
    char oversized[129]{};
    assert(!parse_command(oversized, sizeof(oversized), parsed));

    const auto registrations_before = fake_sim::dispatch_registrations;
    module_init();
    assert(fake_sim::dispatch_registrations == registrations_before + 1); // One WASM callback, including optional IPC.
    assert(connection && !gate.ready && !gate.applying);
    assert(fake_sim::names.empty() && fake_sim::groups.empty());
    // Simulator startup can take far longer than the ownership timeout. Loading
    // the module alone must neither configure inputs nor trigger that watchdog.
    change_started = seconds() - 100;
    Update_StandAlone(0);
    assert(connection && !gate.ready && !gate.applying);
    send("2|startup|1|read");
    assert(fake_sim::names.empty() && !gate.applying); // EFB before OPEN.
    connection_wait_started = seconds() - 10;
    Update_StandAlone(0);
    assert(connection && !gate.ready);
    open_connection();
    assert(gate.applying && !gate.ready);
    assert(fake_sim::names.size() == event_count);
    for (const auto& group : fake_sim::groups) for (const auto& event : group.second) assert(!event.second);
    confirm_configuration();
    assert(gate.ready && !gate.steering_blocked && !gate.quadrant_blocked);
    const auto unobserved_bridge = bridge_status();
    assert(bridge_valid(unobserved_bridge));
    for (const auto position : unobserved_bridge.input) assert(position == -1); // Internal unavailable sentinel is -10.
    auto* unknown_file = std::fopen("artifacts/efb-control-test/tests/native-bridge-unobserved.bin", "wb");
    assert(unknown_file && std::fwrite(&unobserved_bridge, 1, sizeof(unobserved_bridge), unknown_file) == sizeof(unobserved_bridge)); std::fclose(unknown_file);
    assert(gate.tolerance[0] == 0.10 && gate.tolerance[1] == 0.10 && gate.tolerance[2] == 0.10 && gate.tolerance[3] == 0.10);
    send("5|presets|1|tolerance|all|20");
    for (const auto value : gate.tolerance) assert(value == 0.2);
    assert(gate.blocked == 0 && !gate.applying);
    send("5|presets|2|tolerance|all|30");
    for (const auto value : gate.tolerance) assert(value == 0.3);
    assert(bridge_valid(bridge_status()));
    send("5|presets|3|tolerance|all|10");
    for (const auto value : gate.tolerance) assert(value == 0.1);
    assert(fake_sim::priorities[steering_group] == SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE);
    last_publish = seconds();
    const auto published_before = fake_sim::messages.size();
    Update_StandAlone(0);
    assert(fake_sim::messages.size() == published_before);
    last_publish -= 0.11;
    Update_StandAlone(0);
    assert(fake_sim::messages.size() == published_before + 1); // 10 Hz position status, without waiting for a read poll.

    send("2|client|2|set|quadrant|mock");
    assert(gate.applying && !gate.quadrant_blocked); // No optimistic state.
    assert(fake_sim::names.size() == event_count); // Rebuild restores all cleared mappings.
    assert(fake_sim::mapping_calls == 2 * event_count);
    for (const auto& entry : fake_sim::groups[quadrant_group]) assert(entry.second);
    for (const auto& entry : fake_sim::groups[steering_group]) assert(!entry.second);
    SIMCONNECT_RECV_SYSTEM_STATE unrelated{};
    unrelated.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    unrelated.dwRequestID = first_barrier;
    dispatch(&unrelated, sizeof(unrelated), nullptr);
    assert(gate.applying);
    send("2|other-client|3|set|steering|mock");
    assert(gate.pending.request == 2); // Cannot overwrite an in-flight request.
    confirm_configuration();
    assert(gate.quadrant_blocked && !gate.steering_blocked && !gate.applying);
    assert(fake_sim::messages.back().find("\"request\":2") != std::string::npos);

    SIMCONNECT_RECV_EVENT input{};
    input.dwID = SIMCONNECT_RECV_ID_EVENT;
    input.dwData = 1234;
    for (size_t i = 0; i < event_count; ++i) {
        input.uEventID = first_event + static_cast<DWORD>(i);
        dispatch(&input, sizeof(input), nullptr);
    }
    assert(quadrant_activity.seen > 0 && quadrant_activity.blocked == quadrant_activity.seen);
    assert(steering_activity.seen > 0 && steering_activity.blocked == 0);

    const auto observed_before_ex1 = steering_activity.seen;
    SIMCONNECT_RECV_EVENT_EX1 extended{}; extended.dwID = SIMCONNECT_RECV_ID_EVENT_EX1;
    for (size_t i = 0; i < event_count; ++i) if (!std::strcmp(events[i].name, "AXIS_RUDDER_SET")) extended.uEventID = first_event + static_cast<DWORD>(i);
    extended.dwData0 = static_cast<DWORD>(-8192);
    dispatch(&extended, sizeof(extended) - 1, nullptr);
    assert(steering_activity.seen == observed_before_ex1); // Truncated extended packet is ignored.
    dispatch(&extended, sizeof(extended), nullptr);
    assert(steering_activity.seen == observed_before_ex1 + 1 && pickup.inputs[2].detected && pickup.inputs[2].raw == -8192);

    send("2|pickup|1|set|quadrant|me");
    assert((gate.waiting() & select_quadrant) && gate.quadrant_blocked && !gate.applying);
    Update_StandAlone(0);
    assert(gate.quadrant_blocked && !gate.applying); // Unknown inputs cannot attach.
    send("2|pickup|2|set|quadrant|mock");
    assert(!(gate.waiting() & select_quadrant) && gate.quadrant_blocked && !gate.applying);

    send("2|client|4|set|steering|mock");
    confirm_configuration();
    assert(gate.steering_blocked && gate.quadrant_blocked);
    send("2|client|5|restore");
    assert(gate.applying && gate.steering_blocked && gate.quadrant_blocked);
    confirm_configuration();
    assert(!gate.steering_blocked && !gate.quadrant_blocked);
    for (const auto& group : fake_sim::groups) for (const auto& event : group.second) assert(!event.second);

    // Context-menu override releases only its group and cancels only its pickup.
    send("2|force-test|1|set|steering|mock"); confirm_configuration();
    send("2|force-test|2|set|quadrant|mock"); confirm_configuration();
    send("2|force-test|3|set|steering|me");
    send("2|force-test|4|set|quadrant|me");
    assert((gate.waiting() & select_steering) && (gate.waiting() & select_quadrant));
    send("2|force-test|5|force|steering|me");
    assert(gate.applying && gate.steering_blocked && gate.quadrant_blocked);
    assert(!(gate.waiting() & select_steering) && (gate.waiting() & select_quadrant));
    for (const auto& entry : fake_sim::groups[quadrant_group]) assert(entry.second);
    for (const auto& entry : fake_sim::groups[steering_group]) assert(!entry.second);
    confirm_configuration();
    assert(!gate.steering_blocked && gate.quadrant_blocked);
    send("2|force-test|6|set|steering|mock"); confirm_configuration();
    send("2|force-test|7|force|quadrant|me");
    assert(gate.applying && gate.quadrant_blocked && gate.steering_blocked);
    confirm_configuration();
    assert(gate.steering_blocked && !gate.quadrant_blocked && !(gate.waiting() & select_quadrant));
    send("2|force-test|8|force|steering|me"); confirm_configuration();
    assert(!gate.steering_blocked && !gate.quadrant_blocked);

    send("2|client|6|set|quadrant|mock");
    confirm_configuration();
    SIMCONNECT_RECV_EVENT_FILENAME loaded{};
    loaded.dwID = SIMCONNECT_RECV_ID_EVENT_FILENAME;
    loaded.uEventID = aircraft_loaded;
    dispatch(&loaded, sizeof(loaded), nullptr);
    assert(!gate.ready && gate.applying);
    confirm_configuration();
    assert(!gate.quadrant_blocked && !gate.steering_blocked && quadrant_activity.seen == 0);

    send("2|client|7|set|quadrant|mock");
    SIMCONNECT_RECV_EXCEPTION failure{};
    failure.dwID = SIMCONNECT_RECV_ID_EXCEPTION;
    failure.dwException = 7;
    failure.dwSendID = fake_sim::mapping_packets[first_event];
    failure.dwIndex = 2;
    dispatch(&failure, sizeof(failure), nullptr);
    assert(!connection && !gate.ready && !gate.applying && fake_sim::groups.empty());
    assert(std::strstr(error, events[0].name) != nullptr);
    assert(std::strstr(error, "index 2") != nullptr);
    const unsigned closed = fake_sim::closes;
    confirm_configuration();
    assert(!gate.ready); // A late reply cannot revive a failed bridge.
    send("2|client|8|set|steering|mock");
    assert(fake_sim::closes == closed && !connection);

    // A synchronous configuration failure must also close/release partial masks.
    adapter.groups_created = false;
    error[0] = '\0';
    module_init();
    open_connection();
    assert(!gate.applying && !gate.ready); // OPEN before EFB.
    send("2|startup|1|read");
    confirm_configuration();
    fake_sim::fail_configuration = true;
    send("2|client|9|set|steering|mock");
    assert(!connection && !gate.ready && fake_sim::groups.empty());
    fake_sim::fail_configuration = false;

    // A server-side failure during group rebuild identifies Add and the event,
    // and releases partial masks rather than reporting confirmed ownership.
    adapter.groups_created = false;
    error[0] = '\0';
    module_init();
    open_connection();
    send("2|startup|1|read");
    confirm_configuration();
    send("2|client|10|set|steering|mock");
    failure.dwException = 3;
    failure.dwSendID = fake_sim::notification_packets[first_event];
    dispatch(&failure, sizeof(failure), nullptr);
    assert(!connection && !gate.ready && !gate.applying && fake_sim::groups.empty());
    assert(std::strstr(error, "Add AXIS_AILERONS_SET") != nullptr);

    adapter.groups_created = false;
    error[0] = '\0';
    module_init();
    open_connection();
    send("2|startup|1|read");
    change_started = seconds() - 10;
    Update_StandAlone(0);
    assert(!connection && !gate.ready && fake_sim::groups.empty());
    assert(std::strstr(error, "callbacks") != nullptr);
    confirm_configuration();
    assert(!gate.ready); // A timed-out configuration stays released.
    module_deinit();

    adapter.groups_created = false;
    error[0] = '\0';
    module_init();
    send("2|startup|1|read");
    connection_wait_started = seconds() - 20;
    Update_StandAlone(0);
    assert(!connection && !gate.ready && !gate.applying && fake_sim::groups.empty());
    assert(std::strstr(error, "connection-open") != nullptr);
    open_connection(); // A late OPEN must not configure a closed connection.
    assert(!gate.ready && !gate.applying && fake_sim::groups.empty());
    module_deinit();
    // End-to-end pickup: real event dispatch + target packets, mask remains on
    // during alignment and only a matching barrier confirms attachment.
    adapter.groups_created = false;
    error[0] = '\0';
    module_init(); open_connection(); send("2|startup|1|read"); confirm_configuration();
    SIMCONNECT_RECV_SYSTEM_STATE standard_identity{};
    standard_identity.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    standard_identity.dwRequestID = identity_request;
    std::snprintf(standard_identity.szString, sizeof(standard_identity.szString), "%s", "SimObjects/Airplanes/standard-fixture/aircraft.cfg");
    dispatch(&standard_identity, sizeof(standard_identity), nullptr);
    profile_failed = true; // No fixture writes; persistence has its own test below.
    const char* levers[] = {"AXIS_THROTTLE1_SET", "AXIS_PROPELLER1_SET", "AXIS_MIXTURE1_SET",
        "AXIS_THROTTLE2_SET", "AXIS_PROPELLER2_SET", "AXIS_MIXTURE2_SET"};
    double targets[target_count]{};
    targets[axis_count] = 2;
    for (const char* lever : levers) hardware(lever, 0);
    hardware("THROTTLE1_SET", 8192); hardware("THROTTLE2_SET", 8192);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) {
        targets[axis] = 50;
        pickup.inputs[axis].changed -= 0.2;
    }
    positions(targets);
    for (const char* lever : levers) hardware(lever, 8192);
    hardware("THROTTLE1_SET", 12288); hardware("THROTTLE2_SET", 12288);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) {
        targets[axis] = 75;
        pickup.inputs[axis].changed -= 0.2;
    }
    for (unsigned frame = 0; frame < 3; ++frame) positions(targets);
    assert(pickup.engines == 2);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) assert(pickup.inputs[axis].calibrated);
    send("2|end-to-end|1|set|quadrant|mock"); confirm_configuration();
    for (const char* lever : levers) hardware(lever, 8192);
    send("2|end-to-end|2|set|quadrant|me");
    assert((gate.waiting() & select_quadrant) && gate.quadrant_blocked && !gate.applying);
    Update_StandAlone(0);
    assert(gate.quadrant_blocked && !gate.applying);
    for (auto& request : gate.takeovers) if (request.selection == select_quadrant) request.stable_since = seconds() - 0.4;
    Update_StandAlone(0);
    assert(gate.applying && gate.quadrant_blocked && (gate.waiting() & select_quadrant));
    assert(gate.pending.request == 2 && !std::strcmp(gate.pending.client, "end-to-end"));
    confirm_configuration();
    assert(!gate.applying && !gate.quadrant_blocked && !(gate.waiting() & select_quadrant));
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) targets[axis] = 0;
    positions(targets); // A delayed frame after attachment must not unlearn.
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) assert(pickup.inputs[axis].calibrated);
    send("2|end-to-end|3|set|quadrant|mock"); confirm_configuration();
    assert(gate.quadrant_blocked);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) assert(pickup.inputs[axis].calibrated);
    send("2|end-to-end|4|restore"); confirm_configuration();

    // Exercise the actual module/protocol/adapter, rather than only the pure
    // runtime: independent native masks, Auto, confirmed pickup and mixed UI.
    send("3|advanced-test|1|advanced|quadrant|on"); assert(gate.advanced);
    send("3|advanced-test|2|set|quadrant|mock"); confirm_configuration();
    assert(gate.blocked == select_quadrant && fake_sim::groups.size() == 4);
    for (const DWORD id : {quadrant_group, propeller_group, mixture_group}) {
        assert(!fake_sim::groups[id].empty());
        for (const auto& entry : fake_sim::groups[id]) assert(entry.second);
    }
    send("3|advanced-test|3|force|throttle|me");
    assert(gate.applying && gate.blocked == select_quadrant);
    for (const auto& entry : fake_sim::groups[quadrant_group]) assert(!entry.second);
    for (const DWORD id : {propeller_group, mixture_group}) for (const auto& entry : fake_sim::groups[id]) assert(entry.second);
    confirm_configuration(); assert(gate.blocked == (select_propeller | select_mixture));
    send("3|advanced-test|4|advanced|quadrant|off");
    assert(gate.advanced && gate.ready && !gate.applying && gate.blocked == (select_propeller | select_mixture));
    send("3|advanced-test|9|advanced|quadrant|on"); assert(gate.advanced);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) targets[axis] = 75;
    positions(targets); for (const char* lever : levers) hardware(lever, 8192);
    send("4|advanced-test|5|auto|quadrant|on"); assert(gate.automatic == select_quadrant);
    hardware("AXIS_MIXTURE1_SET", 0); hardware("AXIS_MIXTURE2_SET", 0);
    Update_StandAlone(0); assert(gate.waiting() == select_mixture && !gate.applying);
    assert(gate.blocked == (select_propeller | select_mixture)); // Movement alone does not attach.
    send("3|advanced-test|11|advanced|quadrant|off");
    assert(gate.advanced && gate.automatic == select_quadrant && gate.waiting() == select_mixture);
    send("4|advanced-test|12|set|mixture|mock"); // Cancel is available in Simple view.
    assert(!gate.waiting() && gate.blocked == (select_propeller | select_mixture));
    send("3|advanced-test|13|advanced|quadrant|on");
    hardware("AXIS_MIXTURE1_SET", 8192); hardware("AXIS_MIXTURE2_SET", 8192);
    positions(targets); Update_StandAlone(0);
    for (auto& request : gate.takeovers) if (request.selection == select_mixture) request.stable_since = seconds() - 0.4;
    Update_StandAlone(0); assert(gate.applying && (gate.blocked & select_mixture));
    confirm_configuration(); assert(gate.blocked == select_propeller && !gate.waiting());
    send("3|advanced-test|6|force|quadrant|me"); confirm_configuration();
    assert(!gate.blocked);
    // Reset only one pair; its neighboring learned scales must survive.
    send("3|advanced-test|7|learn|mixture|reset");
    assert(pickup.inputs[9].calibrated && pickup.inputs[10].calibrated && !pickup.inputs[11].detected);
    assert(pickup.inputs[12].calibrated && pickup.inputs[13].calibrated && !pickup.inputs[14].detected);
    send("3|advanced-test|8|advanced|quadrant|off"); assert(gate.advanced && gate.automatic == select_quadrant);
    send("4|advanced-test|10|auto|quadrant|off"); assert(!gate.automatic);
    std::cout << "Actual-module Advanced pairs, mixed ownership, per-pair Auto, alignment barrier and scoped relearning passed.\n";
    send("2|end-to-end|3|learn|quadrant|reset");
    assert(!pickup.inputs[steering_axis_count].detected);
    // Exercise complete status serialization with the maximum axis count.
    pickup.engines = 4;
    pickup.target_time = seconds();
    for (size_t axis = 0; axis < axis_count; ++axis) {
        pickup.inputs[axis].detected = true;
        pickup.inputs[axis].calibrated = true;
        pickup.inputs[axis].candidates = 1;
        pickup.inputs[axis].observed = seconds();
        pickup.targets[axis] = 0.5;
    }
    const auto writes_before_guidance = fake_sim::transmissions.size();
    std::vector<unsigned char> ap_bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * control_guidance::value_count);
    auto* ap_packet = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(ap_bytes.data());
    ap_packet->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA; ap_packet->dwRequestID = control_guidance::read_request;
    ap_packet->dwDefineCount = control_guidance::value_count;
    double ap_values[control_guidance::value_count]{};
    for (unsigned index = 0; index < 13; ++index) ap_values[index] = 1;
    ap_values[13] = 359.995; ap_values[14] = 100000; ap_values[15] = 12000; ap_values[16] = 1; ap_values[17] = 2;
    ap_values[18] = ap_values[19] = ap_values[20] = 4;
    std::memcpy(&ap_packet->dwData, ap_values, sizeof(ap_values));
    dispatch(ap_packet, static_cast<DWORD>(ap_bytes.size()), nullptr);
    assert(guidance.fresh(seconds()) && guidance.values.switches == 8191 && fake_sim::transmissions.size() == writes_before_guidance);
    const auto* original_profile=aircraft_definitions;
    char monitor_name[97]{},monitor_units[33]{};
    std::memset(monitor_name,'Q',96);monitor_name[2]='"';std::memset(monitor_units,'N',32);
    cockpit_definition monitor_entries[16]{};
    for(auto& entry:monitor_entries)entry={monitor_name,monitor_units,true,.000001};
    auto monitor_profile=definition_profiles[0];monitor_profile.cockpit=monitor_entries;monitor_profile.cockpit_count=16;monitor_profile.cockpit_identity="abc";
    aircraft_definitions=&monitor_profile;
    ordinary_cockpit.observe(aircraft_definitions,session,shared_session.status(),seconds());
    instrument_monitor.observe(aircraft_definitions,ordinary_cockpit.status());
    instrument_monitor.sequence=12;instrument_monitor.recent_count=12;
    for(unsigned i=0;i<12;++i)instrument_monitor.recent[i]={i,i+1,-1e100,1e100};
    publish();
    std::cout << "STATUS " << fake_sim::messages.back() << '\n';
    aircraft_definitions=original_profile;ordinary_cockpit.reset();instrument_monitor=control_instrument_monitor{};
    // Real module telemetry shares the single WASM callback. A discovery request
    // may observe current masks; it cannot assign Me or alter pending pickup.
    send("3|bridge-test|1|set|quadrant|mock"); confirm_configuration();
    SIMCONNECT_RECV bridge_open{}; bridge_open.dwID = SIMCONNECT_RECV_ID_OPEN;
    const auto before_bridge_setup = fake_sim::last_packet;
    bridge_dispatch(&bridge_open, sizeof(bridge_open), nullptr);
    assert(native_bridge.available && connection);
    SIMCONNECT_RECV_EXCEPTION bridge_exception{}; bridge_exception.dwID = SIMCONNECT_RECV_ID_EXCEPTION;
    bridge_exception.dwSendID = before_bridge_setup + 2; bridge_exception.dwException = SIMCONNECT_EXCEPTION_ALREADY_CREATED;
    bridge_dispatch(&bridge_exception, sizeof(bridge_exception), nullptr);
    assert(native_bridge.available); // Reused persistent area after module reload.
    bridge_packet hello{}; hello.kind = bridge_kind::hello; hello.request = 314;
    std::snprintf(hello.backend, sizeof(hello.backend), "0123456789abcdef0123456789abcdef");
    std::vector<unsigned char> bridge_bytes(sizeof(SIMCONNECT_RECV_CLIENT_DATA) + sizeof(hello));
    auto* bridge_data = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(bridge_bytes.data());
    bridge_data->dwID = SIMCONNECT_RECV_ID_CLIENT_DATA;
    bridge_data->dwRequestID = bridge_data->dwDefineID = control_bridge_transport::command_id;
    std::memcpy(&bridge_data->dwData, &hello, sizeof(hello));
    const auto data_offset = reinterpret_cast<unsigned char*>(&bridge_data->dwData) - bridge_bytes.data();
    const auto packet_bytes = static_cast<DWORD>(data_offset + sizeof(hello));
    bridge_dispatch(bridge_data, packet_bytes - 1, nullptr);
    assert(!fake_sim::client_data.count(control_bridge_transport::reply_id));
    bridge_dispatch(bridge_data, packet_bytes, nullptr);
    bridge_packet reply{};
    const auto& reply_bytes = fake_sim::client_data[control_bridge_transport::reply_id];
    assert(reply_bytes.size() == sizeof(reply)); std::memcpy(&reply, reply_bytes.data(), sizeof(reply));
    assert(bridge_valid(reply) && reply.kind == bridge_kind::reply && reply.request == 314 &&
        !std::strcmp(reply.backend, hello.backend) && !std::strcmp(reply.native, session) && reply.blocked == select_quadrant);
    assert(reply.capabilities == 59 && !(reply.capabilities & 4)); // AP/cockpit transports are not remote physical-output authority.
    auto* bridge_file = std::fopen("artifacts/efb-control-test/tests/native-bridge-status.bin", "wb");
    assert(bridge_file && std::fwrite(&reply, 1, sizeof(reply), bridge_file) == sizeof(reply)); std::fclose(bridge_file);
    // Optional SDK publications are retained until their ordered checkpoint;
    // checkpoint callbacks must not acknowledge/change a hardware mask.
    for (unsigned index = 0; index < 20; ++index) assert(native_bridge.publish(bridge_status()));
    const auto checkpoint = fake_sim::request_id;
    assert(checkpoint >= 0x74000000u);
    SIMCONNECT_RECV_SYSTEM_STATE bridge_checkpoint{}; bridge_checkpoint.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    bridge_checkpoint.dwRequestID = checkpoint;
    dispatch(&bridge_checkpoint, sizeof(bridge_checkpoint), nullptr);
    assert(native_bridge.available && gate.ready && gate.blocked == select_quadrant && !gate.applying);
    hello.kind = static_cast<bridge_kind>(4); // No control command supported by this channel.
    std::memcpy(&bridge_data->dwData, &hello, sizeof(hello));
    const auto serial_before = bridge_serial;
    bridge_dispatch(bridge_data, packet_bytes, nullptr);
    assert(bridge_serial == serial_before && gate.blocked == select_quadrant);
    bridge_exception.dwSendID = fake_sim::last_packet; bridge_exception.dwException = 3;
    dispatch(&bridge_exception, sizeof(bridge_exception), nullptr);
    assert(!native_bridge.available && gate.ready && gate.quadrant_blocked && connection);
    assert(!fake_sim::groups.empty() && gate.quadrant_blocked);
    assert(native_bridge.initialize(connection));
    for (unsigned index = 0; index < 140; ++index) native_bridge.publish(bridge_status()); // No fake checkpoint delivered.
    assert(!native_bridge.available && gate.ready && gate.quadrant_blocked && connection);
    std::cout << "Actual-module discovery telemetry: bounded packet, fresh challenge reply, four-group observation, persistent area reuse and IPC/control failure isolation passed.\n";
    module_deinit();

    // Real module lifecycle plus real profile files: a fresh module receives
    // only a scale hint, never old ownership/position/readiness.
    char test_identity[1024]{};
    assert(profile_identity("SimObjects/Airplanes/ProfileTestBaron/aircraft.cfg", 100, test_identity));
    char test_prefix[512]{};
    std::snprintf(test_prefix, sizeof(test_prefix), "%spickup-%08x-2", FSC_PROFILE_ROOT,
        profile_hash(test_identity, std::strlen(test_identity)));
    auto cleanup = [&]() {
        for (unsigned slot = 0; slot < 2; ++slot) {
            char path[600]{};
            std::snprintf(path, sizeof(path), "%s.%u.bin", test_prefix, slot);
            std::remove(path);
        }
    };
    auto identify = [&](DWORD id) {
        SIMCONNECT_RECV_SYSTEM_STATE state{};
        state.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
        state.dwRequestID = id;
        std::snprintf(state.szString, sizeof(state.szString), "%s", "SimObjects/Airplanes/ProfileTestBaron/aircraft.cfg");
        dispatch(&state, sizeof(state), nullptr);
    };
    auto restart = [&]() {
        adapter.groups_created = false;
        error[0] = '\0';
        module_init(); open_connection(); send("2|profile-test|1|read"); confirm_configuration();
        assert(gate.ready && !gate.steering_blocked && !gate.quadrant_blocked);
    };
    cleanup();
    restart();
    identify(identity_request);
    std::memset(targets, 0, sizeof(targets));
    targets[axis_count] = 2;
    positions(targets);
    assert(profile_loaded && !profile_failed);
    for (const char* lever : levers) hardware(lever, 0);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) {
        targets[axis] = 50;
        pickup.inputs[axis].changed -= 0.2;
    }
    positions(targets);
    for (const char* lever : levers) hardware(lever, 8192);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) {
        targets[axis] = 75;
        pickup.inputs[axis].changed -= 0.2;
    }
    for (unsigned frame = 0; frame < 3; ++frame) positions(targets);
    save_profile();
    assert(profile.generation == 1 && !profile_dirty && !profile_failed);
    send("2|profile-test|2|set|quadrant|mock"); confirm_configuration();
    const DWORD old_identity_request = identity_request;
    module_deinit();
    restart();
    identify(old_identity_request); // A late reply cannot choose a new aircraft profile.
    assert(!aircraft_identity[0]);
    identify(identity_request);
    positions(targets);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) {
        assert(pickup.inputs[axis].saved_candidates == 1);
        assert(!pickup.inputs[axis].detected && pickup.inputs[axis].calibrated);
        assert(pickup.input_position(axis) < 0);
    }
    for (const char* lever : levers) hardware(lever, 8192);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) pickup.inputs[axis].changed -= 0.2;
    for (unsigned frame = 0; frame < 3; ++frame) positions(targets);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis) assert(pickup.inputs[axis].calibrated);
    targets[axis_count] = 0; // Transient loading frame must not erase durable mappings.
    positions(targets);
    assert(profile.mapping[steering_axis_count].scale == 1 && !profile_dirty);
    targets[axis_count] = 2;
    positions(targets);
    send("2|profile-test|2|set|quadrant|mock"); confirm_configuration();
    dispatch(&loaded, sizeof(loaded), nullptr); // Same-aircraft reload: Me, saved scales, fresh readings required.
    confirm_configuration(); identify(identity_request); positions(targets);
    assert(!gate.quadrant_blocked && pickup.inputs[steering_axis_count].saved_candidates == 1);
    assert(pickup.inputs[steering_axis_count].calibrated && !pickup.inputs[steering_axis_count].detected && !(gate.waiting() & select_quadrant));
    // Mappings remain learned without movement. Explicit Relearn clears disk too.
    send("2|profile-test|3|learn|quadrant|reset");
    assert(!pickup.inputs[steering_axis_count].saved_candidates && !profile_dirty);
    module_deinit();
    restart(); identify(identity_request); positions(targets);
    for (size_t axis = steering_axis_count; axis < steering_axis_count + 6; ++axis)
        assert(!pickup.inputs[axis].saved_candidates && !pickup.inputs[axis].calibrated);
    // Storage failure remains a diagnostic, never a reason to release masks.
    send("2|profile-test|4|set|quadrant|mock"); confirm_configuration();
    std::snprintf(profile_prefix, sizeof(profile_prefix), "%s", "artifacts/efb-control-test/no-such-folder/profile");
    profile_dirty = true;
    save_profile();
    assert(profile_failed && gate.ready && gate.quadrant_blocked && connection);
    module_deinit();
    cleanup();
    restart();
    SIMCONNECT_RECV_SYSTEM_STATE baron_identity{};
    baron_identity.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    baron_identity.dwRequestID = identity_request;
    std::snprintf(baron_identity.szString, sizeof(baron_identity.szString), "%s", "SimObjects\\Airplanes\\bksq-aircraft-baronpropress\\aircraft.CFG");
    dispatch(&baron_identity, sizeof(baron_identity), nullptr);
    assert(aircraft_definitions && has_cockpit_references(aircraft_definitions));
    profile_failed = true; // No durable data is written by this read-back-only fixture.
    std::memset(targets, 0, sizeof(targets)); targets[axis_count] = 2;
    targets[11] = targets[14] = 75.51088;
    targets[axis_count + 2] = targets[axis_count + 3] = 100;
    fake_vars::state().value[0] = fake_vars::state().value[1] = 100;
    positions(targets);
    assert(pickup.targets[11] == 1 && pickup.targets[14] == 1);
    targets[11] = targets[14] = 40.11333;
    targets[axis_count + 2] = targets[axis_count + 3] = 53.125;
    fake_vars::state().value[0] = fake_vars::state().value[1] = 53.125;
    positions(targets);
    assert(pickup.targets[11] == 0.53125 && pickup.targets[14] == 0.53125);
    fake_vars::state().percent_storage[0] = fake_vars::state().percent_storage[1] = false;
    fake_vars::state().value[0] = fake_vars::state().value[1] = .53125;
    positions(targets);
    assert(pickup.targets[11] == .53125 && pickup.targets[14] == .53125);
    for (const auto& definition : fake_sim::definitions[target_definition]) assert(definition.first.find("L:") != 0);
    module_deinit();
    std::cout << "Actual-module saved-control restart, fresh validation, durable reset and storage-failure isolation passed.\n";
    std::cout << "Native protocol, ownership, masking, reset and failure tests passed.\n";
}
