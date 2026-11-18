#include <MSFS/MSFS.h>
#include <MSFS/MSFS_CommBus.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <MSFS/Legacy/gauges.h>
#include <SimConnect.h>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <cstdarg>
#include <initializer_list>

#include "control_runtime.h"
#include "control_events.h"
#include "control_pickup.h"
#include "control_profile.h"
#include "control_output_validation_storage.h"
#include "control_connection_storage.h"
#include "control_force_button.h"
#include "control_targets.h"
#include "control_cockpit_refs.h"
#include "control_bridge_transport.h"
#include "control_remote_writer.h"
#include "control_definition_output.h"
#include "control_output_guard.h"
#include "control_setup_wizard.h"
#include "control_session.h"
#include "control_camera.h"
#include "control_altimeters.h"
#include "control_guidance.h"
#include "control_guidance_transport.h"
#include "control_cockpit.h"
#include "control_instrument_monitor.h"
#include "control_cockpit_transport.h"

namespace
{
using namespace fsc_controls;
static_assert(axis_count == 21, "Update the versioned native bridge wire layout when axes change");
constexpr char command_event[] = "FSC_CONTROLS_TEST_COMMAND";
constexpr char status_event[] = "FSC_CONTROLS_TEST_STATUS";
constexpr char session_join_event[] = "FSC_CONTROLS_SESSION_JOIN";
constexpr DWORD steering_group = 0x6100;
constexpr DWORD quadrant_group = 0x6101;
constexpr DWORD propeller_group = 0x6102;
constexpr DWORD mixture_group = 0x6103;
constexpr DWORD aircraft_loaded = 0x6200;
constexpr DWORD first_event = 0x100;
constexpr DWORD first_barrier = 0x10000;
constexpr DWORD target_definition = 0x6300;
constexpr DWORD target_request = 0x6301;
control_pickup pickup;
HANDLE connection{};
control_bridge_transport native_bridge;
control_guidance_transport guidance_bridge;
control_cockpit ordinary_cockpit;
control_cockpit_transport cockpit_bridge;
double last_cockpit_publish = -1;
uint64_t bridge_serial = 0;
char error[120]{};
char session[24]{};
double change_started = 0;
double last_publish = -1;
double last_native_publish = -1;
bool connection_open = false;
bool start_requested = false;
bool observation_started = false;
double connection_wait_started = 0;
unsigned dispatch_count = 0;
DWORD last_dispatch_id = 0;
struct setup_call { DWORD packet; const char* operation; const char* event; };
setup_call setup_calls[2 * event_count + 9]{};
size_t setup_call_count = 0;
DWORD target_packets[target_count + 1]{};
DWORD identity_request = 0x6400;
DWORD identity_packet = 0;
char aircraft_identity[1024]{};
char profile_prefix[512]{};
control_profile profile{};
bool profile_loaded = false;
bool profile_failed = false;
bool profile_dirty = false;
const definition_profile* aircraft_definitions = nullptr;
control_definition_output remote_output;
control_output_guard output_guard;
control_output_check<control_definition_output> output_check;
control_setup_wizard setup_wizard;
command wizard_requests[8]{};
unsigned wizard_request_index = 0;
output_validation_record output_validation{};
char output_validation_prefix[560]{};
bool output_validation_loaded = false, output_validation_failed = false;
bool output_check_restore_auto = false;
uint32_t relearned_axes = 0;
double profile_changed = 0;
#ifndef FSC_PROFILE_ROOT
#define FSC_PROFILE_ROOT "\\work\\"
#endif
control_force_button force_button;
control_instrument_monitor instrument_monitor;
uint32_t hardware_force_request=0;
connection_settings connection_configuration{};
char backend_source[40]{};
uint64_t backend_sequence = 0;
uint32_t backend_phase = 0, backend_code = 0;
double backend_reported = -1;
uint32_t live_backend_phase(double now) { return backend_reported >= 0 && now >= backend_reported && now - backend_reported <= 3 ? backend_phase : 0; }

bool record_target(HRESULT result, size_t index)
{
    if (FAILED(result)) return false;
    (void)SimConnect_GetLastSentPacketID(connection, &target_packets[index]);
    return true;
}

bool record_setup(HRESULT result, const char* operation, const char* event = "")
{
    if (FAILED(result)) return false;
    DWORD packet{};
    if (SUCCEEDED(SimConnect_GetLastSentPacketID(connection, &packet)) && packet &&
        setup_call_count < sizeof(setup_calls) / sizeof(setup_calls[0]))
        setup_calls[setup_call_count++] = {packet, operation, event};
    return true;
}

double seconds()
{
    return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

void trace(const char* stage, unsigned value = 0)
{
#ifndef FSC_TEST_SILENT_TRACE
    std::printf("[FS Copilot Controls] %s (%u)\n", stage, value);
#endif
    // Persist lifecycle diagnostics for the next Windows session as well as the
    // DevMode console. Never log every hardware event.
#ifdef __wasi__
    if (auto* file = std::fopen("\\work\\controls-test.log", "a"))
    {
        std::fprintf(file, "%.3f %s (%u)\n", seconds(), stage, value);
        std::fclose(file);
    }
#endif
}

void request_aircraft_identity()
{
    aircraft_identity[0] = profile_prefix[0] = '\0';
    aircraft_definitions = nullptr;
    pickup.definitions = nullptr;
    pickup.output_response_axes = 0;
    ordinary_cockpit.reset();
    output_check.reset();
    setup_wizard.reset();
    output_check_restore_auto = false;
    output_validation = output_validation_record{};
    output_validation_prefix[0] = '\0';
    output_validation_loaded = output_validation_failed = false;
    (void)remote_output.select_profile(nullptr);
    profile = control_profile{};
    profile_loaded = profile_failed = profile_dirty = false;
    relearned_axes = 0;
    ++identity_request; // Old aircraft responses must not select a new profile.
    identity_packet = 0;
    if (FAILED(SimConnect_RequestSystemState(connection, identity_request, "AircraftLoaded"))) profile_failed = true;
    else (void)SimConnect_GetLastSentPacketID(connection, &identity_packet);
}

void update_profile()
{
    if (!profile_loaded && aircraft_identity[0] && pickup.engines) {
        // The path is used only as an identity, never as a filesystem path.
        // A hash collision still fails the full identity check on disk.
        std::snprintf(profile_prefix, sizeof(profile_prefix), "%spickup-%08x-%u", FSC_PROFILE_ROOT,
            profile_hash(aircraft_identity, std::strlen(aircraft_identity)), pickup.engines);
        profile = control_profile{};
        if (read_profile(profile_prefix, aircraft_identity, pickup.engines, profile)) {
            restore_profile(profile, pickup, relearned_axes);
            trace("Saved control mappings loaded", profile.generation);
        } else {
            std::snprintf(profile.aircraft, sizeof(profile.aircraft), "%s", aircraft_identity);
            profile.engines = pickup.engines;
        }
        profile_loaded = true;
    }
    if (!output_validation_loaded && profile_loaded && aircraft_definitions && pickup.engines == profile.engines) {
        std::snprintf(output_validation_prefix, sizeof(output_validation_prefix), "%s-output", profile_prefix);
        if (read_output_validation(output_validation_prefix, aircraft_identity, pickup.engines, output_validation)) {
            pickup.output_response_axes = output_validation.response_axes;
            output_check.restore_proofs(output_validation.proofs);
            trace("Saved shared output validation loaded", output_check.valid_passes(pickup, aircraft_definitions));
        } else {
            output_validation = output_validation_record{};
            std::snprintf(output_validation.aircraft, sizeof(output_validation.aircraft), "%s", aircraft_identity);
            output_validation.engines = pickup.engines;
        }
        output_validation_loaded = true;
    }
    // Loading transitions can briefly report zero/new engine counts before the
    // AircraftLoaded event. Do not overwrite the preceding aircraft's profile.
    if (profile_loaded && pickup.engines == profile.engines && capture_profile(profile, pickup)) {
        profile_dirty = true;
        profile_changed = seconds();
    }
}

bool save_output_validation()
{
    if (!output_validation_loaded || output_validation_failed) return false;
    (void)output_check.valid_passes(pickup, aircraft_definitions);
    uint64_t current[4]{}; output_check.copy_proofs(current);
    if (!std::memcmp(current, output_validation.proofs, sizeof(current)) && output_validation.response_axes == pickup.output_response_axes) return true;
    auto next = output_validation;
    std::memcpy(next.proofs, current, sizeof(current));
    next.response_axes = pickup.output_response_axes;
    if (write_output_validation(output_validation_prefix, next)) {
        output_validation = next;
        trace("Shared output validation saved", output_check.passed);
        return true;
    }
    output_validation_failed = true;
    trace("Cannot save shared output validation; results remain session-only");
    return false;
}

uint32_t saved_output_validation()
{
    if (!output_validation_loaded || output_validation_failed) return 0;
    const auto valid = output_check.valid_passes(pickup, aircraft_definitions);
    uint64_t current[4]{}; output_check.copy_proofs(current);
    uint32_t saved = 0;
    for (unsigned index = 0; index < 4; ++index)
        if ((valid & (1u << index)) && output_validation.proofs[index] == current[index]) saved |= 1u << index;
    return saved;
}

void save_profile()
{
    if (!profile_loaded || !profile_dirty || profile_failed) return;
    if (write_profile(profile_prefix, profile)) {
        profile_dirty = false;
        trace("Control mappings saved", profile.generation);
    } else {
        profile_failed = true;
        trace("Cannot save control mappings; session learning remains available");
    }
}

uint32_t event_selection(const event_binding& binding)
{
    return fsc_controls::event_selection(binding.target, binding.name);
}

struct sim_adapter
{
    bool groups_created = false;

    bool configure_mask(uint32_t blocked, uint32_t barrier)
    {
        if (!connection) return false;
        setup_call_count = 0;
        const DWORD groups[] = {steering_group, quadrant_group, propeller_group, mixture_group};
        if (groups_created) for (const auto id : groups)
            if (!record_setup(SimConnect_ClearNotificationGroup(connection, id), "Clear control group")) return false;
        for (size_t i = 0; i < event_count; ++i)
        {
            const auto selection = event_selection(events[i]);
            const auto index = selection_index(selection);
            if (index > 3) return false;
            const DWORD id = groups[index];
            const bool mask = !!(blocked & selection);
            // Clearing a notification group can discard its client-event IDs.
            // Re-establish each mapping before adding it to the rebuilt group.
            if (!record_setup(SimConnect_MapClientEventToSimEvent(connection, first_event + static_cast<DWORD>(i), events[i].name), "Map", events[i].name) ||
                !record_setup(SimConnect_AddClientEventToNotificationGroup(connection, id, first_event + static_cast<DWORD>(i), mask), "Add", events[i].name)) return false;
        }
        groups_created = true;
        // HIGHEST is above the maskable range. Use the documented maskable
        // priority, which still precedes the simulator's DEFAULT priority.
        for (const auto id : groups)
            if (!record_setup(SimConnect_SetNotificationGroupPriority(connection, id, SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE), "Control group priority")) return false;
        change_started = seconds();
        trace("Request configuration confirmation", barrier);
        // Wait for the sim to process preceding configuration calls before the
        // UI claims a change took effect. Async exceptions fail the entire test.
        return record_setup(SimConnect_RequestSystemState(connection, first_barrier + barrier, "Sim"), "Confirmation");
    }

    void release()
    {
        if (!connection) return;
        const HANDLE previous = connection;
        connection = {};
        (void)SimConnect_Close(previous);
    }
};

sim_adapter adapter;
control_runtime<sim_adapter> gate(pickup, adapter);
control_remote_writer<control_runtime<sim_adapter>, control_definition_output> remote_writer(pickup, gate, remote_output);
control_world world;
control_camera camera;
control_altimeters altimeters;
control_guidance guidance;
uint64_t camera_preference_revision = 0;
control_session<decltype(gate), decltype(remote_writer)> shared_session(pickup, gate, remote_writer, &world, &altimeters, nullptr, &guidance);
output_check_context output_context() {
    auto context = output_guard.values;
    context.local = shared_session.status().phase == session_phase::idle && !shared_session.holding() && !remote_writer.holding();
    context.ready = gate.ready; context.applying = gate.applying;
    context.blocked = gate.blocked; context.automatic = gate.automatic; context.waiting = gate.waiting();
    return context;
}

const char* setup_reset_reason()
{
    if (!profile_loaded || !output_validation_loaded || !aircraft_identity[0]) return "Waiting for this aircraft's saved setup.";
    if (setup_wizard.active()) return "Finish or cancel the wizard before resetting setup.";
    if (output_check.running()) return "Wait for the current output check to finish.";
    const auto* reason = control_setup_wizard::prerequisite(output_context(), true, aircraft_definitions != nullptr, true, seconds());
    if (reason[0]) return reason;
    if (gate.blocked) return "Return all controls to Me before resetting setup.";
    return "";
}

const char* reset_saved_setup()
{
    const auto* reason = setup_reset_reason();
    if (reason[0]) return reason;
    // Revoke proofs durably before forgetting input calibration. No ownership,
    // hardware position, aircraft state, connection or controller binding changes.
    output_check.invalidate();
    auto cleared_output = output_validation;
    for (auto& proof : cleared_output.proofs) proof = 0;
    cleared_output.response_axes = 0;
    if (!write_output_validation(output_validation_prefix, cleared_output)) {
        output_validation_failed = true;
        return "Could not clear saved output checks. Check WASM storage, then retry Reset setup.";
    }
    output_validation = cleared_output; output_validation_failed = false;
    pickup.output_response_axes = 0;
    auto cleared_profile = profile;
    for (auto& mapping : cleared_profile.mapping) mapping = profile_axis{};
    // Overwrite both generations so an older slot cannot restore old learning.
    control_profile verified{};
    if (!write_profile(profile_prefix, cleared_profile) || !write_profile(profile_prefix, cleared_profile) ||
        !read_profile(profile_prefix, aircraft_identity, pickup.engines, verified) ||
        std::memcmp(&cleared_profile, &verified, sizeof(verified))) {
        profile_failed = true;
        return "Output checks cleared, but saved learning could not be cleared. Check WASM storage, then retry Reset setup.";
    }
    profile = cleared_profile; profile_failed = profile_dirty = false;
    for (auto& input : pickup.inputs) input = pickup_axis{};
    relearned_axes = (1u << axis_count) - 1;
    setup_wizard.reset(); output_check.reset(); output_check_restore_auto = false;
    ++gate.revision;
    trace("Setup reset: learned controls and output checks cleared; connection settings retained");
    return "";
}

char recovery_packet[128]{};
unsigned recovery_size = 0;
double recovery_started = -1;
command recovery_command{};
struct activity
{
    unsigned long seen = 0;
    unsigned long blocked = 0;
    const char* last = "";
    int32_t value = 0;
};
activity steering_activity;
activity quadrant_activity;
activity pair_activity[3];

activity& activity_for(group value)
{
    return value == group::steering ? steering_activity : value == group::quadrant ? quadrant_activity : pair_activity[static_cast<unsigned>(value) - 2];
}

bridge_packet bridge_status(const bridge_packet* hello = nullptr)
{
    bridge_packet packet{};
    std::snprintf(packet.native, sizeof(packet.native), "%s", session);
    if (std::strlen(aircraft_identity) < sizeof(packet.aircraft))
        std::snprintf(packet.aircraft, sizeof(packet.aircraft), "%s", aircraft_identity);
    std::snprintf(packet.adapter, sizeof(packet.adapter), "%s", aircraft_definitions ? aircraft_definitions->adapter : "standard-controls-v1");
    packet.serial = ++bridge_serial; packet.revision = gate.revision;
    const auto now = seconds();
    packet.connection_api = 1; packet.configuration = connection_configuration;
    if (guidance_bridge.available) packet.capabilities |= 16u; // AP transport only; not input/output authority.
    if (cockpit_bridge.available) packet.capabilities |= 32u; // Ordinary definition transport, separate from seat-owned experimental barometers.
    if (output_check.valid_passes(pickup, aircraft_definitions) == select_all && remote_output.available) packet.capabilities |= 4u;
    packet.backend_phase = live_backend_phase(now); packet.backend_code = packet.backend_phase ? backend_code : 0;
    const bool targets_fresh = pickup.target_time >= 0 && now - pickup.target_time <= 1;
    packet.flags = (gate.ready && !gate.applying && packet.aircraft[0] && pickup.engines && targets_fresh ? bridge_ready : 0u) |
        (gate.applying ? bridge_applying : 0u) | (gate.advanced ? bridge_advanced : 0u) |
        (targets_fresh ? bridge_targets_fresh : 0u) | (profile_failed ? bridge_storage_failed : 0u);
    packet.blocked = gate.blocked;
    packet.baro_serial = altimeters.serial;
    packet.baro_pressure16[0] = altimeters.values[0].pressure16; packet.baro_pressure16[1] = altimeters.values[1].pressure16;
    packet.baro_standard = (altimeters.values[0].standard ? 1u : 0u) | (altimeters.values[1].standard ? 2u : 0u);
    packet.baro_flags = shared_session.barometer_flags(now);
    packet.automatic = shared_session.active() ? (shared_session.status().automatic ? select_quadrant : 0u) : shared_session.holding() ? 0u : gate.automatic;
    packet.engines = pickup.engines;
    for (size_t axis = 0; axis < axis_count; ++axis) {
        const auto bit = 1u << axis;
        if (pickup.required_axis(axis)) packet.required |= bit;
        if (pickup.axis_known(axis)) packet.learned |= bit;
        if (gate.axis_fresh(axis, now)) packet.fresh |= bit;
        if (gate.axis_aligned(axis, now)) packet.aligned |= bit;
        const double position = pickup.input_position(axis);
        packet.input[axis] = std::isfinite(position) && position >= 0 && position <= 1 ? position : -1;
        const bool reference_known = aircraft_identity[0] || axis < steering_axis_count || (axis - steering_axis_count) % 3 != 2;
        packet.targets[axis] = reference_known && targets_fresh && std::isfinite(pickup.targets[axis]) && pickup.targets[axis] >= 0 && pickup.targets[axis] <= 1 ? pickup.targets[axis] : -1;
    }
    for (unsigned index = 0; index < 4; ++index) packet.tolerance[index] = static_cast<unsigned>(std::lround(gate.tolerance[index] * 100));
    if (hello) {
        packet.kind = bridge_kind::reply; packet.request = hello->request;
        std::memcpy(packet.backend, hello->backend, sizeof(packet.backend));
    }
    return packet;
}

session_packet shared_status()
{
    auto packet = shared_session.status(); const auto now = seconds();
    if (pickup.autopilot) packet.flags |= session_autopilot;
    packet.world_serial = world.serial;
    std::memcpy(packet.world_values, world.values, sizeof(packet.world_values));
    if (world.fresh(now)) packet.flags |= session_world_fresh;
    if (world.fresh(now) && world.frozen_owned()) packet.flags |= session_world_frozen;
    for (size_t axis = 0; axis < axis_count; ++axis) {
        const auto bit = 1u << axis;
        if (pickup.required_axis(axis)) packet.required |= bit;
        if (gate.axis_fresh(axis, now)) packet.fresh |= bit;
        if (gate.axis_aligned(axis, now)) packet.aligned |= bit;
        // Buttons and pedals can coexist. While Me is active, the cockpit
        // reference contains their combined demand. Never sample reflected
        // remote output as local input while Steering is masked.
        const bool local_canonical = pickup.canonical_axis(axis) && !(gate.blocked & select_steering) && !gate.applying;
        const auto position = local_canonical && pickup.valid_target(axis) ? pickup.targets[axis] : pickup.input_position(axis);
        if (local_canonical && pickup.valid_target(axis) && now - pickup.target_time <= 1) packet.fresh |= bit;
        packet.positions[axis] = std::isfinite(position) && position >= 0 && position <= 1 ? position : -1;
    }
    return packet;
}

void receive_session_consent(const char* data, unsigned int size, void*)
{
    if (!data || (size != 32 && size != 33) || (size == 33 && data[32])) return;
    char nonce[33]{}; std::memcpy(nonce, data, 32);
    if (bridge_token(nonce, sizeof(nonce), 32) && shared_session.arm(nonce, seconds())) {
        (void)world.start(); trace("Local shared-session join armed");
    }
}

void append(char* message, size_t capacity, size_t& used, const char* format, ...)
{
    if (used >= capacity) return;
    va_list args;
    va_start(args, format);
    const int added = std::vsnprintf(message + used, capacity - used, format, args);
    va_end(args);
    if (added < 0 || static_cast<size_t>(added) >= capacity - used) used = capacity;
    else used += static_cast<size_t>(added);
}

void append_json_text(char* message,size_t capacity,size_t& used,const char* value,size_t limit=1024) {
    append(message,capacity,used,"\"");
    for(const unsigned char* c=reinterpret_cast<const unsigned char*>(value);*c&&limit; ++c,--limit) {
        if(*c=='"'||*c=='\\')append(message,capacity,used,"\\%c",*c);
        else if(*c<' '||*c>126)append(message,capacity,used,"\\u%04x",static_cast<unsigned>(*c));
        else append(message,capacity,used,"%c",*c);
    }
    append(message,capacity,used,"\"");
}
void publish(const command& reply = command{}, const char* reply_error = nullptr)
{
    char message[32768]{};
    const auto shared = shared_session.status();
    const auto automatic = shared_session.active() ? (shared.automatic ? select_quadrant : 0u) : shared_session.holding() ? 0u : gate.automatic;
    const auto shared_waiting = (shared.intent_waiting | shared.automatic_waiting) & ~shared.intent_other;
    const char* problem = reply_error ? reply_error : error;
    const int size = std::snprintf(message, sizeof(message),
        "{\"version\":5,\"session\":\"%s\",\"revision\":%u,\"client\":\"%s\",\"request\":%u,"
        "\"ready\":%s,\"applying\":%s,\"steeringBlocked\":%s,\"quadrantBlocked\":%s,\"error\":\"%s\","
        "\"advanced\":%s,\"autoMask\":%u,\"quadrantMixed\":%s,\"quadrantToleranceMixed\":%s,"
        "\"throttleBlocked\":%s,\"propellerBlocked\":%s,\"mixtureBlocked\":%s,"
        "\"learningStorage\":\"%s\",\"mixtureReference\":\"%s\",",
        session, gate.revision, reply.client, reply.request,
        gate.ready ? "true" : "false", gate.applying || recovery_size ? "true" : "false",
        gate.steering_blocked ? "true" : "false", gate.quadrant_blocked ? "true" : "false", problem,
        gate.advanced ? "true" : "false", automatic,
        (gate.blocked & select_quadrant) && !gate.quadrant_blocked ? "true" : "false",
        gate.tolerance[1] != gate.tolerance[2] || gate.tolerance[1] != gate.tolerance[3] ? "true" : "false",
        gate.blocked & select_throttle ? "true" : "false", gate.blocked & select_propeller ? "true" : "false", gate.blocked & select_mixture ? "true" : "false",
        profile_failed ? "unavailable" : !profile_loaded ? "waiting" : profile_dirty ? "saving" : "ready",
        !aircraft_identity[0] ? "waiting" : has_cockpit_references(aircraft_definitions) ? "cockpit" : "standard");
    if (size <= 0 || static_cast<size_t>(size) >= sizeof(message)) return;
    size_t used = static_cast<size_t>(size);
    append(message,sizeof(message),used,"\"forceButton\":{\"saved\":%s,\"ready\":%s,\"armed\":%s,\"listening\":%s,\"captured\":%s,\"button\":%u,\"name\":",force_button.settings.enabled?"true":"false",force_button.ready()?"true":"false",force_button.armed()?"true":"false",force_button.listening()?"true":"false",force_button.captured()?"true":"false",force_button.settings.button+1);
    append_json_text(message,sizeof(message),used,force_button.settings.name,80);
    append(message,sizeof(message),used,",\"error\":");append_json_text(message,sizeof(message),used,force_button.error);
    append(message,sizeof(message),used,",\"devices\":[");
    for(unsigned i=0;i<force_button.count;++i){append(message,sizeof(message),used,"%s{\"id\":%u,\"name\":",i?",":"",force_button.devices[i].DeviceId);append_json_text(message,sizeof(message),used,force_button.devices[i].DeviceName,80);append(message,sizeof(message),used,"}");}
    append(message,sizeof(message),used,"]},");
    append(message, sizeof(message), used, "\"outputCheck\":{\"available\":%s,\"writable\":%s,\"phase\":%u,\"group\":\"%s\",\"passed\":%u,\"restored\":%s,\"storage\":\"%s\",\"saved\":%u},",
        aircraft_definitions ? "true" : "false", remote_output.available ? "true" : "false", static_cast<unsigned>(output_check.phase), group_name(output_check.target),
        output_check.valid_passes(pickup, aircraft_definitions), output_check.restored ? "true" : "false",
        output_validation_failed ? "unavailable" : output_validation_loaded ? "ready" : "waiting", saved_output_validation());
    const auto* wizard_prerequisite = control_setup_wizard::prerequisite(output_context(), output_guard.engines_off(pickup.engines, seconds()), aircraft_definitions != nullptr, remote_output.available, seconds());
    if (!wizard_prerequisite[0] && output_check.running() && !setup_wizard.active()) wizard_prerequisite = "Wait for the current output check to finish.";
    const auto* reset_reason = setup_reset_reason();
    append(message, sizeof(message), used, "\"wizard\":{\"phase\":%u,\"group\":\"%s\",\"needed\":%u,\"axis\":%u,\"ready\":%s,\"message\":\"%s\",\"prerequisite\":\"%s\",\"resetReady\":%s,\"resetReason\":\"%s\"},",
        static_cast<unsigned>(setup_wizard.phase), group_name(setup_wizard.target), setup_wizard.needed_axes,
        static_cast<unsigned>(setup_wizard.failed_axis), !wizard_prerequisite[0] && !output_check.running() ? "true" : "false", setup_wizard.message, wizard_prerequisite,
        !reset_reason[0] ? "true" : "false", reset_reason);
    for (group value : {group::steering, group::quadrant, group::throttle, group::propeller, group::mixture}) {
        const auto& activity = activity_for(value);
        append(message, sizeof(message), used, "\"%s\":{\"seen\":%lu,\"blocked\":%lu,\"last\":\"%s\",\"value\":%d},",
            group_name(value), activity.seen, activity.blocked, activity.last, activity.value);
    }
    append(message, sizeof(message), used,
        "\"connection\":{\"api\":1,\"revision\":%llu,\"relay\":\"%s\",\"port\":%u,\"local\":\"%s\",\"peer\":\"%s\",\"role\":%u,\"seat\":%u,\"backend\":%u,\"code\":%u},",
        static_cast<unsigned long long>(connection_configuration.revision), connection_configuration.relay, connection_configuration.port,
        connection_configuration.local, connection_configuration.peer, connection_configuration.role, connection_configuration.seat,
        live_backend_phase(seconds()), live_backend_phase(seconds()) ? backend_code : 0);
    append(message, sizeof(message), used, "\"camera\":{\"seat\":%u,\"phase\":%u},", camera.seat, static_cast<unsigned>(camera.phase));
    const auto instrument_now = seconds(); const auto instrument_ui = shared_session.instrument_display(instrument_now);
    append(message, sizeof(message), used, "\"altimeters\":{\"serial\":%llu,\"fresh\":%s,\"pressure16\":[%u,%u],\"standard\":[%s,%s],\"nativeFlags\":%u,\"reported\":%s,\"sharedPhase\":%u,\"pending\":%u,\"issue\":%u},",
        static_cast<unsigned long long>(altimeters.serial), altimeters.fresh(instrument_now) ? "true" : "false",
        altimeters.values[0].pressure16, altimeters.values[1].pressure16,
        altimeters.values[0].standard ? "true" : "false", altimeters.values[1].standard ? "true" : "false",
        shared_session.barometer_flags(instrument_now), shared_session.instrument_reported(instrument_now) ? "true" : "false", instrument_ui.phase, instrument_ui.pending, instrument_ui.code);
    const auto& ap = guidance.values; const auto target_slots = guidance.targets();
    const auto& ordinary = ordinary_cockpit.status();
    unsigned ordinary_known = 0;
    for (unsigned index=0;index<ordinary.count;++index) if (ordinary_cockpit.known(index)) ++ordinary_known;
    append(message,sizeof(message),used,"\"cockpit\":{\"available\":%s,\"sharing\":%s,\"count\":%u,\"known\":%u,\"result\":%u,",
        cockpit_bridge.available && ordinary.count ? "true":"false",ordinary.active && ordinary.linked ? "true":"false",
        ordinary.count,ordinary_known,static_cast<unsigned>(ordinary.result));
    append(message,sizeof(message),used,"\"recovery\":{\"phase\":%u,\"checked\":%u,\"corrected\":%u,\"pending\":%u,\"failed\":%u,\"unavailable\":%u,\"issues\":",
        ordinary.resync_phase,ordinary.checked,ordinary.corrected,ordinary.resync_pending,ordinary.failed,ordinary.unavailable);
    append_json_text(message,sizeof(message),used,ordinary.issues);append(message,sizeof(message),used,"},");
    append(message,sizeof(message),used,"\"monitor\":{\"profile\":");append_json_text(message,sizeof(message),used,ordinary.profile);
    append(message,sizeof(message),used,",\"sequence\":%llu,\"catalog\":[",static_cast<unsigned long long>(instrument_monitor.sequence));
    if(aircraft_definitions&&aircraft_definitions->cockpit&&ordinary.count==aircraft_definitions->cockpit_count) {
        const auto first=instrument_monitor.page;
        for(unsigned i=first;i<ordinary.count&&i<first+control_instrument_monitor::page_size;++i) {
            const auto& entry=aircraft_definitions->cockpit[i];
            append(message,sizeof(message),used,"%s{\"index\":%u,\"name\":",i==first?"":",",i);append_json_text(message,sizeof(message),used,entry.name,96);
            append(message,sizeof(message),used,",\"label\":");append_json_text(message,sizeof(message),used,entry.label?entry.label:entry.name,96);
            append(message,sizeof(message),used,",\"units\":");append_json_text(message,sizeof(message),used,entry.units,32);
            append(message,sizeof(message),used,",\"shared\":%s,\"value\":",entry.shared?"true":"false");
            if(ordinary_cockpit.known(i))append(message,sizeof(message),used,"%.12g",ordinary.values[i]);else append(message,sizeof(message),used,"null");
            append(message,sizeof(message),used,"}");
        }
    }
    instrument_monitor.next_page(ordinary.count);
    append(message,sizeof(message),used,"],\"changes\":[");
    for(unsigned i=0;i<instrument_monitor.recent_count;++i) {
        const auto& change=instrument_monitor.recent[i];
        append(message,sizeof(message),used,"%s{\"index\":%u,\"sequence\":%llu,\"before\":%.12g,\"value\":%.12g}",i?",":"",change.index,
            static_cast<unsigned long long>(change.sequence),change.before,change.after);
    }
    append(message,sizeof(message),used,"]}},");
    append(message, sizeof(message), used,
        "\"guidance\":{\"serial\":%llu,\"fresh\":%s,\"switches\":%u,\"heading\":%.5f,\"altitude\":%.3f,\"verticalSpeed\":%.3f,\"gps\":%s,\"nav\":%u,\"slots\":[%u,%u,%u],\"targetSlots\":[%u,%u,%u],\"outputAvailable\":false},",
        static_cast<unsigned long long>(guidance.serial), guidance.fresh(instrument_now) ? "true" : "false", ap.switches,
        ap.heading, ap.altitude, ap.vertical_speed, ap.gps ? "true" : "false", ap.nav, ap.slots[0], ap.slots[1], ap.slots[2],
        target_slots.heading, target_slots.altitude, target_slots.vertical_speed);
    append(message, sizeof(message), used,
        "\"shared\":{\"phase\":%u,\"owners\":[%u,%u,%u,%u],\"attached\":%u,\"pending\":%u,\"waiting\":%u,\"other\":%u,\"holding\":%s,\"needsRecovery\":%s,\"seatRevision\":%llu,\"seat\":%u,\"seatPending\":%u,\"seatRequest\":%llu,\"session\":\"%s\",\"serial\":%llu},\"pickup\":{",
        static_cast<unsigned>(shared.phase), static_cast<unsigned>(shared.owner[0]), static_cast<unsigned>(shared.owner[1]),
        static_cast<unsigned>(shared.owner[2]), static_cast<unsigned>(shared.owner[3]), shared.attached, shared.pending,
        shared_waiting, shared.intent_other, shared_session.holding() ? "true" : "false",
        shared_session.holding() && world.frozen_owned() ? "true" : "false",
        static_cast<unsigned long long>(shared.seat_revision), shared.seat, shared.seat_pending, static_cast<unsigned long long>(shared.seat_request),
        shared.session, static_cast<unsigned long long>(shared.serial));
    const double now = seconds();
    for (group value : {group::steering, group::quadrant, group::throttle, group::propeller, group::mixture}) {
        const auto selection = group_selection(value);
        bool waiting = !!(gate.waiting() & selection);
        if (value == group::quadrant) {
            waiting = false; for (const auto& request : gate.takeovers) if (request.selection == select_quadrant) waiting = true;
        }
        if (shared_session.active()) {
            uint32_t local_detached = 0;
            for (unsigned slot = 0; slot < 4; ++slot) if (shared.owner[slot] == writer_owner::local && !(shared.attached & (1u << slot)) &&
                !((shared.pending | shared_session.paused_intents()) & (1u << slot)) && (shared.generation[slot] == 1 || shared.committed_transfer[slot][0])) local_detached |= 1u << slot;
            waiting = !!((shared_waiting | local_detached) & selection);
            if (value == group::quadrant) waiting = ((shared_waiting | local_detached) & selection) == selection;
        }
        const auto index = value == group::quadrant ? 1u : selection_index(selection);
        append(message, sizeof(message), used, "%s\"%s\":{\"waiting\":%s,\"automation\":%s,\"autoEnabled\":%s,\"tolerance\":%u,\"axes\":[",
            value == group::steering ? "" : ",", group_name(value),
            waiting ? "true" : "false", value == group::steering && pickup.autopilot ? "true" : "false",
            (automatic & selection) ? "true" : "false", static_cast<unsigned>(std::lround(gate.tolerance[index] * 100)));
        bool first = true;
        for (size_t axis = 0; axis < axis_count; ++axis) if (pickup.required_axis(axis) && control_pickup::matches_group(axis, value)) {
            char input[32] = "null", target[32] = "null";
            const double position = pickup.input_position(axis);
            if (position >= 0 && position <= 1) std::snprintf(input, sizeof(input), "%.2f", position * 100);
            const bool reference_known = aircraft_identity[0] || axis < steering_axis_count || (axis - steering_axis_count) % 3 != 2;
            if (reference_known && pickup.target_time >= 0 && now - pickup.target_time <= 1 && std::isfinite(pickup.targets[axis]) && pickup.targets[axis] >= 0 && pickup.targets[axis] <= 1)
                std::snprintf(target, sizeof(target), "%.2f", pickup.targets[axis] * 100);
            append(message, sizeof(message), used,
                "%s{\"id\":\"%s\",\"label\":\"%s\",\"input\":%s,\"target\":%s,\"calibrated\":%s,\"saved\":%s,\"fresh\":%s,\"aligned\":%s,\"fixed\":%s,\"relative\":%s}",
                first ? "" : ",", axes[axis].id, axes[axis].label, input, target,
                pickup.axis_known(axis) ? "true" : "false",
                pickup.inputs[axis].saved_candidates && !pickup.canonical_axis(axis) ? "true" : "false",
                gate.axis_fresh(axis, now) ? "true" : "false",
                gate.axis_aligned(axis, now) ? "true" : "false",
                pickup.canonical_axis(axis) ? "true" : "false", pickup.relative_input(axis) ? "true" : "false");
            first = false;
        }
        append(message, sizeof(message), used, "]}");
    }
    append(message, sizeof(message), used, "}}");
    if (used < sizeof(message)) fsCommBusCall(status_event, message, static_cast<unsigned int>(used + 1), FsCommBusBroadcast_JS);
}

void fail(const char* reason)
{
    trace(reason);
    std::snprintf(error, sizeof(error), "%s", reason);
    const command pending = gate.pending;
    pickup.cancel(group::steering);
    pickup.cancel(group::quadrant);
    shared_session.stop();
    remote_writer.end();
    remote_output.stop();
    gate.fail();
    publish(pending);
}

void try_start_observation()
{
    // Standalone modules load during simulator startup. Do not start the short
    // configuration watchdog until the connection is open AND the EFB asks for
    // status. Startup/loading time is not a failed ownership change.
    if (!connection || !connection_open || !start_requested || observation_started) return;
    observation_started = true;
    if (FAILED(SimConnect_SubscribeToSystemEvent(connection, aircraft_loaded, "AircraftLoaded")))
    {
        fail("Cannot monitor aircraft changes. Test released.");
        return;
    }
    for (size_t axis = 0; axis < axis_count; ++axis) {
        if (!record_target(SimConnect_AddToDataDefinition(connection, target_definition, axes[axis].variable, axes[axis].units, SIMCONNECT_DATATYPE_FLOAT64), axis)) {
            fail("Cannot register cockpit control positions. Test released."); return;
        }
    }
    if (!record_target(SimConnect_AddToDataDefinition(connection, target_definition, "NUMBER OF ENGINES", "Number", SIMCONNECT_DATATYPE_FLOAT64), axis_count) ||
        !record_target(SimConnect_AddToDataDefinition(connection, target_definition, "AUTOPILOT MASTER", "Bool", SIMCONNECT_DATATYPE_FLOAT64), axis_count + 1)) {
        fail("Cannot monitor cockpit control positions. Test released."); return;
    }
    // Observe each visual frame so alignment/output checks do not read a
    // three-frame-old cockpit position at lower simulator frame rates.
    if (!record_target(SimConnect_RequestDataOnSimObject(connection, target_request, target_definition, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 0, 0), sim_target_count)) {
        fail("Cannot monitor cockpit control positions. Test released."); return;
    }
    trace("Start control observation");
    request_aircraft_identity();
    gate.initialize(true, true);
    if (!gate.applying && !gate.ready) fail("Cannot initialize control observation. Test released.");
}

void receive_command(const char* data, unsigned int size, void*)
{
    command cmd{};
    if (!parse_command(data, size, cmd)) return;
    if (cmd.operation == action::wizard_start || cmd.operation == action::wizard_cancel || cmd.operation == action::reset_setup) {
        for (const auto& seen : wizard_requests) if (seen.request == cmd.request && !std::strcmp(seen.client, cmd.client)) { publish(cmd); return; }
        wizard_requests[wizard_request_index++ % 8] = cmd;
    }
    if (cmd.operation == action::read)
    {
        if (!start_requested)
        {
            start_requested = true;
            connection_wait_started = seconds();
            trace("EFB requested status");
        }
        try_start_observation();
        publish(cmd);
        return;
    }
    if(cmd.operation==action::resync) {
        const auto now=seconds();
        const bool accepted=!setup_wizard.active() && !output_check.running() && shared_session.optional_live(now) && ordinary_cockpit.request_resync(shared_session.status(),now);
        publish(cmd,accepted?"":"Cockpit resync needs an active shared session with matching instrument definitions.");return;
    }
    if(cmd.operation==action::force_button) {
        if(setup_wizard.active()||output_check.running()){publish(cmd,"Finish the setup wizard before assigning a button.");return;}
        if(cmd.button_action==1)(void)force_button.listen(cmd.button_device,seconds());
        else if(cmd.button_action==2)force_button.cancel();else (void)force_button.clear();
        publish(cmd,force_button.error);return;
    }
    if (cmd.operation == action::wizard_cancel) {
        setup_wizard.cancel(output_check, seconds()); publish(cmd); return;
    }
    if (setup_wizard.active()) {
        if (cmd.operation == action::force || cmd.operation == action::leave) {
            setup_wizard.interrupt("Setup stopped by explicit local recovery. Auto remains paused.");
            output_check_restore_auto = false;
        } else if (cmd.operation != action::wizard_start) {
            publish(cmd, "Finish or cancel the Setup wizard before changing other controls."); return;
        }
    }
    if (cmd.operation == action::wizard_start) {
        if (!save_output_validation()) { publish(cmd, "Cannot save output validation. Check the simulator's writable WASM storage before setup."); return; }
        const bool accepted = !recovery_size && setup_wizard.start(gate, pickup, output_check, remote_output, aircraft_definitions,
            output_context(), output_guard.engines_off(pickup.engines, seconds()), seconds());
        publish(cmd, accepted ? "" : setup_wizard.message); return;
    }
    if (cmd.operation == action::reset_setup) {
        publish(cmd, reset_saved_setup()); return;
    }
    if (cmd.operation == action::instruments_local) {
        publish(cmd, shared_session.use_local_instruments(cmd.instrument_session, seconds()) ? "" :
            "The instrument session changed or native input recovery is unavailable. Refresh and retry."); return;
    }
    if (!gate.ready || gate.applying || recovery_size)
    {
        publish(cmd, error[0] ? error : "Bridge is busy; wait for the current change.");
        return;
    }
    if (output_check.running()) {
        if (cmd.operation == action::force || cmd.operation == action::leave) { output_check_restore_auto = false; output_check.cancel(); }
        else { publish(cmd, "Wait for the parked output check to finish."); return; }
    }
    if (cmd.operation == action::output_check) {
        const bool pause_auto = output_context().local && !!gate.automatic;
        if (pause_auto && !gate.set_auto(select_quadrant, false, seconds())) { publish(cmd, "Wait before starting the output check."); return; }
        const auto context = output_context();
        bool accepted = output_check.start(cmd.target, pickup, aircraft_definitions, context, remote_output, seconds());
        if (accepted && !save_output_validation()) {
            // Revoke a previous saved pass before the first diagnostic write.
            // A restart during a failed recheck must not restore the old pass.
            output_check.cancel();
            if (pause_auto) (void)gate.set_auto(select_quadrant, true, seconds());
            publish(cmd, "Cannot save output validation. No diagnostic movement was started. Check the simulator's writable WASM storage.");
            return;
        }
        if (!accepted) {
            if (pause_auto) (void)gate.set_auto(select_quadrant, true, seconds());
            char reason[160]{}; std::snprintf(reason, sizeof(reason), "Output check refused: ground=%.0f knots=%.3f brake=%.0f AP=%.0f Auto=%u blocked=%u waiting=%u", context.ground, context.knots, context.parking_brake, context.ap, context.automatic, context.blocked, context.waiting);
            trace(reason);
        }
        output_check_restore_auto = accepted && pause_auto;
        publish(cmd, accepted ? "" : "Park with brake set and AP off. Assign this group to Mock and keep hardware still; Auto pauses for the check."); return;
    }
    if (cmd.operation == action::seat) {
        publish(cmd, shared_session.queue_seat_intent(static_cast<session_intent>(cmd.seat_action + 6), cmd.seat_revision, cmd.seat_request) ?
            "" : "The seat request changed or shared controls are unavailable. Try again."); return;
    }
    if (cmd.operation == action::connect) {
        const auto phase = shared_session.status().phase;
        if (shared_session.holding() || remote_writer.holding() || phase == session_phase::armed || connection_configuration.revision == UINT32_MAX) {
            publish(cmd, "Leave shared controls and recover local controls before changing connection settings."); return;
        }
        auto next = cmd.connection; next.revision = connection_configuration.revision + 1;
#ifndef FSC_CONNECTION_EPHEMERAL
        if (!connection_write(FSC_PROFILE_ROOT "connection-settings", next)) { publish(cmd, "Cannot save connection settings. Previous settings are retained."); return; }
#endif
        connection_configuration = next; backend_reported = -1; backend_source[0] = 0; backend_sequence = 0;
        publish(cmd, ""); return; // Saving preferences never arms consent or changes any control mask.
    }
    if (cmd.operation == action::join) {
        uint64_t seed_hash = 14695981039346656037ull;
        for (const char* current = cmd.client; *current; ++current) { seed_hash ^= static_cast<unsigned char>(*current); seed_hash *= 1099511628211ull; }
        char seed[33]{}; std::snprintf(seed, sizeof(seed), "%016llx%016llx", static_cast<unsigned long long>(seed_hash), static_cast<unsigned long long>(cmd.request));
        const bool armed = shared_session.arm(seed, seconds());
        if (armed) (void)world.start();
        publish(cmd, armed ? "" : "Cannot join while shared controls are active."); return;
    }
    if (cmd.operation == action::leave) {
        shared_session.stop(session_error::none); remote_writer.end();
        publish(cmd, ""); return; // Hold masks until explicit scoped local recovery.
    }
    const auto selection = group_selection(cmd.target);
    if ((shared_session.active() || remote_writer.active()) && cmd.operation != action::tolerance && cmd.operation != action::advanced) {
        if (cmd.operation == action::automatic) {
            publish(cmd, shared_session.queue_intent(selection, cmd.enabled ? session_intent::auto_on : session_intent::auto_off) ?
                "" : "Shared controls are busy; retry after confirmation."); return;
        }
        if (cmd.operation != action::set && cmd.operation != action::force && cmd.operation != action::cancel) {
            publish(cmd, "This action is unavailable during shared controls."); return;
        }
        if (!gate.advanced && cmd.target != group::steering && cmd.target != group::quadrant && cmd.operation != action::cancel) {
            publish(cmd, "Open Advanced engine controls before changing an engine pair."); return;
        }
        const auto intent = cmd.operation == action::cancel ? session_intent::cancel : cmd.operation == action::force ? session_intent::force :
            cmd.blocked ? session_intent::other : session_intent::me;
        publish(cmd, shared_session.queue_intent(selection, intent) ? "" : "Shared controls are busy; retry after confirmation."); return;
    }
    if (!shared_session.active() && !remote_writer.active() && (shared_session.holding() || remote_writer.holding()) && cmd.operation != action::tolerance && cmd.operation != action::advanced) {
        if (cmd.operation != action::set && cmd.operation != action::force && cmd.operation != action::restore) {
            publish(cmd, "Shared link ended. Select Me or Force takeover to recover local controls."); return;
        }
        // A lost lease never resumes solo Auto. Only this explicit local action
        // leaves managed mode; retain other groups' masks and disable Auto.
        if (shared_session.holding() && !shared_session.recover_local(seconds())) {
            if ((world.recovering() || shared_session.recovering_instruments()) && size < sizeof(recovery_packet)) {
                std::memcpy(recovery_packet, data, size); recovery_size = size; recovery_started = seconds(); recovery_command = cmd;
                publish(cmd, ""); return;
            }
            publish(cmd, "Cannot confirm local physics or instrument input restoration; controls remain held."); return;
        }
        gate.set_auto(select_quadrant, false, seconds());
        gate.cancel(select_all, seconds());
        remote_writer.recover_local();
    }
    const bool cancel_pickup = cmd.operation == action::set && cmd.blocked && (gate.waiting() & selection);
    const bool all_tolerances = cmd.operation == action::tolerance && cmd.target == group::all;
    if (!gate.advanced && cmd.target != group::steering && cmd.target != group::quadrant && !cancel_pickup && !all_tolerances) {
        publish(cmd, "Open Advanced engine controls before changing an engine pair."); return;
    }
    if (cmd.operation == action::advanced) {
        if (!gate.set_advanced(true)) { publish(cmd, "Wait for the current change."); return; }
        publish(cmd); return;
    }
    if (cmd.operation == action::automatic) {
        if (!gate.set_auto(selection, cmd.enabled, seconds())) { publish(cmd, "Auto takeover is one setting for the entire quadrant."); return; }
        publish(cmd); return;
    }
    if (cmd.operation == action::tolerance) {
        if (gate.set_tolerance(selection, cmd.tolerance)) shared_session.alignment_changed();
        publish(cmd); return;
    }
    if (cmd.operation == action::learn) {
        if (gate.blocked & selection) { publish(cmd, "Return this group to Me before relearning inputs."); return; }
        for (size_t axis = 0; axis < axis_count; ++axis) if (control_pickup::matches_group(axis, cmd.target)) {
            pickup.inputs[axis] = pickup_axis{}; relearned_axes |= 1u << axis;
        }
        trace("Explicit input relearn", selection);
        update_profile();
        save_profile(); // An explicit reset must also clear the durable mapping.
        ++gate.revision; publish(cmd); return;
    }
    if (cmd.operation == action::cancel) { gate.cancel(selection, seconds()); publish(cmd); return; }
    gate.pending = cmd;
    const bool accepted = cmd.operation == action::set ? (cmd.blocked ? gate.assign_mock(selection, seconds()) : gate.request_me(selection, false, cmd)) :
        gate.force_me(cmd.operation == action::restore ? select_all : selection);
    if (!accepted)
    {
        fail("Simulator configuration failed. Test released; reload the package to retry.");
        return;
    }
    if (gate.applying) publish(); else publish(cmd);
}

// Read-only avionics diagnostic from the EFB. Its content is logged, never
// executed, imported as a route, or passed to the aircraft definition writer.
void receive_route_diagnostic(const char* data, unsigned int size, void*) {
    if (!data || !size || size>262145) return;
    if (data[size-1]==0) --size;
    if (!size || size>262144 || data[0]!='{') return;
    for (unsigned index=0;index<size;++index) if (data[index]<' ' || data[index]>'~') return;
    FILE* file=std::fopen(FSC_PROFILE_ROOT "avionics-route-last.json","wb");
    if (!file) {trace("Could not save avionics route diagnostic");return;}
    const bool written=std::fwrite(data,1,size,file)==size;
    const bool closed=std::fclose(file)==0;
    trace(written && closed ? "Read-only avionics route diagnostic saved" : "Avionics route diagnostic save failed",size);
}

void CALLBACK bridge_dispatch(SIMCONNECT_RECV* data, DWORD size, void*)
{
    if (!data || size < sizeof(SIMCONNECT_RECV)) return;
    if (data->dwID == SIMCONNECT_RECV_ID_OPEN) {
        if (connection) { native_bridge.initialize(connection); guidance_bridge.initialize(connection); cockpit_bridge.initialize(connection); }
    } else if (data->dwID == SIMCONNECT_RECV_ID_CLIENT_DATA) {
        cockpit_packet cockpit_command{};
        if (cockpit_bridge.receive(data, size, cockpit_command)) {
            const auto now = seconds();
            if (shared_session.optional_live(now))
                ordinary_cockpit.apply(cockpit_command, shared_session.status(), now, [](const char* expression) {
                    return !!execute_calculator_code(expression, nullptr, nullptr, nullptr);
                });
            return;
        }
        guidance_packet ap_command{};
        if (guidance_bridge.receive(data, size, ap_command)) {
            shared_session.receive_guidance(ap_command, seconds());
            guidance_bridge.publish(shared_session.guidance_status(seconds()));
            return;
        }
        session_packet session_command{};
        if (native_bridge.receive_session(data, size, session_command)) {
            shared_session.receive(session_command, seconds());
            native_bridge.publish_session(shared_status());
            return;
        }
        bridge_packet hello{};
        if (native_bridge.receive(data, size, hello)) {
            if (hello.kind == bridge_kind::backend) {
                const auto now = seconds(); const auto shared = shared_session.status();
                if (std::strcmp(hello.native, session) || hello.configuration.revision != connection_configuration.revision ||
                    (shared_session.holding() && std::strcmp(hello.backend, shared.backend)) ||
                    (backend_source[0] && std::strcmp(backend_source, hello.backend) && live_backend_phase(now))) return;
                if (std::strcmp(backend_source, hello.backend)) { std::memcpy(backend_source, hello.backend, sizeof(backend_source)); backend_sequence = 0; }
                if (hello.request <= backend_sequence) return;
                backend_sequence = hello.request; backend_phase = hello.backend_phase; backend_code = hello.backend_code; backend_reported = now;
                return; // UI heartbeat only; no masks, session consent or operation acknowledgements.
            }
            // Discovery starts the same non-masking observer as opening the EFB.
            // It cannot assign ownership, change an existing mask, or inject input.
            if (!start_requested && connection) { start_requested = true; connection_wait_started = seconds(); try_start_observation(); }
            native_bridge.publish(bridge_status(&hello));
        }
    } else if (data->dwID == SIMCONNECT_RECV_ID_EXCEPTION && size >= sizeof(SIMCONNECT_RECV_EXCEPTION)) {
        if (!native_bridge.exception(*reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(data))) native_bridge.available = false;
    } else if (data->dwID == SIMCONNECT_RECV_ID_QUIT) native_bridge.available = false;
}

void CALLBACK dispatch(SIMCONNECT_RECV* data, DWORD size, void*)
{
    if (!data) return;
    ++dispatch_count;
    last_dispatch_id = data->dwID;
    switch (data->dwID)
    {
    case SIMCONNECT_RECV_ID_OPEN:
        connection_open = true;
        force_button.initialize(connection,seconds());
        remote_output.initialize(connection, first_event);
        output_guard.initialize(connection);
        camera.initialize(connection);
        camera_preference_revision = 0;
        altimeters.initialize(connection);
        guidance.initialize(connection); // Observation only; no AP events or input masks are installed.
        world.initialize(connection);
        trace("Simulator connection open");
        bridge_dispatch(data, size, nullptr);
        try_start_observation();
        publish();
        break;
    case SIMCONNECT_RECV_ID_SYSTEM_STATE:
    {
        const auto* state = reinterpret_cast<const SIMCONNECT_RECV_SYSTEM_STATE*>(data);
        if (remote_output.confirm(state->dwRequestID)) { output_check.confirm(state->dwRequestID); break; }
        if (camera.confirm(state->dwRequestID)) break;
        if (force_button.confirm(state->dwRequestID)) break;
        if (altimeters.confirm(state->dwRequestID)) break;
        if (guidance.confirm(state->dwRequestID)) break;
        if (guidance_bridge.confirm(state->dwRequestID)) break;
        if (cockpit_bridge.confirm(state->dwRequestID)) break;
        if (world.confirm(state->dwRequestID)) { shared_session.tick(seconds(), native_bridge.available); break; }
        if (native_bridge.confirm(state->dwRequestID)) break;
        if (state->dwRequestID == identity_request) {
            if (size >= sizeof(SIMCONNECT_RECV_SYSTEM_STATE) &&
                profile_identity(state->szString, sizeof(state->szString), aircraft_identity)) {
                aircraft_definitions = find_definition_profile(aircraft_identity);
                pickup.definitions = aircraft_definitions;
                (void)remote_output.select_profile(aircraft_definitions);
                if (has_cockpit_references(aircraft_definitions)) {
                    for (size_t index = 0; index < aircraft_definitions->count; ++index)
                        if (aircraft_definitions->axes[index].cockpit_reference) pickup.inputs[aircraft_definitions->axes[index].axis] = pickup_axis{};
                    trace("Using cockpit references from vanilla definitions");
                }
                update_profile();
                trace("Aircraft identity received for saved controls");
            } else profile_failed = true;
            break;
        }
        trace("Received system state", state->dwRequestID);
        if (state->dwRequestID >= first_barrier && gate.confirm(state->dwRequestID - first_barrier, seconds()))
        {
            trace("Configuration confirmed", gate.barrier);
            shared_session.tick(seconds(), native_bridge.available);
            publish(gate.pending);
        }
        break;
    }
    case SIMCONNECT_RECV_ID_CONTROLLERS_LIST:
        (void)force_button.controllers(reinterpret_cast<const SIMCONNECT_RECV_CONTROLLERS_LIST*>(data),size);break;
    case SIMCONNECT_RECV_ID_CLIENT_DATA:
        bridge_dispatch(data, size, nullptr);
        break;
    case SIMCONNECT_RECV_ID_EVENT:
    case SIMCONNECT_RECV_ID_EVENT_EX1:
    {
        DWORD event_id = 0, value = 0;
        if (data->dwID == SIMCONNECT_RECV_ID_EVENT_EX1) {
            if (size < sizeof(SIMCONNECT_RECV_EVENT_EX1)) break;
            const auto* event = reinterpret_cast<const SIMCONNECT_RECV_EVENT_EX1*>(data);
            event_id = event->uEventID; value = event->dwData0;
        } else {
            if (size < sizeof(SIMCONNECT_RECV_EVENT)) break;
            const auto* event = reinterpret_cast<const SIMCONNECT_RECV_EVENT*>(data);
            event_id = event->uEventID; value = event->dwData;
        }
        bool button_fire=false;
        if(force_button.event(event_id,button_fire)) {
            if(button_fire && hardware_force_request<UINT32_MAX) {
                trace("Hardware Force Steering pressed");
                char request[100]{};std::snprintf(request,sizeof(request),"5|hardware|%u|force|steering|me",++hardware_force_request);
                receive_command(request,static_cast<unsigned>(std::strlen(request)),nullptr);
            }
            break;
        }
        if (event_id < first_event || event_id >= first_event + event_count) break;
        const auto& binding = events[event_id - first_event];
        const auto selection = event_selection(binding);
        const bool steering = selection == select_steering;
        auto& observed = steering ? steering_activity : pair_activity[selection_index(selection) - 1];
        ++observed.seen;
        if (!gate.applying && (gate.blocked & selection)) ++observed.blocked;
        observed.last = binding.name;
        observed.value = static_cast<int32_t>(value);
        if (!steering) {
            ++quadrant_activity.seen; if (!gate.applying && (gate.blocked & selection)) ++quadrant_activity.blocked;
            quadrant_activity.last = binding.name; quadrant_activity.value = observed.value;
        }
        pickup.observe(binding.name, observed.value, seconds());
        break;
    }
    case SIMCONNECT_RECV_ID_SIMOBJECT_DATA:
    {
        if (size < sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA)) break;
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_SIMOBJECT_DATA*>(data);
        if (output_guard.observe(packet, size, seconds())) break;
        if (camera.observe(packet, size, seconds())) break;
        if (altimeters.observe(packet, size, seconds())) break;
        if (guidance.observe(packet, size, seconds())) break;
        if (world.observe(packet, size, seconds())) { shared_session.tick(seconds(), native_bridge.available); break; }
        const size_t offset = reinterpret_cast<const char*>(&packet->dwData) - reinterpret_cast<const char*>(data);
        if (packet->dwRequestID != target_request || packet->dwDefineCount != sim_target_count || size < offset + sizeof(double) * sim_target_count) break;
        double values[target_count]{};
        std::memcpy(values, &packet->dwData, sizeof(double) * sim_target_count);
        read_cockpit_references(values, aircraft_definitions);
        bool learned_before[axis_count]{};
        for (size_t axis = 0; axis < axis_count; ++axis) learned_before[axis] = pickup.inputs[axis].calibrated;
        gate.update_targets(values, seconds(), !aircraft_identity[0]);
        for (size_t axis = 0; axis < axis_count; ++axis) if (!learned_before[axis] && pickup.inputs[axis].calibrated) {
            char message[80]{};
            std::snprintf(message, sizeof(message), "Input learned: %s", axes[axis].id);
            trace(message, pickup.inputs[axis].candidates);
        }
        update_profile();
        break;
    }
    case SIMCONNECT_RECV_ID_EVENT_FILENAME:
    {
        const auto* event = reinterpret_cast<const SIMCONNECT_RECV_EVENT_FILENAME*>(data);
        if (event->uEventID == aircraft_loaded && connection && observation_started)
        {
            remote_writer.end();
            remote_writer.initialize(session);
            shared_session.initialize(session);
            camera.reset_context(); camera_preference_revision = 0;
            altimeters.reset_context();
            guidance.reset_context();
            (void)world.recover(seconds()); world.reset_context(); recovery_size = 0;
            update_profile();
            save_profile();
            steering_activity = activity{};
            quadrant_activity = activity{};
            for (auto& activity : pair_activity) activity = {};
            pickup = control_pickup{};
            request_aircraft_identity();
            gate.initialize(true, true); // Each newly loaded aircraft starts with Me and quadrant Auto on.
            publish();
        }
        break;
    }
    case SIMCONNECT_RECV_ID_EXCEPTION:
    {
        const auto* exception = reinterpret_cast<const SIMCONNECT_RECV_EXCEPTION*>(data);
        if(force_button.exception(*exception)){
            char detail[240]{};std::snprintf(detail,sizeof(detail),"Optional Force input: SDK error %lu send %lu index %lu; %s; button %u; physical controls unchanged",
                static_cast<unsigned long>(exception->dwException),static_cast<unsigned long>(exception->dwSendID),static_cast<unsigned long>(exception->dwIndex),
                force_button.failed_operation(),force_button.failed_button());trace(detail);break;
        }
        if (camera.exception(*exception)) { trace("Seat camera request rejected; control ownership is unchanged"); break; }
        if (altimeters.exception(*exception)) { trace("Altimeter SDK request rejected; control ownership is unchanged"); break; }
        if (guidance.exception(*exception)) { shared_session.stop_guidance(); trace("AP SDK request rejected; physical control ownership is unchanged"); break; }
        if (guidance_bridge.exception(*exception)) {
            if (!guidance_bridge.available) { shared_session.stop_guidance(); trace("AP transport unavailable; physical control ownership is unchanged"); }
            break;
        }
        if (cockpit_bridge.exception(*exception)) { trace("Ordinary instrument transport unavailable; physical controls are unchanged"); break; }
        if (world.exception(*exception)) { shared_session.stop(); trace("World SDK call rejected; masks and owned freezes retained"); break; }
        if (output_guard.exception(*exception)) { trace("Parked output guard unavailable; local ownership is unchanged"); break; }
        if (remote_output.exception(*exception)) {
            char detail[240]{};
            const auto axis = remote_output.failed_axis();
            std::snprintf(detail, sizeof(detail), "Output SDK error %lu (send %lu, index %lu): %s %s; axis=%s",
                static_cast<unsigned long>(exception->dwException), static_cast<unsigned long>(exception->dwSendID), static_cast<unsigned long>(exception->dwIndex),
                remote_output.failed_operation(), remote_output.failed_target(), axis < axis_count ? axes[axis].id : "unknown");
            trace(detail);
            output_check_restore_auto = false;
            output_check.invalidate();
            (void)save_output_validation();
            if (output_check.running() && shared_session.status().phase == session_phase::idle && !remote_writer.holding()) {
                output_check.cancel();
                trace("Parked output SDK error; local ownership unchanged. Recover this group and reload before retrying", exception->dwException);
            } else {
                remote_writer.end(writer_fault::write_failed);
                shared_session.stop();
                trace("Remote output rejected; hardware masks retained", exception->dwException);
            }
            break;
        }
        if (native_bridge.exception(*exception)) break;
        if (exception->dwSendID && exception->dwSendID == identity_packet) {
            profile_failed = true;
            trace("Aircraft identity unavailable; learning remains session-only");
            publish();
            break;
        }
        char reason[120]{};
        std::snprintf(reason, sizeof(reason), "SimConnect error %lu (send %lu, index %lu). Test released; reload to retry.",
            static_cast<unsigned long>(exception->dwException), static_cast<unsigned long>(exception->dwSendID), static_cast<unsigned long>(exception->dwIndex));
        for (size_t i = 0; i < setup_call_count; ++i)
        {
            const auto& call = setup_calls[i];
            if (exception->dwSendID && call.packet == exception->dwSendID)
            {
                std::snprintf(reason, sizeof(reason), "SimConnect error %lu: %s %s (send %lu, index %lu). Test released; reload to retry.",
                    static_cast<unsigned long>(exception->dwException), call.operation, call.event,
                    static_cast<unsigned long>(exception->dwSendID), static_cast<unsigned long>(exception->dwIndex));
                break;
            }
        }
        for (size_t i = 0; i <= sim_target_count; ++i) if (exception->dwSendID && target_packets[i] == exception->dwSendID) {
            const char* name = i < axis_count ? axes[i].variable : i == axis_count ? "NUMBER OF ENGINES" : i == axis_count + 1 ? "AUTOPILOT MASTER" :
                "position updates";
            std::snprintf(reason, sizeof(reason), "SimConnect error %lu: target %s (index %lu). Test released; reload to retry.",
                static_cast<unsigned long>(exception->dwException), name, static_cast<unsigned long>(exception->dwIndex));
            break;
        }
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

// Compile every executor method against the actual WASM SDK even while its
// command transport is disabled; contract-only instantiations are insufficient.
template class fsc_controls::control_remote_writer<fsc_controls::control_runtime<sim_adapter>, fsc_controls::control_definition_output>;
template class fsc_controls::control_session<decltype(gate), decltype(remote_writer)>;

extern "C" MSFS_CALLBACK void module_init(void)
{
    connection_open = start_requested = observation_started = false;
    dispatch_count = 0;
    last_dispatch_id = 0;
    pickup = control_pickup{};
    aircraft_definitions = nullptr;
    aircraft_identity[0] = profile_prefix[0] = '\0';
    profile = control_profile{};
    profile_loaded = profile_failed = profile_dirty = false;
    output_check.reset();
    setup_wizard.reset();
    for (auto& request : wizard_requests) request = command{};
    wizard_request_index = 0;
    output_check_restore_auto = false;
    output_validation = output_validation_record{};
    output_validation_prefix[0] = '\0';
    output_validation_loaded = output_validation_failed = false;
    relearned_axes = 0;
    native_bridge = control_bridge_transport{}; bridge_serial = 0;
    adapter.groups_created = false; // Notification groups belong to the new connection.
    guidance_bridge = control_guidance_transport{};
    ordinary_cockpit.reset(); cockpit_bridge = control_cockpit_transport{}; last_cockpit_publish = -1;
    connection_configuration = connection_settings{};
    force_button.reset();hardware_force_request=0;
    instrument_monitor.reset();
    force_button.load(FSC_PROFILE_ROOT "force-steering-button");
    if(force_button.settings.enabled){start_requested=true;connection_wait_started=seconds();}
    camera = control_camera{}; camera_preference_revision = 0;
    altimeters = control_altimeters{};
    guidance = control_guidance{};
#ifndef FSC_CONNECTION_EPHEMERAL
    (void)connection_read(FSC_PROFILE_ROOT "connection-settings", connection_configuration);
#endif
    backend_source[0] = 0; backend_sequence = 0; backend_reported = -1; backend_phase = backend_code = 0;
    last_native_publish = -1;
    std::memset(target_packets, 0, sizeof(target_packets));
    trace("Module init: waiting for simulator and EFB");
    std::snprintf(session, sizeof(session), "%llx", static_cast<unsigned long long>(seconds() * 1000000));
    remote_output = control_definition_output{};
    world = control_world{}; recovery_size = 0;
    remote_writer.initialize(session);
    shared_session.initialize(session);
    fsCommBusRegister(command_event, receive_command, nullptr);
    fsCommBusRegister("FSC_AVIONICS_ROUTE_DIAGNOSTIC",receive_route_diagnostic,nullptr);
    fsCommBusRegister(session_join_event, receive_session_consent, nullptr);
    if (FAILED(SimConnect_Open(&connection, "FS Copilot EFB Control Test", nullptr, 0, 0, 0)))
    {
        connection = {};
        fail("Cannot connect to the simulator. Reload the package to retry.");
        return;
    }
    // In WASM this registers the dispatch callback once, rather than pumping it
    // every frame (see the SDK's SimConnect_CallDispatch WASM note).
    if (FAILED(SimConnect_CallDispatch(connection, dispatch, nullptr)))
    {
        fail("Cannot register simulator callback. Test released.");
        return;
    }
    // WASM registers its callback once. Optional IPC failures are classified
    // separately inside that callback and never release the control masks.
}

extern "C" MSFS_CALLBACK void Update_StandAlone(float)
{
    const double now = seconds();
    force_button.tick(now);
    const auto previous_check = output_check.phase;
    output_check.tick(pickup, aircraft_definitions, output_context(), remote_output, now);
    if (previous_check != output_check.phase && !output_check.running()) {
        trace(output_check.phase == output_check_phase::passed ? "Parked output check passed" : output_check.issue, group_selection(output_check.target));
        if (output_check.phase == output_check_phase::failed && output_check.failed_axis < axis_count) {
            char mismatch[200]{};
            std::snprintf(mismatch, sizeof(mismatch), "Output mismatch group=%s axis=%s expected=%.4f%% observed=%.4f%% restored=%u",
                group_name(output_check.target), axes[output_check.failed_axis].id, 100 * output_check.failed_expected,
                100 * output_check.failed_observed, output_check.restored ? 1u : 0u);
            trace(mismatch);
        }
        if (output_check_restore_auto && output_check.restored && remote_output.available && output_context().local && gate.ready && !gate.applying)
            (void)gate.set_auto(select_quadrant, true, now);
        output_check_restore_auto = false;
    }
    (void)save_output_validation();
    if (recovery_size) {
        if (shared_session.recover_local(now)) {
            char pending[128]{}; const auto size = recovery_size; std::memcpy(pending, recovery_packet, size); recovery_size = 0;
            gate.set_auto(select_quadrant, false, now); gate.cancel(select_all, now);
            receive_command(pending, size, nullptr);
        } else if ((world.owned() && !world.available) || now - recovery_started > 5) {
            recovery_size = 0; world.cancel_recovery(); shared_session.cancel_instrument_recovery();
            publish(recovery_command, "Local physics or instrument restoration was not confirmed. Controls remain held; retry Me or Force takeover.");
        }
    }
    remote_writer.tick(now);
    shared_session.tick(now, native_bridge.available);
    if (connection_configuration.revision != camera_preference_revision) {
        camera_preference_revision = connection_configuration.revision; camera.request(connection_configuration.seat);
    }
    const auto seat_status = shared_session.status();
    if (seat_status.phase == session_phase::active && seat_status.seat_revision) camera.request(seat_status.seat);
    camera.tick(now);
    if (profile_dirty && now - profile_changed >= 0.5) save_profile();
    if (connection && start_requested && !connection_open && now - connection_wait_started > 15)
        fail("No simulator connection-open callback. Test released; reload the package to retry.");
    if (gate.applying && now - change_started > 5)
    {
        char reason[120]{};
        std::snprintf(reason, sizeof(reason), "Confirmation timed out (open %s, callbacks %u, last %lu). Test released; reload to retry.",
            connection_open ? "yes" : "no", dispatch_count, static_cast<unsigned long>(last_dispatch_id));
        if (shared_session.holding() || remote_writer.holding()) { shared_session.stop(); trace(reason); }
        else fail(reason);
    }
    if (connection && gate.ready && !gate.applying) {
        // The shared backend routes Auto pickup through host authority. Only
        // the confirmed session controller may unmask shared hardware.
        if (!shared_session.holding() && !remote_writer.holding()) gate.tick(now);
        if (!gate.ready && !gate.applying) fail("Aligned takeover failed. Test released; reload to retry.");
    }
    const auto previous_wizard = setup_wizard.phase;
    setup_wizard.tick(gate, pickup, output_check, remote_output, aircraft_definitions, output_context(),
        output_guard.engines_off(pickup.engines, now), now, []() { return save_output_validation(); });
    if (setup_wizard.phase != previous_wizard) {
        char step[200]{};
        std::snprintf(step, sizeof(step), "Setup wizard group=%s phase=%u axis=%u: %s", group_name(setup_wizard.target),
            static_cast<unsigned>(setup_wizard.phase), static_cast<unsigned>(setup_wizard.failed_axis), setup_wizard.message);
        trace(step);
    }
    if (now - last_publish >= 0.1)
    {
        last_publish = now;
        publish();
    }
    // Shared steering samples need a faster transport than the display. Keep
    // the EFB at 10 Hz while current native observations/proofs run at 30 Hz.
    if (now - last_native_publish >= (shared_session.active() ? 1.0 / 30 : 0.1))
    {
        last_native_publish = now;
        if (native_bridge.available) native_bridge.publish(bridge_status());
        if (native_bridge.available) native_bridge.publish_session(shared_status());
        if (guidance_bridge.available) guidance_bridge.publish(shared_session.guidance_status(now));
    }
    if (cockpit_bridge.available && now - last_cockpit_publish >= (shared_session.active() ? .05 : .5)) {
        last_cockpit_publish = now;
        ordinary_cockpit.observe(aircraft_definitions, session, shared_session.status(), now);
        instrument_monitor.observe(aircraft_definitions,ordinary_cockpit.status());
        cockpit_bridge.publish(ordinary_cockpit.status());
    }
}

extern "C" MSFS_CALLBACK void module_deinit(void)
{
    force_button.dispose();
    update_profile();
    save_profile();
    (void)save_output_validation();
    remote_writer.end();
    shared_session.stop();
    (void)world.recover(seconds());
    remote_output.stop();
    adapter.release();
    native_bridge.available = false;
    fsCommBusUnregisterOneEvent(command_event, receive_command, nullptr);
    fsCommBusUnregisterOneEvent("FSC_AVIONICS_ROUTE_DIAGNOSTIC",receive_route_diagnostic,nullptr);
    fsCommBusUnregisterOneEvent(session_join_event, receive_session_consent, nullptr);
}
