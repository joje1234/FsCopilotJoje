// Live read-only capture: notification entries are non-masking at HIGHEST.
// No event, SimVar, InputEvent or camera is written.
#include <Windows.h>
#include <SimConnect.h>
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <cstdlib>
#include <cmath>
#include <cctype>
#include <map>
#include <string>

namespace {
HANDLE client = nullptr;
ULONGLONG started = 0;
unsigned errors = 0;
const char* names[] = {"AXIS_RUDDER_SET", "RUDDER_SET", "AXIS_SENSOR_RUDDER_SET", "RUDDER_AXIS_MINUS", "RUDDER_AXIS_PLUS", "RUDDER_CENTER",
    "RUDDER_LEFT", "RUDDER_RIGHT", "CENTER_AILER_RUDDER", "AXIS_STEERING_SET", "STEERING_SET", "AXIS_LEFT_BRAKE_SET", "AXIS_RIGHT_BRAKE_SET",
    "BRAKES", "BRAKES_LEFT", "BRAKES_RIGHT", "RUDDER_TRIM_SET", "RUDDER_TRIM_SET_EX1"};
unsigned counts[sizeof(names) / sizeof(names[0])]{};
ULONGLONG last_event[sizeof(names) / sizeof(names[0])]{};
std::map<DWORD, std::string> calls;
std::map<UINT64, std::string> inputs;
double seconds() { return (GetTickCount64() - started) / 1000.0; }
void record(HRESULT result, const char* operation)
{
    DWORD id = 0; SimConnect_GetLastSentPacketID(client, &id); calls[id] = operation;
    if (FAILED(result)) { ++errors; std::printf("REJECT %s (%08lx)\n", operation, static_cast<unsigned long>(result)); }
}
void event(DWORD id, DWORD value, bool extended)
{
    if (!id || id > sizeof(names) / sizeof(names[0])) return;
    const auto index = id - 1; ++counts[index];
    const auto now = GetTickCount64();
    if (counts[index] == 1 || now - last_event[index] >= 150) {
        last_event[index] = now;
        std::printf("%.3f KEY%s %s %d\n", seconds(), extended ? "_EX1" : "", names[index], static_cast<int32_t>(value));
    }
}
void CALLBACK receive(SIMCONNECT_RECV* data, DWORD size, void*)
{
    if (data->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* failure = reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(data);
        ++errors; std::printf("SDK exception %lu at %s (index %lu)\n", failure->dwException, calls[failure->dwSendID].c_str(), failure->dwIndex);
    }
    if (data->dwID == SIMCONNECT_RECV_ID_EVENT && size >= sizeof(SIMCONNECT_RECV_EVENT)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_EVENT*>(data); event(packet->uEventID, packet->dwData, false);
    }
    if (data->dwID == SIMCONNECT_RECV_ID_EVENT_EX1 && size >= sizeof(SIMCONNECT_RECV_EVENT_EX1)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_EVENT_EX1*>(data); event(packet->uEventID, packet->dwData0, true);
    }
    if (data->dwID == SIMCONNECT_RECV_ID_SIMOBJECT_DATA && size >= sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SIMOBJECT_DATA*>(data);
        const size_t offset = reinterpret_cast<const char*>(&packet->dwData) - reinterpret_cast<const char*>(data);
        if (packet->dwRequestID == 100 && packet->dwDefineCount == 9 && size >= offset + 9 * sizeof(double)) {
            double positions[9]{}; std::memcpy(positions, &packet->dwData, sizeof(positions));
            static double previous[9]{}; static bool first = true;
            bool changed = first;
            for (unsigned i = 0; i < 9; ++i) if (std::abs(positions[i] - previous[i]) > (i == 0 || i == 6 || i == 7 ? 100 : 0.005)) changed = true;
            if (changed) {
                std::printf("%.3f POSITION rudder=%.0f steer=%.4f brakeL=%.4f brakeR=%.4f ground=%.0f AP=%.0f EX1left=%.0f EX1right=%.0f parking=%.0f\n",
                    seconds(), positions[0], positions[1], positions[2], positions[3], positions[4], positions[5], positions[6], positions[7], positions[8]);
                std::memcpy(previous, positions, sizeof(previous)); first = false;
            }
        }
    }
    if (data->dwID == SIMCONNECT_RECV_ID_ENUMERATE_INPUT_EVENTS && size >= sizeof(SIMCONNECT_RECV_LIST_TEMPLATE)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_ENUMERATE_INPUT_EVENTS*>(data);
        const size_t offset = reinterpret_cast<const char*>(packet->rgData) - reinterpret_cast<const char*>(data);
        if (size < offset || packet->dwArraySize > (size - offset) / sizeof(SIMCONNECT_INPUT_EVENT_DESCRIPTOR)) return;
        for (DWORD i = 0; i < packet->dwArraySize; ++i) {
            const auto& entry = packet->rgData[i]; if (!std::memchr(entry.Name, 0, sizeof(entry.Name))) continue;
            std::string upper(entry.Name); for (auto& c : upper) c = static_cast<char>(std::toupper(static_cast<unsigned char>(c)));
            if (upper.find("RUDDER") == std::string::npos && upper.find("BRAKE") == std::string::npos && upper.find("STEER") == std::string::npos && upper.find("PEDAL") == std::string::npos) continue;
            if (entry.eType != SIMCONNECT_INPUT_EVENT_TYPE_DOUBLE || inputs.count(entry.Hash)) continue;
            inputs[entry.Hash] = entry.Name;
            std::printf("INPUT %s\n", entry.Name); record(SimConnect_SubscribeInputEvent(client, entry.Hash), entry.Name);
        }
    }
    if (data->dwID == SIMCONNECT_RECV_ID_SUBSCRIBE_INPUT_EVENT && size >= sizeof(SIMCONNECT_RECV_SUBSCRIBE_INPUT_EVENT)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SUBSCRIBE_INPUT_EVENT*>(data);
        const auto found = inputs.find(packet->Hash);
        const size_t offset = reinterpret_cast<const char*>(&packet->Value) - reinterpret_cast<const char*>(data);
        if (found != inputs.end() && packet->eType == SIMCONNECT_INPUT_EVENT_TYPE_DOUBLE && size >= offset + sizeof(double)) {
            double value = 0; std::memcpy(&value, &packet->Value, sizeof(value));
            std::printf("%.3f INPUT %s %.6f\n", seconds(), found->second.c_str(), value);
        }
    }
    if (data->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SYSTEM_STATE*>(data);
        if (packet->dwRequestID == 103 && std::memchr(packet->szString, 0, sizeof(packet->szString))) std::printf("AIRCRAFT %s\n", packet->szString);
    }
    std::fflush(stdout);
}
}
int main(int argc, char** argv)
{
    const unsigned duration = argc > 1 ? static_cast<unsigned>(std::strtoul(argv[1], nullptr, 10)) : 90;
    if (duration < 10 || duration > 600) { std::puts("Capture duration must be 10-600 seconds."); return 1; }
    if (FAILED(SimConnect_Open(&client, "FS Copilot read-only pedal capture", nullptr, 0, nullptr, 0))) { std::puts("Cannot connect to MSFS."); return 1; }
    started = GetTickCount64();
    for (DWORD i = 0; i < sizeof(names) / sizeof(names[0]); ++i) {
        record(SimConnect_MapClientEventToSimEvent(client, i + 1, names[i]), names[i]);
        record(SimConnect_AddClientEventToNotificationGroup(client, 99, i + 1, false), names[i]);
    }
    record(SimConnect_SetNotificationGroupPriority(client, 99, SIMCONNECT_GROUP_PRIORITY_HIGHEST), "non-masking observer priority");
    const char* variables[] = {"RUDDER POSITION", "STEER INPUT CONTROL", "BRAKE LEFT POSITION", "BRAKE RIGHT POSITION", "SIM ON GROUND", "AUTOPILOT MASTER", "BRAKE LEFT POSITION EX1", "BRAKE RIGHT POSITION EX1", "BRAKE PARKING POSITION"};
    const char* units[] = {"Position 16k", "Percent Over 100", "Percent Over 100", "Percent Over 100", "Bool", "Bool", "Position 32k", "Position 32k", "Bool"};
    for (unsigned i = 0; i < 9; ++i) record(SimConnect_AddToDataDefinition(client, 100, variables[i], units[i], SIMCONNECT_DATATYPE_FLOAT64), variables[i]);
    record(SimConnect_RequestDataOnSimObject(client, 100, 100, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 5, 0), "read pedal positions");
    record(SimConnect_EnumerateInputEvents(client, 102), "enumerate input events");
    record(SimConnect_RequestSystemState(client, 103, "AircraftLoaded"), "read aircraft identity");
    std::printf("Read-only %u-second pedal capture started. Hardware masks and settings unchanged.\n", duration); std::fflush(stdout);
    while (GetTickCount64() - started < duration * 1000u) { SimConnect_CallDispatch(client, receive, nullptr); Sleep(10); }
    SimConnect_Close(client);
    for (unsigned i = 0; i < sizeof(names) / sizeof(names[0]); ++i) if (counts[i]) std::printf("TOTAL %s %u\n", names[i], counts[i]);
    std::printf("Capture complete: SDK errors=%u, subscribed pedal InputEvents=%zu. No simulator controls were written.\n", errors, inputs.size());
    return 0;
}
