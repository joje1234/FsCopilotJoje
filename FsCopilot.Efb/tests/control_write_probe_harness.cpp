// Compiles the actual diagnostic with the real SDK declarations and local
// implementations of every SDK call. No SimConnect library/server is linked.
#define FSC_OUTPUT_PROBE_TEST_CLOCK
double output_probe_test_now();
#define main output_probe_main
#include "control_write_probe.cpp"
#undef main
#include <cassert>
#include <deque>
#include <map>
#include <vector>

namespace fake_sdk {
double clock = 0;
std::string scenario;
bool sent_open = false, sent_reply = false, mask = false, pending_hardware = false;
bool session_subscribed = false;
uint64_t session_serial = 0;
unsigned opens = 0, closes = 0, clears = 0, writes = 0;
DWORD sent = 1;
std::deque<DWORD> barriers;
std::map<DWORD, std::string> mapped;
bridge_packet state{}, hello{};
double first = 0.25, second = 0.75;
template<class Packet> void client_data(DispatchProc callback, void* context, DWORD request, const Packet& value) {
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_CLIENT_DATA) + sizeof(value));
    auto* data = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(bytes.data()); data->dwID = SIMCONNECT_RECV_ID_CLIENT_DATA;
    data->dwRequestID = request; std::memcpy(&data->dwData, &value, sizeof(value));
    const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&data->dwData) - reinterpret_cast<char*>(data) + sizeof(value));
    callback(data, size, context);
}
}
double output_probe_test_now() { return fake_sdk::clock; }
SIMCONNECTAPI SimConnect_Open(HANDLE* value, LPCSTR, HWND, DWORD, HANDLE, DWORD) { ++fake_sdk::opens; *value = reinterpret_cast<HANDLE>(1); return S_OK; }
SIMCONNECTAPI SimConnect_Close(HANDLE) { ++fake_sdk::closes; fake_sdk::mask = false; return S_OK; }
SIMCONNECTAPI SimConnect_GetLastSentPacketID(HANDLE, DWORD* value) { *value = fake_sdk::sent; return S_OK; }
SIMCONNECTAPI SimConnect_MapClientDataNameToID(HANDLE, const char*, SIMCONNECT_CLIENT_DATA_ID) { ++fake_sdk::sent; return S_OK; }
SIMCONNECTAPI SimConnect_AddToClientDataDefinition(HANDLE, SIMCONNECT_CLIENT_DATA_DEFINITION_ID, DWORD, DWORD, float, DWORD) { ++fake_sdk::sent; return S_OK; }
SIMCONNECTAPI SimConnect_RequestClientData(HANDLE, SIMCONNECT_CLIENT_DATA_ID, SIMCONNECT_DATA_REQUEST_ID request, SIMCONNECT_CLIENT_DATA_DEFINITION_ID, SIMCONNECT_CLIENT_DATA_PERIOD, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG, DWORD, DWORD, DWORD) {
    if (request == local_session_request) fake_sdk::session_subscribed = true;
    ++fake_sdk::sent; return S_OK;
}
SIMCONNECTAPI SimConnect_SetClientData(HANDLE, SIMCONNECT_CLIENT_DATA_ID area, SIMCONNECT_CLIENT_DATA_DEFINITION_ID, SIMCONNECT_CLIENT_DATA_SET_FLAG, DWORD, DWORD size, void* data) {
    assert(area == command_area && size == sizeof(bridge_packet));
    std::memcpy(&fake_sdk::hello, data, sizeof(fake_sdk::hello));
    assert(fake_sdk::hello.kind == bridge_kind::hello && bridge_valid(fake_sdk::hello)); ++fake_sdk::sent; return S_OK;
}
SIMCONNECTAPI SimConnect_AddToDataDefinition(HANDLE, SIMCONNECT_DATA_DEFINITION_ID, const char*, const char*, SIMCONNECT_DATATYPE, float, DWORD) { ++fake_sdk::sent; return S_OK; }
SIMCONNECTAPI SimConnect_RequestDataOnSimObject(HANDLE, SIMCONNECT_DATA_REQUEST_ID, SIMCONNECT_DATA_DEFINITION_ID, SIMCONNECT_OBJECT_ID, SIMCONNECT_PERIOD, SIMCONNECT_DATA_REQUEST_FLAG, DWORD, DWORD, DWORD) { ++fake_sdk::sent; return S_OK; }
SIMCONNECTAPI SimConnect_MapClientEventToSimEvent(HANDLE, SIMCONNECT_CLIENT_EVENT_ID id, const char* name) { fake_sdk::mapped[id] = name; ++fake_sdk::sent; return S_OK; }
SIMCONNECTAPI SimConnect_AddClientEventToNotificationGroup(HANDLE, SIMCONNECT_NOTIFICATION_GROUP_ID group_id, SIMCONNECT_CLIENT_EVENT_ID id, BOOL maskable) {
    assert(group_id == mask_group && maskable && event_selection(events[id - first_event].target, events[id - first_event].name) == select_throttle);
    fake_sdk::mask = true; ++fake_sdk::sent; return fake_sdk::scenario == "mask-failure" ? E_FAIL : S_OK;
}
SIMCONNECTAPI SimConnect_SetNotificationGroupPriority(HANDLE, SIMCONNECT_NOTIFICATION_GROUP_ID group_id, DWORD priority) {
    assert(group_id == mask_group && priority == SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE); ++fake_sdk::sent; return S_OK;
}
SIMCONNECTAPI SimConnect_ClearNotificationGroup(HANDLE, SIMCONNECT_NOTIFICATION_GROUP_ID group_id) {
    assert(group_id == mask_group); ++fake_sdk::clears; fake_sdk::mask = false; ++fake_sdk::sent;
    return fake_sdk::scenario == "clear-failure" ? E_FAIL : S_OK;
}
SIMCONNECTAPI SimConnect_RequestSystemState(HANDLE, SIMCONNECT_DATA_REQUEST_ID request, const char*) {
    ++fake_sdk::sent;
    if (!(fake_sdk::scenario == "mask-timeout" && request == mask_barrier)) fake_sdk::barriers.push_back(request);
    if (request == mask_barrier && fake_sdk::scenario == "hardware") fake_sdk::pending_hardware = true;
    return S_OK;
}
SIMCONNECTAPI SimConnect_TransmitClientEvent_EX1(HANDLE, SIMCONNECT_OBJECT_ID object, SIMCONNECT_CLIENT_EVENT_ID event,
    SIMCONNECT_NOTIFICATION_GROUP_ID priority, SIMCONNECT_EVENT_FLAG flags, DWORD raw, DWORD, DWORD, DWORD, DWORD) {
    const bool standard = fake_sdk::scenario.find("standard-") == 0;
    assert(fake_sdk::mask && object == SIMCONNECT_OBJECT_ID_USER && priority == (standard ? SIMCONNECT_GROUP_PRIORITY_STANDARD : SIMCONNECT_GROUP_PRIORITY_DEFAULT) && flags == SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY);
    assert(fake_sdk::mapped[event] == "THROTTLE1_AXIS_SET_EX1" || fake_sdk::mapped[event] == "THROTTLE2_AXIS_SET_EX1");
    ++fake_sdk::writes; ++fake_sdk::sent;
    if (fake_sdk::scenario == "partial-write" && fake_sdk::writes == 4) return E_FAIL;
    auto position = (static_cast<int32_t>(raw) + 16384) / 32768.0;
    if (!((fake_sdk::scenario == "ignored" || fake_sdk::scenario == "standard-ignored") && fake_sdk::writes >= 3 && fake_sdk::writes <= 4)) {
        if (fake_sdk::mapped[event] == "THROTTLE1_AXIS_SET_EX1") fake_sdk::first = position; else fake_sdk::second = position;
    }
    if (fake_sdk::scenario == "recaptured" || fake_sdk::scenario == "standard-echo") fake_sdk::pending_hardware = true;
    return S_OK;
}
SIMCONNECTAPI SimConnect_CallDispatch(HANDLE, DispatchProc callback, void* context) {
    using namespace fake_sdk;
    fake_sdk::clock += 0.05;
    if (!sent_open) { SIMCONNECT_RECV value{}; value.dwID = SIMCONNECT_RECV_ID_OPEN; callback(&value, sizeof(value), context); sent_open = true; }
    if (session_subscribed && scenario != "candidate-missing") {
        session_packet session{}; std::snprintf(session.native, sizeof(session.native), "%s", scenario == "candidate-wrong-native" ? "def456" : state.native);
        session.serial = scenario == "candidate-stale" ? 1 : ++session_serial;
        if (scenario == "candidate-armed") { session.phase = session_phase::armed; std::memset(session.consent, '1', 32); }
        if (scenario == "candidate-frozen") session.flags = session_world_frozen;
        if (scenario == "candidate-active" || (scenario == "candidate-lost" && writes >= 3)) {
            session.phase = session_phase::active; session.epoch = session.revision = 1;
            std::memset(session.backend, 'a', 32); std::memset(session.session, 'b', 32); std::memset(session.consent, 'c', 32);
        }
        assert(session_valid(session)); client_data(callback, context, local_session_request, session);
    }
    while (!barriers.empty()) {
        SIMCONNECT_RECV_SYSTEM_STATE value{}; value.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE; value.dwRequestID = barriers.front(); barriers.pop_front();
        callback(reinterpret_cast<SIMCONNECT_RECV*>(&value), sizeof(value), context);
    }
    if (pending_hardware) {
        pending_hardware = false; SIMCONNECT_RECV_EVENT_EX1 value{}; value.dwID = SIMCONNECT_RECV_ID_EVENT_EX1;
        for (const auto& event : mapped) if (event.second == "THROTTLE1_AXIS_SET_EX1") { value.uEventID = event.first; break; }
        callback(reinterpret_cast<SIMCONNECT_RECV*>(&value), sizeof(value), context);
    }
    if (mask && (scenario == "jitter" || scenario == "noise-drift")) {
        const int32_t raw = scenario == "noise-drift" && writes >= 3 ? -8520 : -8322;
        SIMCONNECT_RECV_EVENT value{}; value.dwID = SIMCONNECT_RECV_ID_EVENT;
        for (const auto& event : mapped) if (event.second == "THROTTLE_AXIS_SET_EX1") { value.uEventID = event.first; break; }
        value.dwData = static_cast<DWORD>(raw);
        callback(reinterpret_cast<SIMCONNECT_RECV*>(&value), sizeof(value), context);
        state.input[9] = state.input[12] = (raw + 16384) / 32768.0;
    }
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + 4 * sizeof(double));
    auto* guard_data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data()); guard_data->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA;
    guard_data->dwRequestID = guard_request; guard_data->dwDefineCount = 4;
    double values[4] = {1, 0.04, 0, scenario == "no-brake" ? 0.0 : 1.0};
    std::memcpy(&guard_data->dwData, values, sizeof(values));
    callback(guard_data, static_cast<DWORD>(reinterpret_cast<char*>(&guard_data->dwData) - reinterpret_cast<char*>(guard_data) + sizeof(values)), context);
    state.targets[9] = first; state.targets[12] = second;
    if (scenario == "echo") { state.input[9] = first; state.input[12] = second; }
    if (hello.request && !sent_reply) {
        auto reply = state; reply.kind = bridge_kind::reply; reply.request = hello.request; std::memcpy(reply.backend, hello.backend, sizeof(reply.backend));
        reply.serial = ++state.serial; client_data(callback, context, reply_request, reply); sent_reply = true;
    }
    ++state.serial; client_data(callback, context, status_request, state);
    return S_OK;
}
int main(int argc, char** argv) {
    _set_error_mode(_OUT_TO_STDERR);
    _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
    assert(argc == 2); fake_sdk::scenario = argv[1];
    auto& state = fake_sdk::state;
    std::snprintf(state.native, sizeof(state.native), "abc123");
    std::snprintf(state.aircraft, sizeof(state.aircraft), "simobjects/airplanes/bksq-aircraft-baronpropress/aircraft.cfg");
    std::snprintf(state.adapter, sizeof(state.adapter), "bksq-baron58p-cockpit-v1");
    state.flags = bridge_ready | bridge_targets_fresh; state.engines = 2; state.required = state.learned = (1u << 9) | (1u << 12);
    if (fake_sdk::scenario.find("candidate-") == 0) { state.connection_api = 1; state.capabilities = 27; }
    state.input[9] = 0.25; state.input[12] = 0.75;
    if (fake_sdk::scenario == "jitter" || fake_sdk::scenario == "noise-drift") {
        fake_sdk::second = fake_sdk::first; state.input[12] = state.input[9];
    }
    control_profile profile{}; profile.engines = 2; profile.generation = 1; std::snprintf(profile.aircraft, sizeof(profile.aircraft), "%s", state.aircraft);
    for (auto axis : {9, 12}) { profile.mapping[axis].scale = 1; std::snprintf(profile.mapping[axis].event, sizeof(profile.mapping[axis].event), "THROTTLE_AXIS_SET_EX1"); }
    profile.checksum = profile_hash(&profile, offsetof(control_profile, checksum));
    char prefix[600]{}; std::snprintf(prefix, sizeof(prefix), "artifacts/native-output-check/tests/pickup-%08x-2", profile_hash(profile.aircraft, std::strlen(profile.aircraft)));
    if (fake_sdk::scenario != "missing-profile") assert(write_profile(prefix, profile));
    else { for (unsigned slot = 0; slot < 2; ++slot) { char path[600]{}; std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, slot); std::remove(path); } }
    control_sim_output refused;
    refused.initialize(reinterpret_cast<HANDLE>(1), first_event, SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE + 1);
    assert(!refused.available); // Reassigned mask priorities must never be guessed.
    const char* arguments[] = {"control_write_probe", fake_sdk::scenario == "same" ? "--same-state-ground-test" : "--nudge-and-restore", "--profile-root=artifacts/native-output-check/tests", "--group=throttle", "--standard-priority"};
    const auto result = output_probe_main(fake_sdk::scenario.find("standard-") == 0 ? 5 : 4, const_cast<char**>(arguments));
    assert(fake_sdk::opens == 1 && fake_sdk::closes == 1 && !fake_sdk::mask);
    const bool successful = fake_sdk::scenario == "success" || fake_sdk::scenario == "standard-success" || fake_sdk::scenario == "same" || fake_sdk::scenario == "jitter" || fake_sdk::scenario == "candidate-idle";
    assert((result == 0) == successful);
    const bool refused_candidate = fake_sdk::scenario == "candidate-armed" || fake_sdk::scenario == "candidate-active" ||
        fake_sdk::scenario == "candidate-missing" || fake_sdk::scenario == "candidate-wrong-native" || fake_sdk::scenario == "candidate-frozen";
    if (fake_sdk::scenario == "no-brake" || fake_sdk::scenario == "missing-profile" || refused_candidate) assert(!fake_sdk::writes && !fake_sdk::clears);
    else assert(fake_sdk::clears >= 1);
    if (fake_sdk::scenario == "success" || fake_sdk::scenario == "standard-success" || fake_sdk::scenario == "jitter" || fake_sdk::scenario == "candidate-idle") assert(fake_sdk::writes == 6);
    if (fake_sdk::scenario == "noise-drift") assert(fake_sdk::writes == 4);
    if (fake_sdk::scenario == "same") assert(fake_sdk::writes == 2);
    if (fake_sdk::scenario == "hardware" || fake_sdk::scenario == "mask-failure" || fake_sdk::scenario == "mask-timeout") assert(fake_sdk::writes == 0);
    if (fake_sdk::scenario == "ignored" || fake_sdk::scenario == "standard-ignored" || fake_sdk::scenario == "echo") assert(fake_sdk::writes == 6 && fake_sdk::first == 0.25 && fake_sdk::second == 0.75);
    assert(fake_sdk::clock <= 14.1);
    std::printf("PASS actual diagnostic SDK-fake scenario %s: exit=%d, writes=%u, masks cleared=%u, connection closed=%u.\n", argv[1], result, fake_sdk::writes, fake_sdk::clears, fake_sdk::closes);
    return 0;
}
