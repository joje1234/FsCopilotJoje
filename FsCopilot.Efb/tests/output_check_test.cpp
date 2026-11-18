#include <cassert>
#include <iostream>
#include "../Wasm/control_output_check.h"
using namespace fsc_controls;

struct fake_output {
    bool available = true, ignored = false, no_restore = false;
    unsigned writes = 0; DWORD ticket = 0;
    control_pickup& pickup;
    bool write(const output_action* actions, size_t count) {
        ++writes;
        if (!(ignored && writes == 2) && !(no_restore && writes == 3))
            for (size_t i = 0; i < count; ++i) pickup.targets[actions[i].axis] = actions[i].position;
        return true;
    }
    DWORD request_checkpoint() { return ++ticket; }
};
struct fixture {
    control_pickup pickup;
    output_check_context context;
    fake_output output;
    control_output_check<fake_output> check;
    double now = 1;
    fixture() : output{true, false, false, 0, 0, pickup} {
        pickup.engines = 2;
        for (const auto axis : {0, 1, 9, 10, 11, 12, 13, 14}) {
            auto& input = pickup.inputs[axis]; input.calibrated = true; input.candidates = 1;
            std::snprintf(input.event, sizeof(input.event), "%s", axis == 0 ? "AXIS_AILERONS_SET" : axis == 1 ? "AXIS_ELEVATOR_SET" :
                (axis - 9) % 3 == 0 ? "THROTTLE_AXIS_SET_EX1" : (axis - 9) % 3 == 1 ? "PROP_PITCH_AXIS_SET_EX1" : "AXIS_MIXTURE_SET");
            pickup.sample(axis, 0, now); pickup.targets[axis] = .5;
        }
        pickup.target_time = now;
        context.local = context.ready = true; context.blocked = 15;
        context.ground = context.parking_brake = 1; context.observed = now;
    }
    bool start(group selected = group::mixture) { return check.start(selected, pickup, &definition_profiles[0], context, output, now); }
    void tick(bool acknowledge = true) {
        now += .05; pickup.target_time = context.observed = now;
        check.tick(pickup, &definition_profiles[0], context, output, now);
        if (acknowledge) check.confirm(output.ticket);
    }
    void finish(bool acknowledge = true) { for (unsigned i = 0; i < 100 && check.running(); ++i) tick(acknowledge); assert(!check.running()); }
};
int main() {
    for (unsigned invalid = 0; invalid < 10; ++invalid) {
        fixture f;
        if (invalid == 0) f.context.local = false;
        if (invalid == 1) f.context.ready = false;
        if (invalid == 2) f.context.parking_brake = 0;
        if (invalid == 3) f.context.knots = .2;
        if (invalid == 4) f.context.ap = 1;
        if (invalid == 5) f.context.automatic = 14;
        if (invalid == 6) f.context.blocked = 0;
        if (invalid == 7) f.context.observed = 0;
        if (invalid == 8) f.pickup.inputs[11].calibrated = false;
        if (invalid == 9) f.pickup.target_time = 0;
        assert(!f.start() && f.output.writes == 0);
    }
    {
        fixture f; assert(!f.start(group::quadrant));
        for (const auto group : {group::steering, group::throttle, group::propeller, group::mixture}) {
            assert(f.start(group)); f.finish();
            assert(f.check.phase == output_check_phase::passed && f.check.restored);
        }
        assert(f.check.valid_passes(f.pickup, &definition_profiles[0]) == 15);
        assert(f.output.writes == 12 && f.pickup.targets[11] == .5);
        f.pickup.inputs[11].candidates = 32;
        assert(f.check.valid_passes(f.pickup, &definition_profiles[0]) == 15); // Absolute definition output is independent of hardware scale.
        uint64_t saved[4]{}; f.check.copy_proofs(saved);
        f.check.reset(); f.check.restore_proofs(saved);
        for (auto& input : f.pickup.inputs) { input.detected = false; input.observed = -1; }
        assert(f.check.valid_passes(f.pickup, &definition_profiles[0]) == 15 && f.pickup.input_position(0) < 0);
        auto edited = definition_profiles[0]; edited.adapter = "unrelated-definition-or-UI-update";
        assert(f.check.valid_passes(f.pickup, &edited) == 15);
        definition_axis mappings[sizeof(definition_axes_0) / sizeof(definition_axis)]{}; std::memcpy(mappings, definition_axes_0, sizeof(mappings)); edited.axes = mappings;
        for (auto& item : mappings) if (item.axis == 11) item.write = "L:ChangedMixtureOutput";
        assert(f.check.valid_passes(f.pickup, &edited) == 7); // Only the changed output group loses validation.
        f.check.restore_proofs(saved); std::memcpy(mappings, definition_axes_0, sizeof(mappings));
        for (auto& item : mappings) if (item.axis == 10) item.read_units = "Number";
        assert(f.check.valid_passes(f.pickup, &edited) == 11); // Reference/units are also part of validation.
        f.check.restore_proofs(saved); f.pickup.inputs[0].candidates = 32;
        assert(f.check.valid_passes(f.pickup, &definition_profiles[0]) == 14); // Fallback output does depend on its learned scale.
        f.check.restore_proofs(saved); f.pickup.inputs[0].candidates = 1;
        f.pickup.inputs[2].detected = true;
        assert(f.check.valid_passes(f.pickup, &definition_profiles[0]) == 14); // Newly discovered steering output needs validation.
        f.check.restore_proofs(saved); edited.aircraft = "another-aircraft";
        assert(f.check.valid_passes(f.pickup, &edited) == 0);
        f.check.restore_proofs(saved); f.pickup.definitions = &definition_profiles[0];
        assert(f.check.valid_passes(f.pickup, &definition_profiles[0]) == 14); // New rudder/brake routes retire only Steering; stored engine proofs survive.
        f.check.reset(); assert(!f.check.passed);
    }
    {
        fixture f; f.pickup.definitions = &definition_profiles[0];
        for (const auto g : {group::steering, group::throttle, group::propeller, group::mixture}) { assert(f.start(g)); f.finish(); }
        uint64_t saved[4]{}; f.check.copy_proofs(saved);
        assert(f.check.valid_passes(f.pickup, f.pickup.definitions) == 15);
        f.pickup.observe("AXIS_LEFT_BRAKE_SET", 0, f.now);
        assert(f.check.valid_passes(f.pickup, f.pickup.definitions) == 15); // New hardware cannot invalidate a verified output route.
        f.pickup.output_response_axes = 1u << 4;
        assert(f.check.valid_passes(f.pickup, f.pickup.definitions) == 14);
        assert(f.start(group::steering)); f.finish();
        assert(f.check.valid_passes(f.pickup, f.pickup.definitions) == 15);
        f.check.restore_proofs(saved); f.pickup.inputs[4] = pickup_axis{};
        f.pickup.output_response_axes = 0;
        assert(f.check.valid_passes(f.pickup, f.pickup.definitions) == 15); // Existing button result still matches.
    }
    {
        fixture f; f.output.ignored = true; assert(f.start()); f.finish();
        assert(f.check.phase == output_check_phase::failed && f.check.restored && !f.check.passed && f.output.writes == 3);
    }
    {
        fixture f; f.output.no_restore = true; assert(f.start()); f.finish();
        assert(f.check.phase == output_check_phase::failed && !f.check.restored && !f.check.passed);
    }
    {
        fixture f; assert(f.start()); f.finish(false);
        assert(f.check.phase == output_check_phase::failed && !f.check.restored && !f.check.passed); // Matching readout without SDK acknowledgement is insufficient.
    }
    {
        fixture f; assert(f.start()); while (f.output.writes < 2) f.tick();
        f.pickup.inputs[11].raw += 600; f.finish();
        assert(f.check.phase == output_check_phase::failed && f.check.restored && !f.check.passed);
    }
    {
        fixture f; assert(f.start()); while (f.output.writes < 2) f.tick();
        f.context.blocked = 0; const auto writes = f.output.writes; f.tick();
        assert(f.check.phase == output_check_phase::failed && f.output.writes == writes); // Never write after local takeover.
    }
    {
        fixture f; assert(f.start()); f.tick(); f.check.cancel(); const auto writes = f.output.writes; f.tick();
        assert(f.check.phase == output_check_phase::failed && f.output.writes == writes && !f.check.passed);
    }
    std::cout << "Parked output checks passed: Mock-only guarded movement/restoration, SDK acknowledgement, no writes after takeover, restored validation without raw positions, and per-route invalidation independent of UI/unrelated definitions. No simulator access.\n";
}
