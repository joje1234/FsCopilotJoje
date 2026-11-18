#pragma once
#include <cstdint>
#include <cstring>
#include <cmath>
#include "control_connection.h"

namespace fsc_controls {
constexpr uint32_t bridge_magic = 0x31434246; // FBC1, little endian.
constexpr uint32_t bridge_version = 1;
constexpr uint32_t bridge_packet_size = 1024;
constexpr char bridge_status_name[] = "FSC_CONTROLS_NATIVE_STATUS_V1";
constexpr char bridge_command_name[] = "FSC_CONTROLS_NATIVE_COMMAND_V1";
constexpr char bridge_reply_name[] = "FSC_CONTROLS_NATIVE_REPLY_V1";
enum class bridge_kind : uint32_t { status = 1, hello = 2, reply = 3, backend = 4 };
enum bridge_flags : uint32_t { bridge_ready = 1, bridge_applying = 2, bridge_advanced = 4, bridge_targets_fresh = 8, bridge_storage_failed = 16 };
// Capabilities match SessionNegotiation: remote-input isolation (4) is absent
// until the authenticated/fenced simulator writer is implemented and verified.
constexpr uint32_t bridge_capabilities = 1 | 2 | 8;
#pragma pack(push, 1)
struct bridge_packet {
    uint32_t magic = bridge_magic, version = bridge_version;
    bridge_kind kind = bridge_kind::status;
    uint32_t size = bridge_packet_size;
    char native[24]{}, backend[40]{}, aircraft[256]{}, adapter[64]{};
    uint64_t serial = 0, request = 0;
    uint32_t revision = 0, flags = 0, blocked = 0, automatic = 0, engines = 0;
    uint32_t required = 0, learned = 0, fresh = 0, aligned = 0, capabilities = bridge_capabilities;
    double input[21]{}, targets[21]{};
    uint32_t tolerance[4] = {10, 10, 10, 10};
    connection_settings configuration{};
    uint32_t backend_phase = 0, backend_code = 0, connection_api = 0;
    uint32_t baro_pressure16[2]{}, baro_standard = 0, baro_flags = 0;
    uint64_t baro_serial = 0;
};
#pragma pack(pop)
static_assert(sizeof(bridge_packet) == bridge_packet_size && offsetof(bridge_packet, configuration) == 808 &&
    offsetof(bridge_packet, connection_api) == 996 && offsetof(bridge_packet, baro_pressure16) == 1000 &&
    offsetof(bridge_packet, baro_serial) == 1016, "Native wire layout changed");

inline bool bridge_token(const char* value, size_t capacity, size_t exact = 0) {
    size_t length = 0;
    while (length < capacity && value[length]) {
        if (!((value[length] >= 'a' && value[length] <= 'f') || (value[length] >= '0' && value[length] <= '9'))) return false;
        ++length;
    }
    return length > 0 && length < capacity && (!exact || length == exact);
}
inline bool bridge_text(const char* value, size_t capacity) {
    for (size_t i = 0; i < capacity; ++i) {
        if (!value[i]) return true;
        if (static_cast<unsigned char>(value[i]) < 32 || static_cast<unsigned char>(value[i]) > 126) return false;
    }
    return false;
}
inline bool bridge_valid(const bridge_packet& packet) {
    if (packet.magic != bridge_magic || packet.version != bridge_version || packet.size != sizeof(packet) ||
        (packet.kind != bridge_kind::status && packet.kind != bridge_kind::hello && packet.kind != bridge_kind::reply && packet.kind != bridge_kind::backend)) return false;
    if (packet.baro_flags & ~1023u || packet.baro_standard & ~3u ||
        ((packet.baro_flags & 960) && !(packet.baro_flags & 2)) || ((packet.baro_flags & 128) && (packet.baro_flags & 320) != 320) ||
        ((packet.baro_flags & 64) && (packet.baro_flags & 8)) ||
        ((packet.baro_flags & 4) && !(packet.baro_flags & 2)) || ((packet.baro_flags & 8) && (packet.baro_flags & 5) != 5) ||
        ((packet.baro_flags & 48) && !(packet.baro_flags & 2)) ||
        (!packet.baro_serial ? (packet.baro_flags & 1) || packet.baro_standard || packet.baro_pressure16[0] || packet.baro_pressure16[1] :
            packet.baro_pressure16[0] < 8000 || packet.baro_pressure16[0] > 19200 || packet.baro_pressure16[1] < 8000 || packet.baro_pressure16[1] > 19200) ||
        ((packet.kind == bridge_kind::hello || packet.kind == bridge_kind::backend) && (packet.baro_serial || packet.baro_flags))) return false;
    if (packet.connection_api > 1 || packet.backend_phase > 7 || packet.backend_code > 6 ||
        (!packet.connection_api && (!connection_empty(packet.configuration) || packet.backend_phase || packet.backend_code))) return false;
    if (packet.kind == bridge_kind::backend) return packet.connection_api == 1 && !packet.serial && packet.request &&
        bridge_token(packet.native, sizeof(packet.native)) && bridge_token(packet.backend, sizeof(packet.backend), 32) &&
        connection_empty(packet.configuration, true) && !packet.aircraft[0] && !packet.adapter[0] && !packet.flags && !packet.blocked &&
        !packet.automatic && !packet.engines && !packet.required && !packet.learned && !packet.fresh && !packet.aligned && !packet.capabilities;
    if (!connection_empty(packet.configuration) && !connection_valid(packet.configuration)) return false;
    if (packet.kind == bridge_kind::hello)
        return connection_empty(packet.configuration) && !packet.backend_phase && !packet.backend_code && packet.request && bridge_token(packet.backend, sizeof(packet.backend), 32) &&
            (!packet.native[0] || bridge_token(packet.native, sizeof(packet.native)));
    if (!packet.serial || !bridge_token(packet.native, sizeof(packet.native)) ||
        !bridge_text(packet.aircraft, sizeof(packet.aircraft)) || !bridge_text(packet.adapter, sizeof(packet.adapter)) ||
        packet.flags & ~31u || packet.blocked & ~15u || packet.automatic & ~14u || packet.engines > 4 ||
        packet.required >> 21 || packet.learned >> 21 || packet.fresh >> 21 || packet.aligned >> 21 || packet.capabilities & ~63u) return false;
    if (packet.kind == bridge_kind::reply && (!packet.request || !bridge_token(packet.backend, sizeof(packet.backend), 32))) return false;
    if (packet.kind == bridge_kind::status && (packet.request || packet.backend[0])) return false;
    if (packet.flags & bridge_ready && (!packet.aircraft[0] || !packet.adapter[0] || !packet.engines)) return false;
    for (unsigned axis = 0; axis < 21; ++axis)
        if (!std::isfinite(packet.input[axis]) || !std::isfinite(packet.targets[axis]) ||
            (packet.input[axis] != -1 && (packet.input[axis] < 0 || packet.input[axis] > 1)) ||
            (packet.targets[axis] != -1 && (packet.targets[axis] < 0 || packet.targets[axis] > 1))) return false;
    for (const auto value : packet.tolerance) if (value != 1 && value != 2 && value != 3 && value != 5 && value != 10 && value != 20 && value != 30) return false;
    return true;
}
}
