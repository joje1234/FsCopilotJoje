// Production native session, AP channel and SDK mechanics. Input interception
// and all simulator calls are simulated; no Baron routes are certified here.
#define FSC_SESSION_FIXTURE_ONLY
#define _CRT_SECURE_NO_WARNINGS
#include <fstream>
#include "session_test.cpp"
namespace {
struct verified_guidance_input : guidance_input_gate {
    guidance_input_proof current{};
    unsigned holds = 0, releases = 0, requests = 0;
    bool asynchronous = false;
    verified_guidance_input() { current.verified_routes = 8191; current.single_host_engine = true; std::snprintf(current.adapter, sizeof(current.adapter), "test-absolute-ap-v1"); }
    guidance_input_proof proof() const override { return current; }
    bool request(bool capture, bool, uint64_t command_epoch, uint64_t navigation_epoch, uint64_t preparation, double) override {
        current.command_epoch = command_epoch; current.navigation_epoch = navigation_epoch; current.preparation = preparation;
        ++requests; current.capture = capture; current.blocked = 8191; current.ready = !asynchronous; return true;
    }
    void hold(double) override { ++holds; current.capture = false; current.blocked = 8191; current.ready = true; }
    bool release(double) override { ++releases; current.capture = false; current.blocked = 0; current.ready = true; return true; }
};
guidance_packet command_for(const session_packet& main, uint64_t command = 1) {
    guidance_packet p{};
    std::memcpy(p.native, main.native, sizeof(p.native)); std::memcpy(p.backend, main.backend, sizeof(p.backend));
    std::memcpy(p.session, main.session, sizeof(p.session)); std::memcpy(p.consent, main.consent, sizeof(p.consent));
    p.epoch = main.epoch; p.control_revision = main.revision; p.command = command;
    p.command_epoch = p.navigation_epoch = p.preparation = p.revision = 1;
    p.host = (main.flags & session_host) ? 1u : 0; return p;
}
struct ap_fixture {
    fixture flight;
    control_guidance output;
    verified_guidance_input input;
    control_session<decltype(gate), decltype(remote_writer)> native{pickup, gate, remote_writer, nullptr, nullptr, nullptr, &output, &input};
    uint64_t next = 0, sequence = 0;
    double heading = 90;
    uint32_t switches = 0;
    ap_fixture() {
        native.initialize(session); assert(native.arm(consent, flight.now));
        const auto armed = native.status(); std::memcpy(flight.authority.consent, armed.consent, sizeof(armed.consent));
        auto p = flight.command(session_kind::bind); p.flags = session_host; assert(native.receive(p, flight.now));
        assert(gate.confirm(gate.barrier, flight.now)); native.tick(flight.now); assert(native.status().phase == session_phase::active);
        assert(output.initialize(connection) && output.confirm(fake_sim::request_id)); read();
    }
    void read() {
        flight.now += .01;
        std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * control_guidance::value_count);
        auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
        data->dwRequestID = control_guidance::read_request; data->dwDefineCount = control_guidance::value_count;
        double values[control_guidance::value_count]{};
        for (unsigned index = 0; index < 13; ++index) values[index] = (switches & (1u << index)) ? 1 : 0;
        values[13] = heading; values[14] = 5000; values[15] = 500; values[16] = 1; values[17] = 1;
        values[18] = values[19] = values[20] = 1; std::memcpy(&data->dwData, values, sizeof(values));
        assert(output.observe(data, static_cast<DWORD>(bytes.size()), flight.now));
    }
    guidance_packet command(guidance_action action = guidance_action::gate, guidance_setting setting = guidance_setting::master, double value = 0) {
        auto p = command_for(native.status(), ++next); p.action = action; p.setting = setting; p.value = value; p.read_serial = output.serial;
        if (action == guidance_action::apply || action == guidance_action::observe) p.sequence = ++sequence;
        assert(guidance_frame_valid(p)); return p;
    }
    guidance_packet status() { auto p = native.guidance_status(flight.now); assert(guidance_frame_valid(p)); return p; }
    void samples(unsigned count = 3) { for (unsigned index = 0; index < count; ++index) { read(); native.tick(flight.now); } }
    void establish(bool capture = false) {
        auto p = command(); p.capture = capture ? 1u : 0;
        assert(native.receive_guidance(p, flight.now)); native.tick(flight.now); samples();
        assert(status().flags & guidance_gate_ready);
    }
};
void reciprocal() {
    guidance_packet command{}, status{};
    std::ifstream first("artifacts/guidance-channel/tests/guidance-command-from-csharp.bin", std::ios::binary);
    std::ifstream second("artifacts/guidance-channel/tests/guidance-status-from-csharp.bin", std::ios::binary);
    assert(first.read(reinterpret_cast<char*>(&command), sizeof(command)) && first.peek() == EOF);
    assert(second.read(reinterpret_cast<char*>(&status), sizeof(status)) && second.peek() == EOF);
    assert(guidance_frame_valid(command) && command.kind == guidance_kind::command && command.action == guidance_action::apply);
    assert(command.setting == guidance_setting::master && command.value == 0 && command.steering_generation == 7 && command.steering_owner == 2);
    assert(guidance_frame_valid(status) && status.kind == guidance_kind::status && (status.flags & guidance_navigation_ready));
    assert(status.switches == 8191 && status.heading == 359.995 && status.slots == guidance_slots(1, 2, 4));
    std::cout << "PASS reciprocal C# guidance command/status fixtures parsed by production C++ wire.\n";
}
}
int main(int argc, char** argv) {
#ifdef _WIN32
    _set_error_mode(_OUT_TO_STDERR); _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
#endif
    if (argc == 2 && !std::strcmp(argv[1], "--validate-csharp")) { reciprocal(); return 0; }
    assert(argc == 1);
    {
        ap_fixture f; f.establish();
        auto value = f.command(guidance_action::apply, guidance_setting::heading, 120);
        assert(f.native.receive_guidance(value, f.flight.now)); f.native.tick(f.flight.now);
        assert(f.output.confirm(fake_sim::request_id)); f.heading = 120; f.samples();
        const auto completed = barometer_ack(value.command, barometer_result::succeeded);
        assert(f.status().value_ack == completed);
        auto gate = f.command(); gate.revision = 2;
        assert(f.native.receive_guidance(gate, f.flight.now)); f.native.tick(f.flight.now);
        auto reference = f.command(guidance_action::reference);
        assert(!f.native.receive_guidance(reference, f.flight.now));
        assert(f.status().reference_ack == barometer_ack(reference.command, barometer_result::rejected));
        assert(f.status().value_ack == completed);
        auto input = f.command(); input.action = guidance_action::intent_ack; input.sequence = 1;
        assert(!f.native.receive_guidance(input, f.flight.now));
        assert(f.status().value_ack == completed);
        std::cout << "PASS rejected input/reference packets preserve the SDK output acknowledgement.\n";
    }
    {
        ap_fixture f; f.input.asynchronous = true;
        auto p = f.command(); assert(f.native.receive_guidance(p, f.flight.now)); f.native.tick(f.flight.now);
        f.flight.now += .3; f.read(); p = f.command();
        assert(f.native.receive_guidance(p, f.flight.now)); f.native.tick(f.flight.now);
        assert(f.input.requests == 1 && !(f.status().flags & guidance_gate_ready));
        f.input.current.ready = true; f.native.tick(f.flight.now); f.samples();
        assert(f.status().flags & guidance_gate_ready);
        f.flight.now += .3; f.read(); p = f.command();
        assert(f.native.receive_guidance(p, f.flight.now)); f.native.tick(f.flight.now);
        assert(f.input.requests == 1 && (f.status().flags & guidance_gate_ready));
        f.input.current.ready = false; f.native.tick(f.flight.now);
        assert(f.status().flags & guidance_held);
        std::cout << "PASS AP gate lease refresh preserves asynchronous interception; independent proof loss still holds AP.\n";
    }
    {
        fixture f; f.bind(); const auto before = shared_session.status(); auto p = command_for(before);
        const auto writes = fake_sim::transmissions.size();
        std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_CLIENT_DATA) + sizeof(p));
        auto* data = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(bytes.data()); data->dwID = SIMCONNECT_RECV_ID_CLIENT_DATA;
        data->dwRequestID = data->dwDefineID = control_guidance_transport::command_id; std::memcpy(&data->dwData, &p, sizeof(p));
        bridge_dispatch(data, static_cast<DWORD>(bytes.size()), nullptr);
        const auto& raw = fake_sim::client_data[control_guidance_transport::status_id]; assert(raw.size() == sizeof(guidance_packet));
        guidance_packet status{}; std::memcpy(&status, raw.data(), sizeof(status));
        assert(guidance_frame_valid(status) && !(status.flags & guidance_profile));
        assert(status.gate_ack == barometer_ack(1, barometer_result::unsupported));
        assert(shared_session.status().ack == before.ack && gate.blocked == before.blocked && fake_sim::transmissions.size() == writes);
        assert(guidance_bridge.publish(shared_session.guidance_status(f.now)));
        SIMCONNECT_RECV_EXCEPTION failure{}; failure.dwID = SIMCONNECT_RECV_ID_EXCEPTION; failure.dwSendID = fake_sim::last_packet;
        dispatch(&failure, sizeof(failure), nullptr);
        assert(!guidance_bridge.available && native_bridge.available && shared_session.status().phase == session_phase::active);
        assert(shared_session.status().ack == before.ack && gate.blocked == before.blocked);
    }
    {
        ap_fixture f; const auto before = f.native.status(); f.establish();
        auto p = f.command(); p.capture = 1; assert(f.native.receive_guidance(p, f.flight.now)); f.native.tick(f.flight.now); f.samples();
        auto status = f.status(); assert((status.flags & (guidance_gate_ready | guidance_capture)) == (guidance_gate_ready | guidance_capture));
        assert(status.supported == 4095 && status.blocked == 8191 && f.native.status().ack == before.ack);
        auto value = f.command(guidance_action::apply, guidance_setting::heading, 120);
        assert(f.native.receive_guidance(value, f.flight.now)); assert(!f.native.receive_guidance(value, f.flight.now));
        f.native.tick(f.flight.now); const auto writes = fake_sim::transmissions.size(); assert(writes == 1);
        assert(f.status().value_ack == barometer_ack(value.command, barometer_result::pending));
        assert(f.output.confirm(fake_sim::request_id)); f.heading = 120; f.samples();
        status = f.status(); assert(status.value_ack == barometer_ack(value.command, barometer_result::succeeded));
        assert(fake_sim::transmissions.size() == writes && f.native.status().ack == before.ack && gate.blocked == 15);
        std::ofstream out("artifacts/guidance-channel/tests/guidance-status-from-cpp.bin", std::ios::binary);
        assert(out.write(reinterpret_cast<const char*>(&status), sizeof(status)));
    }
    {
        ap_fixture f; f.establish();
        auto nav = f.command(guidance_action::apply, guidance_setting::nav_hold, 1);
        assert(!f.native.receive_guidance(nav, f.flight.now) && fake_sim::transmissions.empty());
        assert(f.status().value_ack == barometer_ack(nav.command, barometer_result::unsupported));
        f.input.current.navigation_ready = true; std::memset(f.input.current.navigation_fingerprint, 'a', 64);
        nav = f.command(guidance_action::apply, guidance_setting::nav_hold, 1); assert(f.native.receive_guidance(nav, f.flight.now));
        f.native.tick(f.flight.now); assert(fake_sim::transmissions.size() == 1 && (f.status().flags & guidance_navigation_ready));
    }
    {
        ap_fixture f; f.establish();
        auto off = f.command(guidance_action::apply, guidance_setting::master, 0); off.steering_generation = 1; off.steering_owner = 1;
        assert(f.native.receive_guidance(off, f.flight.now));
        ++f.flight.authority.revision; ++f.flight.authority.generation[0]; f.flight.authority.owner[0] = writer_owner::remote;
        assert(f.native.receive(f.flight.command(session_kind::state), f.flight.now)); f.native.tick(f.flight.now);
        assert(fake_sim::transmissions.empty() && (f.status().flags & guidance_held) && f.native.status().phase == session_phase::active);
    }
    {
        ap_fixture f; f.establish(); f.input.current.navigation_ready = true; std::memset(f.input.current.navigation_fingerprint, 'a', 64);
        auto nav = f.command(guidance_action::apply, guidance_setting::approach_hold, 1); assert(f.native.receive_guidance(nav, f.flight.now));
        f.input.current.navigation_ready = false; f.native.tick(f.flight.now);
        assert(fake_sim::transmissions.empty() && (f.status().flags & guidance_held) && f.native.status().phase == session_phase::active);
    }
    {
        ap_fixture f; f.establish(); auto p = f.command(guidance_action::apply, guidance_setting::heading, 120);
        ++p.control_revision; assert(!f.native.receive_guidance(p, f.flight.now)); --p.control_revision;
        p.consent[0] = p.consent[0] == 'a' ? 'b' : 'a'; assert(!f.native.receive_guidance(p, f.flight.now));
        assert(fake_sim::transmissions.empty());
    }
    {
        ap_fixture f; f.establish(); f.flight.now += 1.01; f.read();
        assert(f.native.receive(f.flight.command(session_kind::heartbeat), f.flight.now)); f.native.tick(f.flight.now);
        assert((f.status().flags & guidance_held) && f.native.status().phase == session_phase::active);
        assert(!f.native.receive_guidance(f.command(), f.flight.now)); // Backend traffic cannot restart an expired AP gate.
    }
    {
        ap_fixture f; f.establish();
        for (unsigned index = 0; index < 4; ++index) {
            f.flight.now += .35; f.read(); auto p = f.command(); assert(f.native.receive_guidance(p, f.flight.now)); f.native.tick(f.flight.now); f.samples();
        }
        f.flight.now += 6.7; f.read(); assert(!f.native.receive_guidance(f.command(), f.flight.now));
        assert(f.native.status().phase == session_phase::ended && gate.blocked == 15); // AP traffic never renews the physical lease.
    }
    {
        ap_fixture f; f.establish(); auto value = f.command(guidance_action::apply, guidance_setting::heading, 120);
        assert(f.native.receive_guidance(value, f.flight.now)); f.native.stop();
        assert(!f.native.recover_local(f.flight.now) && f.input.releases == 1 && fake_sim::transmissions.empty());
        f.input.current.verified_routes = 0; f.samples(); assert(f.native.recover_local(f.flight.now));
        const auto status = f.status(); assert((status.flags & guidance_local) && !(status.flags & (guidance_held | guidance_gate_ready)) && status.blocked == 0);
        assert(!f.native.receive_guidance(value, f.flight.now));
        assert(f.native.arm(consent, f.flight.now)); // Fresh local consent clears the latch; retained backend commands cannot.
    }
    std::cout << "Guidance channel passed: production transport and no-provider refusal; bound scopes, actual gate/capture proofs, independent SDK/readback acknowledgements, NAV source proof, queued steering-generation fence, separate leases and local recovery. SDK/input routes are simulated; no simulator access.\n";
}
