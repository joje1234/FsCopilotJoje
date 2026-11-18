// Bounded read-only AP/GTN identification. No event subscriptions, output,
// client-data writes, input masks, flight-plan loads or avionics reconfiguration.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <SimConnect.h>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
namespace {
constexpr DWORD definition = 0x7a00, request = 0x7a01, aircraft = 0x7a02;
struct Variable { const char* name; const char* unit; const char* column; };
constexpr Variable variables[] = {
    {"AUTOPILOT MASTER", "Bool", "ap_master"},
    {"AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", "Bool", "flight_director_1"},
    {"AUTOPILOT YAW DAMPER", "Bool", "yaw_damper"},
    {"AUTOPILOT HEADING LOCK", "Bool", "heading_hold"},
    {"AUTOPILOT ALTITUDE LOCK", "Bool", "altitude_hold"},
    {"AUTOPILOT VERTICAL HOLD", "Bool", "vertical_hold"},
    {"AUTOPILOT NAV1 LOCK", "Bool", "nav_hold"},
    {"AUTOPILOT APPROACH HOLD", "Bool", "approach_hold"},
    {"AUTOPILOT APPROACH ARM", "Bool", "approach_arm"},
    {"AUTOPILOT APPROACH CAPTURED", "Bool", "approach_captured"},
    {"AUTOPILOT GLIDESLOPE HOLD", "Bool", "glideslope_hold"},
    {"AUTOPILOT BACKCOURSE HOLD", "Bool", "backcourse_hold"},
    {"AUTOPILOT FLIGHT LEVEL CHANGE", "Bool", "flight_level_change"},
    {"AUTOPILOT HEADING LOCK DIR:1", "Degrees", "heading_target_1_deg"},
    {"AUTOPILOT ALTITUDE LOCK VAR:1", "Feet", "altitude_target_1_ft"},
    {"AUTOPILOT VERTICAL HOLD VAR:1", "Feet per minute", "vertical_target_1_fpm"},
    {"AUTOPILOT HEADING SLOT INDEX", "Number", "heading_slot"},
    {"AUTOPILOT ALTITUDE SLOT INDEX", "Number", "altitude_slot"},
    {"AUTOPILOT VS SLOT INDEX", "Number", "vertical_slot"},
    {"GPS DRIVES NAV1", "Bool", "gps_drives_nav1"},
    {"AUTOPILOT NAV SELECTED", "Number", "nav_receiver"},
    {"L:PMS50_GTN750_RUNNING", "Bool", "gtn_running"},
    {"L:PMS50_GTN750_VERSION", "Number", "gtn_version"}
};
constexpr unsigned count = sizeof(variables) / sizeof(variables[0]);
const auto started = std::chrono::steady_clock::now();
unsigned samples = 0;
bool failed = false;
double seconds() { return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count(); }
bool check(HRESULT value, const char* operation) {
    if (SUCCEEDED(value)) return true;
    std::printf("# SDK failure %s 0x%08lx\n", operation, static_cast<unsigned long>(value)); failed = true; return false;
}
void CALLBACK dispatch(SIMCONNECT_RECV* packet, DWORD size, void*) {
    if (packet->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        if (size < sizeof(SIMCONNECT_RECV_EXCEPTION)) { failed = true; return; }
        const auto* error = reinterpret_cast<SIMCONNECT_RECV_EXCEPTION*>(packet);
        std::printf("# SDK exception %lu send %lu index %lu\n", error->dwException, error->dwSendID, error->dwIndex); failed = true;
    } else if (packet->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) {
        if (size < sizeof(SIMCONNECT_RECV_SYSTEM_STATE)) { failed = true; return; }
        const auto* state = reinterpret_cast<SIMCONNECT_RECV_SYSTEM_STATE*>(packet);
        if (state->dwRequestID == aircraft && std::memchr(state->szString, 0, sizeof(state->szString))) {
            std::printf("# aircraft ");
            for (const char* c = state->szString; *c; ++c) if (static_cast<unsigned char>(*c) >= 32) std::putchar(*c);
            std::putchar('\n');
        }
    } else if (packet->dwID == SIMCONNECT_RECV_ID_SIMOBJECT_DATA) {
        if (size < sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA)) { failed = true; return; }
        const auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(packet);
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (data->dwRequestID != request || data->dwDefineCount != count || size < offset + count * sizeof(double)) { failed = true; return; }
        double values[count]{}; std::memcpy(values, &data->dwData, sizeof(values));
        for (const auto value : values) if (!std::isfinite(value)) { failed = true; return; }
        std::printf("%.3f", seconds()); for (const auto value : values) std::printf(",%.8f", value); std::putchar('\n'); std::fflush(stdout); ++samples;
    } else if (packet->dwID == SIMCONNECT_RECV_ID_QUIT) failed = true;
}
}
int main(int argc, char** argv) {
    int duration = 30;
    if (argc > 2 || (argc == 2 && std::strncmp(argv[1], "--seconds=", 10))) return 2;
    if (argc == 2) {
        char* end = nullptr; const auto value = std::strtol(argv[1] + 10, &end, 10);
        if (!end || *end || value < 1 || value > 120) return 2;
        duration = static_cast<int>(value);
    }
    HANDLE connection{};
    if (!check(SimConnect_Open(&connection, "FS Copilot read-only guidance identification", nullptr, 0, nullptr, 0), "Open")) return 2;
    for (const auto& variable : variables) {
        if (!check(SimConnect_AddToDataDefinition(connection, definition, variable.name, variable.unit, SIMCONNECT_DATATYPE_FLOAT64), variable.name)) break;
    }
    if (!failed) check(SimConnect_RequestSystemState(connection, aircraft, "AircraftLoaded"), "Aircraft identity");
    if (!failed) check(SimConnect_RequestDataOnSimObject(connection, request, definition, SIMCONNECT_OBJECT_ID_USER,
        SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 5, 0), "Read guidance state");
    std::printf("seconds"); for (const auto& variable : variables) std::printf(",%s", variable.column); std::putchar('\n');
    while (!failed && seconds() < duration) { check(SimConnect_CallDispatch(connection, dispatch, nullptr), "Dispatch"); Sleep(10); }
    const auto closed = SUCCEEDED(SimConnect_Close(connection));
    std::printf("# %s read-only guidance capture: samples=%u; no simulator writes. Stock observations alone do not verify custom GTN/WTT guidance or input masking.\n",
        !failed && closed && samples >= 3 ? "PASS" : "FAIL", samples);
    return !failed && closed && samples >= 3 ? 0 : 1;
}
