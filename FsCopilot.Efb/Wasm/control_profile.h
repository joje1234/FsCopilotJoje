#pragma once
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "control_pickup.h"

namespace fsc_controls {
// Only validated mappings are durable. Ownership, raw values, freshness,
// alignment and pending requests are intentionally absent from this format.
struct profile_axis { uint32_t scale = 0; char event[48]{}; };
struct control_profile {
    uint32_t version = 1;
    uint32_t generation = 0;
    char aircraft[1024]{};
    uint32_t engines = 0;
    profile_axis mapping[axis_count]{};
    uint32_t checksum = 0;
};

inline uint32_t profile_hash(const void* data, size_t size)
{
    const auto* bytes = static_cast<const unsigned char*>(data);
    uint32_t hash = 2166136261u;
    for (size_t i = 0; i < size; ++i) hash = (hash ^ bytes[i]) * 16777619u;
    return hash;
}

inline bool profile_identity(const char* path, size_t capacity, char (&identity)[1024])
{
    size_t length = 0;
    while (length < capacity && path[length]) ++length;
    if (!length || length >= capacity || length >= sizeof(identity)) return false;
    std::memset(identity, 0, sizeof(identity));
    for (size_t i = 0; i < length; ++i) {
        const unsigned char c = static_cast<unsigned char>(path[i]);
        identity[i] = c == '\\' ? '/' : c >= 'A' && c <= 'Z' ? static_cast<char>(c + 'a' - 'A') : static_cast<char>(c);
    }
    return true;
}

inline bool valid_profile(const control_profile& value, const char* identity, unsigned engines)
{
    if (value.version != 1 || !value.generation || value.engines != engines || !engines || engines > 4 ||
        !std::memchr(value.aircraft, 0, sizeof(value.aircraft)) || std::strcmp(value.aircraft, identity) ||
        value.checksum != profile_hash(&value, offsetof(control_profile, checksum))) return false;
    for (const auto& mapping : value.mapping) {
        if (mapping.scale && (mapping.scale > 512 || (mapping.scale & (mapping.scale - 1)))) return false;
        if (!std::memchr(mapping.event, 0, sizeof(mapping.event)) || (!!mapping.scale != !!mapping.event[0])) return false;
        for (const char* c = mapping.event; *c; ++c)
            if (!(*c >= 'A' && *c <= 'Z') && !(*c >= '0' && *c <= '9') && *c != '_') return false;
    }
    return true;
}

// Two alternating, checksummed slots preserve the preceding valid generation
// if a write is interrupted. Neither corrupt/truncated slot can activate input.
inline bool read_profile(const char* prefix, const char* identity, unsigned engines, control_profile& result)
{
    bool found = false;
    for (unsigned slot = 0; slot < 2; ++slot) {
        char path[600]{};
        std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, slot);
        auto* file = std::fopen(path, "rb");
        if (!file) continue;
        control_profile candidate{};
        const bool complete = std::fread(&candidate, 1, sizeof(candidate), file) == sizeof(candidate);
        const bool exact = std::fgetc(file) == EOF && !std::ferror(file);
        std::fclose(file);
        if (complete && exact && valid_profile(candidate, identity, engines) && (!found || candidate.generation > result.generation)) {
            result = candidate;
            found = true;
        }
    }
    return found;
}

inline bool write_profile(const char* prefix, control_profile& value)
{
    if (value.generation == UINT32_MAX) return false;
    control_profile next = value;
    ++next.generation;
    next.checksum = profile_hash(&next, offsetof(control_profile, checksum));
    if (!valid_profile(next, next.aircraft, next.engines)) return false;
    char path[600]{};
    std::snprintf(path, sizeof(path), "%s.%u.bin", prefix, next.generation % 2);
    auto* file = std::fopen(path, "wb");
    if (!file) return false;
    const bool complete = std::fwrite(&next, 1, sizeof(next), file) == sizeof(next);
    const bool flushed = std::fflush(file) == 0;
    const bool closed = std::fclose(file) == 0;
    if (!complete || !flushed || !closed) return false;
    value = next;
    return true;
}

inline void restore_profile(const control_profile& profile, control_pickup& pickup, uint32_t relearned_axes)
{
    for (size_t axis = 0; axis < axis_count; ++axis) {
        auto& input = pickup.inputs[axis];
        const auto& mapping = profile.mapping[axis];
        if (!mapping.scale || input.calibrated || (relearned_axes & (1u << axis))) continue;
        if (input.event[0] && std::strcmp(input.event, mapping.event)) {
            // A startup SET echo may precede aircraft identity/cache loading.
            // It cannot replace a saved AXIS source or provide its raw position.
            if (control_pickup::axis_source(mapping.event) && !control_pickup::axis_source(input.event)) input = pickup_axis{};
            else continue;
        }
        input.saved_candidates = mapping.scale;
        input.candidates = mapping.scale;
        input.calibrated = true;
        std::snprintf(input.event, sizeof(input.event), "%s", mapping.event);
        input.confirmations = 0;
        // Identity may arrive after an input event. Reuse only that actual
        // observation, keeping its timestamp; never synthesize a raw position.
        if (input.detected) pickup.sample(axis, input.raw, input.observed);
    }
}

inline void restore_profile(const control_profile& profile, control_pickup& pickup, const bool* relearned)
{
    const uint32_t steering = (1u << steering_axis_count) - 1;
    restore_profile(profile, pickup, (relearned[0] ? steering : 0u) | (relearned[1] ? ((1u << axis_count) - 1) & ~steering : 0u));
}

inline bool capture_profile(control_profile& profile, const control_pickup& pickup)
{
    bool changed = false;
    for (size_t axis = 0; axis < axis_count; ++axis) {
        const auto& input = pickup.inputs[axis];
        profile_axis mapping{};
        if (pickup.active_axis(axis) && input.event[0]) {
            mapping.scale = input.calibrated ? input.candidates : input.saved_candidates;
            if (mapping.scale) std::snprintf(mapping.event, sizeof(mapping.event), "%s", input.event);
        }
        if (std::memcmp(&mapping, &profile.mapping[axis], sizeof(mapping))) {
            profile.mapping[axis] = mapping;
            changed = true;
        }
    }
    return changed;
}
}
