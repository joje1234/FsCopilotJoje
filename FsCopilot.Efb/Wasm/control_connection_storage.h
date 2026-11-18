#pragma once
#include <cstdio>
#include "control_connection.h"

namespace fsc_controls {
#pragma pack(push, 1)
struct connection_file {
    uint32_t magic = 0x31434346, version = 1;
    connection_settings settings{};
    uint32_t checksum = 0;
};
#pragma pack(pop)
static_assert(sizeof(connection_file) == 192, "Connection file layout changed");

inline uint32_t connection_hash(const connection_file& value) {
    uint32_t hash = 2166136261u; const auto* bytes = reinterpret_cast<const uint8_t*>(&value);
    for (size_t index = 0; index < offsetof(connection_file, checksum); ++index) hash = (hash ^ bytes[index]) * 16777619u;
    return hash;
}
inline bool connection_read(const char* prefix, connection_settings& result) {
    bool found = false;
    for (unsigned slot = 0; slot < 2; ++slot) {
        char path[600]{}; std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, slot);
        auto* file = std::fopen(path, "rb"); if (!file) continue;
        connection_file value{};
        const bool complete = std::fread(&value, 1, sizeof(value), file) == sizeof(value);
        const bool exact = std::fgetc(file) == EOF && !std::ferror(file); std::fclose(file);
        if (complete && exact && value.magic == 0x31434346 && value.version == 1 && connection_valid(value.settings) &&
            value.checksum == connection_hash(value) && (!found || value.settings.revision > result.revision)) {
            found = true; result = value.settings;
        }
    }
    return found;
}
inline bool connection_write(const char* prefix, const connection_settings& settings) {
    if (!connection_valid(settings)) return false;
    connection_file value{}; value.settings = settings; value.checksum = connection_hash(value);
    char path[600]{}; std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, static_cast<unsigned>(settings.revision % 2));
    auto* file = std::fopen(path, "wb"); if (!file) return false;
    const bool complete = std::fwrite(&value, 1, sizeof(value), file) == sizeof(value);
    const bool flushed = std::fflush(file) == 0; const bool closed = std::fclose(file) == 0;
    connection_settings verified{};
    return complete && flushed && closed && connection_read(prefix, verified) && std::memcmp(&verified, &settings, sizeof(settings)) == 0;
}
}
