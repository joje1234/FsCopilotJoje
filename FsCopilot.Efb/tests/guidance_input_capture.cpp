// Bounded passive AP route diagnostic. All notification entries are non-masking.
// No event, SimVar, InputEvent, mask, flight plan or camera is written.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <SimConnect.h>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cctype>
#include <cstdint>
#include <map>
#include <string>

namespace {
HANDLE client{};
ULONGLONG started = 0;
unsigned errors = 0, samples = 0, input_changes = 0;
bool quit = false;
constexpr DWORD definition = 0x7a00, state_request = 0x7a01, aircraft_request = 0x7a02, enumerate_request = 0x7a03, group = 0x7a04;
const char* keys[] = {
    "AP_MASTER", "AUTOPILOT_ON", "AUTOPILOT_OFF", "AUTOPILOT_DISENGAGE_TOGGLE",
    "AP_HDG_HOLD", "AP_HDG_HOLD_ON", "AP_HDG_HOLD_OFF", "AP_PANEL_HEADING_HOLD", "AP_PANEL_HEADING_ON", "AP_PANEL_HEADING_OFF", "AP_PANEL_HEADING_SET",
    "HEADING_BUG_SET", "AP_HEADING_BUG_SET_EX1", "HEADING_BUG_INC", "HEADING_BUG_DEC",
    "AP_ALT_HOLD", "AP_ALT_HOLD_ON", "AP_ALT_HOLD_OFF", "AP_PANEL_ALTITUDE_HOLD", "AP_PANEL_ALTITUDE_ON", "AP_PANEL_ALTITUDE_OFF", "AP_PANEL_ALTITUDE_SET",
    "AP_ALT_VAR_SET_ENGLISH", "AP_ALT_VAR_SET_METRIC", "AP_ALT_VAR_INC", "AP_ALT_VAR_DEC",
    "AP_VS_HOLD", "AP_VS_ON", "AP_VS_OFF", "AP_VS_SET", "AP_VS_VAR_SET_ENGLISH", "AP_VS_VAR_INC", "AP_VS_VAR_DEC",
    "AP_NAV1_HOLD", "AP_NAV1_HOLD_ON", "AP_NAV1_HOLD_OFF", "AP_APR_HOLD", "AP_APR_HOLD_ON", "AP_APR_HOLD_OFF",
    "AP_BC_HOLD", "AP_BC_HOLD_ON", "AP_BC_HOLD_OFF", "FLIGHT_LEVEL_CHANGE", "FLIGHT_LEVEL_CHANGE_ON", "FLIGHT_LEVEL_CHANGE_OFF",
    "YAW_DAMPER_TOGGLE", "YAW_DAMPER_ON", "YAW_DAMPER_OFF", "YAW_DAMPER_SET", "TOGGLE_FLIGHT_DIRECTOR",
    "AP_PITCH_LEVELER", "AP_PITCH_LEVELER_ON", "AP_PITCH_LEVELER_OFF", "AP_PITCH_REF_INC_UP", "AP_PITCH_REF_INC_DN", "AP_PITCH_REF_SET",
    "AP_ATT_HOLD", "AP_ATT_HOLD_ON", "AP_ATT_HOLD_OFF", "AP_NAV_SELECT_SET", "TOGGLE_GPS_DRIVES_NAV1"
};
constexpr unsigned key_count = sizeof(keys) / sizeof(keys[0]);
unsigned counts[key_count]{};
struct Variable { const char* name; const char* unit; const char* label; };
const Variable variables[] = {
    {"AUTOPILOT MASTER", "Bool", "master"}, {"AUTOPILOT HEADING LOCK", "Bool", "hdg"},
    {"AUTOPILOT ALTITUDE LOCK", "Bool", "alt"}, {"AUTOPILOT VERTICAL HOLD", "Bool", "vs"},
    {"AUTOPILOT NAV1 LOCK", "Bool", "nav"}, {"AUTOPILOT APPROACH HOLD", "Bool", "apr"},
    {"AUTOPILOT APPROACH ARM", "Bool", "apr_arm"}, {"AUTOPILOT APPROACH CAPTURED", "Bool", "apr_captured"},
    {"AUTOPILOT GLIDESLOPE HOLD", "Bool", "gs"}, {"AUTOPILOT BACKCOURSE HOLD", "Bool", "bc"},
    {"AUTOPILOT FLIGHT LEVEL CHANGE", "Bool", "flc"}, {"AUTOPILOT YAW DAMPER", "Bool", "yd"},
    {"AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", "Bool", "fd"}, {"AUTOPILOT PITCH HOLD", "Bool", "pitch"},
    {"AUTOPILOT PITCH HOLD REF", "Degrees", "pitch_ref"},
    {"AUTOPILOT HEADING LOCK DIR:1", "Degrees", "heading_1"}, {"AUTOPILOT ALTITUDE LOCK VAR:1", "Feet", "altitude_1"},
    {"AUTOPILOT VERTICAL HOLD VAR:1", "Feet per minute", "vertical_1"},
    {"AUTOPILOT HEADING SLOT INDEX", "Number", "heading_slot"}, {"AUTOPILOT ALTITUDE SLOT INDEX", "Number", "altitude_slot"},
    {"AUTOPILOT VS SLOT INDEX", "Number", "vs_slot"}, {"GPS DRIVES NAV1", "Bool", "gps"}, {"AUTOPILOT NAV SELECTED", "Number", "radio"},
    {"CIRCUIT ON:61", "Bool", "ap_circuit"}, {"SIM ON GROUND", "Bool", "on_ground"}, {"GROUND VELOCITY", "Knots", "ground_knots"}
};
// Do not add LVar definitions here: SimConnect can redefine their shared units.
// Read aircraft-specific references through the native typed Vars API instead.
constexpr unsigned variable_count = sizeof(variables) / sizeof(variables[0]);
std::map<DWORD, std::string> calls;
std::map<UINT64, std::string> inputs;
double seconds() { return (GetTickCount64() - started) / 1000.0; }
void record(HRESULT result, const char* operation) {
    DWORD packet = 0; SimConnect_GetLastSentPacketID(client, &packet); calls[packet] = operation;
    if (FAILED(result)) { ++errors; std::printf("ERROR %s 0x%08lx\n", operation, static_cast<unsigned long>(result)); }
}
bool relevant(const char* name) {
    std::string upper(name); for (auto& c : upper) c = static_cast<char>(std::toupper(static_cast<unsigned char>(c)));
    const char* tokens[] = {"AUTOPILOT", "KFC", "KAS", "HEADING", "HDG", "ALTITUDE", "VERTICAL", "CWS", "FLIGHT_DIRECTOR", "PITCH", "AP_"};
    for (const auto* token : tokens) if (upper.find(token) != std::string::npos) return true;
    return false;
}
void event(DWORD id, const DWORD* values, unsigned count, bool extended) {
    if (id == 0 || id > key_count) return;
    ++counts[id - 1]; std::printf("%.3f KEY%s %s", seconds(), extended ? "_EX1" : "", keys[id - 1]);
    for (unsigned index = 0; index < count; ++index) std::printf(" %ld", static_cast<long>(static_cast<int32_t>(values[index])));
    std::putchar('\n');
}
void CALLBACK receive(SIMCONNECT_RECV* data, DWORD size, void*) {
    if (data->dwID == SIMCONNECT_RECV_ID_EXCEPTION && size >= sizeof(SIMCONNECT_RECV_EXCEPTION)) {
        const auto* error = reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(data); ++errors;
        std::printf("%.3f SDK_EXCEPTION %lu send=%lu operation=%s index=%lu\n", seconds(), error->dwException, error->dwSendID,
            calls[error->dwSendID].c_str(), error->dwIndex);
    } else if (data->dwID == SIMCONNECT_RECV_ID_EVENT && size >= sizeof(SIMCONNECT_RECV_EVENT)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_EVENT*>(data); event(packet->uEventID, &packet->dwData, 1, false);
    } else if (data->dwID == SIMCONNECT_RECV_ID_EVENT_EX1 && size >= sizeof(SIMCONNECT_RECV_EVENT_EX1)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_EVENT_EX1*>(data);
        const DWORD values[] = {packet->dwData0, packet->dwData1, packet->dwData2, packet->dwData3, packet->dwData4};
        event(packet->uEventID, values, 5, true);
    } else if (data->dwID == SIMCONNECT_RECV_ID_SIMOBJECT_DATA && size >= sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SIMOBJECT_DATA*>(data);
        const auto offset = reinterpret_cast<const char*>(&packet->dwData) - reinterpret_cast<const char*>(data);
        if (packet->dwRequestID != state_request || packet->dwDefineCount != variable_count || size < offset + variable_count * sizeof(double)) { ++errors; return; }
        double values[variable_count]{}; std::memcpy(values, &packet->dwData, sizeof(values));
        static double previous[variable_count]{};
        for (unsigned index = 0; index < variable_count; ++index) {
            if (!std::isfinite(values[index])) { ++errors; return; }
            const double threshold = !std::strcmp(variables[index].label, "ground_knots") ? .1 : .0001;
            if (!samples || std::fabs(values[index] - previous[index]) > threshold)
                std::printf("%.3f STATE %s %.8f\n", seconds(), variables[index].label, values[index]);
        }
        std::memcpy(previous, values, sizeof(values)); ++samples;
    } else if (data->dwID == SIMCONNECT_RECV_ID_ENUMERATE_INPUT_EVENTS && size >= sizeof(SIMCONNECT_RECV_LIST_TEMPLATE)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_ENUMERATE_INPUT_EVENTS*>(data);
        const auto offset = reinterpret_cast<const char*>(packet->rgData) - reinterpret_cast<const char*>(data);
        if (size < offset || packet->dwArraySize > (size - offset) / sizeof(SIMCONNECT_INPUT_EVENT_DESCRIPTOR)) { ++errors; return; }
        std::printf("%.3f INPUT_PAGE %lu/%lu entries=%lu\n", seconds(), packet->dwEntryNumber, packet->dwOutOf, packet->dwArraySize);
        for (DWORD index = 0; index < packet->dwArraySize; ++index) {
            const auto& entry = packet->rgData[index];
            if (!std::memchr(entry.Name, 0, sizeof(entry.Name)) || !relevant(entry.Name) || inputs.count(entry.Hash)) continue;
            std::printf("%.3f INPUT_ROUTE %s type=%u\n", seconds(), entry.Name, static_cast<unsigned>(entry.eType));
            if (entry.eType != SIMCONNECT_INPUT_EVENT_TYPE_DOUBLE || inputs.size() >= 512) continue;
            inputs[entry.Hash] = entry.Name; record(SimConnect_SubscribeInputEvent(client, entry.Hash), entry.Name);
        }
    } else if (data->dwID == SIMCONNECT_RECV_ID_SUBSCRIBE_INPUT_EVENT && size >= sizeof(SIMCONNECT_RECV_SUBSCRIBE_INPUT_EVENT)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SUBSCRIBE_INPUT_EVENT*>(data);
        const auto found = inputs.find(packet->Hash);
        const auto offset = reinterpret_cast<const char*>(&packet->Value) - reinterpret_cast<const char*>(data);
        if (found != inputs.end() && packet->eType == SIMCONNECT_INPUT_EVENT_TYPE_DOUBLE && size >= offset + sizeof(double)) {
            double value = 0; std::memcpy(&value, &packet->Value, sizeof(value)); ++input_changes;
            std::printf("%.3f INPUT %s %.8f\n", seconds(), found->second.c_str(), value);
        }
    } else if (data->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE && size >= sizeof(SIMCONNECT_RECV_SYSTEM_STATE)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SYSTEM_STATE*>(data);
        if (packet->dwRequestID == aircraft_request && std::memchr(packet->szString, 0, sizeof(packet->szString))) std::printf("AIRCRAFT %s\n", packet->szString);
    } else if (data->dwID == SIMCONNECT_RECV_ID_QUIT) quit = true;
    std::fflush(stdout);
}
}
int main(int argc, char** argv) {
    long duration = 120;
    if (argc > 2 || (argc == 2 && std::strncmp(argv[1], "--seconds=", 10))) return 2;
    if (argc == 2) { char* end = nullptr; duration = std::strtol(argv[1] + 10, &end, 10); if (!end || *end) return 2; }
    if (duration < 1 || duration > 7200) return 2;
    if (FAILED(SimConnect_Open(&client, "FS Copilot passive AP route capture", nullptr, 0, nullptr, 0))) { std::puts("Cannot connect to MSFS."); return 2; }
    started = GetTickCount64();
    for (DWORD index = 0; index < key_count; ++index) {
        record(SimConnect_MapClientEventToSimEvent(client, index + 1, keys[index]), keys[index]);
        record(SimConnect_AddClientEventToNotificationGroup(client, group, index + 1, false), keys[index]);
    }
    record(SimConnect_SetNotificationGroupPriority(client, group, SIMCONNECT_GROUP_PRIORITY_HIGHEST), "non-masking observer priority");
    for (const auto& variable : variables) record(SimConnect_AddToDataDefinition(client, definition, variable.name, variable.unit, SIMCONNECT_DATATYPE_FLOAT64), variable.name);
    record(SimConnect_RequestDataOnSimObject(client, state_request, definition, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 2, 0), "read AP state");
    record(SimConnect_RequestSystemState(client, aircraft_request, "AircraftLoaded"), "read aircraft identity");
    record(SimConnect_EnumerateInputEvents(client, enumerate_request), "enumerate input routes");
    std::printf("READY %ld-second passive AP capture; all notification entries non-masking; no simulator writes.\n", duration); std::fflush(stdout);
    while (!quit && seconds() < duration) {
        const auto dispatched = SimConnect_CallDispatch(client, receive, nullptr);
        if (FAILED(dispatched)) { record(dispatched, "Dispatch"); break; }
        Sleep(10);
    }
    const bool closed = SUCCEEDED(SimConnect_Close(client));
    unsigned events = 0; for (unsigned index = 0; index < key_count; ++index) if (counts[index]) { events += counts[index]; std::printf("TOTAL %s %u\n", keys[index], counts[index]); }
    const bool passed = !errors && closed && !quit && samples >= 3;
    std::printf("%s passive AP capture: samples=%u key_events=%u input_routes=%zu input_changes=%u errors=%u; no simulator writes. Observations do not certify suppression or output.\n",
        passed ? "PASS" : "FAIL", samples, events, inputs.size(), input_changes, errors);
    return passed ? 0 : 1;
}
