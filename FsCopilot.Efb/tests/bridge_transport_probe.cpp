// Real SDK transport integration, using isolated test ClientData names. Includes
// the production native transport. No aircraft/control event or data write API
// is called. Pair with FsCopilot.Native.Probe --prefix=<printed prefix>.
#include <Windows.h>
#include <SimConnect.h>
#include <cstdio>
#include "../Wasm/control_bridge_transport.h"
using namespace fsc_controls;
namespace {
HANDLE client{};
control_bridge_transport bridge;
char prefix[40]{};
uint64_t serial = 0;
unsigned failures = 0, replies = 0;
ULONGLONG first_hello = 0;
bool check_session = false, session_received = false, session_state_received = false, session_input_received = false;
session_packet session_telemetry{};
uint64_t session_serial = 0;
session_packet session_status() {
    auto p = session_telemetry;
    std::snprintf(p.native, sizeof(p.native), "%s", "abc123def456");
    std::snprintf(p.consent, sizeof(p.consent), "%s", "11111111111111111111111111111111");
    p.kind = session_kind::status; p.serial = ++session_serial;
    if (!session_received) p.phase = session_phase::armed;
    return p;
}
bridge_packet status(const bridge_packet* hello = nullptr) {
    bridge_packet p{};
    std::snprintf(p.native, sizeof(p.native), "abc123def456");
    std::snprintf(p.aircraft, sizeof(p.aircraft), "bridge-transport-fixture");
    std::snprintf(p.adapter, sizeof(p.adapter), "native-transport-fixture-v1");
    p.serial = ++serial; p.revision = 7; p.flags = bridge_ready | bridge_targets_fresh;
    p.engines = 2; p.required = p.learned = p.fresh = 0x7e03; p.blocked = 14;
    p.tolerance[1] = 20; p.tolerance[2] = 30;
    for (unsigned axis = 0; axis < 21; ++axis) p.input[axis] = p.targets[axis] = -1;
    p.input[0] = p.input[1] = p.targets[0] = p.targets[1] = 0.5;
    p.input[9] = 0.23; p.input[12] = 0.77; p.targets[11] = 0.61;
    if (hello) { p.kind = bridge_kind::reply; p.request = hello->request; std::memcpy(p.backend, hello->backend, sizeof(p.backend)); }
    return p;
}
void CALLBACK receive(SIMCONNECT_RECV* data, DWORD bytes, void*) {
    if (data->dwID == SIMCONNECT_RECV_ID_OPEN) {
        if (!bridge.initialize(client, prefix)) ++failures;
        else { std::puts("READY native transport test areas initialized"); std::fflush(stdout); }
    } else if (data->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* error = reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(data);
        if (!bridge.exception(*error) || !bridge.available) {
            std::printf("SDK failure: %lu (send %lu, index %lu)\n", error->dwException, error->dwSendID, error->dwIndex); ++failures;
        }
    } else if (data->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) {
        bridge.confirm(reinterpret_cast<const SIMCONNECT_RECV_SYSTEM_STATE*>(data)->dwRequestID);
    } else {
        session_packet command{};
        if (check_session && bridge.receive_session(data, bytes, command)) {
            if (session_received) {
                if (!session_state_received && command.kind == session_kind::state && command.command == 2 && command.revision == 2 &&
                    command.generation[1] == 2 && command.owner[1] == writer_owner::remote && command.attached == select_throttle &&
                    !std::strcmp(command.committed_transfer[1], "44444444444444444444444444444444")) {
                    session_state_received = true; session_telemetry = command; session_telemetry.flags = session_host;
                    session_telemetry.phase = session_phase::active; session_telemetry.ack = 2;
                    session_telemetry.result = session_result::succeeded; session_telemetry.blocked = 15; session_telemetry.barrier = 2;
                } else if (session_state_received && !session_input_received && command.kind == session_kind::input && command.command == 3 &&
                    command.revision == 2 && command.generation[1] == 2 && command.selection == select_throttle && command.sequence == 1 &&
                    command.axes == ((1u << 9) | (1u << 12)) && command.positions[9] == 0.25 && command.positions[12] == 0.75) {
                    session_input_received = true; session_telemetry.command = 3;
                    std::puts("SESSION fixture state/commit ticket and input bytes verified; no aircraft/control writes");
                } else { ++failures; return; }
                if (!bridge.publish_session(session_status())) ++failures;
                return;
            }
            if (command.kind != session_kind::bind || command.epoch != 7 || command.command != 1 ||
                std::strcmp(command.native, "abc123def456") || std::strcmp(command.consent, "11111111111111111111111111111111") ||
                command.engines != 2 || command.axes != 0x7e03 || command.attached || command.pending || command.flags != session_host) { ++failures; return; }
            for (unsigned index = 0; index < 4; ++index)
                if (command.owner[index] != writer_owner::local || command.generation[index] != 1) { ++failures; return; }
            // Fixture acknowledgement only: this server has no control runtime,
            // never configures a hardware mask and never transmits an event.
            session_received = true; session_telemetry = command; session_telemetry.phase = session_phase::active;
            session_telemetry.ack = command.command; session_telemetry.result = session_result::succeeded;
            session_telemetry.blocked = 15; session_telemetry.barrier = 1;
            if (!bridge.publish_session(session_status())) ++failures;
            std::puts("SESSION fixture command bytes and acknowledgement verified; no hardware mask or aircraft writes");
            return;
        }
        bridge_packet hello{};
        if (bridge.receive(data, bytes, hello)) {
            if (!first_hello) first_hello = GetTickCount64();
            if (!bridge.publish(status(&hello))) ++failures; else ++replies;
        }
    }
}
}
int main(int argc, char** argv) {
    check_session = argc > 2 && !std::strcmp(argv[2], "--session");
    if (argc > 1) {
        if (std::strlen(argv[1]) > 32 || std::strncmp(argv[1], "FSC_TEST_", 9)) return 3;
        std::snprintf(prefix, sizeof(prefix), "%s", argv[1]);
    } else std::snprintf(prefix, sizeof(prefix), "FSC_TEST_%lu_", GetCurrentProcessId());
    std::printf("PREFIX %s\n", prefix); std::fflush(stdout);
    const auto opened = SimConnect_Open(&client, "FS Copilot isolated native transport validation", nullptr, 0, nullptr, 0);
    if (FAILED(opened)) {
        std::printf("Cannot connect to MSFS (%08lx). No settings changed.\n", static_cast<unsigned long>(opened)); return 1;
    }
    const auto started = GetTickCount64(); auto last = started;
    while (!failures && GetTickCount64() - started < 30000 && (!first_hello || GetTickCount64() - first_hello < (check_session ? 3000u : 2000u))) {
        SimConnect_CallDispatch(client, receive, nullptr);
        const auto now = GetTickCount64();
        if (bridge.available && now - last >= 100) {
            if (!bridge.publish(status())) ++failures;
            if (check_session && !bridge.publish_session(session_status())) ++failures;
            last = now;
        }
        Sleep(10);
    }
    SimConnect_Close(client);
    std::printf("Native transport SDK probe: replies=%u, packets=%llu, failures=%u. ClientData test namespace only; no aircraft/control writes.\n", replies, serial, failures);
    return replies && !failures && (!check_session || (session_received && session_state_received && session_input_received)) ? 0 : 2;
}
