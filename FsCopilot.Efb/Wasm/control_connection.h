#pragma once
#include <cstdint>
#include <cstddef>
#include <cstring>

namespace fsc_controls {
#pragma pack(push, 1)
struct connection_settings {
    uint64_t revision = 0;
    char relay[128]{}, local[16]{}, peer[16]{};
    uint32_t port = 0, role = 0, seat = 0; // Role: host 1 / guest 2. Seat: left 1 / right 2.
};
#pragma pack(pop)
static_assert(sizeof(connection_settings) == 180, "Connection settings layout changed");
inline bool connection_text(const char* text, size_t capacity) {
    size_t end = 0; while (end < capacity && text[end]) ++end;
    if (end == capacity) return false;
    for (size_t index = end; index < capacity; ++index) if (text[index]) return false;
    return true;
}
inline bool connection_code(const char* text, size_t capacity) {
    if (!connection_text(text, capacity) || std::strlen(text) != 8) return false;
    for (unsigned index = 0; index < 8; ++index)
        if (!(text[index] >= 'a' && text[index] <= 'z') && !(text[index] >= '0' && text[index] <= '9')) return false;
    return true;
}
inline bool connection_host(const char* text, size_t capacity) {
    if (!connection_text(text, capacity)) return false;
    const auto length = std::strlen(text);
    if (!length || length > 127) return false;
    size_t label = 0;
    for (size_t index = 0; index < length; ++index) {
        const auto c = text[index];
        if (c == '.') { if (!label || text[index - 1] == '-') return false; label = 0; continue; }
        const bool alnum = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        if ((!alnum && c != '-') || (!label && c == '-') || ++label > 63) return false;
    }
    return label && text[length - 1] != '-';
}
inline bool connection_empty(const connection_settings& value, bool revision_only = false) {
    connection_settings empty{}; if (revision_only) empty.revision = value.revision;
    return std::memcmp(&value, &empty, sizeof(value)) == 0;
}
inline bool connection_valid(const connection_settings& value) {
    return value.revision && value.revision <= UINT32_MAX && connection_host(value.relay, sizeof(value.relay)) &&
        connection_code(value.local, sizeof(value.local)) && connection_code(value.peer, sizeof(value.peer)) &&
        std::strcmp(value.local, value.peer) && value.port >= 1 && value.port <= 65535 &&
        value.role >= 1 && value.role <= 2 && value.seat >= 1 && value.seat <= 2;
}
}
