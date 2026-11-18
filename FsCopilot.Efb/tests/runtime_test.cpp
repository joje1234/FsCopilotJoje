#include <cassert>
#include <iostream>
#include "../Wasm/control_runtime.h"
using namespace fsc_controls;
struct adapter {
    uint32_t requested = 0, barrier = 0;
    unsigned calls = 0, releases = 0;
    bool fail = false;
    bool configure_mask(uint32_t value, uint32_t id) { requested = value; barrier = id; ++calls; return !fail; }
    void release() { ++releases; }
};
struct fixture {
    control_pickup pickup;
    adapter simulator;
    control_runtime<adapter> runtime;
    double values[target_count]{};
    fixture() : runtime(pickup, simulator) {
        values[axis_count] = 2;
        for (size_t axis = 0; axis < axis_count; ++axis) {
            pickup.inputs[axis].calibrated = true; pickup.inputs[axis].candidates = axis < steering_axis_count ? 1u : 2u;
            if (axis >= steering_axis_count) values[axis] = 50;
        }
        for (const auto axis : {0u, 1u, 9u, 10u, 11u, 12u, 13u, 14u}) pickup.sample(axis, axis < 9 ? 0 : 8192, 1);
        assert(runtime.initialize() && runtime.applying && !runtime.ready);
        assert(!runtime.confirm(100, 1)); assert(runtime.confirm(runtime.barrier, 1));
        runtime.update_targets(values, 1); assert(runtime.set_advanced(true));
    }
    void mock(uint32_t selection, double now = 2) { assert(runtime.assign_mock(selection, now)); if (runtime.applying) assert(runtime.confirm(runtime.barrier, now)); }
    void target(double now) { runtime.update_targets(values, now); }
    void pair(unsigned type, int raw, double now) { pickup.sample(9 + type, raw, now); pickup.sample(12 + type, raw, now); }
};
int main()
{
    fixture f; f.mock(select_throttle);
    assert(f.runtime.blocked == select_throttle && !f.runtime.applying);
    assert(f.runtime.set_advanced(false) && f.runtime.blocked == select_throttle); // Mixed owners cannot be hidden by closing Advanced.
    assert(!f.runtime.set_auto(select_steering, true, 2) && !f.runtime.set_auto(select_throttle, true, 2));
    assert(f.runtime.set_auto(select_quadrant, true, 2));
    f.pair(0, 8192 + 164, 2.1); f.target(2.1); f.runtime.tick(2.1); assert(!f.runtime.waiting()); // 1%.
    f.pair(0, 8192 + 327, 2.2); f.target(2.2); f.runtime.tick(2.2); assert(!f.runtime.waiting()); // Below 2%.
    f.pair(0, 8192 + 328, 2.3); f.target(2.3); f.runtime.tick(2.3);
    assert(f.runtime.waiting() == select_throttle && f.runtime.blocked == select_throttle); // Cumulative >2%, still masked.
    assert(f.runtime.takeovers[0].automatic);
    const auto calls_before = f.simulator.calls;
    f.target(2.61); f.runtime.tick(2.61);
    assert(f.runtime.applying && f.runtime.blocked == select_throttle && f.simulator.calls == calls_before + 1);
    f.runtime.tick(2.7); assert(f.simulator.calls == calls_before + 1); // No duplicate submissions while confirming.
    assert(f.runtime.confirm(f.runtime.barrier, 2.7));
    assert(!f.runtime.blocked && !f.runtime.waiting());

    fixture exact;
    exact.pickup.inputs[9].candidates = exact.pickup.inputs[12].candidates = 4u;
    exact.pair(0, 50, 1.5); exact.mock(select_throttle);
    assert(exact.runtime.set_auto(select_quadrant, true, 2));
    exact.pair(0, 52, 2.1); exact.target(2.1); exact.runtime.tick(2.1); assert(!exact.runtime.waiting()); // Exactly 2% is not enough.
    exact.pair(0, 53, 2.2); exact.target(2.2); exact.runtime.tick(2.2); assert(exact.runtime.waiting() == select_throttle);

    fixture hold; hold.mock(select_quadrant);
    assert(hold.runtime.set_auto(select_quadrant, true, 2));
    hold.pair(2, 10000, 2.1); hold.target(2.1); hold.runtime.tick(2.1);
    assert(hold.runtime.waiting() == select_mixture && hold.runtime.blocked == select_quadrant);
    hold.target(2.6); hold.runtime.tick(2.6); assert(!hold.runtime.applying); // Movement arms pickup; 11% misalignment cannot attach.
    hold.pair(2, 9000, 2.7); hold.target(2.7); hold.runtime.tick(2.7);
    hold.target(3.01); hold.runtime.tick(3.01); assert(hold.runtime.applying);
    assert(hold.runtime.confirm(hold.runtime.barrier, 3.02));
    assert(hold.runtime.blocked == (select_throttle | select_propeller)); // Mixtures only.

    fixture cancel; cancel.mock(select_quadrant); assert(cancel.runtime.set_auto(select_quadrant, true, 2));
    cancel.pair(1, 9000, 2.1); cancel.target(2.1); cancel.runtime.tick(2.1); assert(cancel.runtime.waiting() == select_propeller);
    assert(cancel.runtime.set_auto(select_quadrant, false, 2.2));
    cancel.target(2.8); cancel.runtime.tick(2.8); assert(!cancel.runtime.waiting() && !cancel.runtime.applying && cancel.runtime.blocked == select_quadrant);
    assert(cancel.runtime.request_me(select_propeller));
    assert(cancel.runtime.set_auto(select_quadrant, false, 2.9)); assert(cancel.runtime.waiting() == select_propeller); // Manual request survives Auto off.
    assert(cancel.runtime.cancel(select_propeller, 2.9)); assert(!cancel.runtime.waiting());

    fixture refresh; refresh.mock(select_throttle); refresh.pickup.inputs[9].detected = refresh.pickup.inputs[12].detected = false;
    assert(refresh.runtime.set_auto(select_quadrant, true, 2));
    refresh.pair(0, 12000, 2.1); refresh.target(2.1); refresh.runtime.tick(2.1); assert(!refresh.runtime.waiting()); // First physical reading is a baseline.
    refresh.values[9] = refresh.values[12] = 90;
    refresh.target(2.2); refresh.runtime.tick(2.2); assert(!refresh.runtime.waiting()); // Cockpit movement/refresh is not hardware movement.
    refresh.pair(0, 12400, 2.3); refresh.target(2.3); refresh.runtime.tick(2.3); assert(refresh.runtime.waiting() == select_throttle);

    fixture mixed; mixed.mock(select_throttle); assert(mixed.runtime.request_me(select_quadrant));
    assert(mixed.runtime.applying && mixed.runtime.blocked == select_throttle && mixed.simulator.requested == select_quadrant);
    assert(mixed.runtime.confirm(mixed.runtime.barrier, 2.1));
    mixed.pair(0, 8192, 2.2); mixed.pair(1, 8192, 2.2); mixed.pair(2, 8192, 2.2);
    mixed.target(2.2); mixed.runtime.tick(2.2); mixed.target(2.51); mixed.runtime.tick(2.51);
    assert(mixed.runtime.applying && mixed.simulator.requested == 0); assert(mixed.runtime.confirm(mixed.runtime.barrier, 2.52));
    assert(!mixed.runtime.blocked && mixed.runtime.set_advanced(false));

    fixture both; both.mock(select_quadrant);
    assert(both.runtime.set_advanced(false) && both.runtime.set_auto(select_quadrant, true, 2));
    both.pair(0, 9000, 2.1); both.pair(2, 9000, 2.1); both.target(2.1); both.runtime.tick(2.1);
    assert(both.runtime.waiting() == (select_throttle | select_mixture) && both.runtime.automatic == select_quadrant);
    assert(!both.runtime.set_auto(select_mixture, false, 2.2)); // No individual mode mutation.
    assert(both.runtime.set_auto(select_quadrant, false, 2.2));
    assert(!both.runtime.waiting() && both.runtime.blocked == select_quadrant);
    assert(both.runtime.set_auto(select_quadrant, true, 2.3));
    both.pair(0, 10000, 2.4); both.pair(2, 10000, 2.4); both.target(2.4); both.runtime.tick(2.4);
    assert(both.runtime.waiting() == (select_throttle | select_mixture));
    both.pair(0, 9000, 2.5); both.pair(2, 9000, 2.5); both.target(2.5); both.runtime.tick(2.5);
    both.target(2.81); both.runtime.tick(2.81); assert(both.runtime.applying);
    assert(both.runtime.confirm(both.runtime.barrier, 2.82));
    assert(both.runtime.blocked == (select_propeller | select_mixture));
    both.runtime.tick(2.83); assert(both.runtime.applying); assert(both.runtime.confirm(both.runtime.barrier, 2.84));
    assert(both.runtime.blocked == select_propeller && both.runtime.automatic == select_quadrant);

    fixture presets; presets.mock(select_quadrant);
    presets.pickup.inputs[9].candidates = presets.pickup.inputs[12].candidates = 4u;
    const auto mask_calls = presets.simulator.calls;
    presets.pair(0, 65, 2.1); presets.target(2.1);
    assert(!presets.runtime.aligned(select_throttle, 2.1));
    assert(presets.runtime.set_tolerance(select_all, 20) && presets.runtime.aligned(select_throttle, 2.1));
    presets.pair(0, 75, 2.2); presets.target(2.2); assert(!presets.runtime.aligned(select_throttle, 2.2));
    assert(presets.runtime.set_tolerance(select_all, 30) && presets.runtime.aligned(select_throttle, 2.2));
    presets.pair(0, 80, 2.3); presets.target(2.3); assert(presets.runtime.aligned(select_throttle, 2.3)); // Inclusive 30%.
    presets.pair(0, 81, 2.4); presets.target(2.4); assert(!presets.runtime.aligned(select_throttle, 2.4));
    assert(presets.runtime.blocked == select_quadrant && presets.simulator.calls == mask_calls);
    for (const auto tolerance : presets.runtime.tolerance) assert(tolerance == 0.3);
    assert(!presets.runtime.set_tolerance(select_all, 100));
    presets.pair(0, 75, 2.5); presets.target(2.5); assert(presets.runtime.request_me(select_throttle)); presets.runtime.tick(2.5);
    assert(presets.runtime.set_tolerance(select_all, 20)); presets.runtime.tick(2.6); assert(!presets.runtime.applying);
    assert(presets.runtime.set_tolerance(select_all, 30)); presets.runtime.tick(2.7); presets.runtime.tick(2.99); assert(!presets.runtime.applying);
    presets.target(3.01); presets.runtime.tick(3.01); assert(presets.runtime.applying);
    assert(presets.runtime.confirm(presets.runtime.barrier, 3.02) && presets.runtime.blocked == (select_propeller | select_mixture));

    fixture independent; independent.mock(select_quadrant);
    assert(independent.runtime.request_me(select_throttle) && independent.runtime.request_me(select_mixture));
    assert(independent.runtime.force_me(select_throttle));
    assert(independent.runtime.waiting() == select_mixture); assert(independent.runtime.confirm(independent.runtime.barrier, 2.2));
    assert(independent.runtime.blocked == (select_propeller | select_mixture));
    independent.simulator.fail = true;
    assert(!independent.runtime.force_me(select_mixture));
    assert(!independent.runtime.ready && !independent.runtime.waiting() && !independent.runtime.automatic && independent.simulator.releases == 1);
    std::cout << "Four-group native runtime: strict >2% cumulative Auto trigger, no steering Auto, fresh baselines, independent pairs, alignment hold, confirmation, mixed quadrant, cancellation and failure release passed.\n";
}
