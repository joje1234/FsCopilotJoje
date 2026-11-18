#pragma once
#include "control_session_timing.h"
#include "control_barometer_channel.h"
#include "control_guidance_channel.h"

namespace fsc_controls {
// Local consent is delivered through CommBus, never the retained ClientData
// channel. All ownership comes from a bound backend; this class supplies native
// suppression/alignment/attachment proofs, not an independent authority model.
template<class Runtime, class Writer> class control_session {
public:
    control_session(control_pickup& pickup, Runtime& runtime, Writer& writer, control_world* world = nullptr,
        control_altimeters* altimeters = nullptr, altimeter_input_gate* instrument_gate = nullptr,
        control_guidance* autopilot = nullptr, guidance_input_gate* guidance_gate = nullptr) :
        pickup_(pickup), runtime_(runtime), writer_(writer), world_(world), barometers_(altimeters, instrument_gate), guidance_(autopilot, guidance_gate) {}
    bool configure_guidance_adapter(guidance_input_gate* adapter) { return phase_ == session_phase::idle && !holding_ && guidance_.configure(adapter); }
    void stop_guidance() { guidance_.stop(); }
    bool receive_guidance(const guidance_packet& p, double now) {
        if (!guidance_frame_valid(p) || p.kind != guidance_kind::command || !std::isfinite(now) || now < renewed_) return false;
        if (active() && now - renewed_ >= session_recovery_seconds) { stop(session_error::expired); return false; }
        if (!guidance_scope(p) || p.control_revision != state_.revision || (!host_ && !world_->live(now))) return false;
        return guidance_.receive(p, now); // A separate channel; never renew the physical-control lease.
    }
    guidance_packet guidance_status(double now) {
        guidance_packet p{}; p.kind = guidance_kind::status;
        std::memcpy(p.native, native_, sizeof(p.native));
        if (binding_.backend[0] && binding_.session[0] && consent_[0] && binding_.epoch && state_.revision) {
            std::memcpy(p.backend, binding_.backend, sizeof(p.backend)); std::memcpy(p.session, binding_.session, sizeof(p.session));
            std::memcpy(p.consent, consent_, sizeof(p.consent)); p.epoch = binding_.epoch; p.control_revision = state_.revision;
        }
        guidance_.validate_intents(state_.generation[0], state_.owner[0]);
        guidance_.status(p, now); p.host = host_ ? 1u : 0;
        if (guidance_serial_ != UINT64_MAX) p.serial = ++guidance_serial_;
        return p;
    }
    uint32_t barometer_flags(double now) const { return barometers_.flags(now); }
    bool recovering_instruments() const { return barometers_.recovering(); }
    void cancel_instrument_recovery() { barometers_.cancel_recovery(); }
    bool instrument_reported(double now) const { return phase_ == session_phase::active && instrument_ui_.seat_revision == seat_revision_ && instrument_ui_at_ >= 0 && std::isfinite(now) && now >= instrument_ui_at_ && now - instrument_ui_at_ <= 1; }
    instrument_feedback instrument_display(double now) const { return instrument_reported(now) ? instrument_ui_ : instrument_feedback{}; }
    bool use_local_instruments(const char* session, double now) {
        if (!session || (binding_.session[0] ? std::strcmp(binding_.session, session) : std::strcmp(session, "0"))) return false;
        return barometers_.request_local(now);
    }
    bool configure_instrument_adapter(altimeter_input_gate* adapter) {
        return phase_ == session_phase::idle && !holding_ && barometers_.configure(adapter);
    }
    void initialize(const char* native) {
        guidance_.reset();
        std::snprintf(native_, sizeof(native_), "%s", native);
        consent_[0] = 0; binding_ = writer_binding{}; state_ = writer_state{};
        phase_ = session_phase::idle; result_ = session_result::idle; code_ = session_error::none;
        // Aircraft changes keep the native instance, so its status serial and
        // arm counter must remain monotonic across this reset.
        holding_ = guidance_.holding(); host_ = false; received_ = ack_ = 0; operation_ = operation::none;
        clear_proofs();
    }
    bool holding() const { return holding_; }
    bool active() const { return phase_ == session_phase::active || phase_ == session_phase::binding; }
    bool optional_live(double now) const {
        return phase_ == session_phase::active && std::isfinite(now) && now >= renewed_ && now - renewed_ < 2 &&
            (host_ || (world_ && world_->live(now)));
    }
    void alignment_changed() { stable_since_ = -1; }
    // Local UI intentions never change masks. The backend consumes the bounded
    // FIFO separately from native preparation/attachment acknowledgements.
    bool queue_intent(uint32_t selection, session_intent action) {
        if (phase_ != session_phase::active || !valid_selection(selection) ||
            action == session_intent::none || static_cast<uint32_t>(action) > 6 ||
            ((action == session_intent::auto_on || action == session_intent::auto_off) && selection != select_quadrant) ||
            intent_count_ == 8 || intent_serial_ == UINT64_MAX) return false;
        intents_[(intent_head_ + intent_count_++) % 8] = {++intent_serial_, selection, action};
        if (action == session_intent::auto_on || action == session_intent::auto_off) return true;
        automatic_waiting_ &= ~selection;
        intent_waiting_ &= ~selection; intent_other_ &= ~selection;
        intent_paused_ &= ~selection;
        if (action == session_intent::cancel) intent_paused_ |= selection;
        if (action == session_intent::me || action == session_intent::force) intent_waiting_ |= selection;
        else if (action == session_intent::other) intent_other_ |= selection;
        return true;
    }
    uint32_t paused_intents() const { return intent_paused_; }
    bool queue_seat_intent(session_intent action, uint64_t revision, uint64_t ticket) {
        if (phase_ != session_phase::active || !seat_intent(action) || !revision || revision != seat_revision_ ||
            intent_count_ == 8 || intent_serial_ == UINT64_MAX) return false;
        if (action == session_intent::seat_request ? seat_pending_ || ticket :
            !seat_pending_ || ticket != seat_request_ || (action == session_intent::seat_cancel ? seat_pending_ != 1 && seat_pending_ != 3 :
                action == session_intent::seat_decline ? seat_pending_ != 2 && seat_pending_ != 4 : seat_pending_ != 2)) return false;
        intents_[(intent_head_ + intent_count_++) % 8] = {++intent_serial_, 0, action, revision, ticket};
        return true; // Neither a seat request nor its confirmation changes a control mask.
    }
    bool arm(const char* consent, double now) {
        if (!consent || !bridge_token(consent, 33, 32) || active() || barometers_.holding() || guidance_.holding() || (holding_ && world_ && world_->frozen_owned()) || !runtime_.ready || runtime_.applying ||
            !std::isfinite(now) || now < 0 || arm_generation_ == UINT64_MAX) return false;
        guidance_.reset(); // Only a fresh local join clears the AP local-only latch.
        uint64_t hash = 14695981039346656037ull;
        const char* seeds[] = {native_, consent};
        for (const char* value : seeds) for (const char* current = value; *current; ++current) {
            hash ^= static_cast<unsigned char>(*current); hash *= 1099511628211ull;
        }
        // The native counter makes every local arm distinct even if an EFB
        // callback repeats its random seed. Keep it across aircraft resets.
        std::snprintf(consent_, sizeof(consent_), "%016llx%016llx", static_cast<unsigned long long>(hash),
            static_cast<unsigned long long>(++arm_generation_));
        binding_ = writer_binding{}; state_ = writer_state{}; writer_.initialize(native_);
        phase_ = session_phase::armed; result_ = session_result::idle; code_ = session_error::none;
        received_ = ack_ = 0; host_ = false; operation_ = operation::none; armed_until_ = now + 60;
        clear_proofs(); return true;
    }
    bool recover_local(double now = 0) {
        if (active() || !holding_) return false;
        if (!barometers_.recover(now)) return false;
        if (!guidance_.recover(now)) return false;
        if (world_ && !world_->recover(now)) return false;
        holding_ = false; phase_ = session_phase::idle; operation_ = operation::none;
        world_mode_ = world_started_ = false;
        consent_[0] = 0; writer_.recover_local(); return true;
    }
    void stop(session_error code = session_error::native_failed) {
        if (holding_ && phase_ != session_phase::ended) held_mask_ = runtime_.blocked;
        writer_.end(); phase_ = session_phase::ended; operation_ = operation::none;
        barometers_.stop();
        guidance_.stop();
        if (world_mode_ && world_) world_->end();
        clear_intents();
        automatic_ = automatic_waiting_ = 0;
        seat_revision_ = seat_request_ = 0; seat_ = seat_pending_ = 0;
        result_ = session_result::closed; code_ = code; approved_ = 0; request_ = session_packet{};
        for (unsigned index = 0; index < 4; ++index) { eligible_[index] = 0; forced_[index] = false; prepared_[index][0] = 0; }
    }
    bool receive(const session_packet& p, double now) {
        if (!session_valid(p) || p.kind == session_kind::status || std::strcmp(p.native, native_)) return false;
        if (p.kind == session_kind::bind) return bind(p, now);
        if (!active() || !same_binding(p) || p.command <= received_ || !std::isfinite(now) || now < renewed_) return false;
        received_ = p.command;
        if (p.kind == session_kind::end) { ack_ = p.command; stop(session_error::none); return true; }
        if (p.kind == session_kind::barometer) {
            if (now - renewed_ >= session_recovery_seconds) { stop(session_error::expired); return false; }
            const auto body = barometer_payload(p);
            if (phase_ != session_phase::active || p.revision != state_.revision) {
                barometers_.reject(p.command, body.action == barometer_action::gate || body.action == barometer_action::stop); return false;
            }
            return barometers_.receive(body, p.command, seat_revision_, seat_, now); // Never renew the control lease or replace its proof.
        }
        if (p.kind == session_kind::instrument_feedback) {
            if (now - renewed_ >= session_recovery_seconds) { stop(session_error::expired); return false; }
            const auto body = instrument_payload(p);
            if (phase_ != session_phase::active || p.revision != state_.revision || body.seat_revision != seat_revision_ ||
                body.epoch < instrument_ui_.epoch || body.preparation < instrument_ui_.preparation || body.revision < instrument_ui_.revision) return false;
            instrument_ui_ = body; instrument_ui_at_ = now;
            return true; // Descriptive EFB state changes neither input masks nor the physical-control lease/proof.
        }
        if (p.kind == session_kind::world) return receive_world(p, now);
        if (p.kind == session_kind::input) {
            if (!host_ || phase_ != session_phase::active) return false;
            const auto index = selection_index(p.selection);
            if (index >= 4) return false;
            writer_frame frame{}; frame.binding = binding_; frame.revision = p.revision;
            frame.generation = p.generation[index]; frame.sequence = p.sequence; frame.axes = p.axes;
            frame.target = index == 0 ? group::steering : index == 1 ? group::throttle : index == 2 ? group::propeller : group::mixture;
            std::memcpy(frame.positions, p.positions, sizeof(frame.positions)); return writer_.apply(frame, now);
        }
        if (now - renewed_ >= session_recovery_seconds) { stop(session_error::expired); return false; }
        if (p.kind == session_kind::intent_ack) {
            if (!intent_count_ || p.sequence != intents_[intent_head_].id) return false;
            intent_head_ = (intent_head_ + 1) % 8; --intent_count_;
            return true; // Does not renew the lease or replace a native proof.
        }
        if (p.kind == session_kind::state) return change_state(p, now);
        if (p.kind == session_kind::feedback) {
            if (phase_ != session_phase::active || p.revision != state_.revision || p.automatic != automatic_) return false;
            if (p.seat_revision < seat_revision_ || (p.seat_revision == seat_revision_ &&
                (p.seat != seat_ || p.seat_pending != seat_pending_ || p.seat_request != seat_request_))) return false;
            seat_revision_ = p.seat_revision; seat_ = p.seat; seat_pending_ = p.seat_pending; seat_request_ = p.seat_request;
            automatic_waiting_ = p.automatic_waiting;
            return true; // UI feedback changes neither hardware nor the lease/proof.
        }
        if (p.revision != state_.revision) return reject(p.command, session_error::stale_authority);
        renewed_ = now;
        if (phase_ == session_phase::active && !writer_.update(binding_, state_, now)) { stop(); return false; }
        if (p.kind == session_kind::heartbeat) return true; // Never replace a pending operation's acknowledgement.
        if (phase_ != session_phase::active || operation_ != operation::none || !matches_epochs(p)) return reject(p.command, session_error::invalid_state);
        if (p.kind == session_kind::prepare) {
            if (p.flags & session_host || !session_token(p.ticket, sizeof(p.ticket)) || (state_.pending & p.selection) != p.selection) return reject(p.command, session_error::invalid_state);
            for (unsigned index = 0; index < 4; ++index) if (p.selection & (1u << index))
                if (std::strcmp(p.ticket, pending_[index]) || state_.generation[index] == UINT64_MAX) return reject(p.command, session_error::invalid_state);
            start(p, operation::prepare, runtime_.blocked | p.selection); tick(now); return true;
        }
        if (p.kind == session_kind::attach) {
            if (p.flags) return reject(p.command, session_error::invalid_state);
            for (unsigned index = 0; index < 4; ++index) if (p.selection & (1u << index))
                if (state_.owner[index] != writer_owner::local || (state_.pending & (1u << index)) ||
                    eligible_[index] != state_.generation[index] || std::strcmp(p.ticket, prepared_[index])) return reject(p.command, session_error::not_prepared);
            start(p, operation::align, runtime_.blocked & ~p.selection); tick(now); return true;
        }
        return reject(p.command, session_error::invalid_state);
    }
    void tick(double now, bool transport_available = true) {
        if (phase_ == session_phase::ended) barometers_.tick(now, seat_);
        if (phase_ == session_phase::armed && now >= armed_until_) {
            phase_ = holding_ ? session_phase::ended : session_phase::idle; consent_[0] = 0;
        }
        if (active()) {
            if (!transport_available) { stop(session_error::transport_failed); return; }
            if (!std::isfinite(now) || now < renewed_ || now - renewed_ >= session_recovery_seconds) { stop(session_error::expired); return; }
            if (!runtime_.ready) { stop(); return; }
            if (phase_ == session_phase::active && !writer_.active()) { stop(); return; }
            if (phase_ == session_phase::active && world_mode_ && (!world_ || !world_->live(now))) { stop(); return; }
            if (phase_ == session_phase::active) {
                barometers_.tick(now, seat_);
                if (const auto* queued = guidance_.queued()) if (!guidance_scope(*queued)) guidance_.reject_queued();
                guidance_.tick(now, state_.generation[0], state_.owner[0]);
            }
        }
        if (phase_ == session_phase::ended) {
            // Cancel an in-flight attachment by restoring the last confirmed
            // mask after its barrier. Never report the abandoned attachment.
            if (holding_ && runtime_.ready && !runtime_.applying && runtime_.blocked != held_mask_)
                if (!runtime_.managed_mask(held_mask_)) code_ = session_error::native_failed;
            return;
        }
        if (phase_ == session_phase::active) for (unsigned index = 0; index < 4; ++index) {
            const auto bit = 1u << index;
            if (!(state_.attached & bit) || state_.pending & bit) continue;
            if (state_.owner[index] == writer_owner::local && !(runtime_.blocked & bit)) intent_waiting_ &= ~bit;
            if (state_.owner[index] == writer_owner::remote) intent_other_ &= ~bit;
        }
        if (!active() || operation_ == operation::none || runtime_.applying) return;
        if (operation_ == operation::align) {
            if (!alignment(now, false)) return;
            operation_ = operation::attach;
        }
        if (!mask_requested_) {
            if (!runtime_.managed_mask(desired_mask_)) { stop(); return; }
            expected_barrier_ = runtime_.barrier; mask_requested_ = true; return;
        }
        if (runtime_.barrier != expected_barrier_ || runtime_.blocked != desired_mask_) { stop(); return; }
        if (operation_ == operation::binding) {
            if (world_mode_) {
                if (!world_) { stop(); return; }
                if (!world_started_) {
                    world_started_ = true;
                    if (!world_->begin(host_, now)) { stop(); return; }
                }
                if (!world_->ready(now)) return;
            }
            if (!writer_.begin(binding_, state_, now)) { stop(); return; }
            if (world_mode_) world_->arm_stream(now);
            phase_ = session_phase::active;
        } else if (operation_ == operation::prepare) {
            if ((request_.flags & session_incoming) && !alignment(now, !!(request_.flags & session_force))) return;
            for (unsigned index = 0; index < 4; ++index) if ((request_.selection & (1u << index)) && (request_.flags & session_incoming)) {
                eligible_[index] = state_.generation[index] + 1; forced_[index] = !!(request_.flags & session_force);
                std::snprintf(prepared_[index], sizeof(prepared_[index]), "%s", request_.ticket);
            }
        } else if (operation_ == operation::attach) approved_ |= request_.selection;
        operation_ = operation::none; result_ = session_result::succeeded; code_ = session_error::none;
    }
    session_packet status() {
        session_packet p{}; p.serial = ++serial_; p.phase = phase_; p.result = result_; p.code = code_;
        std::snprintf(p.native, sizeof(p.native), "%s", native_); std::snprintf(p.consent, sizeof(p.consent), "%s", consent_);
        std::memcpy(p.backend, binding_.backend, sizeof(p.backend)); std::memcpy(p.session, binding_.session, sizeof(p.session));
        p.epoch = binding_.epoch; p.command = received_; p.ack = ack_; p.revision = state_.revision;
        std::memcpy(p.generation, state_.generation, sizeof(p.generation)); std::memcpy(p.owner, state_.owner, sizeof(p.owner));
        p.attached = state_.attached; p.pending = state_.pending; p.engines = state_.engines; p.axes = state_.axes;
        std::memcpy(p.pending_transfer, pending_, sizeof(p.pending_transfer));
        std::memcpy(p.committed_transfer, committed_, sizeof(p.committed_transfer));
        p.flags = host_ ? session_host : 0u; p.selection = request_.selection;
        std::memcpy(p.ticket, request_.ticket, sizeof(p.ticket)); p.blocked = runtime_.blocked; p.barrier = runtime_.barrier;
        if (intent_count_) {
            const auto& intent = intents_[intent_head_];
            p.intent_id = intent.id; p.intent_selection = intent.selection; p.intent_action = intent.action;
            p.intent_seat_revision = intent.seat_revision; p.intent_seat_request = intent.seat_request;
        }
        p.intent_waiting = intent_waiting_; p.intent_other = intent_other_;
        p.automatic = automatic_; p.automatic_waiting = automatic_waiting_;
        p.seat_revision = seat_revision_; p.seat = seat_; p.seat_pending = seat_pending_; p.seat_request = seat_request_;
        p.barometer_gate_ack = barometers_.gate_ack; p.barometer_value_ack = barometers_.value_ack;
        if (world_mode_) p.flags |= session_world;
        return p;
    }
private:
    enum class operation { none, binding, state, prepare, align, attach } operation_ = operation::none;
    control_pickup& pickup_; Runtime& runtime_; Writer& writer_;
    control_world* world_ = nullptr;
    control_barometer_channel barometers_;
    control_guidance_channel guidance_;
    uint64_t guidance_serial_ = 0;
    bool guidance_scope(const guidance_packet& p) const {
        if (phase_ != session_phase::active || std::strcmp(p.native, native_) || std::strcmp(p.backend, binding_.backend) ||
            std::strcmp(p.session, binding_.session) || std::strcmp(p.consent, consent_) || p.epoch != binding_.epoch || p.host != static_cast<uint32_t>(host_)) return false;
        if (!host_ && (!world_mode_ || !world_ || !world_->frozen_owned())) return false;
        return !p.steering_generation || (p.steering_generation == state_.generation[0] && p.steering_owner == static_cast<uint32_t>(state_.owner[0]));
    }
    instrument_feedback instrument_ui_{};
    double instrument_ui_at_ = -1;
    bool world_mode_ = false, world_started_ = false;
    uint64_t world_sequence_ = 0;
    char native_[24]{}, consent_[40]{}, pending_[4][40]{}, prepared_[4][40]{}, committed_[4][40]{};
    writer_binding binding_{}; writer_state state_{}; session_packet request_{};
    session_phase phase_ = session_phase::idle; session_result result_ = session_result::idle;
    session_error code_ = session_error::none;
    uint64_t serial_ = 0, received_ = 0, ack_ = 0, eligible_[4]{}, arm_generation_ = 0;
    uint32_t approved_ = 0, held_mask_ = 0, desired_mask_ = 0, expected_barrier_ = 0;
    bool holding_ = false, host_ = false, mask_requested_ = false, forced_[4]{};
    double armed_until_ = -1, renewed_ = -1, stable_since_ = -1;
    struct local_intent { uint64_t id; uint32_t selection; session_intent action; uint64_t seat_revision = 0, seat_request = 0; } intents_[8]{};
    uint64_t intent_serial_ = 0;
    unsigned intent_head_ = 0, intent_count_ = 0;
    uint32_t intent_waiting_ = 0, intent_other_ = 0, intent_paused_ = 0;
    uint32_t automatic_ = 0, automatic_waiting_ = 0;
    uint64_t seat_revision_ = 0, seat_request_ = 0;
    uint32_t seat_ = 0, seat_pending_ = 0;
    void clear_intents() { intent_head_ = intent_count_ = 0; intent_waiting_ = intent_other_ = intent_paused_ = 0; }
    void clear_proofs() {
        barometers_.reset();
        instrument_ui_ = instrument_feedback{}; instrument_ui_at_ = -1;
        seat_revision_ = seat_request_ = 0; seat_ = seat_pending_ = 0;
        world_mode_ = world_started_ = false; world_sequence_ = 0;
        clear_intents();
        automatic_ = automatic_waiting_ = 0;
        approved_ = 0; request_ = session_packet{};
        std::memset(pending_, 0, sizeof(pending_)); std::memset(prepared_, 0, sizeof(prepared_));
        std::memset(committed_, 0, sizeof(committed_));
        for (unsigned index = 0; index < 4; ++index) { eligible_[index] = 0; forced_[index] = false; }
    }
    bool same_binding(const session_packet& p) const {
        return !std::strcmp(p.consent, consent_) && !std::strcmp(p.backend, binding_.backend) &&
            !std::strcmp(p.session, binding_.session) && p.epoch == binding_.epoch;
    }
    bool matches_epochs(const session_packet& p) const {
        if (!valid_selection(p.selection)) return false;
        for (unsigned index = 0; index < 4; ++index) if ((p.selection & (1u << index)) && p.generation[index] != state_.generation[index]) return false;
        return true;
    }
    bool reject(uint64_t command, session_error code) {
        // A new malformed operation cannot destroy an existing pending proof.
        if (operation_ == operation::none) { ack_ = command; result_ = session_result::rejected; code_ = code; }
        return false;
    }
    void start(const session_packet& p, operation op, uint32_t mask) {
        request_ = p; operation_ = op; desired_mask_ = mask; mask_requested_ = false;
        expected_barrier_ = 0; stable_since_ = -1; ack_ = p.command;
        result_ = session_result::pending; code_ = session_error::none;
    }
    bool receive_world(const session_packet& p, double now) {
        if (host_ || !world_mode_ || !world_ || phase_ != session_phase::active || !runtime_.ready || runtime_.applying ||
            now - renewed_ >= session_recovery_seconds || p.revision != state_.revision || p.axes != state_.axes ||
            !p.sequence || p.sequence <= world_sequence_ || !world_->ready(now)) return false;
        for (unsigned index = 0; index < 4; ++index) if (p.generation[index] != state_.generation[index]) return false;
        for (size_t axis = 0; axis < axis_count; ++axis) if ((state_.axes & (1u << axis)) && p.positions[axis] < 0) return false;
        // Validate every remote-owned control plan before exposing any part of
        // the world packet to the SDK. Local owners are never mirrored.
        for (unsigned index = 0; index < 4; ++index) {
            const auto bit = 1u << index;
            if (!(runtime_.blocked & bit) || (state_.owner[index] == writer_owner::local && (state_.attached & bit) && !(state_.pending & bit)) || (index == 0 && pickup_.autopilot)) continue;
            const auto target = index == 0 ? group::steering : index == 1 ? group::throttle : index == 2 ? group::propeller : group::mixture;
            uint32_t required = 0;
            for (size_t axis = 0; axis < axis_count; ++axis) if ((state_.axes & (1u << axis)) && control_pickup::matches_group(axis, target)) required |= 1u << axis;
            output_action plan[axis_count]{}; size_t count = 0;
            if (!group_output(pickup_, target, required, p.positions, plan, count)) return false;
        }
        world_sequence_ = p.sequence; // A partial SDK batch cannot be replayed.
        if (!world_->write(p.world_values, now)) { stop(); return false; }
        for (unsigned index = 0; index < 4; ++index) {
            const auto bit = 1u << index;
            if (!(runtime_.blocked & bit) || (state_.owner[index] == writer_owner::local && (state_.attached & bit) && !(state_.pending & bit)) || (index == 0 && pickup_.autopilot)) continue;
            writer_frame frame{}; frame.binding = binding_; frame.revision = state_.revision;
            frame.generation = state_.generation[index]; frame.sequence = p.sequence;
            frame.target = index == 0 ? group::steering : index == 1 ? group::throttle : index == 2 ? group::propeller : group::mixture;
            for (size_t axis = 0; axis < axis_count; ++axis) if ((state_.axes & (1u << axis)) && control_pickup::matches_group(axis, frame.target)) frame.axes |= 1u << axis;
            std::memcpy(frame.positions, p.positions, sizeof(frame.positions));
            if (!writer_.mirror(frame, now)) { stop(); return false; }
        }
        return true;
    }
    bool bind(const session_packet& p, double now) {
        const auto state = session_state(p);
        if (phase_ != session_phase::armed || now >= armed_until_ || !std::isfinite(now) || !std::strcmp(consent_, "") ||
            std::strcmp(p.consent, consent_) || p.automatic || p.flags & ~(session_host | session_world) || !runtime_.ready || runtime_.applying ||
            !Writer::valid_state_data(state) || state.attached || state.pending || state.engines != pickup_.engines ||
            !(pickup_.target_time >= 0 && now >= pickup_.target_time && now - pickup_.target_time <= 1)) return false;
        for (size_t axis = 0; axis < axis_count; ++axis) if (pickup_.required_axis(axis) && !(state.axes & (1u << axis))) return false;
        for (const auto& ticket : p.committed_transfer) if (ticket[0]) return false;
        binding_ = session_binding(p); state_ = state; received_ = p.command; renewed_ = now;
        automatic_ = p.automatic; automatic_waiting_ = 0;
        host_ = !!(p.flags & session_host); holding_ = true; phase_ = session_phase::binding;
        world_mode_ = !!(p.flags & session_world); world_started_ = false; world_sequence_ = 0;
        for (unsigned index = 0; index < 4; ++index) if (state_.owner[index] == writer_owner::local) {
            eligible_[index] = state_.generation[index];
            std::snprintf(prepared_[index], sizeof(prepared_[index]), "%s", binding_.session);
        }
        start(p, operation::binding, select_all); tick(now); return true;
    }
    bool change_state(const session_packet& p, double now) {
        const auto next = session_state(p);
        if (phase_ != session_phase::active || p.flags || !Writer::valid_state_data(next) ||
            (next.revision == state_.revision && p.automatic != automatic_)) { stop(session_error::invalid_state); return false; }
        for (unsigned index = 0; index < 4; ++index) {
            if ((next.revision == state_.revision && std::strcmp(p.pending_transfer[index], pending_[index])) ||
                (next.generation[index] == state_.generation[index] && std::strcmp(p.committed_transfer[index], committed_[index]))) {
                stop(session_error::invalid_state); return false;
            }
        }
        for (unsigned index = 0; index < 4; ++index) if ((next.attached & (1u << index)) && next.owner[index] == writer_owner::local &&
            (!(approved_ & (1u << index)) || next.generation[index] != state_.generation[index])) { stop(session_error::not_prepared); return false; }
        if (!writer_.update(binding_, next, now)) { stop(session_error::stale_authority); return false; }
        for (unsigned index = 0; index < 4; ++index) {
            const auto bit = 1u << index;
            if (next.owner[index] != writer_owner::local || next.generation[index] != state_.generation[index] || next.pending & bit || !(next.attached & bit)) approved_ &= ~bit;
            const bool preparing = (next.pending & bit) && next.generation[index] != UINT64_MAX && eligible_[index] == next.generation[index] + 1 && !std::strcmp(prepared_[index], p.pending_transfer[index]);
            const bool initial = !p.committed_transfer[index][0] && !std::strcmp(prepared_[index], binding_.session);
            const bool committed = !(next.pending & bit) && next.owner[index] == writer_owner::local && eligible_[index] == next.generation[index] &&
                (initial || !std::strcmp(prepared_[index], p.committed_transfer[index]));
            if (!preparing && !committed) { eligible_[index] = 0; forced_[index] = false; prepared_[index][0] = 0; }
        }
        state_ = next; std::memcpy(pending_, p.pending_transfer, sizeof(pending_)); renewed_ = now;
        automatic_ = p.automatic; automatic_waiting_ = 0;
        std::memcpy(committed_, p.committed_transfer, sizeof(committed_));
        start(p, operation::state, select_all & ~approved_); tick(now); return true;
    }
    bool alignment(double now, bool force) {
        if (operation_ == operation::align) {
            force = true;
            for (unsigned index = 0; index < 4; ++index) if ((request_.selection & (1u << index)) && !forced_[index]) force = false;
        }
        if (force) return true;
        if (!runtime_.aligned(request_.selection, now)) { stable_since_ = -1; return false; }
        if (stable_since_ < 0) stable_since_ = now;
        return now - stable_since_ >= 0.3;
    }
};
}
