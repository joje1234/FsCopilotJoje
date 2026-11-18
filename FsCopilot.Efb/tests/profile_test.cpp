#define _CRT_SECURE_NO_WARNINGS // Exercise the same portable stdio used by WASM.
#include <cassert>
#include <iostream>
#include "../Wasm/control_profile.h"
using namespace fsc_controls;

int main()
{
    const char* prefix = "artifacts/efb-control-test/tests/profile-fixture";
    const char* slots[] = {"artifacts/efb-control-test/tests/profile-fixture.0.bin", "artifacts/efb-control-test/tests/profile-fixture.1.bin"};
    for (const char* path : slots) std::remove(path);
    control_profile profile;
    assert(profile_identity("SimObjects\\Airplanes\\TestBaron\\aircraft.cfg", 100, profile.aircraft));
    assert(!std::strcmp(profile.aircraft, "simobjects/airplanes/testbaron/aircraft.cfg"));
    char normalized[1024]{};
    assert(!profile_identity("", 1, normalized));
    assert(!profile_identity("bad", 3, normalized)); // Unterminated bounded packet.
    profile.engines = 2;
    control_pickup original;
    double values[target_count]{};
    values[axis_count] = 2;
    original.observe("AXIS_MIXTURE_SET", 0, 1);
    values[11] = values[14] = 50;
    original.update(values, 1.2, false, false, false);
    original.observe("AXIS_MIXTURE_SET", 8192, 2);
    values[11] = values[14] = 75;
    for (unsigned frame = 0; frame < 3; ++frame) original.update(values, 2.2 + frame * 0.05, false, false, false);
    assert(original.inputs[11].calibrated && original.inputs[14].calibrated);
    assert(capture_profile(profile, original));
    assert(profile.mapping[11].scale == 1 && profile.mapping[14].scale == 1);
    assert(!profile.mapping[0].scale);
    assert(write_profile(prefix, profile) && profile.generation == 1);
    control_profile loaded;
    assert(read_profile(prefix, profile.aircraft, 2, loaded));
    assert(!read_profile(prefix, "another aircraft", 2, loaded));
    assert(!read_profile(prefix, profile.aircraft, 1, loaded));
    bool relearned[2]{};
    control_pickup restarted;
    restore_profile(loaded, restarted, relearned);
    assert(restarted.inputs[11].saved_candidates == 1 && restarted.inputs[11].calibrated);
    assert(!restarted.inputs[11].detected && restarted.inputs[11].observed < 0);
    assert(!restarted.requests[1].waiting && !restarted.ready(group::quadrant, 3));
    // Mapping survives restart; a saved raw/default zero cannot align anything.
    assert(restarted.input_position(11) < 0 && !restarted.aligned(11, 3));
    restarted.update(values, 3, false, false, false);
    assert(!capture_profile(loaded, restarted)); // Waiting for movement never erases disk.
    restarted.update(values, 3, false, false, false);
    assert(restarted.inputs[11].calibrated && restarted.input_position(11) < 0);
    restarted.detach(group::quadrant, 3);
    restarted.observe("AXIS_MIXTURE_SET", 0, 4);
    values[11] = values[14] = 50;
    restarted.update(values, 4, false, true, false); // Works while masked, at ambiguous centre.
    assert(restarted.input_position(11) == 0.5 && restarted.aligned(11, 4));
    restarted.observe("AXIS_MIXTURE_SET", 16384, 5);
    values[11] = values[14] = 100;
    restarted.update(values, 5, false, true, false);
    assert(restarted.input_position(11) == 1 && restarted.aligned(11, 5));
    assert(restarted.inputs[11].calibrated && restarted.inputs[14].calibrated);
    assert(!restarted.inputs[11].saved_candidates && !capture_profile(loaded, restarted));
    auto with_pedals = loaded;
    with_pedals.mapping[2].scale = 1;
    std::snprintf(with_pedals.mapping[2].event, sizeof(with_pedals.mapping[2].event), "%s", "AXIS_RUDDER_SET");
    control_pickup pedals;
    restore_profile(with_pedals, pedals, relearned);
    assert(pedals.required_axis(2) && !pedals.inputs[2].detected); // Saved optional axes cannot vanish from pickup.

    control_pickup single_position;
    restore_profile(loaded, single_position, relearned);
    single_position.observe("AXIS_MIXTURE_SET", 8192, 1);
    for (unsigned frame = 0; frame < 3; ++frame) single_position.update(values, 1.2 + frame * 0.05, false, false, false);
    assert(single_position.inputs[11].calibrated); // No second position required.
    single_position.observe("AXIS_MIXTURE1_SET", 8192, 2);
    assert(!single_position.inputs[11].calibrated && !single_position.inputs[11].saved_candidates);
    assert(single_position.inputs[14].calibrated); // Only affected binding resets.

    control_pickup saved_with_echo;
    restore_profile(loaded, saved_with_echo, relearned);
    saved_with_echo.observe("MIXTURE1_SET", 12288, 1);
    assert(saved_with_echo.inputs[11].saved_candidates == 1 && !saved_with_echo.inputs[11].detected);
    saved_with_echo.observe("AXIS_MIXTURE_SET", 8192, 2);
    saved_with_echo.observe("MIXTURE1_SET", 12288, 2.01);
    for (unsigned frame = 0; frame < 3; ++frame) saved_with_echo.update(values, 2.2 + frame * 0.05, false, false, false);
    assert(saved_with_echo.inputs[11].calibrated && saved_with_echo.inputs[11].raw == 8192);

    control_pickup early_echo;
    early_echo.observe("MIXTURE1_SET", 12288, 1); // Feedback arrives before aircraft identity.
    restore_profile(loaded, early_echo, relearned);
    assert(early_echo.inputs[11].calibrated && early_echo.inputs[11].saved_candidates == 1);
    assert(!early_echo.inputs[11].detected && early_echo.input_position(11) < 0);
    early_echo.update(values, 1.1, false, false, false);
    assert(!capture_profile(loaded, early_echo));
    early_echo.observe("AXIS_MIXTURE_SET", 0, 2);
    assert(early_echo.input_position(11) == 0.5);

    control_pickup delayed_reference;
    restore_profile(loaded, delayed_reference, relearned);
    delayed_reference.observe("AXIS_MIXTURE_SET", 8192, 1);
    values[11] = values[14] = 25; // Lagging cockpit read-back cannot erase a saved scale.
    for (unsigned frame = 0; frame < 3; ++frame) delayed_reference.update(values, 1.2 + frame * 0.05, false, false, false);
    assert(delayed_reference.inputs[11].calibrated && delayed_reference.inputs[11].candidates == 1);
    assert(!delayed_reference.aligned(11, 1.3));
    assert(!capture_profile(loaded, delayed_reference));
    control_pickup early_input;
    early_input.observe("AXIS_MIXTURE_SET", 0, 2);
    restore_profile(loaded, early_input, relearned);
    assert(early_input.input_position(11) == 0.5 && early_input.inputs[11].observed == 2);
    control_pickup invalid_range;
    restore_profile(loaded, invalid_range, relearned);
    invalid_range.observe("AXIS_MIXTURE_SET", 100000, 3);
    assert(!invalid_range.inputs[11].calibrated && !invalid_range.inputs[11].saved_candidates);
    relearned[1] = true;
    control_pickup reset;
    restore_profile(loaded, reset, relearned);
    assert(!reset.inputs[11].saved_candidates); // Explicit Relearn before late load.

    // A reset is durable; corruption falls back to the preceding valid slot.
    profile.mapping[11] = profile_axis{};
    assert(write_profile(prefix, profile) && profile.generation == 2);
    assert(read_profile(prefix, profile.aircraft, 2, loaded) && !loaded.mapping[11].scale);
    auto* damaged = std::fopen(slots[0], "wb");
    assert(damaged);
    std::fputs("interrupted write", damaged);
    std::fclose(damaged);
    assert(read_profile(prefix, profile.aircraft, 2, loaded) && loaded.generation == 1);
    auto invalid = loaded;
    invalid.mapping[11].scale = 3; // Ambiguous masks are never trusted, even if checksummed.
    invalid.checksum = profile_hash(&invalid, offsetof(control_profile, checksum));
    assert(!valid_profile(invalid, profile.aircraft, 2));
    invalid = loaded;
    invalid.version = 99;
    invalid.checksum = profile_hash(&invalid, offsetof(control_profile, checksum));
    assert(!valid_profile(invalid, profile.aircraft, 2));
    invalid = loaded;
    invalid.mapping[11].scale ^= 2; // Accidental corruption.
    assert(!valid_profile(invalid, profile.aircraft, 2));
    damaged = std::fopen(slots[1], "wb");
    assert(damaged);
    std::fwrite(&invalid, 1, sizeof(invalid), damaged);
    std::fclose(damaged);
    assert(!read_profile(prefix, profile.aircraft, 2, loaded)); // Both slots invalid: normal learning, no attachment.
    assert(!write_profile("artifacts/efb-control-test/nonexistent-directory/profile", loaded));
    for (const char* path : slots) std::remove(path);
    assert(!read_profile(prefix, profile.aircraft, 2, loaded));
    std::cout << "Saved-profile restart without relearning, fresh hardware requirement, centre/endpoint, echo rejection, changed events, reset and damaged-file recovery passed.\n";
}
