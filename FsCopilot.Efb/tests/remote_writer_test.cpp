// Actual WASM adapter/executor, with SDK writes captured by the contract fake.
// This runner never connects to MSFS or changes actual aircraft controls.
#define _CRT_SECURE_NO_WARNINGS
#define FSC_PROFILE_ROOT "artifacts/efb-control-test/tests/"
#include <cassert>
#include <iostream>
#include <limits>
#include "../Wasm/control_test.cpp"

namespace {
void confirm_mask() {
    SIMCONNECT_RECV_SYSTEM_STATE reply{}; reply.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
    reply.dwRequestID = first_barrier + gate.barrier; dispatch(&reply, sizeof(reply), nullptr);
}
struct fixture {
    writer_binding binding{};
    writer_state state{};
    double now = 0;
    fixture() {
        fake_sim::fail_configuration = false; fake_sim::fail_transmission_after = -1;
        fake_sim::transmissions.clear();
        adapter.groups_created = false; error[0] = 0;
        module_init();
        SIMCONNECT_RECV opened{}; opened.dwID = SIMCONNECT_RECV_ID_OPEN; dispatch(&opened, sizeof(opened), nullptr);
        const char* read = "5|writer-test|1|read"; receive_command(read, static_cast<unsigned>(std::strlen(read)), nullptr);
        confirm_mask();
        pickup.engines = 2; now = seconds(); pickup.target_time = now;
        const char* names[] = {"AXIS_AILERONS_SET", "AXIS_ELEVATOR_SET", "AXIS_RUDDER_SET", nullptr, nullptr, nullptr, nullptr, nullptr, nullptr,
            "THROTTLE_AXIS_SET_EX1", "PROP_PITCH_AXIS_SET_EX1", "AXIS_MIXTURE_SET", "THROTTLE_AXIS_SET_EX1", "PROP_PITCH_AXIS_SET_EX1", "AXIS_MIXTURE_SET"};
        for (size_t axis = 0; axis < 15; ++axis) if (names[axis]) {
            pickup.inputs[axis].calibrated = true; pickup.inputs[axis].candidates = axis < 2 ? 32 : 1;
            pickup.inputs[axis].detected = axis < 3;
            std::snprintf(pickup.inputs[axis].event, sizeof(pickup.inputs[axis].event), "%s", names[axis]);
        }
        assert(gate.assign_mock(select_all, now)); confirm_mask();
        assert(gate.set_advanced(true));
        std::snprintf(binding.native, sizeof(binding.native), "%s", session);
        std::snprintf(binding.backend, sizeof(binding.backend), "%s", "0123456789abcdef0123456789abcdef");
        std::snprintf(binding.session, sizeof(binding.session), "%s", "fedcba9876543210fedcba9876543210"); binding.epoch = 17;
        state.revision = 1; state.engines = 2;
        for (size_t axis = 0; axis < axis_count; ++axis) if (pickup.required_axis(axis)) state.axes |= 1u << axis;
        for (unsigned index = 0; index < 4; ++index) { state.generation[index] = 1; state.owner[index] = writer_owner::local; }
    }
    ~fixture() { module_deinit(); }
    void begin() { assert(remote_writer.begin(binding, state, now)); }
    void grant(uint32_t selection) {
        ++state.revision; state.attached |= selection;
        for (unsigned index = 0; index < 4; ++index) if (selection & (1u << index)) {
            ++state.generation[index]; state.owner[index] = writer_owner::remote;
        }
        assert(remote_writer.update(binding, state, now));
    }
    writer_frame frame(group target, uint64_t sequence = 1) const {
        writer_frame result{}; result.binding = binding; result.revision = state.revision; result.target = target;
        const auto index = selection_index(group_selection(target));
        result.generation = index < 4 ? state.generation[index] : 1; result.sequence = sequence;
        for (size_t axis = 0; axis < axis_count; ++axis) {
            result.positions[axis] = axis < 12 ? 0.25 : 0.75;
            if ((state.axes & (1u << axis)) && control_pickup::matches_group(axis, target)) result.axes |= 1u << axis;
        }
        return result;
    }
};
}

