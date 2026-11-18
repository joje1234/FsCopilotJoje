// Explicit, bounded ground test. No hardware masking or axis writes.
// Refuses airborne/moving/already-frozen aircraft; writes its exact current
// frozen state once, then restores the original freeze settings on every path.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <SimConnect.h>
#include <chrono>
#include <cstdio>
#include "../Wasm/control_world.h"
namespace {
fsc_controls::control_world world;
const auto started = std::chrono::steady_clock::now();
bool failed = false, opened = false;
double on_ground = 0, speed = 999, guard_at = -1;
constexpr DWORD guard_definition = 0x6600, guard_request = 0x6601;
double now() { return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count(); }
void CALLBACK dispatch(SIMCONNECT_RECV* packet, DWORD size, void*) {
    if (packet->dwID == SIMCONNECT_RECV_ID_OPEN) { opened = true; if (!world.start()) failed = true; }
    else if (packet->dwID == SIMCONNECT_RECV_ID_SIMOBJECT_DATA) {
        auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(packet);
        if (data->dwRequestID == guard_request && data->dwDefineCount == 2 &&
            size >= reinterpret_cast<char*>(&data->dwData) - reinterpret_cast<char*>(data) + sizeof(double) * 2) {
            double values[2]{}; std::memcpy(values, &data->dwData, sizeof(values));
            on_ground = values[0]; speed = values[1]; guard_at = now();
        } else world.observe(data, size, now());
    } else if (packet->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) world.confirm(reinterpret_cast<SIMCONNECT_RECV_SYSTEM_STATE*>(packet)->dwRequestID);
    else if (packet->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* error = reinterpret_cast<SIMCONNECT_RECV_EXCEPTION*>(packet);
        std::printf("SDK error %lu send %lu index %lu\n", error->dwException, error->dwSendID, error->dwIndex);
        failed = true; world.exception(*error);
    }
}
void pump(HANDLE connection) { SimConnect_CallDispatch(connection, dispatch, nullptr); Sleep(10); }
}
int main(int argc, char** argv) {
    if (argc != 2 || std::strcmp(argv[1], "--same-state-ground-test")) { std::puts("Requires --same-state-ground-test."); return 2; }
    HANDLE connection{};
    const auto connected = SimConnect_Open(&connection, "FS Copilot bounded same-state ground test", nullptr, 0, nullptr, 0);
    if (FAILED(connected)) { std::printf("No simulator connection (0x%08lx); no writes.\n", static_cast<unsigned long>(connected)); return 2; }
    world.initialize(connection);
    SimConnect_AddToDataDefinition(connection, guard_definition, "SIM ON GROUND", "Bool", SIMCONNECT_DATATYPE_FLOAT64);
    SimConnect_AddToDataDefinition(connection, guard_definition, "GROUND VELOCITY", "Knots", SIMCONNECT_DATATYPE_FLOAT64);
    SimConnect_RequestDataOnSimObject(connection, guard_request, guard_definition, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 1, 0);
    while (!failed && now() < 8 && (world.serial < 3 || !world.fresh(now()) || guard_at < 0 || now() - guard_at > 0.25)) pump(connection);
    if (!opened || failed || !world.fresh(now()) || guard_at < 0 || now() - guard_at > 0.25 || on_ground != 1 ||
        !std::isfinite(speed) || std::abs(speed) > 0.1 || world.frozen != 0) {
        std::printf("REFUSED without writes: ground=%.0f, speed=%.5f knots, frozen=%u, fresh=%d, SDK failure=%d.\n", on_ground, speed, world.frozen, world.fresh(now()), failed);
        SimConnect_Close(connection); return 3;
    }
    bool written = false;
    if (world.begin(false, now())) {
        const auto deadline = now() + 3;
        while (!failed && !world.ready(now()) && now() < deadline) pump(connection);
        if (!failed && world.ready(now())) {
            double same[fsc_controls::world_value_count]{}; std::memcpy(same, world.values, sizeof(same));
            written = world.write(same, now());
        }
    }
    const auto restore_deadline = now() + 5;
    bool restored = false;
    while (!(restored = world.recover(now())) && now() < restore_deadline) {
        if (!world.available) world.cancel_recovery();
        pump(connection);
    }
    const bool passed = written && restored && !failed && world.frozen == 0;
    std::printf("%s actual SDK ground check: same-state write=%d, original freeze restored=%d, frozen=%u, SDK failure=%d. No hardware masks or axis writes.\n",
        passed ? "PASS" : "FAIL", written, restored, world.frozen, failed);
    SimConnect_Close(connection); return passed ? 0 : 1;
}
