// Live SDK integration check. Masks/transmits ONLY a private custom event:
// no aircraft-control, camera or instrument write is performed.
#include <Windows.h>
#include <SimConnect.h>
#include <cstdio>
#include <cstring>
#include <vector>

namespace {
constexpr DWORD event_id = 0x7100, group_id = 0x7101, camera_definition = 0x7102;
struct endpoint {
    HANDLE handle = nullptr;
    bool barrier = false, camera = false;
    unsigned high = 0, low = 0, standard = 0, errors = 0;
    ~endpoint() { if (handle) SimConnect_Close(handle); }
};
void check(endpoint& value, HRESULT result, const char* operation)
{
    if (FAILED(result)) { ++value.errors; std::printf("Immediate failure: %s (%08lx)\n", operation, static_cast<unsigned long>(result)); }
}
void CALLBACK receive(SIMCONNECT_RECV* data, DWORD size, void* context)
{
    auto& value = *static_cast<endpoint*>(context);
    if (data->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* error = reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(data);
        ++value.errors; std::printf("SDK exception %lu (send %lu, index %lu)\n", error->dwException, error->dwSendID, error->dwIndex);
    }
    if (data->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) {
        const auto* state = reinterpret_cast<const SIMCONNECT_RECV_SYSTEM_STATE*>(data);
        if (state->dwRequestID == 1) value.barrier = true;
        if (state->dwRequestID == 3 && std::memchr(state->szString, 0, sizeof(state->szString)))
            std::printf("Aircraft: %s\n", state->szString);
    }
    if (data->dwID == SIMCONNECT_RECV_ID_EVENT) {
        const auto* event = reinterpret_cast<const SIMCONNECT_RECV_EVENT*>(data);
        if (event->uEventID == event_id) { if (event->dwData == 1) ++value.high; if (event->dwData == 2) ++value.low; if (event->dwData == 3) ++value.standard; }
    }
    if (data->dwID == SIMCONNECT_RECV_ID_EVENT_EX1 && size >= sizeof(SIMCONNECT_RECV_EVENT_EX1)) {
        const auto* event = reinterpret_cast<const SIMCONNECT_RECV_EVENT_EX1*>(data);
        if (event->uEventID == event_id) { if (event->dwData0 == 1) ++value.high; if (event->dwData0 == 2) ++value.low; if (event->dwData0 == 3) ++value.standard; }
    }
    if (data->dwID == SIMCONNECT_RECV_ID_SIMOBJECT_DATA && size >= sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA)) {
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SIMOBJECT_DATA*>(data);
        const size_t offset = reinterpret_cast<const char*>(&packet->dwData) - reinterpret_cast<const char*>(data);
        if (packet->dwRequestID == 2 && packet->dwDefineCount == 4 && size >= offset + 4 * sizeof(double)) {
            double values[4]{}; std::memcpy(values, &packet->dwData, sizeof(values)); value.camera = true;
            std::printf("Read-only camera: state=%.0f, viewType=%.0f, optionIndex=%.0f, pilotViewOptions=%.0f\n", values[0], values[1], values[2], values[3]);
        }
    }
}
void pump(endpoint& mask, endpoint& sink, ULONGLONG duration)
{
    const auto start = GetTickCount64();
    while (GetTickCount64() - start < duration) {
        SimConnect_CallDispatch(mask.handle, receive, &mask); SimConnect_CallDispatch(sink.handle, receive, &sink); Sleep(10);
    }
}
}
int main()
{
    endpoint mask, sink;
    if (FAILED(SimConnect_Open(&mask.handle, "FS Copilot custom-event mask probe", nullptr, 0, nullptr, 0)) ||
        FAILED(SimConnect_Open(&sink.handle, "FS Copilot custom-event sink probe", nullptr, 0, nullptr, 0))) {
        std::puts("Cannot connect to MSFS. No simulator state was changed."); return 1;
    }
    for (auto* value : {&mask, &sink}) {
        check(*value, SimConnect_MapClientEventToSimEvent(value->handle, event_id, "FSCOPILOT.RemoteRoute.Probe"), "map private event");
        check(*value, SimConnect_AddClientEventToNotificationGroup(value->handle, group_id, event_id, value == &mask), "subscribe private event");
        check(*value, SimConnect_SetNotificationGroupPriority(value->handle, group_id,
            value == &mask ? SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE : SIMCONNECT_GROUP_PRIORITY_DEFAULT), "private group priority");
        check(*value, SimConnect_RequestSystemState(value->handle, 1, "Sim"), "configuration barrier");
    }
    const auto started = GetTickCount64();
    while ((!mask.barrier || !sink.barrier) && GetTickCount64() - started < 5000) pump(mask, sink, 20);
    if (!mask.barrier || !sink.barrier || mask.errors || sink.errors) { std::puts("Configuration failed."); return 2; }
    check(sink, SimConnect_TransmitClientEvent_EX1(sink.handle, SIMCONNECT_OBJECT_ID_USER, event_id,
        SIMCONNECT_GROUP_PRIORITY_HIGHEST, SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY, 1, 0, 0, 0, 0), "private high-priority transmission");
    pump(mask, sink, 500);
    check(sink, SimConnect_TransmitClientEvent_EX1(sink.handle, SIMCONNECT_OBJECT_ID_USER, event_id,
        SIMCONNECT_GROUP_PRIORITY_DEFAULT, SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY, 2, 0, 0, 0, 0), "private default-priority transmission");
    pump(mask, sink, 500);
    check(sink, SimConnect_TransmitClientEvent_EX1(sink.handle, SIMCONNECT_OBJECT_ID_USER, event_id,
        SIMCONNECT_GROUP_PRIORITY_STANDARD, SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY, 3, 0, 0, 0, 0), "private standard-priority transmission");
    pump(mask, sink, 500);
    std::printf("Custom-event routing: mask(high=%u, default=%u, standard=%u), sink(high=%u, default=%u, standard=%u)\n", mask.high, mask.low, mask.standard, sink.high, sink.low, sink.standard);
    const bool isolated = mask.high == 1 && mask.low == 0 && mask.standard == 0 && sink.high == 0 && sink.low == 1 && sink.standard == 1 && !mask.errors && !sink.errors;
    const char* cameras[] = {"CAMERA STATE", "CAMERA VIEW TYPE AND INDEX:0", "CAMERA VIEW TYPE AND INDEX:1", "CAMERA VIEW TYPE AND INDEX MAX:1"};
    for (const auto* variable : cameras) check(sink, SimConnect_AddToDataDefinition(sink.handle, camera_definition, variable, "Number", SIMCONNECT_DATATYPE_FLOAT64), variable);
    check(sink, SimConnect_RequestDataOnSimObject(sink.handle, 2, camera_definition, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_ONCE), "read camera state");
    check(sink, SimConnect_RequestSystemState(sink.handle, 3, "AircraftLoaded"), "read aircraft identity");
    pump(mask, sink, 1000);
    std::printf("Priority isolation=%s, camera read-back=%s, SDK errors=%u. No aircraft controls or cameras were written.\n",
        isolated ? "PASS" : "FAIL", sink.camera ? "PASS" : "UNAVAILABLE", mask.errors + sink.errors);
    return isolated && sink.camera && !mask.errors && !sink.errors ? 0 : 3;
}
