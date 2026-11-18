// Windows integration check against the running simulator. Observes only:
// every notification entry has masking=false; no control event is transmitted.
#include <Windows.h>
#include <SimConnect.h>
#include <cstdio>
#include <map>
#include <string>
#include "../Wasm/control_runtime.h"
#include "../Wasm/control_events.h"

namespace {
HANDLE client = nullptr;
bool confirmed = false;
DWORD expected_confirmation = 0;
unsigned failures = 0;
std::map<DWORD, std::string> requests;

void record(HRESULT result, const char* operation)
{
    DWORD packet = 0;
    SimConnect_GetLastSentPacketID(client, &packet);
    requests[packet] = operation;
    if (FAILED(result)) {
        std::printf("Immediate failure: %s (%08lx)\n", operation, static_cast<unsigned long>(result));
        ++failures;
    }
}

void CALLBACK receive(SIMCONNECT_RECV* packet, DWORD, void*)
{
    if (packet->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* error = reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(packet);
        const auto operation = requests.find(error->dwSendID);
        std::printf("Rejected: %s (error %lu, send %lu, index %lu)\n",
            operation == requests.end() ? "unknown call" : operation->second.c_str(),
            error->dwException, error->dwSendID, error->dwIndex);
        ++failures;
    }
    if (packet->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) {
        const auto* state = reinterpret_cast<const SIMCONNECT_RECV_SYSTEM_STATE*>(packet);
        if (state->dwRequestID == expected_confirmation) confirmed = true;
    }
}

bool confirm_configuration(DWORD request)
{
    expected_confirmation = request;
    confirmed = false;
    record(SimConnect_RequestSystemState(client, request, "Sim"), "Configuration barrier");
    const auto started = GetTickCount64();
    while (!confirmed && GetTickCount64() - started < 6000) {
        SimConnect_CallDispatch(client, receive, nullptr);
        Sleep(10);
    }
    return confirmed && !failures;
}
}

int main()
{
    if (FAILED(SimConnect_Open(&client, "FS Copilot non-masking event validation", nullptr, 0, nullptr, 0))) {
        std::puts("Cannot connect. Start MSFS before running this check.");
        return 1;
    }
    record(SimConnect_SubscribeToSystemEvent(client, 0x6200, "AircraftLoaded"), "AircraftLoaded subscription");
    unsigned configurations = 0;
    // Validate startup AND repeated ownership-style rebuilds. Keep masking
    // disabled throughout so this tool cannot detach the user's hardware.
    for (unsigned phase = 0; phase < 4; ++phase) {
        if (phase) {
            for (DWORD group = 0x6100; group <= 0x6103; ++group)
                record(SimConnect_ClearNotificationGroup(client, group), "Clear control group");
        }
        for (size_t i = 0; i < fsc_controls::event_count; ++i) {
            const auto& event = fsc_controls::events[i];
            record(SimConnect_MapClientEventToSimEvent(client, 0x100 + static_cast<DWORD>(i), event.name), event.name);
            const auto selection = fsc_controls::event_selection(event.target, event.name);
            const auto index = fsc_controls::selection_index(selection);
            if (index > 3) { ++failures; continue; }
            const DWORD group = 0x6100 + index;
            record(SimConnect_AddClientEventToNotificationGroup(client, group, 0x100 + static_cast<DWORD>(i), false),
                (std::string("Observe ") + event.name).c_str());
        }
        for (DWORD group = 0x6100; group <= 0x6103; ++group)
            record(SimConnect_SetNotificationGroupPriority(client, group, SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE), "Control group priority");
        if (!confirm_configuration(0x10001 + phase)) break;
        ++configurations;
    }
    SimConnect_Close(client);
    std::printf("Events checked=%zu, groups=4, configurations=%u/4, failures=%u. No hardware inputs were masked.\n",
        fsc_controls::event_count, configurations, failures);
    return configurations == 4 && !failures ? 0 : 2;
}
