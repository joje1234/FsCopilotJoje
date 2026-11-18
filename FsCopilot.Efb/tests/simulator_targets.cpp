// Read-only validation of the position definitions used by aligned takeover.
#include <Windows.h>
#include <SimConnect.h>
#include <cstdio>
#include <cstring>
#include <map>
#include <string>
#include "../Wasm/control_targets.h"
using namespace fsc_controls;
namespace {
HANDLE client = nullptr;
bool received = false;
bool identified = false;
unsigned failures = 0;
std::map<DWORD, std::string> definitions;
void CALLBACK receive(SIMCONNECT_RECV* data, DWORD size, void*)
{
    if (data->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* error = reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(data);
        std::printf("Rejected %s (error %lu, index %lu)\n", definitions[error->dwSendID].c_str(), error->dwException, error->dwIndex);
        ++failures;
    }
    if (data->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE && size >= sizeof(SIMCONNECT_RECV_SYSTEM_STATE)) {
        const auto* state = reinterpret_cast<const SIMCONNECT_RECV_SYSTEM_STATE*>(data);
        if (state->dwRequestID == 3 && std::memchr(state->szString, 0, sizeof(state->szString)) && state->szString[0]) {
            std::printf("Aircraft identity = %s\n", state->szString);
            identified = true;
        }
    }
    if (data->dwID != SIMCONNECT_RECV_ID_SIMOBJECT_DATA) return;
    const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SIMOBJECT_DATA*>(data);
    const size_t offset = reinterpret_cast<const char*>(&packet->dwData) - reinterpret_cast<const char*>(data);
    if (packet->dwRequestID != 2 || packet->dwDefineCount != sim_target_count || size < offset + sizeof(double) * sim_target_count) return;
    double values[target_count]{};
    std::memcpy(values, &packet->dwData, sizeof(double) * sim_target_count);
    for (size_t axis = 0; axis < axis_count; ++axis) std::printf("%s = %.5f %s\n", axes[axis].variable, values[axis], axes[axis].units);
    std::printf("Engines=%.0f Autopilot=%.0f\n", values[axis_count], values[axis_count + 1]);
    std::puts("Standard SimVars only. Typed cockpit mixture references are read inside the WASM module.");
    received = true;
}
void define(const char* name, const char* units)
{
    if (FAILED(SimConnect_AddToDataDefinition(client, 1, name, units, SIMCONNECT_DATATYPE_FLOAT64))) ++failures;
    DWORD packet{};
    SimConnect_GetLastSentPacketID(client, &packet);
    definitions[packet] = name;
}
}
int main()
{
    if (FAILED(SimConnect_Open(&client, "FS Copilot read-only alignment targets", nullptr, 0, nullptr, 0))) {
        std::puts("Cannot connect to MSFS. No controls were changed."); return 1;
    }
    for (const auto& axis : axes) define(axis.variable, axis.units);
    define("NUMBER OF ENGINES", "Number");
    define("AUTOPILOT MASTER", "Bool");
    if (FAILED(SimConnect_RequestDataOnSimObject(client, 2, 1, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_ONCE))) ++failures;
    if (FAILED(SimConnect_RequestSystemState(client, 3, "AircraftLoaded"))) ++failures;
    DWORD identity_packet{};
    SimConnect_GetLastSentPacketID(client, &identity_packet);
    definitions[identity_packet] = "AircraftLoaded identity";
    const auto started = GetTickCount64();
    while ((!received || !identified) && GetTickCount64() - started < 6000) { SimConnect_CallDispatch(client, receive, nullptr); Sleep(10); }
    SimConnect_Close(client);
    std::printf("Position data=%s; aircraft identity=%s; failures=%u. No controls were changed.\n", received ? "received" : "missing", identified ? "received" : "missing", failures);
    return received && identified && !failures ? 0 : 2;
}
