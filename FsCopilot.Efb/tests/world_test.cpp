// Reuse the production controller fixture, not a duplicate authority model.
#define FSC_SESSION_FIXTURE_ONLY
#include "session_test.cpp"

namespace {
void sample_world(fixture& f, uint32_t frozen) {
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * 15);
    auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data()); data->dwRequestID = control_world::read_request; data->dwDefineCount = 15;
    double values[15] = {0.8, 0.15, 1000, 0.02, -0.03, 2.5, 5, 2, 150, 0.1, 0.2, 0.3};
    for (unsigned index = 0; index < 3; ++index) values[12 + index] = (frozen >> index) & 1u;
    std::memcpy(&data->dwData, values, sizeof(values));
    const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&data->dwData) - reinterpret_cast<char*>(data) + sizeof(values));
    assert(world.observe(data, size, f.now)); shared_session.tick(f.now, true);
}
void bind_world(fixture& f, bool host = false, uint32_t original = 0) {
    fake_sim::object_writes.clear(); fake_sim::fail_object_write = false;
    f.arm(); const auto registration = fake_sim::request_id; sample_world(f, original);
    auto bind = f.command(session_kind::bind); bind.flags = session_world | (host ? session_host : 0u);
    assert(shared_session.receive(bind, f.now)); f.confirm();
    if (!host) {
        const auto freeze = fake_sim::request_id;
        assert(shared_session.status().phase == session_phase::binding && !world.confirm(registration));
        sample_world(f, 7); assert(shared_session.status().phase == session_phase::binding);
        assert(world.confirm(freeze)); shared_session.tick(f.now, true);
    }
    assert(shared_session.status().phase == session_phase::active && gate.blocked == 15);
}
session_packet world_command(fixture& f, uint64_t sequence = 1) {
    auto packet = f.command(session_kind::heartbeat); packet.kind = session_kind::world; packet.sequence = sequence;
    const double values[] = {0.81, 0.16, 4321, 0.05, -0.06, 2.6, 6, 3, 160, 0.2, 0.3, 0.4};
    std::memcpy(packet.world_values, values, sizeof(values));
    for (auto& position : packet.positions) position = 0.65;
    assert(session_valid(packet)); return packet;
}
}
int main() {
    {
        fixture f; bind_world(f); auto frame = world_command(f);
        for (size_t first = 9; first <= 11; ++first) { frame.positions[first] = .25; frame.positions[first+3] = .75; }
        assert(shared_session.receive(frame, f.now));
        for (size_t axis = 9; axis < 15; ++axis) {
            output_action expected{}; assert(output_for_axis(pickup, axis, frame.positions[axis], expected));
            unsigned matched = 0;
            for (const auto& sent : fake_sim::transmissions)
                if (fake_sim::names[sent.event] == expected.event) { assert(static_cast<int32_t>(sent.raw) == expected.raw); ++matched; }
            assert(matched == 1);
        }
        assert(shared_session.active() && gate.blocked == 15);
    }
    for (size_t axis = 0; axis < axis_count; ++axis) {
        fixture f; bind_world(f);
        auto frame = world_command(f); frame.positions[axis] = -1;
        const auto before = fake_sim::transmissions.size();
        assert(session_valid(frame) && shared_session.receive(frame, f.now));
        assert(fake_sim::object_writes.size() == 1 && shared_session.active() && gate.blocked == 15);
        const bool required = !!(f.authority.axes & (1u << axis));
        assert(fake_sim::transmissions.size() == before + (required ? 6 : 8));
        if (required) {
            assert(!shared_session.target_current(axis));
            const auto observed = bridge_status();
            assert(observed.targets[axis] == -1 && !(observed.aligned & (1u << axis)));
            assert(!(shared_status().aligned & (1u << axis)));
        }
        if (required) for (size_t i = before; i < fake_sim::transmissions.size(); ++i)
            assert(event_selection(events[fake_sim::transmissions[i].event-first_event]) != axis_selection(axis));
        // No stale substitution or catch-up: the next fresh complete frame resumes.
        frame = world_command(f, 2);
        const auto resumed = fake_sim::transmissions.size();
        assert(shared_session.receive(frame, f.now) && fake_sim::transmissions.size() == resumed + 8);
        assert(shared_session.target_current(axis));
        assert(shared_session.active() && world.owned());
    }
    {
        fixture f; bind_world(f);
        // Longer than both recovery deadlines: valid physics with every control
        // unavailable must keep the guest stream alive without a control write.
        for (uint64_t step = 1; step <= 14; ++step) {
            if (step > 1) f.now += .75;
            pickup.target_time = f.now; sample_world(f, 7);
            assert(shared_session.receive(f.command(session_kind::heartbeat), f.now));
            auto frame = world_command(f, step);
            for (auto& position : frame.positions) position = -1;
            const auto before = fake_sim::transmissions.size();
            assert(shared_session.receive(frame, f.now) && shared_session.active());
            assert(fake_sim::transmissions.size() == before && gate.blocked == 15 && world.owned());
        }
        assert(fake_sim::object_writes.size() == 14);
        auto bad = world_command(f, 15); bad.positions[10] = std::numeric_limits<double>::quiet_NaN();
        assert(!session_valid(bad) && !shared_session.receive(bad, f.now));
        assert(shared_session.active() && fake_sim::object_writes.size() == 14);
    }
    {
        fixture f; bind_world(f); const auto before = fake_sim::transmissions.size();
        auto frame = world_command(f); assert(shared_session.receive(frame, f.now));
        assert(fake_sim::object_writes.size() == 1 && fake_sim::transmissions.size() == before + 8);
        double written[12]{}; std::memcpy(written, fake_sim::object_writes.back().bytes.data(), sizeof(written));
        assert(written[2] == 4321 && written[4] == -0.06 && gate.blocked == 15);
        frame.command = ++f.next; assert(!shared_session.receive(frame, f.now));
        frame = world_command(f, 2); ++frame.generation[0]; assert(!shared_session.receive(frame, f.now));
        assert(fake_sim::object_writes.size() == 1);
        f.attach(select_throttle, f.authority.session);
        const auto attached = fake_sim::transmissions.size(); frame = world_command(f, 2);
        assert(shared_session.receive(frame, f.now) && fake_sim::transmissions.size() == attached + 6);
        for (size_t index = attached; index < fake_sim::transmissions.size(); ++index) {
            const auto& name = fake_sim::names[fake_sim::transmissions[index].event];
            assert(name != "AXIS_THROTTLE1_SET" && name != "AXIS_THROTTLE2_SET");
        }
        auto failed = world_command(f, 3); fake_sim::fail_object_write = true;
        assert(!shared_session.receive(failed, f.now) && shared_session.status().phase == session_phase::ended && gate.blocked == 13 && world.owned());
        assert(!shared_session.recover_local(f.now)); const auto restore = fake_sim::request_id;
        assert(gate.blocked == 13 && world.recovering());
        sample_world(f, 0); assert(!shared_session.recover_local(f.now)); // Readback alone is insufficient.
        assert(world.confirm(restore)); assert(shared_session.recover_local(f.now));
        assert(!world.owned() && gate.blocked == 13); // Physics recovery does not attach another hardware group.
        fake_sim::fail_object_write = false;
    }
    {
        fixture f; bind_world(f, true);
        assert(fake_sim::transmissions.empty() && !world.owned());
        assert(!shared_session.receive(world_command(f), f.now) && fake_sim::object_writes.empty());
    }
    {
        fixture f; bind_world(f, false, 4);
        shared_session.stop(); assert(!shared_session.arm(consent, f.now));
        assert(!shared_session.recover_local(f.now));
        const auto restore = fake_sim::request_id; sample_world(f, 4); assert(world.confirm(restore));
        assert(shared_session.recover_local(f.now) && world.frozen == 4); // Preserve the pre-existing attitude freeze.
        assert(shared_session.arm(consent, f.now)); // A fresh join cannot inherit our previous freeze as its original state.
    }
    {
        fixture f; bind_world(f);
        f.now += 0.6; pickup.target_time = f.now; sample_world(f, 7);
        assert(shared_session.receive(f.command(session_kind::heartbeat), f.now));
        f.now += 0.5; pickup.target_time = f.now; sample_world(f, 7);
        assert(shared_session.status().phase == session_phase::active && gate.blocked == 15 && world.owned());
        assert(!world.ready(f.now+1.01)); // A retained session still requires fresh local readback before any write.
        f.now += 7; pickup.target_time = f.now; sample_world(f, 7);
        assert(shared_session.status().phase == session_phase::ended && gate.blocked == 15 && world.owned());
    }
    std::cout << "Native world: fresh freeze barrier/readback, guest-only pose writes, masked cockpit mirror, replay/epoch fences, write failure, explicit recovery, prior freeze preservation and world-stream expiry passed. No actual simulator writes.\n";
}
