#pragma once
#include <cstddef>
#include "control_remote_writer.h"
#include "control_world.h"

namespace fsc_controls {
constexpr uint32_t session_magic = 0x31534346; // FCS1, little endian.
constexpr char session_status_name[] = "FSC_CONTROLS_NATIVE_SESSION_STATUS_V1";
constexpr char session_command_name[] = "FSC_CONTROLS_NATIVE_SESSION_COMMAND_V1";
enum class session_kind : uint32_t { status = 1, bind, state, prepare, attach, input, end, heartbeat, intent_ack, world, feedback, barometer, instrument_feedback };
enum class barometer_action : uint32_t { gate = 1, seed, apply, observe, stop };
enum class barometer_result : uint32_t { idle, pending, succeeded, rejected, failed, unsupported };
constexpr uint64_t barometer_max_command = UINT32_MAX >> 3;
inline bool barometer_ack_valid(uint32_t value) { return !value || ((value >> 3) && (value & 7) >= 1 && (value & 7) <= 5); }
inline uint32_t barometer_ack(uint64_t command, barometer_result result) { return static_cast<uint32_t>(command << 3) | static_cast<uint32_t>(result); }
enum class session_intent : uint32_t { none, me, other, cancel, force, auto_on, auto_off, seat_request, seat_accept, seat_decline, seat_cancel };
inline bool seat_intent(session_intent value) { return value >= session_intent::seat_request && value <= session_intent::seat_cancel; }
enum class session_phase : uint32_t { idle, armed, binding, active, ended };
enum class session_result : uint32_t { idle, pending, succeeded, rejected, closed };
enum class session_error : uint32_t { none, invalid_state, stale_authority, not_prepared, native_failed, expired, transport_failed };
enum session_flags : uint32_t { session_host = 1, session_incoming = 2, session_force = 4, session_autopilot = 8,
    session_world = 16, session_world_fresh = 32, session_world_frozen = 64 };
#pragma pack(push, 1)
struct instrument_feedback {
    uint64_t epoch = 0, preparation = 0, revision = 0, seat_revision = 0;
    uint32_t phase = 0, pending = 0, protocol = 0, code = 0;
    uint8_t reserved[48]{};
};
struct session_barometer {
    uint64_t epoch = 0, preparation = 0, revision = 0, seat_revision = 0, sequence = 0;
    barometer_action action = barometer_action::gate;
    uint32_t instrument = 0, pressure16 = 0, standard = 0, mask = 0, seat = 0;
    uint64_t read_serial = 0;
    uint8_t reserved[24]{};
};
struct session_packet {
    uint32_t magic = session_magic, version = 1;
    session_kind kind = session_kind::status;
    uint32_t size = 1024;
    char native[24]{}, backend[40]{}, session[40]{}, consent[40]{};
    uint64_t epoch = 0, command = 0, revision = 0;
    char ticket[40]{};
    uint64_t generation[4]{};
    writer_owner owner[4]{};
    uint32_t attached = 0, pending = 0, engines = 0, axes = 0, selection = 0, flags = 0;
    uint64_t sequence = 0;
    double positions[21]{};
    uint64_t serial = 0, ack = 0;
    session_result result = session_result::idle;
    uint32_t blocked = 0, barrier = 0, required = 0, fresh = 0, aligned = 0;
    char pending_transfer[4][40]{};
    session_phase phase = session_phase::idle;
    session_error code = session_error::none;
    char committed_transfer[4][40]{};
    uint64_t intent_id = 0;
    uint32_t intent_selection = 0;
    session_intent intent_action = session_intent::none;
    uint32_t intent_waiting = 0, intent_other = 0;
    double world_values[world_value_count]{};
    uint64_t world_serial = 0;
    uint32_t automatic = 0, automatic_waiting = 0;
    uint64_t seat_revision = 0;
    uint32_t seat = 0, seat_pending = 0;
    uint64_t seat_request = 0, intent_seat_revision = 0, intent_seat_request = 0;
    uint32_t barometer_gate_ack = 0, barometer_value_ack = 0;
};
#pragma pack(pop)
static_assert(sizeof(session_packet) == 1024 && offsetof(session_packet, sequence) == 296 &&
    offsetof(session_packet, positions) == 304 && offsetof(session_packet, serial) == 472 &&
    offsetof(session_packet, pending_transfer) == 512 && offsetof(session_packet, phase) == 672 &&
    offsetof(session_packet, committed_transfer) == 680 && offsetof(session_packet, intent_id) == 840 &&
    offsetof(session_packet, automatic) == 968 && offsetof(session_packet, seat_revision) == 976 &&
    offsetof(session_packet, intent_seat_revision) == 1000 && offsetof(session_packet, barometer_gate_ack) == 1016 && offsetof(session_packet, barometer_value_ack) == 1020 &&
    sizeof(session_barometer) == 96 && offsetof(session_barometer, read_serial) == 64 && sizeof(instrument_feedback) == 96, "Session wire layout changed");

inline instrument_feedback instrument_payload(const session_packet& packet) {
    instrument_feedback body{}; std::memcpy(&body, packet.world_values, sizeof(body)); return body;
}
inline void instrument_payload(session_packet& packet, const instrument_feedback& body) { std::memcpy(packet.world_values, &body, sizeof(body)); }
inline bool instrument_feedback_valid(const instrument_feedback& body) {
    if (!body.epoch || !body.revision || !body.seat_revision || body.seat_revision > UINT32_MAX || body.phase > 8 || body.pending > 3 || body.protocol != 3 || body.code > 1 ||
        (!body.preparation && body.phase != 0 && body.phase != 4 && body.phase != 6 && body.phase != 8)) return false;
    for (const auto value : body.reserved) if (value) return false;
    return true;
}

inline session_barometer barometer_payload(const session_packet& packet) {
    session_barometer body{}; std::memcpy(&body, packet.world_values, sizeof(body)); return body;
}
inline void barometer_payload(session_packet& packet, const session_barometer& body) { std::memcpy(packet.world_values, &body, sizeof(body)); }
inline bool barometer_valid(const session_barometer& b) {
    if (!b.epoch || !b.preparation || !b.revision || !b.seat_revision || b.seat_revision > UINT32_MAX || (b.seat != 1 && b.seat != 2) ||
        static_cast<uint32_t>(b.action) < 1 || static_cast<uint32_t>(b.action) > 5 || b.standard > 1) return false;
    for (const auto value : b.reserved) if (value) return false;
    if (b.action == barometer_action::gate || b.action == barometer_action::stop)
        return !b.instrument && !b.pressure16 && !b.standard && !b.sequence && (b.action == barometer_action::gate ? b.mask >= 1 && b.mask <= 3 : !b.mask);
    return b.instrument >= 1 && b.instrument <= 2 && b.pressure16 >= 8000 && b.pressure16 <= 19200 && !b.mask &&
        (b.action == barometer_action::seed ? !b.sequence : b.sequence != 0);
}

inline bool session_hex(const char* value, size_t capacity, size_t exact = 0, bool optional = false) {
    if (!((optional && !value[0]) || bridge_token(value, capacity, exact))) return false;
    size_t end = 0; while (end < capacity && value[end]) ++end;
    for (size_t index = end; index < capacity; ++index) if (value[index]) return false;
    return true;
}
inline bool session_token(const char* value, size_t capacity, bool optional = false) {
    return session_hex(value, capacity, 32, optional);
}
inline bool session_valid(const session_packet& p) {
    if (p.magic != session_magic || p.version != 1 || p.size != sizeof(p) ||
        static_cast<uint32_t>(p.kind) < 1 || static_cast<uint32_t>(p.kind) > 13 ||
        !session_hex(p.native, sizeof(p.native)) || !session_token(p.backend, sizeof(p.backend), p.kind == session_kind::status) ||
        !session_token(p.session, sizeof(p.session), p.kind == session_kind::status) ||
        !session_token(p.consent, sizeof(p.consent), p.kind == session_kind::status) || !session_token(p.ticket, sizeof(p.ticket), true) ||
        p.attached & ~15u || p.pending & ~15u || p.selection & ~15u || p.flags & ~(p.kind == session_kind::status ? 127u : 23u) || p.engines > 4 ||
        p.axes >> 21 || p.blocked & ~15u || p.required >> 21 || p.fresh >> 21 || p.aligned >> 21 ||
        static_cast<uint32_t>(p.result) > 4 || static_cast<uint32_t>(p.phase) > 4 || static_cast<uint32_t>(p.code) > 6) return false;
    if (!barometer_ack_valid(p.barometer_gate_ack) || !barometer_ack_valid(p.barometer_value_ack) ||
        (p.kind != session_kind::status && (p.barometer_gate_ack || p.barometer_value_ack)) ||
        (p.barometer_gate_ack >> 3) > p.command || (p.barometer_value_ack >> 3) > p.command) return false;
    if (p.kind == session_kind::barometer) {
        if (!barometer_valid(barometer_payload(p)) || p.command > barometer_max_command || p.selection || p.flags || p.axes || p.sequence || p.ticket[0]) return false;
        for (const auto value : p.positions) if (value != -1) return false;
    }
    const bool status = p.kind == session_kind::status, seating = seat_intent(p.intent_action);
    if (p.kind == session_kind::instrument_feedback) {
        if (!instrument_feedback_valid(instrument_payload(p)) || p.selection || p.flags || p.axes || p.sequence || p.ticket[0]) return false;
        for (const auto value : p.positions) if (value != -1) return false;
    }
    if (p.seat_revision > UINT32_MAX || p.seat_request > UINT32_MAX ||
        (!p.seat_revision ? p.seat || p.seat_pending || p.seat_request :
            (p.seat != 1 && p.seat != 2) || p.seat_pending > 4 || (!p.seat_pending != !p.seat_request)) ||
        (!status && p.kind != session_kind::feedback && (p.seat_revision || p.seat || p.seat_pending || p.seat_request)) ||
        (seating ? !status || p.intent_selection || !p.intent_seat_revision || p.intent_seat_revision > UINT32_MAX ||
            p.intent_seat_request > UINT32_MAX || ((p.intent_action == session_intent::seat_request) != !p.intent_seat_request) :
            p.intent_seat_revision || p.intent_seat_request)) return false;
    if (p.automatic > 1 || p.automatic_waiting & ~14u || (p.automatic_waiting && !p.automatic) ||
        (p.kind != session_kind::status && p.kind != session_kind::bind && p.kind != session_kind::state && p.kind != session_kind::feedback && p.automatic) ||
        (p.kind != session_kind::status && p.kind != session_kind::feedback && p.automatic_waiting) ||
        ((p.intent_action == session_intent::auto_on || p.intent_action == session_intent::auto_off) && p.intent_selection != select_quadrant) ||
        (p.kind == session_kind::feedback && (p.selection || p.flags))) return false;
    if (p.kind != session_kind::barometer && p.kind != session_kind::instrument_feedback && !world_valid(p.world_values)) return false;
    if (p.kind != session_kind::status && p.kind != session_kind::world && p.kind != session_kind::barometer && p.kind != session_kind::instrument_feedback) {
        for (const auto value : p.world_values) if (value) return false;
    }
    if (p.kind != session_kind::status && p.world_serial) return false;
    if (p.kind == session_kind::status && (p.flags & session_world_fresh) && !p.world_serial) return false;
    if (p.kind == session_kind::world && (!p.sequence || p.selection || p.flags)) return false;
    if (p.kind != session_kind::status && p.kind != session_kind::bind && p.flags & session_world) return false;
    for (unsigned index = 0; index < 4; ++index) {
        if (static_cast<uint32_t>(p.owner[index]) > 2 || !session_token(p.pending_transfer[index], sizeof(p.pending_transfer[index]), !(p.pending & (1u << index))) ||
            !session_token(p.committed_transfer[index], sizeof(p.committed_transfer[index]), true)) return false;
        if (!(p.pending & (1u << index)) && p.pending_transfer[index][0]) return false;
    }
    for (const auto position : p.positions) if (!std::isfinite(position) || (position != -1 && (position < 0 || position > 1))) return false;
    if (p.intent_waiting & ~15u || p.intent_other & ~15u || p.intent_waiting & p.intent_other ||
        static_cast<uint32_t>(p.intent_action) > 10 ||
        (p.intent_id ? (!seating && !valid_selection(p.intent_selection)) || p.intent_action == session_intent::none :
            p.intent_selection || p.intent_action != session_intent::none)) return false;
    if (p.kind == session_kind::status) return p.serial != 0;
    if (p.intent_id || p.intent_selection || p.intent_action != session_intent::none || p.intent_waiting || p.intent_other ||
        (p.kind == session_kind::intent_ack && !p.sequence)) return false;
    return p.epoch && p.command && !p.serial && !p.ack && p.result == session_result::idle &&
        p.phase == session_phase::idle && p.code == session_error::none && !p.blocked && !p.barrier && !p.required && !p.fresh && !p.aligned;
}
inline writer_binding session_binding(const session_packet& p) {
    writer_binding value{}; std::memcpy(value.native, p.native, sizeof(value.native));
    std::memcpy(value.backend, p.backend, sizeof(value.backend)); std::memcpy(value.session, p.session, sizeof(value.session)); value.epoch = p.epoch; return value;
}
inline writer_state session_state(const session_packet& p) {
    writer_state value{}; value.revision = p.revision; value.attached = p.attached; value.pending = p.pending;
    value.engines = p.engines; value.axes = p.axes;
    std::memcpy(value.generation, p.generation, sizeof(value.generation)); std::memcpy(value.owner, p.owner, sizeof(value.owner)); return value;
}
}
