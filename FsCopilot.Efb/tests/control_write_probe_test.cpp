#define _CRT_SECURE_NO_WARNINGS
#include <cassert>
#include <iostream>
#include "same_state_control_check.h"
using namespace fsc_controls;
int main() {
    bridge_packet observed{}; std::snprintf(observed.native, sizeof(observed.native), "abc123");
    std::snprintf(observed.aircraft, sizeof(observed.aircraft), "simobjects/airplanes/bksq-aircraft-baronpropress/aircraft.cfg");
    std::snprintf(observed.adapter, sizeof(observed.adapter), "bksq-baron58p-cockpit-v1");
    observed.serial = 2; observed.flags = bridge_ready | bridge_targets_fresh; observed.engines = 2;
    observed.required = observed.learned = (1u << 9) | (1u << 12); observed.targets[9] = observed.input[9] = 0.25;
    observed.targets[12] = observed.input[12] = 0.995;
    control_profile saved{}; saved.engines = 2; saved.generation = 1;
    std::snprintf(saved.aircraft, sizeof(saved.aircraft), "%s", observed.aircraft);
    for (auto axis : {9, 12}) { saved.mapping[axis].scale = 1; std::snprintf(saved.mapping[axis].event, sizeof(saved.mapping[axis].event), "THROTTLE_AXIS_SET_EX1"); }
    saved.checksum = profile_hash(&saved, offsetof(control_profile, checksum));
    output_check_guard guard{10, 1, 0.04, 0, 1};
    same_state_control_check test;
    assert(test.prepare(observed, saved, group::throttle, 10.1, 10, guard));
    assert(test.count == 2 && test.axes == observed.required && test.matches_profile(saved));
    assert(!std::strcmp(test.actions[0].event, "THROTTLE1_AXIS_SET_EX1") && !std::strcmp(test.actions[1].event, "THROTTLE2_AXIS_SET_EX1"));
    assert(test.actions[0].raw != test.actions[1].raw);
    assert(!test.prepare(observed, saved, group::throttle, 10.1, 10, guard));
    auto changed_profile = saved; ++changed_profile.checksum; assert(!test.matches_profile(changed_profile));
    output_action nudge[axis_count]{}; size_t count = 0; assert(test.make_nudge(nudge, count) && count == 2);
    assert(nudge[0].raw > test.actions[0].raw && nudge[1].raw < test.actions[1].raw); // Stay in range at the upper stop.
    auto current = observed; ++current.serial;
    assert(test.current(current, 10.1, 10, guard, true));
    assert(!test.current(current, 10.1, 10, guard, true, true)); // Unchanged read-back cannot prove a nudge.
    current.targets[9] = 0.26; current.targets[12] = 0.985;
    assert(test.current(current, 10.1, 10, guard, true, true));
    assert(!test.current(current, 10.1, 10, guard, true)); // Original position must be observed again after restoration.
    current.input[9] += 0.01; assert(!test.current(current, 10.1, 10, guard, true, true)); // Self-recapture/hardware movement fails proof.
    current = observed; current.serial = 1; assert(!test.current(current, 10.1, 10, guard));
    {
        auto baseline = observed; baseline.input[12] = baseline.targets[12] = baseline.input[9];
        same_state_control_check noisy; assert(noisy.prepare(baseline, saved, group::throttle, 10.1, 10, guard));
        auto measured = baseline; ++measured.serial;
        measured.input[9] = measured.input[12] = control_pickup::prediction(-8322, 0);
        assert(!noisy.current(measured, 10.1, 10, guard, true)); // No captured hardware evidence yet.
        assert(noisy.hardware_noise("THROTTLE_AXIS_SET_EX1", -8322));
        assert(noisy.current(measured, 10.1, 10, guard, true));
        assert(!noisy.hardware_noise("THROTTLE1_AXIS_SET_EX1", -8192)); // Even a same-value output echo fails.
        assert(!noisy.hardware_noise("THROTTLE2_AXIS_SET_EX1", -8192));
        assert(!noisy.hardware_noise("THROTTLE_INCR", -8192));
        assert(!noisy.hardware_noise("THROTTLE_AXIS_SET_EX1", -8392)); // More than 0.45% from initial hardware pose.
        measured.input[9] = baseline.input[9] + .01;
        assert(!noisy.current(measured, 10.1, 10, guard, true));
    }
    auto bad = observed;
    {
        auto candidate = observed; candidate.connection_api = 1; candidate.capabilities = 27;
        same_state_control_check unconfirmed;
        assert(!unconfirmed.prepare(candidate, saved, group::throttle, 10.1, 10, guard));
        auto idle = guard; idle.unbound_session_fresh = true;
        same_state_control_check confirmed;
        assert(confirmed.prepare(candidate, saved, group::throttle, 10.1, 10, idle));
        idle.unbound_session_fresh = false;
        assert(!confirmed.current(candidate, 10.1, 10, idle));
        idle.unbound_session_fresh = true; candidate.capabilities = 31;
        same_state_control_check shared_output;
        assert(!shared_output.prepare(candidate, saved, group::throttle, 10.1, 10, idle));
    }
    for (unsigned scenario = 0; scenario < 13; ++scenario) {
        bad = observed; auto unsafe = guard; auto profile = saved; double at = 10;
        if (scenario == 0) unsafe.ground = 0;
        if (scenario == 1) unsafe.knots = 0.101;
        if (scenario == 2) unsafe.parking_brake = 0;
        if (scenario == 3) unsafe.at = 9;
        if (scenario == 4) at = 9;
        if (scenario == 5) bad.blocked = 2;
        if (scenario == 6) bad.automatic = 14;
        if (scenario == 7) bad.connection_api = 1;
        if (scenario == 8) bad.learned &= ~(1u << 12);
        if (scenario == 9) bad.targets[9] = -1;
        if (scenario == 10) profile.mapping[12].scale = 0;
        if (scenario == 11) bad.flags |= bridge_storage_failed;
        if (scenario == 12) bad.aircraft[0] = 'x';
        same_state_control_check rejected;
        assert(!rejected.prepare(bad, profile, group::throttle, 10.1, at, unsafe) && rejected.count == 0);
    }
    same_state_control_check rejected;
    assert(!rejected.prepare(observed, saved, group::quadrant, 10.1, 10, guard));
    {
        auto mixture = observed; mixture.required = mixture.learned = (1u << 11) | (1u << 14);
        mixture.input[11] = mixture.targets[11] = .5; mixture.input[14] = mixture.targets[14] = 1;
        auto profile = saved;
        for (const auto axis : {11,14}) { profile.mapping[axis].scale = 1; std::snprintf(profile.mapping[axis].event, sizeof(profile.mapping[axis].event), "AXIS_MIXTURE_SET"); }
        profile.checksum = profile_hash(&profile, offsetof(control_profile, checksum));
        same_state_control_check explicit_set;
        assert(explicit_set.prepare(mixture, profile, group::mixture, 10.1, 10, guard));
        assert(explicit_set.use_mixture_set_route());
        assert(!std::strcmp(explicit_set.actions[0].event, "MIXTURE1_SET") && explicit_set.actions[0].raw == 8192);
        assert(!std::strcmp(explicit_set.actions[1].event, "MIXTURE2_SET") && explicit_set.actions[1].raw == 16384);
        output_action moved[axis_count]{}; size_t count = 0;
        assert(explicit_set.make_nudge(moved,count) && count == 2 && moved[0].raw > 8192 && moved[1].raw < 16384);
        assert(!test.use_mixture_set_route()); // Throttle output cannot silently change routes.
    }
    std::cout << "Output diagnostic passed: parked/fresh solo preflight, saved per-engine mapping, one-percent bounded targets, distinct movement proof, exact restoration proof, hardware echo rejection and malformed/stale context refusal. No simulator access.\n";
}
