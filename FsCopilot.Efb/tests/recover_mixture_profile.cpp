// Offline recovery planner. Reads actual checksummed profiles, fills only
// missing mixture mappings, and writes a new profile under the OUTPUT prefix.
// It never modifies either input prefix or communicates with the simulator.
#define _CRT_SECURE_NO_WARNINGS
#include <cassert>
#include <algorithm>
#include <iostream>
#include "../Wasm/control_profile.h"
using namespace fsc_controls;
namespace {
constexpr const char* aircraft = "simobjects/airplanes/bksq-aircraft-baronpropress/aircraft.cfg";
uint32_t merge(control_profile& current, const control_profile& backup) {
    uint32_t restored = 0;
    for (const size_t axis : {size_t(11), size_t(14)}) if (!current.mapping[axis].scale && backup.mapping[axis].scale) {
        current.mapping[axis] = backup.mapping[axis]; restored |= 1u << axis;
    }
    return restored;
}
void tests() {
    control_profile current{}, backup{};
    for (const size_t axis : {size_t(9), size_t(11), size_t(14)}) {
        backup.mapping[axis].scale = 1; std::snprintf(backup.mapping[axis].event, sizeof(backup.mapping[axis].event), "AXIS_MIXTURE_SET");
    }
    current.mapping[9].scale = 2; std::snprintf(current.mapping[9].event, sizeof(current.mapping[9].event), "THROTTLE1_SET");
    auto before = current;
    assert(merge(current, backup) == ((1u << 11) | (1u << 14)));
    for (size_t axis = 0; axis < axis_count; ++axis) if (axis != 11 && axis != 14)
        assert(!std::memcmp(&current.mapping[axis], &before.mapping[axis], sizeof(profile_axis)));
    current.mapping[11].scale = 2; before = current;
    assert(!merge(current, backup) && !std::memcmp(&current, &before, sizeof(current)));
    current.mapping[14] = {}; backup.mapping[14] = {}; before = current;
    assert(!merge(current, backup) && !std::memcmp(&current, &before, sizeof(current)));
    std::cout << "Recovery planner passed: only missing mixtures restored; newly learned and unrelated axes retained; absent backup mapping never invented.\n";
}
}
int main(int argc, char** argv) {
    if (argc == 2 && !std::strcmp(argv[1], "--test")) { tests(); return 0; }
    if (argc != 4) { std::cout << "Usage: recover_mixture_profile CURRENT_PREFIX BACKUP_PREFIX OUTPUT_PREFIX\n"; return 2; }
    control_profile current{}, backup{};
    if (!read_profile(argv[1], aircraft, 2, current) || !read_profile(argv[2], aircraft, 2, backup)) {
        std::cerr << "Current/backup aircraft profiles must both pass production checksum/schema validation. No output written.\n"; return 3;
    }
    const auto original = current;
    const auto restored = merge(current, backup);
    std::cout << "current_generation=" << original.generation << " backup_generation=" << backup.generation << " restored_mask=" << restored << '\n';
    if (!restored) { std::cout << "No missing mixture mappings to recover. Input files unchanged.\n"; return 0; }
    current.generation = (std::max)(current.generation, backup.generation);
    if (!write_profile(argv[3], current)) { std::cerr << "Could not write validated recovery output.\n"; return 4; }
    control_profile verified{};
    if (!read_profile(argv[3], aircraft, 2, verified) || std::memcmp(&verified, &current, sizeof(current))) return 5;
    for (size_t axis = 0; axis < axis_count; ++axis) if (!(restored & (1u << axis)) &&
        std::memcmp(&verified.mapping[axis], &original.mapping[axis], sizeof(profile_axis))) return 6;
    std::cout << "Verified recovery output generation=" << current.generation << " slot=" << current.generation % 2 << "; both input prefixes unchanged.\n";
}
