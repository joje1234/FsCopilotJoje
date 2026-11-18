// Read-only identification capture for the Baron's two altimeters and seat views.
// No event subscriptions, event transmissions, SetData or ClientData writes.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <SimConnect.h>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
namespace {
constexpr DWORD definition = 0x7900, request = 0x7901, aircraft = 0x7902;
constexpr const char* variables[] = {
    "KOHLSMAN SETTING MB:1", "KOHLSMAN SETTING MB:2", "KOHLSMAN SETTING MB EX1:1", "KOHLSMAN SETTING MB EX1:2", "KOHLSMAN SETTING STD:1", "KOHLSMAN SETTING STD:2",
    "L:INSTRUMENT_Altimeter_1", "L:INSTRUMENT_Altimeter_2", "CAMERA STATE", "CAMERA VIEW TYPE AND INDEX:0",
    "CAMERA VIEW TYPE AND INDEX:1", "CAMERA VIEW TYPE AND INDEX MAX:1"
};
constexpr unsigned variable_count = sizeof(variables) / sizeof(variables[0]);
const auto started = std::chrono::steady_clock::now();
unsigned samples = 0;
bool failed = false;
double now() { return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count(); }
bool check(HRESULT value, const char* operation) {
    if (SUCCEEDED(value)) return true;
    std::printf("# SDK failure %s 0x%08lx\n", operation, static_cast<unsigned long>(value)); failed = true; return false;
}
void CALLBACK dispatch(SIMCONNECT_RECV* packet, DWORD size, void*) {
    if (packet->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* error = reinterpret_cast<SIMCONNECT_RECV_EXCEPTION*>(packet);
        std::printf("# SDK exception %lu send %lu index %lu\n", error->dwException, error->dwSendID, error->dwIndex); failed = true;
    } else if (packet->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) {
        const auto* state = reinterpret_cast<SIMCONNECT_RECV_SYSTEM_STATE*>(packet);
        if (state->dwRequestID == aircraft && std::memchr(state->szString, 0, sizeof(state->szString))) {
            std::printf("# aircraft ");
            for (const char* c = state->szString; *c; ++c) if (static_cast<unsigned char>(*c) >= 32) std::putchar(*c);
            std::putchar('\n');
        }
    } else if (packet->dwID == SIMCONNECT_RECV_ID_SIMOBJECT_DATA) {
        const auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(packet);
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (data->dwRequestID != request || data->dwDefineCount != variable_count || size < offset + variable_count * sizeof(double)) { failed = true; return; }
        double values[variable_count]{}; std::memcpy(values, &data->dwData, sizeof(values));
        for (const auto value : values) if (!std::isfinite(value)) { failed = true; return; }
        std::printf("%.3f", now()); for (const auto value : values) std::printf(",%.8f", value); std::putchar('\n'); std::fflush(stdout); ++samples;
    } else if (packet->dwID == SIMCONNECT_RECV_ID_QUIT) failed = true;
}
}
int main(int argc, char** argv) {
    int seconds = 30;
    if (argc > 2 || (argc == 2 && std::strncmp(argv[1], "--seconds=", 10))) return 2;
    if (argc == 2) {
        char* end = nullptr; const auto value = std::strtol(argv[1] + 10, &end, 10);
        if (!end || *end || value < 1 || value > 120) return 2;
        seconds = static_cast<int>(value);
    }
    HANDLE connection{};
    if (!check(SimConnect_Open(&connection, "FS Copilot read-only cockpit identification", nullptr, 0, nullptr, 0), "Open")) return 2;
    const char* units[] = {"Millibars", "Millibars", "Millibars", "Millibars", "Bool", "Bool", "Number", "Number", "Number", "Number", "Number", "Number"};
    for (unsigned index = 0; index < variable_count; ++index)
        check(SimConnect_AddToDataDefinition(connection, definition, variables[index], units[index], SIMCONNECT_DATATYPE_FLOAT64), variables[index]);
    check(SimConnect_RequestSystemState(connection, aircraft, "AircraftLoaded"), "Aircraft identity");
    check(SimConnect_RequestDataOnSimObject(connection, request, definition, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 5, 0), "Read cockpit state");
    std::puts("seconds,baro_left_candidate_mb,baro_right_candidate_mb,tuned_qnh_1_mb,tuned_qnh_2_mb,std_1,std_2,altimeter_lvar_1,altimeter_lvar_2,camera_state,view_type,view_option,pilot_view_max");
    while (!failed && now() < seconds) { check(SimConnect_CallDispatch(connection, dispatch, nullptr), "Dispatch"); Sleep(10); }
    const auto closed = SUCCEEDED(SimConnect_Close(connection));
    std::printf("# %s read-only cockpit capture: samples=%u; no simulator writes. Candidate indices require correlation with actual knob/view changes.\n",
        !failed && closed && samples >= 3 ? "PASS" : "FAIL", samples);
    return !failed && closed && samples >= 3 ? 0 : 1;
}