int main() {
    {
        fixture f;
        assert(remote_output.select_profile(&definition_profiles[0]));
        fake_vars::state() = fake_vars::registry{};
        f.begin();
        assert(!remote_writer.apply(f.frame(group::mixture), f.now) && !fake_vars::state().sets);
        f.grant(select_mixture);
        const auto frame = f.frame(group::mixture);
        assert(remote_writer.apply(frame, f.now));
        assert(fake_vars::state().sets == 2 && fake_vars::state().value[0] == 25 && fake_vars::state().value[1] == 75);
        assert(fake_sim::transmissions.empty());
        assert(!remote_writer.apply(frame, f.now) && fake_vars::state().sets == 2);
        auto stale = f.frame(group::mixture, 2); --stale.generation;
        assert(!remote_writer.apply(stale, f.now) && fake_vars::state().sets == 2);
        remote_writer.end();
        assert(!remote_writer.apply(f.frame(group::mixture, 3), f.now) && fake_vars::state().sets == 2);
    }
    {
        fixture f;
        assert(!remote_writer.apply(f.frame(group::throttle), f.now));
        auto bad = f.binding; bad.native[0] = bad.native[0] == 'a' ? 'b' : 'a';
        assert(!remote_writer.begin(bad, f.state, f.now));
        assert(gate.force_me(select_steering)); confirm_mask();
        assert(!remote_writer.begin(f.binding, f.state, f.now)); // No bind before all native masks are confirmed.
        assert(gate.assign_mock(select_all, f.now)); confirm_mask();
        auto attached = f.state; attached.attached = select_throttle;
        assert(!remote_writer.begin(f.binding, attached, f.now));
        f.begin(); assert(!remote_writer.begin(f.binding, f.state, f.now));
        assert(fake_sim::transmissions.empty());
    }
    {
        fixture f; f.begin(); f.grant(select_quadrant);
        for (const auto target : {group::throttle, group::propeller, group::mixture}) {
            const auto start = fake_sim::transmissions.size();
            assert(remote_writer.apply(f.frame(target), f.now));
            assert(fake_sim::transmissions.size() == start + 2);
            const auto& first = fake_sim::transmissions[start]; const auto& second = fake_sim::transmissions[start + 1];
            assert(first.raw != second.raw && first.event != second.event);
            for (const auto* call : {&first, &second}) {
                assert(call->priority == SIMCONNECT_GROUP_PRIORITY_DEFAULT && call->flags == SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY);
                for (const auto value : call->extra) assert(!value);
                assert(event_selection(events[call->event - first_event]) == group_selection(target));
            }
            assert(!remote_writer.apply(f.frame(target), f.now)); // At most once per pair, not a global sequence.
        }
        assert(!remote_writer.apply(f.frame(group::quadrant), f.now));
        assert(!remote_writer.apply(f.frame(group::steering), f.now)); // Still locally owned.
        assert(fake_sim::transmissions.size() == 6);
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        const auto good = f.frame(group::throttle);
        for (unsigned mutation = 0; mutation < 10; ++mutation) {
            auto bad = good;
            switch (mutation) {
            case 0: ++bad.binding.epoch; break;
            case 1: bad.binding.backend[0] = 'a'; break;
            case 2: bad.binding.session[0] = 'a'; break;
            case 3: bad.binding.native[0] = 'x'; break;
            case 4: --bad.revision; break;
            case 5: --bad.generation; break;
            case 6: bad.sequence = 0; break;
            case 7: bad.axes &= ~(1u << 12); break;
            case 8: bad.axes |= 1u << 10; break;
            case 9: bad.positions[12] = std::numeric_limits<double>::quiet_NaN(); break;
            }
            assert(!remote_writer.apply(bad, f.now)); assert(fake_sim::transmissions.empty());
        }
        pickup.inputs[12].calibrated = false;
        assert(!remote_writer.apply(good, f.now) && fake_sim::transmissions.empty());
        pickup.inputs[12].calibrated = true;
        assert(remote_writer.apply(good, f.now)); // Rejected frames never consume a valid sequence.
    }
    {
        fixture f; f.begin(); f.grant(select_quadrant);
        auto throttle = f.frame(group::throttle);
        assert(remote_writer.apply(throttle, f.now));
        ++f.state.revision; f.state.attached &= ~select_throttle; f.state.pending = select_throttle;
        assert(remote_writer.update(f.binding, f.state, f.now));
        assert(!remote_writer.apply(throttle, f.now)); // Queued values invalidated even before the owner changes.
        assert(!remote_writer.apply(f.frame(group::throttle, 2), f.now));
        assert(remote_writer.apply(f.frame(group::mixture), f.now));
        ++f.state.revision; ++f.state.generation[1]; f.state.owner[1] = writer_owner::local;
        f.state.pending = 0; f.state.attached |= select_throttle;
        assert(remote_writer.update(f.binding, f.state, f.now));
        assert(!remote_writer.apply(f.frame(group::throttle, 2), f.now));
        f.grant(select_throttle);
        assert(remote_writer.apply(f.frame(group::throttle), f.now)); // Fresh owner generation may restart its sequence.
        assert(!remote_writer.apply(f.frame(group::mixture), f.now)); // Unchanged pair cannot replay across other transfers.
    }
    {
        fixture f; f.begin(); f.grant(select_steering | select_throttle);
        assert(gate.force_me(select_steering)); // Simulate a native reconfiguration between acceptance and execution.
        assert(!remote_writer.apply(f.frame(group::throttle), f.now));
        confirm_mask();
        assert(!remote_writer.apply(f.frame(group::steering), f.now)); // Actual mask is authoritative.
        assert(remote_writer.apply(f.frame(group::throttle), f.now));
        pickup.target_time = f.now - 1.01;
        assert(!remote_writer.apply(f.frame(group::throttle, 2), f.now));
        pickup.target_time = f.now;
        assert(remote_writer.apply(f.frame(group::throttle, 2), f.now));
    }
    {
        fixture f; assert(gate.set_auto(select_quadrant, true, f.now)); f.begin(); f.grant(select_throttle);
        const auto held = gate.blocked;
        const char* force = "5|writer-test|2|force|throttle|me";
        receive_command(force, static_cast<unsigned>(std::strlen(force)), nullptr);
        assert(!gate.applying && gate.blocked == held); // EFB cannot bypass live session ownership.
        remote_writer.tick(f.now + 2);
        assert(remote_writer.active()); // Retain authority during a short gap; stale readback cannot authorize output.
        assert(!remote_writer.apply(f.frame(group::throttle), f.now + 2) && fake_sim::transmissions.empty());
        remote_writer.tick(f.now + 7.9); assert(remote_writer.active());
        remote_writer.tick(f.now + 8);
        assert(!remote_writer.active() && remote_writer.holding() && remote_writer.fault() == writer_fault::lease_expired);
        Update_StandAlone(0);
        assert(gate.blocked == held && !gate.applying && fake_sim::transmissions.empty());
        assert(!remote_writer.update(f.binding, f.state, f.now + 8)); // No stale heartbeat can revive a closed lease.
        receive_command(force, static_cast<unsigned>(std::strlen(force)), nullptr);
        assert(gate.applying && !gate.automatic && !remote_writer.holding()); confirm_mask();
        assert(gate.blocked == (held & ~select_throttle)); // Explicit recovery affects only the chosen group.
        assert(!remote_writer.begin(f.binding, f.state, f.now));
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        assert(remote_writer.update(f.binding, f.state, f.now + 1.9)); // Exact snapshot heartbeat renews the lease.
        pickup.target_time = f.now + 2.1;
        assert(remote_writer.apply(f.frame(group::throttle), f.now + 2.1));
        auto conflict = f.state; conflict.owner[1] = writer_owner::local;
        assert(!remote_writer.update(f.binding, conflict, f.now + 2.2));
        assert(remote_writer.fault() == writer_fault::authority_invalid && !remote_writer.active());
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        ++f.state.revision; f.state.owner[1] = writer_owner::local; // Owner changes must advance its generation.
        assert(!remote_writer.update(f.binding, f.state, f.now));
        assert(remote_writer.fault() == writer_fault::authority_invalid);
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        pickup.inputs[4].detected = true; remote_writer.tick(f.now);
        assert(remote_writer.fault() == writer_fault::schema_changed && remote_writer.holding());
        assert(!remote_writer.apply(f.frame(group::throttle), f.now) && fake_sim::transmissions.empty());
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        fake_sim::fail_transmission_after = 1;
        assert(!remote_writer.apply(f.frame(group::throttle), f.now)); // SDK may fail after the first engine.
        assert(fake_sim::transmissions.size() == 1 && remote_writer.fault() == writer_fault::write_failed);
        assert(!remote_writer.apply(f.frame(group::throttle), f.now));
        assert(connection && gate.blocked == select_all && fake_sim::transmissions.size() == 1);
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        assert(remote_writer.apply(f.frame(group::throttle), f.now));
        const auto closes = fake_sim::closes;
        SIMCONNECT_RECV_EXCEPTION error{}; error.dwID = SIMCONNECT_RECV_ID_EXCEPTION; error.dwException = 3;
        error.dwSendID = fake_sim::transmissions.back().packet; dispatch(&error, sizeof(error), nullptr);
        assert(!remote_output.available && !remote_writer.active() && remote_writer.holding());
        assert(connection && gate.ready && gate.blocked == select_all && fake_sim::closes == closes);
        assert(!remote_writer.apply(f.frame(group::throttle, 2), f.now));
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        unsigned checkpoints = 0; const auto mask_barrier = gate.barrier;
        for (uint64_t sequence = 1; sequence <= 300; ++sequence) {
            assert(remote_writer.apply(f.frame(group::throttle, sequence), f.now));
            if (fake_sim::request_id >= 0x75000000u) {
                SIMCONNECT_RECV_SYSTEM_STATE reply{}; reply.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE;
                reply.dwRequestID = fake_sim::request_id; dispatch(&reply, sizeof(reply), nullptr); ++checkpoints;
            }
        }
        assert(checkpoints && remote_output.available && gate.barrier == mask_barrier && gate.blocked == select_all);
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        uint64_t sequence = 1;
        while (remote_writer.apply(f.frame(group::throttle, sequence++), f.now)) assert(sequence < 200);
        assert(!remote_writer.active() && !remote_output.available && fake_sim::transmissions.size() < 256);
        assert(connection && gate.ready && gate.blocked == select_all); // Missing checkpoints cannot discard pending error IDs.
    }
    {
        fixture f; f.begin(); f.grant(select_steering | select_throttle);
        pickup.autopilot = true;
        assert(!remote_writer.apply(f.frame(group::steering), f.now) && fake_sim::transmissions.empty());
        assert(remote_writer.apply(f.frame(group::throttle), f.now));
        pickup.autopilot = false;
        assert(remote_writer.apply(f.frame(group::steering), f.now)); // AP interlock does not consume an otherwise valid sequence.
    }
    for (unsigned engines = 1; engines <= 4; ++engines) {
        fixture f; pickup.engines = f.state.engines = engines; f.state.axes = 7;
        const char* names[] = {"THROTTLE_AXIS_SET_EX1", "PROP_PITCH_AXIS_SET_EX1", "AXIS_MIXTURE_SET"};
        for (size_t axis = steering_axis_count; axis < steering_axis_count + 3 * engines; ++axis) {
            f.state.axes |= 1u << axis;
            pickup.inputs[axis].calibrated = true; pickup.inputs[axis].candidates = 1;
            std::snprintf(pickup.inputs[axis].event, sizeof(pickup.inputs[axis].event), "%s", names[(axis - steering_axis_count) % 3]);
        }
        f.begin(); f.grant(select_quadrant);
        for (const auto target : {group::throttle, group::propeller, group::mixture}) assert(remote_writer.apply(f.frame(target), f.now));
        assert(fake_sim::transmissions.size() == engines * 3);
    }
    {
        fixture f; f.begin(); f.grant(select_throttle);
        const auto queued = f.frame(group::throttle);
        SIMCONNECT_RECV_EVENT_FILENAME changed{}; changed.dwID = SIMCONNECT_RECV_ID_EVENT_FILENAME;
        changed.uEventID = aircraft_loaded; dispatch(&changed, sizeof(changed), nullptr);
        assert(!remote_writer.active() && !remote_writer.apply(queued, f.now) && fake_sim::transmissions.empty());
    }
    std::cout << "Remote writer: actual SDK adapter, independent pairs for 1-4 engines, binding/revision/generation/sequence fences, complete frames, confirmed masks, pending transfers, lease recovery, aircraft/schema changes, partial/async errors and bounded checkpoints passed. No actual simulator writes.\n";
}
