#pragma once
#include <cstdint>
#include <cstddef>
#include <initializer_list>
#include <cstring>
#include <cmath>
namespace fsc_controls {
constexpr unsigned cockpit_limit = 512;
constexpr char cockpit_status_name[] = "FSC_COCKPIT_STATUS_V2", cockpit_command_name[] = "FSC_COCKPIT_COMMAND_V2";
enum class cockpit_kind : uint32_t { status = 1, apply, report };
enum class cockpit_result : uint32_t { idle, pending, succeeded, rejected, failed };
#pragma pack(push,1)
struct cockpit_packet {
    uint32_t magic = 0x31504346, version = 2;
    cockpit_kind kind = cockpit_kind::status;
    uint32_t size = 8192;
    char native[24]{}, backend[40]{}, session[40]{}, profile[64]{};
    uint64_t serial = 0, command = 0, read_serial = 0;
    uint32_t index = 0, count = 0;
    cockpit_result result = cockpit_result::idle;
    uint32_t active = 0;
    double values[cockpit_limit]{};
    uint32_t valid[cockpit_limit / 32]{};
    char expression[2048]{};
    uint32_t linked = 0;
    uint64_t resync_request = 0, resync_ack = 0;
    uint32_t resync_phase = 0, checked = 0, corrected = 0, resync_pending = 0, failed = 0, unavailable = 0;
    char issues[1024]{};
    uint8_t reserved[692]{};
};
#pragma pack(pop)
static_assert(sizeof(cockpit_packet) == 8192 && offsetof(cockpit_packet, values) == 224 && offsetof(cockpit_packet, expression) == 4384, "Cockpit IPC layout changed");
inline bool cockpit_valid(const cockpit_packet& p) {
    if (p.magic != 0x31504346 || p.version != 2 || p.size != sizeof(p) ||
        (p.kind != cockpit_kind::status && p.kind != cockpit_kind::apply && p.kind != cockpit_kind::report) || p.count > cockpit_limit || p.index >= cockpit_limit ||
        static_cast<unsigned>(p.result) > 4 || p.active > 1 || p.linked > 1) return false;
    for (const auto* field : {p.native, p.backend, p.session, p.profile}) {
        const size_t capacity = field == p.native ? sizeof(p.native) : field == p.profile ? sizeof(p.profile) : sizeof(p.session);
        if (!std::memchr(field, 0, capacity)) return false;
    }
    if (!std::memchr(p.expression, 0, sizeof(p.expression))) return false;
    if (!std::memchr(p.issues,0,sizeof(p.issues)) || p.resync_phase>4 || p.checked>512 || p.corrected>p.checked ||
        p.resync_pending>512 || p.failed>512 || p.unavailable>512 || p.checked+p.resync_pending+p.failed+p.unavailable>512) return false;
    for(const char* c=p.issues;*c;++c)if(*c<' '||*c>'~')return false;
    for (const auto byte : p.reserved) if (byte) return false;
    for (unsigned index = 0; index < cockpit_limit; ++index)
        if ((p.valid[index / 32] & (1u << (index % 32))) && (index >= p.count || !std::isfinite(p.values[index]))) return false;
    if (p.kind != cockpit_kind::status && (!p.command || !p.read_serial)) return false;
    return p.kind != cockpit_kind::apply || (p.expression[0] && std::isfinite(p.values[0]) && std::isfinite(p.values[1]) &&
        std::abs(p.values[0]) <= 1e12 && std::abs(p.values[1]) <= 1e12);
}
}
