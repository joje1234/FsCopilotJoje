#include <MSFS/MSFS.h>
#include <MSFS/MSFS_CommBus.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <SimConnect.h>
#include <chrono>
#include <cstdio>
#include <cstring>

#include "control_gate.h"
#include "control_events.h"

namespace
{
using namespace fsc_controls;
constexpr char command_event[] = "FSC_CONTROLS_TEST_COMMAND";
constexpr char status_event[] = "FSC_CONTROLS_TEST_STATUS";
constexpr DWORD steering_group = 0x6100;
constexpr DWORD quadrant_group = 0x6101;
constexpr DWORD aircraft_loaded = 0x6200;
constexpr DWORD first_event = 0x100;
constexpr DWORD first_barrier = 0x10000;
HANDLE connection = nullptr;
char error[120]{};
char session[24]{};
double change_started = 0;
double last_publish = -1;

double seconds()
{
    return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

struct sim_adapter
{
    bool groups_created = false;

    bool configure(bool steering, bool quadrant, uint32_t barrier)
    {
        if (!connection) return false;
        // Rebuild the two small groups. With Me selected they observe events but
        // let them pass; with Mock selected the simulator never receives them.
        if (groups_created &&
            (FAILED(SimConnect_ClearNotificationGroup(connection, steering_group)) ||
             FAILED(SimConnect_ClearNotificationGroup(connection, quadrant_group)))) return false;
        for (size_t i = 0; i < event_count; ++i)
        {
            const bool is_steering = events[i].target == group::steering;
            const DWORD id = is_steering ? steering_group : quadrant_group;
            const bool mask = is_steering ? steering : quadrant;
            if (FAILED(SimConnect_AddClientEventToNotificationGroup(connection, id, first_event + static_cast<DWORD>(i), mask))) return false;
        }
        groups_created = true;
        // HIGHEST is above the maskable range. Use the documented maskable
        // priority, which still precedes the simulator's DEFAULT priority.
        if (FAILED(SimConnect_SetNotificationGroupPriority(connection, steering_group, SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE)) ||
            FAILED(SimConnect_SetNotificationGroupPriority(connection, quadrant_group, SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE))) return false;
        change_started = seconds();
        // Wait for the sim to process preceding configuration calls before the
        // UI claims a change took effect. Async exceptions fail the entire test.
        return SUCCEEDED(SimConnect_RequestSystemState(connection, first_barrier + barrier, "Sim"));
    }

    void release()
    {
        if (!connection) return;
        const HANDLE previous = connection;
        connection = nullptr;
        (void)SimConnect_Close(previous);
    }
};

sim_adapter adapter;
control_gate<sim_adapter> gate(adapter);
struct activity
{
    unsigned long seen = 0;
    unsigned long blocked = 0;
    const char* last = "";
    int32_t value = 0;
};
activity steering_activity;
activity quadrant_activity;

void publish(const command& reply = command{}, const char* reply_error = nullptr)
{
    char message[1024]{};
    const char* problem = reply_error ? reply_error : error;
    const int size = std::snprintf(message, sizeof(message),
        "{\"version\":1,\"session\":\"%s\",\"revision\":%u,\"client\":\"%s\",\"request\":%u,"
        "\"ready\":%s,\"applying\":%s,\"steeringBlocked\":%s,\"quadrantBlocked\":%s,\"error\":\"%s\","
        "\"steering\":{\"seen\":%lu,\"blocked\":%lu,\"last\":\"%s\",\"value\":%d},"
        "\"quadrant\":{\"seen\":%lu,\"blocked\":%lu,\"last\":\"%s\",\"value\":%d}}",
        session, gate.revision, reply.client, reply.request,
        gate.ready ? "true" : "false", gate.applying ? "true" : "false",
        gate.steering_blocked ? "true" : "false", gate.quadrant_blocked ? "true" : "false", problem,
        steering_activity.seen, steering_activity.blocked, steering_activity.last, steering_activity.value,
        quadrant_activity.seen, quadrant_activity.blocked, quadrant_activity.last, quadrant_activity.value);
    if (size > 0 && static_cast<size_t>(size) < sizeof(message))
        fsCommBusCall(status_event, message, static_cast<unsigned int>(size + 1), FsCommBusBroadcast_JS);
}

void fail(const char* reason)
{
    std::snprintf(error, sizeof(error), "%s", reason);
    const command pending = gate.pending;
    gate.fail();
    publish(pending);
}

void receive_command(const char* data, unsigned int size, void*)
{
    command cmd{};
    if (!parse_command(data, size, cmd)) return;
    if (cmd.operation == action::read)
    {
        publish(cmd);
        return;
    }
    if (!gate.ready || gate.applying)
    {
        publish(cmd, error[0] ? error : "Bridge is busy; wait for the current change.");
        return;
    }
    if (!gate.request(cmd))
    {
        fail("Simulator configuration failed. Test released; reload the package to retry.");
        return;
    }
    publish(); // Still pending; never an optimistic acknowledgement.
}

void CALLBACK dispatch(SIMCONNECT_RECV* data, DWORD, void*)
{
    if (!data) return;
    switch (data->dwID)
    {
    case SIMCONNECT_RECV_ID_SYSTEM_STATE:
    {
        const auto* state = reinterpret_cast<const SIMCONNECT_RECV_SYSTEM_STATE*>(data);
        if (state->dwRequestID >= first_barrier && gate.confirm(state->dwRequestID - first_barrier)) publish(gate.pending);
        break;
    }
    case SIMCONNECT_RECV_ID_EVENT:
    {
        const auto* event = reinterpret_cast<const SIMCONNECT_RECV_EVENT*>(data);
        if (event->uEventID < first_event || event->uEventID >= first_event + event_count) break;
        const auto& binding = events[event->uEventID - first_event];
        const bool steering = binding.target == group::steering;
        auto& observed = steering ? steering_activity : quadrant_activity;
        ++observed.seen;
        if (!gate.applying && (steering ? gate.steering_blocked : gate.quadrant_blocked)) ++observed.blocked;
        observed.last = binding.name;
        observed.value = static_cast<int32_t>(event->dwData);
        break;
    }
    case SIMCONNECT_RECV_ID_EVENT_FILENAME:
    {
        const auto* event = reinterpret_cast<const SIMCONNECT_RECV_EVENT_FILENAME*>(data);
        if (event->uEventID == aircraft_loaded && connection)
        {
            steering_activity = activity{};
            quadrant_activity = activity{};
            gate.initialize(); // Each newly loaded aircraft starts with Me.
            publish();
        }
        break;
    }
    case SIMCONNECT_RECV_ID_EXCEPTION:
    {
        const auto* exception = reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(data);
        char reason[120]{};
        std::snprintf(reason, sizeof(reason), "SimConnect error %lu (send %lu, index %lu). Test released; reload to retry.",
            static_cast<unsigned long>(exception->dwException), static_cast<unsigned long>(exception->dwSendID), static_cast<unsigned long>(exception->dwIndex));
        fail(reason);
        break;
    }
    case SIMCONNECT_RECV_ID_QUIT:
        fail("Simulator connection closed. Test released.");
        break;
    default:
        break;
    }
}
}

extern "C" MSFS_CALLBACK void module_init(void)
{
    std::snprintf(session, sizeof(session), "%llx", static_cast<unsigned long long>(seconds() * 1000000));
    fsCommBusRegister(command_event, receive_command, nullptr);
    if (FAILED(SimConnect_Open(&connection, "FS Copilot EFB Control Test", nullptr, 0, 0, 0)))
    {
        connection = nullptr;
        fail("Cannot connect to the simulator. Reload the package to retry.");
        return;
    }
    for (size_t i = 0; i < event_count; ++i)
    {
        if (FAILED(SimConnect_MapClientEventToSimEvent(connection, first_event + static_cast<DWORD>(i), events[i].name)))
        {
            fail("Cannot register control events. Test released.");
            return;
        }
    }
    if (FAILED(SimConnect_SubscribeToSystemEvent(connection, aircraft_loaded, "AircraftLoaded")))
    {
        fail("Cannot monitor aircraft changes. Test released.");
        return;
    }
    // In WASM this registers the dispatch callback once, rather than pumping it
    // every frame (see the SDK's SimConnect_CallDispatch WASM note).
    if (FAILED(SimConnect_CallDispatch(connection, dispatch, nullptr)))
    {
        fail("Cannot register simulator callback. Test released.");
        return;
    }
    gate.initialize();
    if (!gate.applying) fail("Cannot initialize control observation. Test released.");
}

extern "C" MSFS_CALLBACK void Update_StandAlone(float)
{
    const double now = seconds();
    if (gate.applying && now - change_started > 5) fail("Simulator did not confirm the change. Test released; reload to retry.");
    if (now - last_publish >= 0.5)
    {
        last_publish = now;
        publish();
    }
}

extern "C" MSFS_CALLBACK void module_deinit(void)
{
    adapter.release();
    fsCommBusUnregisterOneEvent(command_event, receive_command, nullptr);
}
