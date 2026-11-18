#pragma once
#include "control_altimeters.h"
#include "control_session_protocol.h"

namespace fsc_controls {
struct altimeter_input_proof {
    uint64_t epoch = 0, preparation = 0;
    uint32_t blocked = 0, verified_routes = 0;
    bool ready = false;
};
// Implemented by an aircraft input adapter, never by a backend claim. A profile
// must verify both pressure references and every supported knob/input route.
// Production currently supplies no provider, so every instrument write refuses.
class altimeter_input_gate {
public:
    virtual ~altimeter_input_gate() = default;
    virtual altimeter_input_proof proof() const = 0;
    virtual bool request(uint32_t mask, uint64_t epoch, uint64_t preparation, double now) = 0;
    virtual void hold(double now) = 0;
};

class control_barometer_channel {
public:
    control_barometer_channel(control_altimeters* output = nullptr, altimeter_input_gate* gate = nullptr) : output_(output), gate_(gate) {}
    uint32_t gate_ack = 0, value_ack = 0;
    bool transport() const { return output_ != nullptr; }
    bool holding() const { return held_; }
    bool recovering() const { return recovering_; }
    bool local_only() const { return local_only_; }
    bool request_local(double now) {
        if (!std::isfinite(now) || !output_) return false;
        local_only_ = true; stop();
        if (held_) { local_requested_ = true; local_until_ = now + 5; (void)recover(now); }
        return true;
    }
    void cancel_recovery() { if (recovering_) stop(); }
    bool configure(altimeter_input_gate* gate) {
        if (managed_ || held_ || recovering_) return false;
        gate_ = gate; return true;
    }
    bool profile() const { return output_ && gate_ && gate_->proof().verified_routes == 3 && gate_->proof().blocked <= 3; }
    altimeter_input_proof input() const { return gate_ ? gate_->proof() : altimeter_input_proof{}; }
    uint32_t flags(double now) const {
        const auto proof = input();
        const bool fresh = output_ && output_->fresh(now), supported = profile();
        const bool ready = supported && fresh && managed_ && gate_confirmed_ && proof.ready && proof.epoch == epoch_ &&
            proof.preparation == preparation_ && proof.blocked == gate_job_.body.mask;
        const bool input_ready = ((!managed_ && !held_ && !recovering_ && !local_requested_ && !proof.blocked) || proof.ready) && proof.blocked <= 3;
        return (fresh ? 1u : 0u) | (transport() ? 2u : 0u) | (supported ? 4u : 0u) | (ready ? 8u : 0u) |
            (transport() && proof.blocked <= 3 ? proof.blocked << 4 : 0u) | (local_only_ ? 64u : 0u) |
            (recovering_ || local_requested_ ? 128u : 0u) | (held_ ? 256u : 0u) | (transport() && input_ready ? 512u : 0u);
    }
    void reset() {
        stop(); gate_ack = value_ack = 0; revision_ = last_command_ = 0; seat_ = seeded_ = 0;
        if (!held_) { epoch_ = preparation_ = 0; local_only_ = false; }
        sequence_[0] = sequence_[1] = 0;
    }
    void stop() {
        if ((managed_ || recovering_) && gate_) { gate_->hold(last_time_); held_ = true; }
        recovering_ = local_requested_ = false;
        if (output_) output_->supersede();
        if (gate_job_.active) acknowledge(gate_ack, gate_job_.command, barometer_result::failed);
        if (value_job_.active) acknowledge(value_ack, value_job_.command, barometer_result::failed);
        gate_job_.active = value_job_.active = gate_confirmed_ = managed_ = false;
    }
    bool recover(double now) {
        if (!held_) return true;
        local_only_ = true;
        last_time_ = now;
        if (!std::isfinite(now) || !output_ || !gate_ || !output_->fresh(now) || output_->checkpoint_pending()) return false;
        if (!recovering_) {
            if (!gate_->request(0, epoch_, preparation_, now)) { gate_->hold(now); return false; }
            recovering_ = true; recovery_floor_ = output_->serial; recover_until_ = now + 3; return false;
        }
        if (now >= recover_until_) { stop(); return false; }
        const auto proof = input();
        if (!proof.ready || proof.blocked || proof.epoch != epoch_ || proof.preparation != preparation_ || output_->serial <= recovery_floor_) return false;
        held_ = recovering_ = false; return true;
    }
    bool receive(const session_barometer& body, uint64_t command, uint64_t seat_revision, uint32_t local_seat, double now) {
        const bool gate_command = body.action == barometer_action::gate || body.action == barometer_action::stop;
        auto& ack = gate_command ? gate_ack : value_ack;
        if (!barometer_valid(body) || !command || command > barometer_max_command || command <= last_command_ || !std::isfinite(now)) return false;
        last_command_ = command;
        last_time_ = now;
        if (held_ || local_only_) { ack = barometer_ack(command, barometer_result::failed); return false; }
        if (!profile()) { ack = barometer_ack(command, barometer_result::unsupported); return false; }
        if (body.seat_revision != seat_revision || body.seat != local_seat || !output_->fresh(now) || body.read_serial > output_->serial ||
            body.epoch < epoch_ || body.preparation < preparation_ || body.revision < revision_ ||
            (body.epoch == epoch_ && seat_ && body.seat != seat_)) { ack = barometer_ack(command, barometer_result::rejected); return false; }
        const auto proof = input();
        if (body.epoch > epoch_) {
            if (body.action != barometer_action::gate || body.mask != 3 ||
                (epoch_ && (!proof.ready || proof.blocked != 3 || value_job_.active || output_->checkpoint_pending()))) {
                ack = barometer_ack(command, barometer_result::rejected); return false;
            }
            epoch_ = body.epoch; seeded_ = 0; seat_ = local_seat; sequence_[0] = sequence_[1] = 0;
        }
        if (body.preparation > preparation_ && !gate_command) { ack = barometer_ack(command, barometer_result::rejected); return false; }
        if (gate_command) {
            const uint32_t owned = local_seat == 1 ? 1u : 2u, other = 3u & ~owned;
            if (body.action == barometer_action::gate && (body.mask != 3 && (body.mask != other || seeded_ != 3 || value_job_.active))) {
                ack = barometer_ack(command, barometer_result::rejected); return false;
            }
            if (body.action == barometer_action::stop) {
                held_ = true;
                if (value_job_.active) {
                    if (value_job_.started && !output_->supersede()) { fail(); return false; }
                    acknowledge(value_ack, value_job_.command, barometer_result::failed); value_job_.active = false;
                }
            }
            preparation_ = body.preparation; revision_ = body.revision; gate_confirmed_ = false;
            managed_ = true;
            const auto mask = body.action == barometer_action::stop ? 3u : body.mask;
            gate_job_ = job(body, command, now); gate_job_.body.mask = mask;
            gate_ack = barometer_ack(command, barometer_result::pending);
            if (!gate_->request(mask, epoch_, preparation_, now)) { fail(); return false; }
            return true;
        }
        const auto owned_index = local_seat;
        if ((body.action == barometer_action::seed && (!gate_confirmed_ || !proof.ready || proof.blocked != 3 || value_job_.active)) ||
            (body.action != barometer_action::seed && seeded_ != 3) ||
            (value_job_.active && body.instrument != value_job_.body.instrument) ||
            (body.action != barometer_action::seed && body.sequence <= sequence_[body.instrument - 1]) ||
            (body.action == barometer_action::apply && body.instrument == owned_index) ||
            (body.action == barometer_action::observe && body.instrument != owned_index && proof.blocked != 3)) {
            ack = barometer_ack(command, barometer_result::rejected); return false;
        }
        if (value_job_.active && value_job_.started && !output_->supersede()) { fail(); return false; }
        if (body.action != barometer_action::seed) sequence_[body.instrument - 1] = body.sequence;
        revision_ = body.revision; value_job_ = job(body, command, now);
        value_ack = barometer_ack(command, barometer_result::pending); return true;
    }
    void reject(uint64_t command, bool gate_command) {
        if (command && command <= barometer_max_command) {
            last_command_ = command > last_command_ ? command : last_command_;
            acknowledge(gate_command ? gate_ack : value_ack, command, barometer_result::rejected);
        }
    }
    void tick(double now, uint32_t local_seat) {
        last_time_ = now;
        if (local_requested_) {
            if (!std::isfinite(now) || now >= local_until_) { stop(); return; }
            if (recover(now)) local_requested_ = false;
            return;
        }
        if (recovering_ && (!std::isfinite(now) || now >= recover_until_ || !gate_ || !output_ || !output_->fresh(now))) { stop(); return; }
        if (!managed_) return;
        if (!profile() || !output_->fresh(now) || !std::isfinite(now) || (seat_ != local_seat && value_job_.active)) { fail(); return; }
        auto proof = input();
        if (gate_confirmed_ && (!proof.ready || proof.epoch != epoch_ || proof.preparation != preparation_ || proof.blocked != gate_job_.body.mask)) {
            fail(); return;
        }
        if (gate_job_.active) {
            if (now >= gate_job_.deadline) { fail(); return; }
            if (proof.ready && proof.epoch == epoch_ && proof.preparation == preparation_ && proof.blocked == gate_job_.body.mask) {
                if (!gate_job_.started) { gate_job_.started = true; gate_job_.floor = output_->serial; }
                if (output_->serial > gate_job_.floor) {
                    gate_job_.active = false; gate_confirmed_ = true;
                    acknowledge(gate_ack, gate_job_.command, barometer_result::succeeded);
                }
            }
        }
        if (!value_job_.active) return;
        if (now >= value_job_.deadline || output_->result == altimeter_result::failed) { fail(); return; }
        if (!gate_confirmed_ || !proof.ready || proof.epoch != epoch_ || proof.preparation != preparation_ || proof.blocked != gate_job_.body.mask) return;
        if (!value_job_.started) {
            if (!output_->ready_for_output(now)) return;
            const auto& b = value_job_.body;
            if (b.action == barometer_action::seed && proof.blocked != 3) { fail(); return; }
            if (!output_->begin(b.instrument, {b.pressure16, b.standard != 0}, now,
                b.action == barometer_action::observe ? altimeter_operation::observe : altimeter_operation::apply)) { fail(); return; }
            value_job_.started = true; value_job_.floor = output_->serial;
        }
        output_->tick(now);
        if (output_->result == altimeter_result::failed) { fail(); return; }
        if (output_->result == altimeter_result::confirmed && output_->serial > value_job_.floor) {
            if (value_job_.body.action == barometer_action::seed) seeded_ |= 1u << (value_job_.body.instrument - 1);
            acknowledge(value_ack, value_job_.command, barometer_result::succeeded); value_job_.active = false;
        }
    }
private:
    struct operation {
        session_barometer body{}; uint64_t command = 0, floor = 0; double deadline = 0;
        bool active = false, started = false;
    } gate_job_, value_job_;
    control_altimeters* output_; altimeter_input_gate* gate_;
    uint64_t epoch_ = 0, preparation_ = 0, revision_ = 0, last_command_ = 0;
    uint64_t sequence_[2]{};
    uint32_t seat_ = 0, seeded_ = 0;
    bool gate_confirmed_ = false, managed_ = false, held_ = false, recovering_ = false, local_only_ = false, local_requested_ = false;
    uint64_t recovery_floor_ = 0;
    double last_time_ = 0, recover_until_ = 0, local_until_ = 0;
    static operation job(const session_barometer& body, uint64_t command, double now) {
        operation value{}; value.body = body; value.command = command; value.deadline = now + 3; value.active = true; return value;
    }
    static void acknowledge(uint32_t& target, uint64_t command, barometer_result result) {
        if (command >= (target >> 3)) target = barometer_ack(command, result);
    }
    void fail() { stop(); }
};
}
