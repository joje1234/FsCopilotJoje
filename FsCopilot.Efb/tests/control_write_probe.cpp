// Bounded, explicit diagnostic of the production SDK output adapter on the
// accepted SOLO Baron module. Never starts a shared session or changes physics.
#define WIN32_LEAN_AND_MEAN
#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <SimConnect.h>
#include <chrono>
#include <algorithm>
#include <string>
#include "same_state_control_check.h"
#include "../Wasm/control_runtime.h"
#include "../Wasm/control_sim_output.h"
#include "../Wasm/control_session_protocol.h"

namespace {
using namespace fsc_controls;
constexpr DWORD status_area = 0x7c00, command_area = 0x7c01, reply_area = 0x7c02;
constexpr DWORD status_request = 0x7c10, reply_request = 0x7c11, guard_definition = 0x7d00, guard_request = 0x7d01;
constexpr DWORD local_session_area = 0x7c03, local_session_request = 0x7c12;
constexpr DWORD mask_group = 0x7e00, mask_barrier = 0x7e01, write_barrier = 0x7e02, clear_barrier = 0x7e03, first_event = 0x8000;
#ifndef FSC_OUTPUT_PROBE_TEST_CLOCK
const auto started = std::chrono::steady_clock::now();
#endif
HANDLE connection{};
control_sim_output output;
bridge_packet observed{};
output_check_guard guard;
char backend[40]{}, native[24]{};
bool failed = false, opened = false, masked = false, mask_confirmed = false, write_confirmed = false, clear_confirmed = false;
unsigned observations = 0, hardware_events = 0, interfering_events = 0, noise_events = 0, write_attempts = 0;
same_state_control_check* active_check = nullptr;
double observed_at = -1;
session_packet local_session{};
double local_session_at = -1;
uint32_t selection = 0;
double now() {
#ifdef FSC_OUTPUT_PROBE_TEST_CLOCK
    return ::output_probe_test_now();
#else
    return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count();
#endif
}
bool check(HRESULT result, const char* operation) {
    if (SUCCEEDED(result)) return true;
    std::printf("SDK call failed: %s (0x%08lx).\n", operation, static_cast<unsigned long>(result)); failed = true; return false;
}
void CALLBACK dispatch(SIMCONNECT_RECV* packet, DWORD size, void*) {
    if (packet->dwID == SIMCONNECT_RECV_ID_OPEN) opened = true;
    else if (packet->dwID == SIMCONNECT_RECV_ID_CLIENT_DATA) {
        const auto* data = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(packet);
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (data->dwRequestID == local_session_request) {
            if (size < offset + sizeof(session_packet)) return;
            session_packet value{}; std::memcpy(&value, &data->dwData, sizeof(value));
            if (session_valid(value) && value.kind == session_kind::status && value.serial > local_session.serial) {
                local_session = value; local_session_at = now();
            }
            return;
        }
        if ((data->dwRequestID != status_request && data->dwRequestID != reply_request) || size < offset + sizeof(bridge_packet)) return;
        bridge_packet value{}; std::memcpy(&value, &data->dwData, sizeof(value));
        if (!bridge_valid(value)) return;
        if (value.kind == bridge_kind::reply && value.request == 1 && !std::strcmp(value.backend, backend)) {
            if (native[0] && std::strcmp(native, value.native)) { failed = true; return; }
            std::memcpy(native, value.native, sizeof(native));
        }
        if (!native[0] || std::strcmp(native, value.native) || (value.kind != bridge_kind::reply && value.kind != bridge_kind::status) ||
            value.serial <= observed.serial) return;
        observed = value; observed_at = now(); ++observations;
    } else if (packet->dwID == SIMCONNECT_RECV_ID_SIMOBJECT_DATA) {
        const auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(packet);
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (data->dwRequestID == guard_request && data->dwDefineCount == 4 && size >= offset + 4 * sizeof(double)) {
            double values[4]{}; std::memcpy(values, &data->dwData, sizeof(values));
            guard = {now(), values[0], values[1], values[2], values[3]};
        }
    } else if (packet->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) {
        const auto request = reinterpret_cast<SIMCONNECT_RECV_SYSTEM_STATE*>(packet)->dwRequestID;
        if (request == mask_barrier) mask_confirmed = true;
        if (request == write_barrier) write_confirmed = true;
        if (request == clear_barrier) clear_confirmed = true;
        output.confirm(request);
    } else if (packet->dwID == SIMCONNECT_RECV_ID_EVENT || packet->dwID == SIMCONNECT_RECV_ID_EVENT_EX1) {
        const auto event = packet->dwID == SIMCONNECT_RECV_ID_EVENT ? reinterpret_cast<SIMCONNECT_RECV_EVENT*>(packet)->uEventID :
            reinterpret_cast<SIMCONNECT_RECV_EVENT_EX1*>(packet)->uEventID;
        if (event >= first_event && event < first_event + event_count &&
            event_selection(events[event - first_event].target, events[event - first_event].name) == selection) {
            ++hardware_events;
            const auto raw = packet->dwID == SIMCONNECT_RECV_ID_EVENT ? reinterpret_cast<SIMCONNECT_RECV_EVENT*>(packet)->dwData :
                reinterpret_cast<SIMCONNECT_RECV_EVENT_EX1*>(packet)->dwData0;
            const bool noise = active_check && active_check->hardware_noise(events[event - first_event].name, static_cast<int32_t>(raw));
            if (noise) ++noise_events; else ++interfering_events;
            std::printf("%.3f %s %s raw=%ld extended=%d after-write-attempts=%u\n", now(), noise ? "NOISE_EVENT" : "MASK_EVENT", events[event - first_event].name,
                static_cast<long>(static_cast<int32_t>(raw)), packet->dwID == SIMCONNECT_RECV_ID_EVENT_EX1, write_attempts);
        }
    } else if (packet->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* error = reinterpret_cast<SIMCONNECT_RECV_EXCEPTION*>(packet);
        std::printf("SDK exception %lu (send %lu, index %lu).\n", error->dwException, error->dwSendID, error->dwIndex);
        output.exception(*error); failed = true;
    } else if (packet->dwID == SIMCONNECT_RECV_ID_QUIT) failed = true;
}
void pump() {
    if (!check(SimConnect_CallDispatch(connection, dispatch, nullptr), "Dispatch")) return;
#ifndef FSC_OUTPUT_PROBE_TEST_CLOCK
    Sleep(10);
#endif
}
struct lifetime {
    ~lifetime() {
        // An unsuccessful check still clears only THIS client's temporary mask.
        // Never release or overwrite the installed module's ownership settings.
        if (connection) {
            if (masked && !clear_confirmed) (void)SimConnect_ClearNotificationGroup(connection, mask_group);
            (void)SimConnect_Close(connection); connection = nullptr;
        }
    }
};
bool unbound_session_fresh() {
    if (local_session_at < 0 || now() < local_session_at || now() - local_session_at > .25 || !native[0] ||
        std::strcmp(local_session.native, native) || local_session.phase != session_phase::idle || local_session.backend[0] ||
        local_session.session[0] || local_session.consent[0] || local_session.blocked || local_session.attached || local_session.pending ||
        (local_session.flags & session_world_frozen)) return false;
    for (const auto owner : local_session.owner) if (owner != writer_owner::none) return false;
    return true;
}
const output_check_guard& current_guard() { guard.unbound_session_fresh = unbound_session_fresh(); return guard; }
bool fresh_guard() {
    (void)current_guard();
    return (observed.connection_api != 1 || guard.unbound_session_fresh) && guard.at >= 0 && now() >= guard.at &&
        now() - guard.at <= .25 && guard.ground == 1 && guard.parking_brake == 1 && std::isfinite(guard.knots) && std::abs(guard.knots) <= .1;
}
bool write_and_observe(const output_action* actions, size_t count, same_state_control_check& test, bool nudged, double mask_until) {
    const auto serial = observed.serial; write_confirmed = false;
    if (failed || interfering_events || !fresh_guard()) return false;
    ++write_attempts;
    for (size_t index = 0; index < count; ++index)
        std::printf("%.3f WRITE_ATTEMPT %s raw=%ld nudged=%d\n", now(), actions[index].event, static_cast<long>(actions[index].raw), nudged);
    if (!output.write(actions, count) ||
        !check(SimConnect_RequestSystemState(connection, write_barrier, "Sim"), "Output checkpoint")) return false;
    unsigned matches = 0; uint64_t previous = serial;
    while (!failed && !interfering_events && fresh_guard() && now() < mask_until && matches < 3) {
        pump();
        if (observed.serial > previous) {
            previous = observed.serial;
            if (write_confirmed && test.current(observed, now(), observed_at, current_guard(), true, nudged)) ++matches;
            else matches = 0;
        }
    }
    return !failed && !interfering_events && write_confirmed && matches >= 3;
}
}
int main(int argc, char** argv) {
    std::string root; group target = group::throttle; bool explicit_mode = false, nudge = false, group_option = false, mixture_set_route = false, standard_priority = false;
    for (int index = 1; index < argc; ++index) {
        const std::string arg = argv[index];
        if (arg == "--same-state-ground-test" || arg == "--nudge-and-restore") {
            if (explicit_mode) return 2;
            explicit_mode = true; nudge = arg == "--nudge-and-restore";
        } else if (arg == "--mixture-set-route" && !mixture_set_route) mixture_set_route = true;
        else if (arg == "--standard-priority" && !standard_priority) standard_priority = true;
        else if (arg.find("--profile-root=") == 0 && root.empty()) root = arg.substr(15);
        else if (!group_option && (arg == "--group=throttle" || arg == "--group=rpm" || arg == "--group=mixture")) {
            group_option = true; target = arg == "--group=throttle" ? group::throttle : arg == "--group=rpm" ? group::propeller : group::mixture;
        }
        else { std::printf("Unknown option: %s\n", arg.c_str()); return 2; }
    }
    if (!explicit_mode || root.empty() || root.length() > 480) { std::puts("Requires --same-state-ground-test OR --nudge-and-restore, --profile-root=<WASM work directory>, optional --group=throttle|rpm|mixture."); return 2; }
    if (mixture_set_route && target != group::mixture) { std::puts("--mixture-set-route requires --group=mixture."); return 2; }
    selection = group_selection(target);
    if (!check(SimConnect_Open(&connection, "FS Copilot bounded control-output diagnostic", nullptr, 0, nullptr, 0), "Open")) return 2;
    lifetime cleanup;
    const DWORD areas[] = {status_area, command_area, reply_area};
    const char* names[] = {bridge_status_name, bridge_command_name, bridge_reply_name};
    for (unsigned index = 0; index < 3; ++index) {
        if (!check(SimConnect_MapClientDataNameToID(connection, names[index], areas[index]), "Map observation area") ||
            !check(SimConnect_AddToClientDataDefinition(connection, areas[index], 0, sizeof(bridge_packet)), "Observation layout")) return 2;
    }
    check(SimConnect_RequestClientData(connection, status_area, status_request, status_area, SIMCONNECT_CLIENT_DATA_PERIOD_ON_SET), "Observe native");
    check(SimConnect_RequestClientData(connection, reply_area, reply_request, reply_area, SIMCONNECT_CLIENT_DATA_PERIOD_ON_SET), "Observe discovery");
    const char* variables[] = {"SIM ON GROUND", "GROUND VELOCITY", "AUTOPILOT MASTER", "BRAKE PARKING POSITION"};
    for (unsigned index = 0; index < 4; ++index) check(SimConnect_AddToDataDefinition(connection, guard_definition, variables[index], index == 1 ? "Knots" : "Bool", SIMCONNECT_DATATYPE_FLOAT64), "Ground guard");
    check(SimConnect_RequestDataOnSimObject(connection, guard_request, guard_definition, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_VISUAL_FRAME), "Observe ground guard");
    std::snprintf(backend, sizeof(backend), "%016llx%016llx", static_cast<unsigned long long>(GetTickCount64()), static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(connection)));
    bridge_packet hello{}; hello.kind = bridge_kind::hello; hello.request = 1; std::memcpy(hello.backend, backend, sizeof(hello.backend));
    check(SimConnect_SetClientData(connection, command_area, command_area, SIMCONNECT_CLIENT_DATA_SET_FLAG_DEFAULT, 0, sizeof(hello), &hello), "Fresh discovery");
    while (!failed && now() < 8 && (observations < 3 || guard.at < 0)) pump();
    if (!failed && observed.connection_api == 1) {
        check(SimConnect_MapClientDataNameToID(connection, session_status_name, local_session_area), "Map unbound session observation");
        check(SimConnect_AddToClientDataDefinition(connection, local_session_area, 0, sizeof(session_packet)), "Session observation layout");
        check(SimConnect_RequestClientData(connection, local_session_area, local_session_request, local_session_area, SIMCONNECT_CLIENT_DATA_PERIOD_ON_SET), "Observe unbound session");
    }
    while (!failed && now() < 8 && !fresh_guard()) pump();
    char identity[1024]{}, prefix[600]{}; control_profile saved{}; same_state_control_check test;
    if (!profile_identity(observed.aircraft, sizeof(observed.aircraft), identity)) return 3;
    std::snprintf(prefix, sizeof(prefix), "%s\\pickup-%08x-%u", root.c_str(), profile_hash(identity, std::strlen(identity)), observed.engines);
    if (failed || !opened || observations < 3 || !read_profile(prefix, identity, observed.engines, saved) ||
        !test.prepare(observed, saved, target, now(), observed_at, current_guard())) {
        std::printf("REFUSED before masking/writing. Need fresh solo/unbound Baron, both groups Me, Auto off, saved mappings, zero groundspeed and parking brake. ground=%.0f speed=%.5f brake=%.0f native=%s API=%u unbound=%d.\n", guard.ground, guard.knots, guard.parking_brake, native, observed.connection_api, guard.unbound_session_fresh); return 3;
    }
    if (mixture_set_route && !test.use_mixture_set_route()) return 3;
    for (size_t axis = 0; axis < axis_count; ++axis) if (test.axes & (1u << axis))
        std::printf("%.3f BASELINE axis=%zu input=%.8f cockpit=%.8f\n", now(), axis, observed.input[axis], observed.targets[axis]);
    active_check = &test;
    // Only selected-group events are mapped/masked. The output adapter uses
    // their production table IDs and starts BELOW the HIGHEST_MASKABLE group.
    masked = true; const auto mask_until = now() + 4;
    for (size_t event = 0; event < event_count && !failed; ++event) if (event_selection(events[event].target, events[event].name) == selection) {
        check(SimConnect_MapClientEventToSimEvent(connection, first_event + static_cast<DWORD>(event), events[event].name), "Map selected control");
        check(SimConnect_AddClientEventToNotificationGroup(connection, mask_group, first_event + static_cast<DWORD>(event), TRUE), "Mask selected control");
    }
    check(SimConnect_SetNotificationGroupPriority(connection, mask_group, SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE), "Mask priority");
    check(SimConnect_RequestSystemState(connection, mask_barrier, "Sim"), "Mask checkpoint");
    while (!failed && !mask_confirmed && now() < mask_until) pump();
    control_profile rechecked{};
    bool same = false, moved = false, restored = false;
    output.initialize(connection, first_event, standard_priority ? SIMCONNECT_GROUP_PRIORITY_STANDARD : SIMCONNECT_GROUP_PRIORITY_DEFAULT);
    if (!failed && mask_confirmed && !interfering_events && read_profile(prefix, identity, observed.engines, rechecked) &&
        test.matches_profile(rechecked) && test.current(observed, now(), observed_at, current_guard())) {
        same = write_and_observe(test.actions, test.count, test, false, (std::min)(mask_until, now() + 0.9));
        if (same && nudge) {
            output_action change[axis_count]{}; size_t count = 0;
            if (test.make_nudge(change, count)) moved = write_and_observe(change, count, test, true, (std::min)(mask_until, now() + 0.9));
            // Restore even after a failed read-back, provided the SDK adapter
            // has not failed and the same native aircraft is still present.
            if (!std::strcmp(observed.native, native) && !std::strcmp(observed.aircraft, identity) && output.available && now() < mask_until)
                restored = write_and_observe(test.actions, test.count, test, false, mask_until);
        } else restored = same;
    }
    const bool output_failed = !output.available; output.stop();
    const bool cleared = check(SimConnect_ClearNotificationGroup(connection, mask_group), "Remove own control mask");
    const bool clear_sent = cleared && check(SimConnect_RequestSystemState(connection, clear_barrier, "Sim"), "Mask removal checkpoint");
    const auto clear_until = now() + 2;
    while (clear_sent && !clear_confirmed && now() < clear_until) pump();
    const bool released = cleared && clear_confirmed;
    active_check = nullptr;
    const bool passed = same && (!nudge || moved) && restored && released && !failed && !output_failed && !interfering_events;
    std::printf("%s bounded %s SDK check: same-state=%d, 1%% movement=%d, original read-back=%d, own mask released=%d, high-priority events=%u, SDK failure=%d, write attempts=%u, bounded noise=%u, interference=%u. No physics writes or native ownership changes.\n",
        passed ? "PASS" : "FAIL", group_name(target), same, moved, restored, released, hardware_events, failed || output_failed, write_attempts, noise_events, interfering_events);
    if (!nudge) std::puts("Same-state acceptance alone does not prove functional remote movement. The explicit --nudge-and-restore mode is required for that evidence.");
    return passed ? 0 : 1;
}
