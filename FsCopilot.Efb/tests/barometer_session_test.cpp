// Production session + SDK adapter; all input proofs and simulator calls are
// simulated. This does not verify the Baron's custom rotary input routes.
#define FSC_SESSION_FIXTURE_ONLY
#define _CRT_SECURE_NO_WARNINGS
#include <fstream>
#include "session_test.cpp"
namespace {
struct verified_input : altimeter_input_gate {
    altimeter_input_proof current{0, 0, 0, 3, false};
    uint32_t requested = 0, holds = 0;
    altimeter_input_proof proof() const override { return current; }
    bool request(uint32_t mask, uint64_t epoch, uint64_t preparation, double) override {
        requested = mask; current.epoch = epoch; current.preparation = preparation; current.ready = false; return true;
    }
    void hold(double) override { ++holds; current.blocked = 3; current.ready = true; }
    void confirm() { current.blocked = requested; current.ready = true; }
};
struct instrument_fixture {
    fixture flight;
    verified_input input;
    control_session<decltype(gate), decltype(remote_writer)> native{pickup, gate, remote_writer, nullptr, &altimeters, &input};
    instrument_fixture() {
        native.initialize(session); assert(native.arm(consent, flight.now));
        const auto armed = native.status(); std::memcpy(flight.authority.consent, armed.consent, sizeof(armed.consent));
        auto p = flight.command(session_kind::bind); p.flags = session_host; assert(native.receive(p, flight.now));
        assert(gate.confirm(gate.barrier, flight.now)); native.tick(flight.now);
        assert(native.status().phase == session_phase::active);
        altimeters = control_altimeters{}; assert(altimeters.initialize(connection) && altimeters.confirm(fake_sim::request_id));
        read();
        auto feedback = flight.command(session_kind::feedback); feedback.flags = 0; feedback.seat_revision = 1; feedback.seat = 1;
        assert(native.receive(feedback, flight.now));
    }
    void read() {
        flight.now += .01;
        std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * 4);
        auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
        data->dwRequestID = control_altimeters::read_request; data->dwDefineCount = 4;
        const double values[] = {1000, 0, 1010, 0}; std::memcpy(&data->dwData, values, sizeof(values));
        const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&data->dwData) - reinterpret_cast<char*>(data) + sizeof(values));
        assert(altimeters.observe(data, size, flight.now));
    }
    session_packet command(barometer_action action, uint32_t index = 0) {
        auto packet = flight.command(session_kind::heartbeat); packet.kind = session_kind::barometer; packet.axes = packet.flags = 0;
        for (auto& value : packet.positions) value = -1;
        session_barometer body{}; body.epoch = body.preparation = body.revision = body.seat_revision = body.seat = 1;
        body.read_serial = altimeters.serial; body.action = action; body.instrument = index;
        body.pressure16 = index == 1 ? 16000u : index == 2 ? 16160u : 0u;
        if (action == barometer_action::apply || action == barometer_action::observe) body.sequence = 1;
        if (action == barometer_action::gate) body.mask = 3;
        barometer_payload(packet, body); assert(session_valid(packet)); return packet;
    }
    void mask_both() {
        auto packet = command(barometer_action::gate); const auto before = native.status();
        assert(native.receive(packet, flight.now));
        assert(native.status().ack == before.ack && native.status().barometer_gate_ack == barometer_ack(packet.command, barometer_result::pending));
        input.confirm(); native.tick(flight.now); read(); native.tick(flight.now);
        assert(native.status().barometer_gate_ack == barometer_ack(packet.command, barometer_result::succeeded));
        assert((native.barometer_flags(flight.now) & 63) == 63); // Fresh/transport/profile/gates + both blocked.
    }
};
}
int main() {
#ifdef _WIN32
    _set_error_mode(_OUT_TO_STDERR); _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
#endif
    {
        instrument_fixture f; f.mask_both(); const auto flight_ack = f.native.status().ack;
        for (uint32_t index = 1; index <= 2; ++index) {
            auto packet = f.command(barometer_action::seed, index); assert(f.native.receive(packet, f.flight.now));
            f.native.tick(f.flight.now);
            for (unsigned i = 0; i < 3; ++i) { f.read(); f.native.tick(f.flight.now); }
            const auto status = f.native.status();
            assert(session_valid(status) && status.ack == flight_ack && status.barometer_value_ack == barometer_ack(packet.command, barometer_result::succeeded));
            if (index == 2) {
                std::ofstream file("artifacts/altimeter-native-command/tests/barometer-status-from-cpp.bin", std::ios::binary);
                assert(file.write(reinterpret_cast<const char*>(&status), sizeof(status)));
                const auto observation = bridge_status();
                assert(bridge_valid(observation) && (observation.baro_flags & ~513u) == 2); // Production has transport, no verified profile.
                std::ofstream observed("artifacts/altimeter-native-command/tests/barometer-observation-from-cpp.bin", std::ios::binary);
                assert(observed.write(reinterpret_cast<const char*>(&observation), sizeof(observation)));
            }
        }
        auto packet = f.command(barometer_action::gate); auto body = barometer_payload(packet); body.mask = 2; barometer_payload(packet, body);
        assert(f.native.receive(packet, f.flight.now)); f.input.confirm(); f.native.tick(f.flight.now); f.read(); f.native.tick(f.flight.now);
        assert((f.native.barometer_flags(f.flight.now) & 63) == 47); // Only other seat's input remains blocked.
        auto stale = f.command(barometer_action::observe, 1); body = barometer_payload(stale); body.sequence = 1; body.seat_revision = 2;
        barometer_payload(stale, body); assert(!f.native.receive(stale, f.flight.now));
        assert(f.native.status().ack == flight_ack && f.native.status().barometer_value_ack == barometer_ack(stale.command, barometer_result::rejected));
        auto retired = f.command(barometer_action::gate); retired.consent[0] = retired.consent[0] == 'a' ? 'b' : 'a';
        const auto previous = f.native.status().barometer_gate_ack;
        assert(!f.native.receive(retired, f.flight.now) && f.native.status().barometer_gate_ack == previous);
        assert(fake_sim::transmissions.empty() && gate.blocked == 15);
        f.native.stop(); assert(f.input.holds == 1 && !f.native.arm(consent, f.flight.now));
        assert(!f.native.recover_local(f.flight.now)); f.input.confirm();
        assert(!f.native.recover_local(f.flight.now)); // Gate-ready alone is not a new pressure sample.
        f.read(); assert(f.native.recover_local(f.flight.now));
        assert(f.input.current.blocked == 0 && f.native.arm(consent, f.flight.now));
    }
    {
        instrument_fixture f; f.mask_both();
        f.flight.now += 1.5; f.read();
        auto packet = f.command(barometer_action::gate); assert(f.native.receive(packet, f.flight.now));
        f.flight.now += 6.51; f.native.tick(f.flight.now);
        assert(f.native.status().phase == session_phase::ended && f.native.status().code == session_error::expired && f.input.holds == 1);
        assert(fake_sim::transmissions.empty());
    }
    {
        instrument_fixture f; f.mask_both(); const auto before = f.native.status();
        assert(!f.native.use_local_instruments("00000000000000000000000000000000", f.flight.now));
        assert(f.native.use_local_instruments(before.session, f.flight.now));
        assert((f.native.barometer_flags(f.flight.now) & 448) == 448);
        f.input.confirm(); f.read(); f.native.tick(f.flight.now);
        const auto flags = f.native.barometer_flags(f.flight.now);
        assert((flags & 64) && (flags & 512) && !(flags & 432));
        const auto after = f.native.status();
        assert(after.phase == session_phase::active && after.ack == before.ack && after.blocked == before.blocked && gate.blocked == 15);
        assert(!f.native.receive(f.command(barometer_action::gate), f.flight.now) && fake_sim::transmissions.empty());
    }
    {
        instrument_fixture f; const auto original = f.native.status();
        auto report = f.flight.command(session_kind::heartbeat); report.kind = session_kind::instrument_feedback; report.flags = report.axes = 0;
        for (auto& position : report.positions) position = -1;
        instrument_feedback body{}; body.epoch = body.preparation = body.revision = body.seat_revision = 1; body.phase = 2; body.protocol = 3;
        instrument_payload(report, body); assert(session_valid(report) && f.native.receive(report, f.flight.now));
        assert(f.native.instrument_reported(f.flight.now) && f.native.instrument_display(f.flight.now).phase == 2);
        assert(f.native.status().ack == original.ack && f.native.status().blocked == original.blocked && f.input.holds == 0);
        f.flight.now += 1.5; f.read(); report.command = ++f.flight.next;
        assert(f.native.receive(report, f.flight.now)); f.flight.now += 6.51; f.native.tick(f.flight.now);
        assert(f.native.status().code == session_error::expired && !f.native.instrument_reported(f.flight.now));
        assert(fake_sim::transmissions.empty());
    }
    std::cout << "Native instrument session checks passed: bound seat/revision/consent, independent acknowledgements, local instrument-only recovery, rejected retired backend commands, descriptive UI feedback without masks or lease renewal and explicit read-back recovery. Input provider and SDK are simulated.\n";
}
