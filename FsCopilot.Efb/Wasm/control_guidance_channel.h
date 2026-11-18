#pragma once
#include "control_guidance_protocol.h"

namespace fsc_controls {
struct guidance_input_proof {
    uint64_t command_epoch = 0, navigation_epoch = 0, preparation = 0;
    uint32_t verified_routes = 0, blocked = 0;
    bool ready = false, capture = false, single_host_engine = false;
    bool navigation_ready = false;
    char navigation_fingerprint[65]{};
    char adapter[64]{};
};
struct guidance_captured_input {
    uint64_t command_epoch = 0, navigation_epoch = 0, preparation = 0;
    guidance_setting setting = guidance_setting::master;
    double value = 0;
};
// An aircraft adapter must implement actual input interception/capture and
// verify the one authoritative AP engine. Backend declarations cannot supply it.
class guidance_input_gate {
public:
    virtual ~guidance_input_gate() = default;
    virtual guidance_input_proof proof() const = 0;
    virtual bool request(bool capture, bool host, uint64_t command_epoch, uint64_t navigation_epoch, uint64_t preparation, double now) = 0;
    virtual void hold(double now) = 0;
    virtual bool release(double now) = 0;
    virtual bool poll(guidance_captured_input&) { return false; }
    // Logical input reference only. It must not write simulator state or masks.
    virtual bool reference(const guidance_values&, uint64_t) { return false; }
};
class control_guidance_channel {
public:
    control_guidance_channel(control_guidance* output = nullptr, guidance_input_gate* gate = nullptr) : output_(output), gate_(gate) {}
    bool configure(guidance_input_gate* gate) { if (managed_ || held_ || recovering_) return false; gate_ = gate; return true; }
    bool holding() const { return held_; }
    bool profile() const { return output_ && gate_ && gate_->proof().verified_routes == 8191 && gate_->proof().blocked <= 8191 &&
        guidance_adapter_valid(gate_->proof().adapter, sizeof(gate_->proof().adapter)); }
    void reset() {
        stop();
        if (held_) return;
        command_epoch_ = navigation_epoch_ = preparation_ = revision_ = received_ = sequence_ = 0;
        gate_ack_ = value_ack_ = reference_ack_ = error_ = 0; local_only_ = false; intent_head_ = intent_count_ = 0; last_intent_ack_ = 0;
    }
    void stop() {
        if (managed_ && gate_) { gate_->hold(last_time_); held_ = true; }
        if (output_) (void)output_->supersede();
        if (gate_job_.active) gate_ack_ = barometer_ack(gate_job_.command, barometer_result::failed);
        if (value_job_.active) value_ack_ = barometer_ack(value_job_.packet.command, barometer_result::failed);
        gate_job_.active = value_job_.active = gate_confirmed_ = managed_ = recovering_ = false;
        intent_head_ = intent_count_ = 0;
    }
    const guidance_packet* queued() const { return value_job_.active && !value_job_.started ? &value_job_.packet : nullptr; }
    void reject_queued() { error_ = 2; stop(); }
    bool recover(double now) {
        if (!held_) return true;
        local_only_ = true;
        if (!std::isfinite(now) || !gate_ || !output_ || !output_->fresh(now) || output_->checkpoint_pending()) return false;
        if (!recovering_) {
            if (!gate_->release(now)) return false;
            recovering_ = true; recovery_floor_ = output_->serial; recovery_until_ = now + 3; return false;
        }
        if (now >= recovery_until_) { recovering_ = false; return false; }
        const auto proof = gate_->proof();
        if (!proof.ready || proof.blocked || proof.capture || output_->serial < recovery_floor_ || output_->serial - recovery_floor_ < 3) return false;
        held_ = recovering_ = false; return true;
    }
    bool receive(const guidance_packet& p, double now) {
        if (!guidance_frame_valid(p) || p.kind != guidance_kind::command || !std::isfinite(now) || now < last_time_ || p.command <= received_) return false;
        received_ = p.command; last_time_ = now;
        if (managed_ && now - renewed_ >= 1) { fail(5); return false; }
        if (p.action == guidance_action::poll) return true; // No native control/gate lease is renewed.
        uint32_t input_ack = 0; // Input acknowledgements retire the FIFO head, not SDK output.
        auto& ack = p.action == guidance_action::gate || p.action == guidance_action::stop ? gate_ack_ :
            p.action == guidance_action::reference ? reference_ack_ : p.action == guidance_action::intent_ack ? input_ack : value_ack_;
        if (held_ || local_only_) { ack = barometer_ack(p.command, barometer_result::failed); return false; }
        if (!profile()) { ack = barometer_ack(p.command, barometer_result::unsupported); error_ = 3; return false; }
        if (!output_->fresh(now) || p.read_serial > output_->serial || p.command_epoch < command_epoch_ || p.navigation_epoch < navigation_epoch_ ||
            p.preparation < preparation_ || p.revision < revision_) { ack = barometer_ack(p.command, barometer_result::rejected); return false; }
        const bool new_preparation = p.preparation > preparation_;
        if (new_preparation && (p.action != guidance_action::gate || p.capture)) { ack = barometer_ack(p.command, barometer_result::rejected); return false; }
        const bool epoch_change = p.command_epoch != command_epoch_ || p.navigation_epoch != navigation_epoch_;
        const auto proof = gate_->proof();
        if (p.action == guidance_action::intent_ack) {
            if (p.command_epoch != command_epoch_ || p.navigation_epoch != navigation_epoch_ || p.preparation != preparation_ ||
                !intent_count_ || intents_[intent_head_].id != p.sequence) return false;
            last_intent_ack_ = p.sequence; intent_head_ = (intent_head_ + 1) % 8; --intent_count_; return true;
        }
        if (p.action == guidance_action::reference) {
            if (p.command_epoch != command_epoch_ || p.navigation_epoch != navigation_epoch_ || p.preparation != preparation_ || p.sequence < last_intent_ack_) return false;
            guidance_values values{}; values.switches = p.switches; values.heading = p.heading; values.altitude = p.altitude;
            values.vertical_speed = p.vertical_speed; values.gps = p.gps != 0; values.nav = p.nav;
            for (unsigned index = 0; index < intent_count_; ++index) {
                const auto& pending = intents_[(intent_head_ + index) % 8];
                if (pending.id > p.sequence) guidance_assign(values, pending.setting, pending.value);
            }
            reference_ack_ = barometer_ack(p.command, gate_->reference(values, p.sequence) ? barometer_result::succeeded : barometer_result::unsupported);
            return (reference_ack_ & 7) == static_cast<uint32_t>(barometer_result::succeeded);
        }
        if (!new_preparation && epoch_change && (p.action != guidance_action::gate || !gate_confirmed_ || proof.capture ||
            value_job_.active || output_->checkpoint_pending())) { ack = barometer_ack(p.command, barometer_result::rejected); return false; }
        if (p.action == guidance_action::gate) {
            const bool changing = new_preparation || epoch_change || capture_ != (p.capture != 0);
            if (new_preparation && value_job_.active) {
                (void)output_->supersede(); value_ack_ = barometer_ack(value_job_.packet.command, barometer_result::failed); value_job_.active = false;
            }
            command_epoch_ = p.command_epoch; navigation_epoch_ = p.navigation_epoch; preparation_ = p.preparation; revision_ = p.revision;
            if (new_preparation || epoch_change) sequence_ = 0;
            capture_ = p.capture != 0; host_ = p.host != 0; managed_ = true; renewed_ = now; error_ = 0;
            if (changing) gate_confirmed_ = false;
            if (changing) intent_head_ = intent_count_ = 0;
            if (new_preparation || epoch_change || (changing && !capture_)) { last_intent_ack_ = 0; reference_ack_ = 0; }
            if (changing || !gate_job_.active) gate_job_ = {true, false, p.command, 0, now + 2};
            else gate_job_.command = p.command; // A refresh cannot extend an unfinished gate's deadline.
            gate_ack_ = barometer_ack(p.command, barometer_result::pending);
            // A lease refresh must not restart asynchronous input interception.
            // Its proof is still checked on every tick, including after refresh.
            if (changing && !gate_->request(capture_, host_, command_epoch_, navigation_epoch_, preparation_, now)) { fail(4); return false; }
            return true;
        }
        if (p.command_epoch != command_epoch_ || p.navigation_epoch != navigation_epoch_ || p.preparation != preparation_ ||
            p.host != static_cast<uint32_t>(host_)) { ack = barometer_ack(p.command, barometer_result::rejected); return false; }
        revision_ = p.revision;
        if (p.action == guidance_action::stop) { stop(); gate_ack_ = barometer_ack(p.command, barometer_result::succeeded); return true; }
        const bool disconnect = p.action == guidance_action::apply && p.setting == guidance_setting::master && p.value == 0;
        if ((!gate_confirmed_ && !disconnect) || !proof.ready || proof.verified_routes != 8191 || proof.blocked != 8191 || !proof.single_host_engine ||
            (!disconnect && !proof_matches(proof)) || p.sequence <= sequence_) { ack = barometer_ack(p.command, barometer_result::rejected); return false; }
        if (p.action == guidance_action::apply && !(supported() & (1u << static_cast<uint32_t>(p.setting)))) {
            ack = barometer_ack(p.command, barometer_result::unsupported); return false;
        }
        const bool navigation = p.value != 0 && (p.setting == guidance_setting::nav_hold || p.setting == guidance_setting::approach_hold ||
            (p.setting == guidance_setting::master && (output_->values.switches & (16 | 32 | 64 | 128 | 256))));
        if (p.action == guidance_action::apply && navigation && (!proof.navigation_ready || !session_hex(proof.navigation_fingerprint, sizeof(proof.navigation_fingerprint), 64))) {
            ack = barometer_ack(p.command, barometer_result::unsupported); return false;
        }
        if (value_job_.active && value_job_.started && !output_->supersede()) { fail(4); return false; }
        sequence_ = p.sequence; value_job_ = {true, false, p, now + 2};
        value_ack_ = barometer_ack(p.command, barometer_result::pending); return true;
    }
    void tick(double now, uint64_t steering_generation = 0, writer_owner steering_owner = writer_owner::none) {
        if (!managed_) return;
        if (!std::isfinite(now) || now < last_time_ || now - renewed_ >= 1 || !profile() || !output_->fresh(now)) { fail(5); return; }
        last_time_ = now;
        const auto proof = gate_->proof();
        if (gate_confirmed_ && !proof_matches(proof)) { fail(3); return; }
        if (value_job_.active) {
            if (now >= value_job_.until || output_->result == guidance_result::failed) { fail(4); return; }
            auto& value = value_job_;
            const bool needs_navigation = value.packet.action == guidance_action::apply && value.packet.value != 0 &&
                (value.packet.setting == guidance_setting::nav_hold || value.packet.setting == guidance_setting::approach_hold ||
                    (value.packet.setting == guidance_setting::master && (output_->values.switches & (16 | 32 | 64 | 128 | 256))));
            if (needs_navigation && (!proof.navigation_ready || !session_hex(proof.navigation_fingerprint, sizeof(proof.navigation_fingerprint), 64))) { fail(3); return; }
            const bool disconnect = value.packet.action == guidance_action::apply && value.packet.setting == guidance_setting::master && value.packet.value == 0;
            if (!value.started && (output_->ready_for_output(now) || disconnect)) {
                if (output_->begin(value.packet.setting, value.packet.value, now,
                    value.packet.action == guidance_action::observe ? guidance_operation::observe : guidance_operation::apply)) value.started = true;
                else if (output_->result == guidance_result::failed || output_->ready_for_output(now)) { fail(4); return; }
            }
            if (value.started) {
                output_->tick(now);
                if (output_->result == guidance_result::failed) { fail(4); return; }
                if (output_->result == guidance_result::confirmed) {
                    value_ack_ = barometer_ack(value.packet.command, barometer_result::succeeded); value.active = false;
                }
            }
        }
        if (gate_job_.active) {
            if (now >= gate_job_.until) { fail(5); return; }
            if (proof_matches(proof) && !value_job_.active && !output_->checkpoint_pending()) {
                if (!gate_job_.started) { gate_job_.started = true; gate_job_.floor = output_->serial; }
                if (output_->serial >= gate_job_.floor && output_->serial - gate_job_.floor >= 3) {
                    gate_job_.active = false; gate_confirmed_ = true;
                    gate_ack_ = barometer_ack(gate_job_.command, barometer_result::succeeded);
                }
            }
        }
        validate_intents(steering_generation, steering_owner);
        if (gate_confirmed_ && proof_matches(proof) && (reference_ack_ & 7) == static_cast<uint32_t>(barometer_result::succeeded)) for (unsigned index = 0; index < 8; ++index) {
            guidance_captured_input input{}; if (!gate_->poll(input)) break;
            if (input.command_epoch != command_epoch_ || input.navigation_epoch != navigation_epoch_ || input.preparation != preparation_ ||
                !guidance_valid(input.setting, input.value) || !(supported() & (1u << static_cast<uint32_t>(input.setting)))) continue;
            const bool emergency = !capture_ && input.setting == guidance_setting::master && input.value == 0 && steering_owner == writer_owner::local;
            if (!capture_ && !emergency) continue;
            if (intent_count_ > 1) {
                auto& tail = intents_[(intent_head_ + intent_count_ - 1) % 8];
                if (tail.setting == input.setting && !tail.steering_generation && !emergency) { tail.value = input.value; continue; }
            }
            if (intent_count_ == 8 || intent_serial_ == UINT64_MAX) { fail(6); return; }
            intents_[(intent_head_ + intent_count_++) % 8] = {++intent_serial_, input.setting, input.value, emergency ? steering_generation : 0};
        }
    }
    void validate_intents(uint64_t generation, writer_owner owner) {
        while (intent_count_ && intents_[intent_head_].steering_generation &&
            (owner != writer_owner::local || generation != intents_[intent_head_].steering_generation)) {
            intent_head_ = (intent_head_ + 1) % 8; --intent_count_;
        }
    }
    void status(guidance_packet& p, double now) const {
        p.kind = guidance_kind::status; p.command = received_; p.command_epoch = command_epoch_; p.navigation_epoch = navigation_epoch_;
        p.preparation = preparation_; p.revision = revision_; p.capture = capture_ ? 1u : 0; p.host = host_ ? 1u : 0;
        p.gate_ack = gate_ack_; p.value_ack = value_ack_; p.reference_ack = reference_ack_; p.error = error_;
        const auto proof = gate_ ? gate_->proof() : guidance_input_proof{};
        const bool fresh = output_ && output_->fresh(now), verified = profile();
        const bool ready = managed_ && gate_confirmed_ && verified && fresh && proof_matches(proof) && !held_ && !local_only_;
        p.flags = (fresh ? guidance_fresh : 0u) | (output_ ? guidance_transport : 0u) | (verified ? guidance_profile : 0u) |
            (ready ? guidance_gate_ready : 0u) | (held_ ? guidance_held : 0u) | (ready && capture_ ? guidance_capture : 0u) |
            (proof.single_host_engine ? guidance_single_host : 0u) | (gate_job_.active || value_job_.active ? guidance_pending : 0u) | (local_only_ ? guidance_local : 0u);
        if (ready && proof.navigation_ready && session_hex(proof.navigation_fingerprint, sizeof(proof.navigation_fingerprint), 64)) {
            p.flags |= guidance_navigation_ready; std::memcpy(p.navigation_fingerprint, proof.navigation_fingerprint, sizeof(p.navigation_fingerprint));
        }
        p.supported = verified ? supported() : 0; p.blocked = proof.blocked <= 8191 ? proof.blocked : 0;
        if (verified) std::memcpy(p.adapter, proof.adapter, sizeof(p.adapter));
        if (ready && intent_count_) {
            const auto& intent = intents_[intent_head_]; p.sequence = intent.id; p.setting = intent.setting; p.value = intent.value;
            p.steering_generation = intent.steering_generation; p.steering_owner = intent.steering_generation ? 1u : 0u;
        }
        if (!output_) return;
        const auto& values = output_->values; const auto slots = output_->targets();
        p.read_serial = output_->serial; p.switches = values.switches; p.heading = values.heading; p.altitude = values.altitude;
        p.vertical_speed = values.vertical_speed; p.gps = values.gps ? 1u : 0; p.nav = values.nav;
        p.slots = guidance_slots(values.slots[0], values.slots[1], values.slots[2]);
        p.target_slots = guidance_slots(slots.heading, slots.altitude, slots.vertical_speed);
    }
private:
    control_guidance* output_ = nullptr;
    guidance_input_gate* gate_ = nullptr;
    uint64_t command_epoch_ = 0, navigation_epoch_ = 0, preparation_ = 0, revision_ = 0, received_ = 0, sequence_ = 0, recovery_floor_ = 0;
    uint32_t gate_ack_ = 0, value_ack_ = 0, reference_ack_ = 0, error_ = 0;
    uint64_t last_intent_ack_ = 0;
    bool managed_ = false, held_ = false, local_only_ = false, recovering_ = false, gate_confirmed_ = false, capture_ = false, host_ = false;
    double last_time_ = 0, renewed_ = -1, recovery_until_ = 0;
    struct gate_job { bool active = false, started = false; uint64_t command = 0, floor = 0; double until = 0; } gate_job_;
    struct value_job { bool active = false, started = false; guidance_packet packet{}; double until = 0; } value_job_;
    struct intent { uint64_t id = 0; guidance_setting setting = guidance_setting::master; double value = 0; uint64_t steering_generation = 0; } intents_[8]{};
    unsigned intent_head_ = 0, intent_count_ = 0;
    uint64_t intent_serial_ = 0;
    bool proof_matches(const guidance_input_proof& p) const { return p.ready && p.verified_routes == 8191 && p.blocked == 8191 &&
        p.command_epoch == command_epoch_ && p.navigation_epoch == navigation_epoch_ && p.preparation == preparation_ && p.capture == capture_ && p.single_host_engine; }
    uint32_t supported() const {
        if (!output_) return 0;
        uint32_t mask = 4095; const auto& value = output_->values; const auto slots = output_->targets();
        if (value.slots[0] != slots.heading) mask &= ~(1u << 1);
        if (value.slots[1] != slots.altitude) mask &= ~(1u << 2);
        if (value.slots[2] != slots.vertical_speed) mask &= ~(1u << 3);
        return mask;
    }
    void fail(uint32_t error) { error_ = error; stop(); }
};
}
