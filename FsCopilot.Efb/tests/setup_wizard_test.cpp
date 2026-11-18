#include <cassert>
#include <iostream>
#include <vector>
#include "../Wasm/control_setup_wizard.h"
using namespace fsc_controls;
struct mask_adapter {
    std::vector<uint32_t> masks;
    bool configure_mask(uint32_t mask, uint32_t) { masks.push_back(mask); return true; }
    void release() {}
};
struct wizard_output {
    control_pickup& pickup;
    bool available = true, ignore_movement = false;
    unsigned writes = 0; DWORD ticket = 0;
    bool write(const output_action* actions, size_t count) {
        ++writes;
        if (!(ignore_movement && writes % 3 == 2))
            for (size_t item = 0; item < count; ++item) pickup.targets[actions[item].axis] = actions[item].position;
        return true;
    }
    DWORD request_checkpoint() { return ++ticket; }
};
struct fixture {
    control_pickup pickup;
    mask_adapter adapter;
    control_runtime<mask_adapter> gate;
    wizard_output output;
    control_output_check<wizard_output> check;
    control_setup_wizard wizard;
    output_check_context c;
    double now = 1;
    unsigned saves = 0;
    bool save_ok = true, engines_off = true, move_hardware = true;
    bool offset_pedals = false;
    fixture(bool automatic = true) : gate(pickup, adapter), output{pickup} {
        pickup.engines = 2; pickup.definitions = &definition_profiles[0];
        assert(gate.initialize(true, automatic) && gate.confirm(gate.barrier, now));
        for (const auto axis : {0,1,9,10,11,12,13,14}) {
            auto& input = pickup.inputs[axis]; input.calibrated = true; input.candidates = 1;
            std::snprintf(input.event, sizeof(input.event), "%s", axis == 0 ? "AXIS_AILERONS_SET" : axis == 1 ? "AXIS_ELEVATOR_SET" :
                (axis - 9) % 3 == 0 ? "THROTTLE_AXIS_SET_EX1" : (axis - 9) % 3 == 1 ? "PROP_PITCH_AXIS_SET_EX1" : "AXIS_MIXTURE_SET");
            pickup.sample(axis, 0, now); pickup.targets[axis] = .5;
        }
        pickup.targets[2] = .5; context();
    }
    void context() {
        c.local = c.ready = true; c.applying = gate.applying; c.blocked = gate.blocked; c.automatic = gate.automatic; c.waiting = gate.waiting();
        c.ground = c.parking_brake = 1; c.observed = pickup.target_time = now;
    }
    bool start() { context(); return wizard.start(gate, pickup, check, output, pickup.definitions, c, engines_off, now); }
    void tick(bool parked = true) {
        now += .05;
        if (gate.applying) assert(gate.confirm(gate.barrier, now));
        if (move_hardware) {
            for (const auto axis : {0,1,9,10,11,12,13,14}) pickup.sample(axis, 0, now);
            if (offset_pedals) {
                pickup.observe("AXIS_RUDDER_SET", 8192, now);
                pickup.observe("AXIS_LEFT_BRAKE_SET", 0, now);
                pickup.observe("AXIS_RIGHT_BRAKE_SET", 8192, now);
            }
        }
        context(); if (!parked) c.ground = 0;
        check.confirm(output.ticket);
        check.tick(pickup, pickup.definitions, c, output, now);
        gate.tick(now); context(); if (!parked) c.ground = 0;
        wizard.tick(gate, pickup, check, output, pickup.definitions, c, engines_off, now, [&]() { ++saves; return save_ok; });
    }
    void finish() { for (unsigned i = 0; i < 1000 && wizard.active(); ++i) tick(); assert(!wizard.active()); }
    void seed(unsigned mask) {
        assert(gate.set_auto(select_quadrant, false, now));
        for (unsigned index = 0; index < 4; ++index) if (mask & (1u << index)) {
            const auto group = index == 0 ? group::steering : index == 1 ? group::throttle : index == 2 ? group::propeller : group::mixture;
            assert(gate.assign_mock(1u << index, now)); assert(gate.confirm(gate.barrier, now)); context();
            assert(check.start(group, pickup, pickup.definitions, c, output, now));
            for (unsigned i = 0; i < 100 && check.running(); ++i) tick();
            assert(check.phase == output_check_phase::passed);
            assert(gate.force_me(1u << index)); assert(gate.confirm(gate.barrier, now));
        }
    }
};
int main() {
    {
        fixture f; assert(f.start()); f.finish();
        assert(f.wizard.phase == wizard_phase::complete && f.check.passed == 15);
        for (size_t axis = 2; axis < 6; ++axis) assert(!f.pickup.inputs[axis].detected && f.pickup.required_axis(axis));
        for (size_t axis = 6; axis < steering_axis_count; ++axis) assert(!f.pickup.required_axis(axis));
    }
    for (unsigned saved = 0; saved < 16; ++saved) for (bool automatic : {false, true}) {
        fixture f(automatic); f.seed(saved); assert(f.gate.set_auto(select_quadrant, automatic, f.now));
        const auto writes = f.output.writes;
        const auto masks = f.adapter.masks.size();
        assert(f.start()); assert(f.start()); // Repeated start cannot restart an active run.
        f.finish(); assert(f.wizard.phase == wizard_phase::complete && f.check.passed == 15);
        assert(!f.gate.blocked && !!f.gate.automatic == automatic);
        assert(f.output.writes - writes == 3 * (4 - static_cast<unsigned>((saved & 1) + ((saved >> 1) & 1) + ((saved >> 2) & 1) + ((saved >> 3) & 1))));
        if (saved == 15) assert(f.adapter.masks.size() == masks);
    }
    {
        fixture f; f.offset_pedals = true;
        f.pickup.targets[2] = .25; f.pickup.targets[4] = .27; f.pickup.targets[5] = .53;
        assert(f.start()); f.finish();
        assert(f.wizard.phase == wizard_phase::complete && f.check.passed == 15 && !f.gate.blocked);
        assert(std::abs(f.pickup.targets[2] - .25) < .00001 && std::abs(f.pickup.targets[4] - .27) < .00001);
    }
    {
        fixture f; f.engines_off = false; assert(!f.start() && f.output.writes == 0 && f.gate.automatic);
        f.engines_off = true; f.context(); f.c.parking_brake = 0;
        assert(!f.wizard.start(f.gate, f.pickup, f.check, f.output, f.pickup.definitions, f.c, true, f.now));
    }
    {
        fixture f; assert(f.start()); f.move_hardware = false;
        for (unsigned i = 0; i < 40; ++i) f.tick();
        assert(f.wizard.phase == wizard_phase::input && f.wizard.needed_axes && f.output.writes == 0);
        f.move_hardware = true; f.finish(); assert(f.wizard.phase == wizard_phase::complete);
    }
    for (auto cancel_at : {wizard_phase::prepare, wizard_phase::input, wizard_phase::mask, wizard_phase::check, wizard_phase::takeover}) {
        fixture f; assert(f.start());
        for (unsigned i = 0; i < 200 && f.wizard.phase != cancel_at; ++i) f.tick();
        assert(f.wizard.phase == cancel_at);
        if (cancel_at == wizard_phase::check) { f.tick(); assert(f.output.writes); }
        f.wizard.cancel(f.check, f.now); f.finish();
        assert(f.wizard.phase == wizard_phase::cancelled && !f.gate.blocked && f.gate.automatic);
        assert(std::abs(f.pickup.targets[0] - .5) < .000001 && f.pickup.targets[4] == 0);
    }
    {
        fixture f; f.output.ignore_movement = true; assert(f.start()); f.finish();
        assert(f.wizard.phase == wizard_phase::failed && f.check.restored && !f.gate.blocked && !f.gate.automatic);
        assert(f.wizard.failed_axis < axis_count && !f.check.passed);
    }
    {
        fixture f; f.save_ok = false; assert(f.start()); f.finish();
        assert(f.wizard.phase == wizard_phase::failed && !f.output.writes && !f.gate.automatic);
    }
    {
        fixture f; assert(f.start()); f.tick(false);
        assert(f.wizard.phase == wizard_phase::failed && !f.output.writes && !f.gate.automatic);
    }
    std::cout << "Setup wizard passed: all 16 cached-result sets with Auto on/off, hardware prompts, scoped masked checks, saved results, aligned Me, cancellation including restoration, output/save/context failures and duplicate Start. SDK simulated; no simulator access.\n";
}
