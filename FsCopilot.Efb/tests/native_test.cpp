// Exercises the actual WASM module against a small SimConnect contract fake.
// A pass verifies our logic, not the simulator's implementation of masking.
#include <cassert>
#include <iostream>
#include "../Wasm/control_test.cpp"

void confirm_configuration()
{
    SIMCONNECT_RECV_SYSTEM_STATE state{};
    state.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    state.dwRequestID = fake_sim::request_id;
    dispatch(&state, sizeof(state), nullptr);
}

void send(const char* packet) { receive_command(packet, static_cast<unsigned int>(std::strlen(packet)), nullptr); }

int main()
{
    command parsed{};
    const char* good = "1|test-client|4294967295|set|quadrant|mock";
    assert(parse_command(good, std::strlen(good), parsed));
    assert(parsed.request == UINT32_MAX && parsed.target == group::quadrant && parsed.blocked);
    assert(parse_command("1|client|1|read", 16, parsed)); // Includes terminating NUL.
    const char* bad[] = { "", "2|client|1|read", "1|client|0|read", "1|client|4294967296|read", "1|client|1x|read",
        "1|client|1|set|quadrant|maybe", "1|client|1|set|unknown|mock", "1|client|1|read|extra", "1|bad\"id|1|read",
        "1||1|read", "1|client|1|restore|extra|mock", "1|client|1|set|quadrant|mock|extra" };
    for (const auto* packet : bad) assert(!parse_command(packet, std::strlen(packet), parsed));
    const char embedded[] = "1|client|1|read\0hidden";
    assert(!parse_command(embedded, sizeof(embedded) - 1, parsed));
    char oversized[129]{};
    assert(!parse_command(oversized, sizeof(oversized), parsed));

    module_init();
    assert(connection && !gate.ready && gate.applying);
    assert(fake_sim::names.size() == event_count);
    for (const auto& group : fake_sim::groups) for (const auto& event : group.second) assert(!event.second);
    confirm_configuration();
    assert(gate.ready && !gate.steering_blocked && !gate.quadrant_blocked);
    assert(fake_sim::priorities[steering_group] == SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE);

    send("1|client|2|set|quadrant|mock");
    assert(gate.applying && !gate.quadrant_blocked); // No optimistic state.
    for (const auto& entry : fake_sim::groups[quadrant_group]) assert(entry.second);
    for (const auto& entry : fake_sim::groups[steering_group]) assert(!entry.second);
    SIMCONNECT_RECV_SYSTEM_STATE unrelated{};
    unrelated.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    unrelated.dwRequestID = first_barrier;
    dispatch(&unrelated, sizeof(unrelated), nullptr);
    assert(gate.applying);
    send("1|other-client|3|set|steering|mock");
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

    send("1|client|4|set|steering|mock");
    confirm_configuration();
    assert(gate.steering_blocked && gate.quadrant_blocked);
    send("1|client|5|restore");
    assert(gate.applying && gate.steering_blocked && gate.quadrant_blocked);
    confirm_configuration();
    assert(!gate.steering_blocked && !gate.quadrant_blocked);
    for (const auto& group : fake_sim::groups) for (const auto& event : group.second) assert(!event.second);

    send("1|client|6|set|quadrant|mock");
    confirm_configuration();
    SIMCONNECT_RECV_EVENT_FILENAME loaded{};
    loaded.dwID = SIMCONNECT_RECV_ID_EVENT_FILENAME;
    loaded.uEventID = aircraft_loaded;
    dispatch(&loaded, sizeof(loaded), nullptr);
    assert(!gate.ready && gate.applying);
    confirm_configuration();
    assert(!gate.quadrant_blocked && !gate.steering_blocked && quadrant_activity.seen == 0);

    send("1|client|7|set|quadrant|mock");
    SIMCONNECT_RECV_EXCEPTION failure{};
    failure.dwID = SIMCONNECT_RECV_ID_EXCEPTION;
    failure.dwException = 3;
    dispatch(&failure, sizeof(failure), nullptr);
    assert(!connection && !gate.ready && !gate.applying && fake_sim::groups.empty());
    const unsigned closed = fake_sim::closes;
    confirm_configuration();
    assert(!gate.ready); // A late reply cannot revive a failed bridge.
    send("1|client|8|set|steering|mock");
    assert(fake_sim::closes == closed && !connection);

    // A synchronous configuration failure must also close/release partial masks.
    adapter.groups_created = false;
    error[0] = '\0';
    module_init();
    confirm_configuration();
    fake_sim::fail_configuration = true;
    send("1|client|9|set|steering|mock");
    assert(!connection && !gate.ready && fake_sim::groups.empty());
    fake_sim::fail_configuration = false;

    adapter.groups_created = false;
    error[0] = '\0';
    module_init();
    change_started = seconds() - 10;
    Update_StandAlone(0);
    assert(!connection && !gate.ready && fake_sim::groups.empty());
    module_deinit();
    std::cout << "Native protocol, ownership, masking, reset and failure tests passed.\n";
}
