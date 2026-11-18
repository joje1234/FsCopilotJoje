#pragma once
#include "control_profile.h"

namespace fsc_controls {
#pragma pack(push, 1)
struct output_validation_record_v1 {
    uint32_t magic = 0x31564f46, version = 1, generation = 0;
    char aircraft[1024]{};
    uint32_t engines = 0;
    uint64_t proofs[4]{};
    uint32_t checksum = 0;
};
struct output_validation_record {
    uint32_t magic = 0x31564f46, version = 2, generation = 0;
    char aircraft[1024]{};
    uint32_t engines = 0;
    uint64_t proofs[4]{};
    uint32_t response_axes = 0, checksum = 0;
};
#pragma pack(pop)
static_assert(sizeof(output_validation_record_v1) == 1076 && sizeof(output_validation_record) == 1080, "Output validation storage layout changed");

inline bool valid_output_validation(const output_validation_record& value, const char* aircraft, unsigned engines) {
    return value.magic == 0x31564f46 && value.version == 2 && value.generation && engines && engines <= 4 && value.engines == engines &&
        !(value.response_axes >> (steering_axis_count + engines * 3)) &&
        std::memchr(value.aircraft, 0, sizeof(value.aircraft)) && !std::strcmp(value.aircraft, aircraft) &&
        value.checksum == profile_hash(&value, offsetof(output_validation_record, checksum));
}
inline bool read_output_validation_slot(const char* prefix, unsigned slot, output_validation_record& value) {
    char path[600]{}; std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, slot);
    auto* file = std::fopen(path, "rb"); if (!file) return false;
    uint32_t header[2]{};
    if (std::fread(header, 1, sizeof(header), file) != sizeof(header) || std::fseek(file, 0, SEEK_SET)) { std::fclose(file); return false; }
    if (header[0] == 0x31564f46 && header[1] == 1) {
        output_validation_record_v1 old{};
        const bool full = std::fread(&old, 1, sizeof(old), file) == sizeof(old);
        const bool exact = std::fgetc(file) == EOF && !std::ferror(file);
        std::fclose(file);
        if (!full || !exact || old.checksum != profile_hash(&old, offsetof(output_validation_record_v1, checksum))) return false;
        value = output_validation_record{}; value.generation = old.generation; value.engines = old.engines;
        std::memcpy(value.aircraft, old.aircraft, sizeof(value.aircraft)); std::memcpy(value.proofs, old.proofs, sizeof(value.proofs));
        value.checksum = profile_hash(&value, offsetof(output_validation_record, checksum));
        return true;
    }
    const bool complete = std::fread(&value, 1, sizeof(value), file) == sizeof(value);
    const bool exact = std::fgetc(file) == EOF && !std::ferror(file);
    std::fclose(file); return complete && exact;
}
// Both independent copies must agree on a proof. A truncated/interrupted write
// can lose a pass, but cannot resurrect a proof already revoked in either copy.
// This is separate from saved hardware mappings and never stores raw positions,
// ownership, alignment, session membership or an instruction to move controls.
inline bool read_output_validation(const char* prefix, const char* aircraft, unsigned engines, output_validation_record& result) {
    output_validation_record copies[2]{};
    for (unsigned slot = 0; slot < 2; ++slot)
        if (!read_output_validation_slot(prefix, slot, copies[slot]) || !valid_output_validation(copies[slot], aircraft, engines)) return false;
    result = copies[copies[1].generation > copies[0].generation ? 1 : 0];
    for (unsigned index = 0; index < 4; ++index) if (copies[0].proofs[index] != copies[1].proofs[index]) result.proofs[index] = 0;
    const auto changed_response = copies[0].response_axes ^ copies[1].response_axes;
    for (size_t axis = 0; axis < axis_count; ++axis) if (changed_response & (1u << axis))
        result.proofs[axis < steering_axis_count ? 0 : 1 + (axis-steering_axis_count)%3] = 0;
    result.checksum = profile_hash(&result, offsetof(output_validation_record, checksum));
    return true;
}
inline bool write_output_validation(const char* prefix, output_validation_record& value) {
    if (value.generation == UINT32_MAX) return false;
    auto next = value; ++next.generation;
    next.checksum = profile_hash(&next, offsetof(output_validation_record, checksum));
    if (!valid_output_validation(next, next.aircraft, next.engines)) return false;
    for (unsigned offset = 0; offset < 2; ++offset) {
        const unsigned slot = (next.generation + offset) % 2;
        char path[600]{}; std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, slot);
        auto* file = std::fopen(path, "wb"); if (!file) return false;
        const bool complete = std::fwrite(&next, 1, sizeof(next), file) == sizeof(next);
        const bool flushed = std::fflush(file) == 0;
        const bool closed = std::fclose(file) == 0;
        output_validation_record verified{};
        if (!complete || !flushed || !closed || !read_output_validation_slot(prefix, slot, verified) || std::memcmp(&next, &verified, sizeof(next))) return false;
    }
    value = next; return true;
}
}
