// Standalone native RPC tests. Input-gate proofs and every SDK call are local
// test doubles; passing these checks does not verify Baron knob input routes.
#define _CRT_SECURE_NO_WARNINGS
#include <cassert>
#include <cstdlib>
#include <fstream>
#include <iostream>
#include <vector>
#include "../Wasm/control_barometer_channel.h"
using namespace fsc_controls;
namespace {
struct fake_gate : altimeter_input_gate {
    altimeter_input_proof state{0, 0, 0, 3, false};
    uint32_t requested = 0, holds = 0;
    bool accepts = true;
    altimeter_input_proof proof() const override { return state; }
    bool request(uint32_t mask, uint64_t epoch, uint64_t preparation, double) override {
        requested = mask; state.epoch = epoch; state.preparation = preparation; state.ready = false; return accepts;
    }
    void hold(double) override { ++holds; state.blocked = 3; state.ready = true; }
    void confirm() { state.blocked = requested; state.ready = true; }
};
struct fixture {
    control_altimeters output;
    fake_gate gate;
    control_barometer_channel channel{&output, &gate};
    uint64_t command = 0;
    double now = 0, first = 1000, second = 1010;
    fixture() {
        fake_sim::fail_transmission_after = -1; fake_sim::transmissions.clear();
        assert(output.initialize(static_cast<HANDLE>(1)) && output.confirm(fake_sim::request_id)); sample();
    }
    void sample() {
        now += .01;
        std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * 4);
        auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
        data->dwRequestID = control_altimeters::read_request; data->dwDefineCount = 4;
        const double values[] = {first, 0, second, 0}; std::memcpy(&data->dwData, values, sizeof(values));
        const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&data->dwData) - reinterpret_cast<char*>(data) + sizeof(values));
        assert(output.observe(data, size, now));
    }
    session_barometer body(barometer_action action, uint32_t index = 0, uint32_t pressure = 0, uint64_t sequence = 0) {
        session_barometer b{}; b.epoch = b.preparation = b.revision = b.seat_revision = b.seat = 1;
        b.action = action; b.instrument = index; b.pressure16 = pressure; b.sequence = sequence; b.read_serial = output.serial;
        return b;
    }
    bool send(session_barometer b) { return channel.receive(b, ++command, 1, 1, now); }
    void mask(uint32_t value) {
        auto b = body(barometer_action::gate); b.mask = value; assert(send(b));
        gate.confirm(); channel.tick(now, 1);
        assert((channel.gate_ack & 7) == static_cast<uint32_t>(barometer_result::pending));
        sample(); channel.tick(now, 1);
        assert(channel.gate_ack == barometer_ack(command, barometer_result::succeeded));
    }
    void finish(double desired_first, double desired_second) {
        first = desired_first; second = desired_second;
        if (output.checkpoint_pending()) assert(output.confirm(fake_sim::request_id));
        for (unsigned i = 0; i < 3; ++i) { sample(); channel.tick(now, 1); }
        assert((channel.value_ack & 7) == static_cast<uint32_t>(barometer_result::succeeded));
    }
    void establish() {
        mask(3);
        assert(send(body(barometer_action::seed, 1, 16000))); channel.tick(now, 1); finish(1000, 1010);
        assert(send(body(barometer_action::seed, 2, 16160))); channel.tick(now, 1); finish(1000, 1010);
        mask(2); assert(fake_sim::transmissions.empty());
    }
};
void cross_language_wire() {
    for (const char* name : {"barometer-from-csharp.bin", "barometer-status-from-csharp.bin", "instrument-feedback-from-csharp.bin"}) {
        std::ifstream file(std::string("artifacts/altimeter-native-command/tests/") + name, std::ios::binary);
        session_packet packet{}; assert(file.read(reinterpret_cast<char*>(&packet), sizeof(packet)) && session_valid(packet));
        if (packet.kind == session_kind::barometer) {
            const auto b = barometer_payload(packet);
            assert(b.action == barometer_action::apply && b.instrument == 2 && b.pressure16 == 16200 && b.standard == 1 && b.read_serial == 18);
            auto bad = b; bad.reserved[0] = 1; barometer_payload(packet, bad); assert(!session_valid(packet));
        } else if (packet.kind == session_kind::instrument_feedback) {
            const auto body = instrument_payload(packet);
            assert(body.epoch == 1 && body.preparation == 2 && body.revision == 3 && body.seat_revision == 4 && body.phase == 2 && body.pending == 3 && body.protocol == 3);
        } else assert(packet.ack == 7 && packet.barometer_gate_ack == barometer_ack(44, barometer_result::succeeded) &&
            packet.barometer_value_ack == barometer_ack(43, barometer_result::pending));
    }
}
}
int main() {
#ifdef _WIN32
    _set_error_mode(_OUT_TO_STDERR); _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
#endif
    cross_language_wire();
    {
        fixture f; control_barometer_channel unsupported(&f.output);
        auto b = f.body(barometer_action::gate); b.mask = 3;
        assert(!unsupported.receive(b, 1, 1, 1, f.now));
        assert(unsupported.gate_ack == barometer_ack(1, barometer_result::unsupported) && fake_sim::transmissions.empty());
        assert(!f.send(f.body(barometer_action::seed, 1, 16000))); // No initial gate/epoch.
        f.mask(3); auto wrong = f.body(barometer_action::gate); wrong.mask = 2;
        assert(!f.send(wrong)); // Both initial baselines must be confirmed before unmasking either owner.
    }
    {
        fixture f; f.establish();
        assert(!f.send(f.body(barometer_action::apply, 1, 16016, 1))); // Never overwrite this seat's own knob.
        assert(f.send(f.body(barometer_action::observe, 1, 16016, 1))); f.channel.tick(f.now, 1);
        f.finish(1001, 1010); assert(fake_sim::transmissions.empty());
        assert(!f.send(f.body(barometer_action::observe, 1, 16016, 1))); // Sequence replay under a new RPC id.
        assert(f.send(f.body(barometer_action::apply, 2, 16176, 1))); f.channel.tick(f.now, 1);
        const auto value_command = f.command;
        auto gate = f.body(barometer_action::gate); gate.mask = 3; gate.preparation = 2;
        assert(f.send(gate)); f.gate.confirm(); f.channel.tick(f.now, 1); f.sample(); f.channel.tick(f.now, 1);
        assert(f.channel.value_ack == barometer_ack(value_command, barometer_result::pending));
        const auto gate_ack = f.channel.gate_ack;
        f.finish(1001, 1011); assert(f.channel.gate_ack == gate_ack && fake_sim::transmissions.size() == 1);
    }
    {
        fixture f; f.establish();
        assert(f.send(f.body(barometer_action::apply, 2, 16176, 1))); f.channel.tick(f.now, 1);
        auto invalid = f.body(barometer_action::apply, 2, 16192, 2); invalid.seat_revision = 2;
        assert(!f.send(invalid)); const auto rejected = f.channel.value_ack;
        assert(f.output.confirm(fake_sim::request_id)); f.first = 1000; f.second = 1011;
        for (unsigned i = 0; i < 3; ++i) { f.sample(); f.channel.tick(f.now, 1); }
        assert(f.channel.value_ack == rejected); // Older completion cannot replace the newer command's rejection.
        f.gate.state.ready = false; f.channel.tick(f.now, 1); assert(f.gate.holds == 1);
    }
    {
        fixture f; f.establish(); fake_sim::fail_transmission_after = 0;
        assert(f.send(f.body(barometer_action::apply, 2, 16176, 1))); f.channel.tick(f.now, 1);
        assert((f.channel.value_ack & 7) == static_cast<uint32_t>(barometer_result::failed) && f.gate.holds == 1);
        fake_sim::fail_transmission_after = -1;
    }
    {
        fixture f; f.establish();
        assert(f.send(f.body(barometer_action::apply, 2, 16176, 1))); f.channel.tick(f.now, 1);
        assert(!f.send(f.body(barometer_action::observe, 1, 16000, 1))); // One instrument cannot supersede the other's in-flight write.
        const auto rejected = f.channel.value_ack;
        assert(f.output.confirm(fake_sim::request_id)); f.second = 1011;
        for (unsigned i = 0; i < 3; ++i) { f.sample(); f.channel.tick(f.now, 1); }
        assert(f.channel.value_ack == rejected && fake_sim::transmissions.size() == 1);
        assert(f.send(f.body(barometer_action::observe, 1, 16000, 1))); f.channel.tick(f.now, 1); f.finish(1000, 1011);
    }
    {
        fixture f; f.establish();
        auto next = f.body(barometer_action::gate); next.epoch = 2; next.seat_revision = 2; next.seat = 2; next.mask = 3;
        assert(!f.channel.receive(next, ++f.command, 2, 2, f.now)); // Old owner still has an unmasked knob.
        f.mask(3); next.read_serial = f.output.serial;
        assert(f.channel.receive(next, ++f.command, 2, 2, f.now)); f.gate.confirm(); f.channel.tick(f.now, 2);
        f.sample(); f.channel.tick(f.now, 2);
        assert(f.channel.gate_ack == barometer_ack(f.command, barometer_result::succeeded));
        next.mask = 1; assert(!f.channel.receive(next, ++f.command, 2, 2, f.now)); // New seat requires new baseline proofs.
        auto retired = f.body(barometer_action::apply, 2, 16176, 1);
        assert(!f.channel.receive(retired, ++f.command, 2, 2, f.now) && fake_sim::transmissions.empty());
    }
    {
        fixture f; f.establish(); f.channel.stop(); assert(f.channel.holding());
        assert(!f.channel.recover(f.now)); f.gate.confirm();
        assert(!f.channel.recover(f.now)); f.gate.state.verified_routes = 0;
        f.channel.tick(f.now, 1); assert(f.channel.holding() && f.gate.holds == 1 && f.gate.state.blocked == 0);
        assert(!f.channel.recover(f.now)); f.sample();
        assert(f.channel.recover(f.now) && !f.channel.holding());
        assert(f.channel.local_only() && !f.channel.profile()); // Known mask release works after profile verification is lost.
        assert(!(f.channel.flags(f.now) & 8)); // Local recovery is not shared-session gate readiness.
    }
    {
        fixture f; f.establish();
        assert(f.send(f.body(barometer_action::apply, 2, 16176, 1))); const auto queued = f.command;
        assert(f.send(f.body(barometer_action::stop))); // Stop arrives before the queued SDK operation starts.
        assert(f.channel.holding() && f.channel.value_ack == barometer_ack(queued, barometer_result::failed));
        f.gate.confirm(); f.channel.tick(f.now, 1); f.sample(); f.channel.tick(f.now, 1);
        assert(f.channel.gate_ack == barometer_ack(f.command, barometer_result::succeeded) && fake_sim::transmissions.empty());
        auto restart = f.body(barometer_action::gate); restart.mask = 2;
        assert(!f.send(restart) && fake_sim::transmissions.empty()); // Held instrument authority cannot resume from a backend packet.
    }
    std::cout << "Barometer RPC checks passed: C# wire compatibility, verified-provider refusal, initial masked baselines, indexed ownership, replay rejection, concurrent independent gate/value acknowledgements, old completion fencing and failure holds. SDK and input gates are simulated; no simulator access.\n";
}
