#pragma once
#include "control_guidance.h"
#include "control_session_protocol.h"

namespace fsc_controls {
constexpr uint32_t guidance_magic = 0x31414746; // FGA1, little endian.
constexpr char guidance_status_name[] = "FSC_CONTROLS_GUIDANCE_STATUS_V1";
constexpr char guidance_command_name[] = "FSC_CONTROLS_GUIDANCE_COMMAND_V1";
enum class guidance_kind : uint32_t { command = 1, status };
enum class guidance_action : uint32_t { gate = 1, apply, observe, stop, poll, intent_ack, reference };
enum guidance_flags : uint32_t { guidance_fresh = 1, guidance_transport = 2, guidance_profile = 4, guidance_gate_ready = 8,
    guidance_held = 16, guidance_capture = 32, guidance_single_host = 64, guidance_pending = 128, guidance_local = 256, guidance_navigation_ready = 512 };
#pragma pack(push, 1)
struct guidance_packet {
    uint32_t magic = guidance_magic, version = 1;
    guidance_kind kind = guidance_kind::command;
    uint32_t size = 512;
    char native[24]{}, backend[40]{}, session[40]{}, consent[40]{};
    uint64_t epoch = 0, control_revision = 0, command = 0, command_epoch = 0, navigation_epoch = 0, preparation = 0, revision = 0, sequence = 0;
    guidance_action action = guidance_action::gate;
    guidance_setting setting = guidance_setting::master;
    double value = 0;
    uint32_t capture = 0, host = 0;
    uint64_t read_serial = 0;
    uint32_t gate_ack = 0, value_ack = 0;
    uint64_t serial = 0;
    uint32_t flags = 0, supported = 0, blocked = 0, slots = 0, target_slots = 0, switches = 0;
    double heading = 0, altitude = 0, vertical_speed = 0;
    uint32_t gps = 0, nav = 0, error = 0;
    uint64_t steering_generation = 0;
    uint32_t steering_owner = 0;
    char navigation_fingerprint[65]{};
    char adapter[64]{};
    uint32_t reference_ack = 0;
    uint8_t reserved[35]{};
};
#pragma pack(pop)
static_assert(sizeof(guidance_packet) == 512 && offsetof(guidance_packet, epoch) == 160 &&
    offsetof(guidance_packet, action) == 224 && offsetof(guidance_packet, value) == 232 &&
    offsetof(guidance_packet, read_serial) == 248 && offsetof(guidance_packet, serial) == 264 &&
    offsetof(guidance_packet, heading) == 296 && offsetof(guidance_packet, steering_generation) == 332 &&
    offsetof(guidance_packet, navigation_fingerprint) == 344 && offsetof(guidance_packet, adapter) == 409 &&
    offsetof(guidance_packet, reference_ack) == 473 && offsetof(guidance_packet, reserved) == 477, "Guidance wire layout changed");
inline bool guidance_adapter_valid(const char* value, size_t capacity) {
    size_t size = 0;
    while (size < capacity && value[size]) {
        const char c = value[size++];
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.')) return false;
    }
    if (!size || size == capacity) return false;
    for (size_t index = size; index < capacity; ++index) if (value[index]) return false;
    return true;
}
inline uint32_t guidance_slots(uint32_t first, uint32_t second, uint32_t third) { return first | (second << 3) | (third << 6); }
inline bool guidance_slots_valid(uint32_t slots, uint32_t maximum) {
    return !(slots >> 9) && (slots & 7) <= maximum && ((slots >> 3) & 7) <= maximum && ((slots >> 6) & 7) <= maximum;
}
inline bool guidance_frame_valid(const guidance_packet& p) {
    const bool status = p.kind == guidance_kind::status;
    if (p.magic != guidance_magic || p.version != 1 || p.size != sizeof(p) ||
        (p.kind != guidance_kind::command && !status) || !session_hex(p.native, sizeof(p.native)) ||
        !session_token(p.backend, sizeof(p.backend), status) || !session_token(p.session, sizeof(p.session), status) ||
        !session_token(p.consent, sizeof(p.consent), status) || p.capture > 1 || p.host > 1 || p.gps > 1 || p.nav > 2 ||
        p.flags & ~1023u || p.supported & ~8191u || p.blocked & ~8191u || p.switches & ~8191u || p.error > 6 ||
        !guidance_slots_valid(p.slots, 4) || !guidance_slots_valid(p.target_slots, 3) ||
        !guidance_valid(p.setting, p.value) || !guidance_valid(guidance_setting::heading, p.heading) ||
        !guidance_valid(guidance_setting::altitude, p.altitude) || !guidance_valid(guidance_setting::vertical_speed, p.vertical_speed) ||
        !barometer_ack_valid(p.gate_ack) || !barometer_ack_valid(p.value_ack) || !barometer_ack_valid(p.reference_ack) ||
        (p.gate_ack >> 3) > p.command || (p.value_ack >> 3) > p.command || (p.reference_ack >> 3) > p.command || p.command > barometer_max_command ||
        static_cast<uint32_t>(p.action) < 1 || static_cast<uint32_t>(p.action) > 7) return false;
    for (const auto byte : p.reserved) if (byte) return false;
    if (p.flags & guidance_profile) { if (!guidance_adapter_valid(p.adapter, sizeof(p.adapter))) return false; }
    else for (const auto byte : p.adapter) if (byte) return false;
    if ((p.flags & guidance_navigation_ready) ? !(p.flags & guidance_gate_ready) || !session_hex(p.navigation_fingerprint, sizeof(p.navigation_fingerprint), 64) :
        !session_hex(p.navigation_fingerprint, sizeof(p.navigation_fingerprint), 64, true) || p.navigation_fingerprint[0]) return false;
    const bool bound = p.backend[0] && p.session[0] && p.consent[0] && p.epoch && p.control_revision;
    if (!bound && (p.backend[0] || p.session[0] || p.consent[0] || p.epoch || p.control_revision)) return false;
    const bool context = p.command_epoch && p.navigation_epoch && p.preparation && p.revision;
    if (!context && (p.command_epoch || p.navigation_epoch || p.preparation || p.revision)) return false;
    if (p.steering_generation ? p.setting != guidance_setting::master || p.value ||
        (status ? !p.sequence || p.steering_owner != 1 : p.action != guidance_action::apply || (p.steering_owner != 1 && p.steering_owner != 2)) : p.steering_owner != 0) return false;
    if (status) {
        if (!p.serial || p.action != guidance_action::gate || (!p.sequence && (p.value || p.setting != guidance_setting::master))) return false;
        if (p.sequence && (!(p.flags & guidance_gate_ready) || (!(p.flags & guidance_capture) && !p.steering_generation))) return false;
        if ((p.flags & guidance_fresh) && (!(p.flags & guidance_transport) || !p.read_serial)) return false;
        if ((p.flags & guidance_profile) && (!(p.flags & guidance_transport) || !p.supported)) return false;
        if ((p.flags & guidance_gate_ready) && (!bound || !context || (p.flags & (guidance_fresh | guidance_profile | guidance_single_host)) !=
            (guidance_fresh | guidance_profile | guidance_single_host) || p.blocked != 8191 || (p.flags & (guidance_held | guidance_local)))) return false;
        if ((p.flags & guidance_capture) && (!(p.flags & guidance_gate_ready) || !p.capture)) return false;
        if (!p.read_serial && (p.flags & guidance_fresh || p.switches || p.heading || p.altitude || p.vertical_speed || p.gps || p.nav || p.slots)) return false;
        return true;
    }
    if (!bound || !p.command || (p.action != guidance_action::poll && !context) || p.serial || p.flags || p.supported || p.blocked ||
        p.slots || p.target_slots || p.error || p.gate_ack || p.value_ack || p.reference_ack) return false;
    if (p.action == guidance_action::reference) return p.setting == guidance_setting::master && !p.value && !p.capture;
    if (p.switches || p.heading || p.altitude || p.vertical_speed || p.gps || p.nav) return false;
    const bool value = p.action == guidance_action::apply || p.action == guidance_action::observe;
    if (p.action == guidance_action::intent_ack) return p.sequence && p.setting == guidance_setting::master && !p.value && !p.capture;
    return value ? p.sequence && !p.capture : !p.sequence && p.setting == guidance_setting::master && !p.value &&
        (p.action == guidance_action::gate || !p.capture);
}
}
