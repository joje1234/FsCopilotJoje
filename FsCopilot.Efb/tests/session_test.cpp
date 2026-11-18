// Actual native session, mask adapter, writer and transport; all SDK calls are
// captured locally. No connection to MSFS or actual aircraft/control writes.
#define _CRT_SECURE_NO_WARNINGS
#define FSC_PROFILE_ROOT "artifacts/efb-control-test/tests/"
#include <cassert>
#include <iostream>
#include "../Wasm/control_test.cpp"

namespace {
constexpr char consent[] = "11111111111111111111111111111111";
constexpr char transfer[] = "22222222222222222222222222222222";
constexpr char transfer2[] = "33333333333333333333333333333333";
struct fixture {
    session_packet authority{};
    uint64_t next = 0;
    double now = 0;
    fixture() {
        adapter.groups_created = false; error[0] = 0;
        fake_sim::fail_configuration = false; fake_sim::fail_transmission_after = -1; fake_sim::transmissions.clear();
        module_init(); SIMCONNECT_RECV opened{}; opened.dwID = SIMCONNECT_RECV_ID_OPEN; dispatch(&opened, sizeof(opened), nullptr);
        const char* read = "5|session-test|1|read"; receive_command(read, static_cast<unsigned>(std::strlen(read)), nullptr);
        now = seconds(); assert(gate.confirm(gate.barrier, now));
        pickup.engines = 2; pickup.target_time = now;
        const char* names[] = {"AXIS_AILERONS_SET", "AXIS_ELEVATOR_SET", "AXIS_THROTTLE1_SET", "AXIS_PROPELLER1_SET", "AXIS_MIXTURE1_SET",
            "AXIS_THROTTLE2_SET", "AXIS_PROPELLER2_SET", "AXIS_MIXTURE2_SET"};
        const unsigned indices[] = {0, 1, 9, 10, 11, 12, 13, 14};
        for (unsigned item = 0; item < 8; ++item) {
            const auto axis = indices[item]; auto& input = pickup.inputs[axis];
            input.calibrated = true; input.candidates = 1;
            std::snprintf(input.event, sizeof(input.event), "%s", names[item]);
            pickup.sample(axis, 0, now); pickup.targets[axis] = 0.5; authority.axes |= 1u << axis;
        }
        std::snprintf(authority.native, sizeof(authority.native), "%s", session);
        std::snprintf(authority.backend, sizeof(authority.backend), "%s", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        std::snprintf(authority.session, sizeof(authority.session), "%s", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        std::snprintf(authority.consent, sizeof(authority.consent), "%s", consent);
        authority.epoch = 7; authority.revision = 1; authority.engines = 2;
        for (unsigned index = 0; index < 4; ++index) { authority.generation[index] = 1; authority.owner[index] = writer_owner::local; }
    }
    ~fixture() { module_deinit(); }
    session_packet command(session_kind kind, uint32_t selection = 0, const char* ticket = "") {
        auto p = authority; p.kind = kind; p.command = ++next; p.selection = selection;
        if (kind != session_kind::bind && kind != session_kind::state && kind != session_kind::feedback) p.automatic = 0;
        std::snprintf(p.ticket, sizeof(p.ticket), "%s", ticket); assert(session_valid(p)); return p;
    }
    void arm() {
        receive_session_consent(consent, sizeof(consent), nullptr); auto status = shared_session.status();
        assert(status.phase == session_phase::armed); std::memcpy(authority.consent, status.consent, sizeof(authority.consent));
    }
    void tick(double delta = 0) {
        now += delta; pickup.target_time = now;
        for (auto& input : pickup.inputs) if (input.detected) input.observed = now;
        remote_writer.tick(now); shared_session.tick(now, native_bridge.available);
    }
    void confirm() { assert(gate.applying && gate.confirm(gate.barrier, now)); shared_session.tick(now, native_bridge.available); }
    void bind(bool host = true) {
        arm(); auto p = command(session_kind::bind); p.flags = host ? session_host : 0;
        assert(shared_session.receive(p, now)); assert(gate.applying && shared_session.status().result == session_result::pending);
        confirm(); assert(shared_session.status().phase == session_phase::active && remote_writer.active());
        assert(shared_session.status().result == session_result::succeeded);
    }
    void state() { ++authority.revision; assert(shared_session.receive(command(session_kind::state), now)); confirm(); }
    void attach(uint32_t selection, const char* ticket) {
        assert(shared_session.receive(command(session_kind::attach, selection, ticket), now));
        tick(0.31); assert(gate.applying && shared_session.status().result == session_result::pending);
        confirm(); assert(shared_session.status().result == session_result::succeeded);
        authority.attached |= selection; state();
    }
    void pending(const char* ticket = transfer) {
        authority.attached &= ~select_throttle; authority.pending = select_throttle;
        std::snprintf(authority.pending_transfer[1], sizeof(authority.pending_transfer[1]), "%s", ticket); state();
    }
    void commit(writer_owner owner) {
        std::memcpy(authority.committed_transfer[1], authority.pending_transfer[1], sizeof(authority.committed_transfer[1]));
        ++authority.generation[1]; authority.owner[1] = owner; authority.pending = 0;
        std::memset(authority.pending_transfer[1], 0, sizeof(authority.pending_transfer[1])); state();
    }
    session_packet input() {
        auto p = command(session_kind::input, select_throttle); p.sequence = 1; p.axes = (1u << 9) | (1u << 12);
        p.positions[9] = 0.25; p.positions[12] = 0.75; return p;
    }
    void remote() {
        authority.owner[1] = writer_owner::remote; ++authority.generation[1]; authority.attached = select_throttle;
        std::snprintf(authority.committed_transfer[1], sizeof(authority.committed_transfer[1]), "%s", "44444444444444444444444444444444"); state();
    }
};
}

#ifndef FSC_SESSION_FIXTURE_ONLY
int main(int argc, char** argv) {
    if (argc == 2) {
        auto* file = std::fopen(argv[1], "rb"); session_packet packet{};
        assert(file && std::fread(&packet, 1, sizeof(packet), file) == sizeof(packet) && std::fgetc(file) == EOF); std::fclose(file);
        assert(session_valid(packet) && packet.kind == session_kind::prepare && packet.command == 42 && packet.epoch == 7);
        assert(packet.flags == session_incoming && packet.pending == select_throttle && packet.generation[1] == 2 && packet.owner[1] == writer_owner::remote);
        assert(!std::strcmp(packet.pending_transfer[1], transfer) && !std::strcmp(packet.ticket, transfer));
        assert(!std::strcmp(packet.committed_transfer[1], "44444444444444444444444444444444"));
    }
    {
        fixture f; const auto barrier = gate.barrier;
        auto p = f.command(session_kind::bind); p.flags = session_host;
        assert(!shared_session.receive(p, f.now)); assert(gate.barrier == barrier && !gate.blocked);
        f.arm(); std::memcpy(p.consent, f.authority.consent, sizeof(p.consent)); auto stale = p; stale.consent[0] = stale.consent[0] == '2' ? '3' : '2';
        assert(!shared_session.receive(stale, f.now) && !gate.applying);
        assert(shared_session.receive(p, f.now)); assert(gate.applying && !remote_writer.active());
        assert(!shared_session.receive(p, f.now)); f.confirm(); assert(remote_writer.active());
        auto status = shared_status(); assert(session_valid(status) && status.ack == p.command && status.blocked == select_all);
        auto* file = std::fopen("artifacts/native-session/tests/session-status.bin", "wb");
        assert(file && std::fwrite(&status, 1, sizeof(status), file) == sizeof(status)); std::fclose(file);
        p = f.command(session_kind::heartbeat);
        file = std::fopen("artifacts/native-session/tests/session-command.bin", "wb");
        assert(file && std::fwrite(&p, 1, sizeof(p), file) == sizeof(p)); std::fclose(file);
        for (const auto index : {0u, 4u, 8u, 12u, 680u, 1023u}) {
            auto invalid = p; reinterpret_cast<unsigned char*>(&invalid)[index] = 255; assert(!session_valid(invalid));
        }
    }
    {
        fixture f; assert(!shared_session.optional_live(f.now)); f.bind();
        assert(shared_session.optional_live(f.now));
        assert(shared_session.optional_live(f.now + 1.99));
        assert(!shared_session.optional_live(f.now - .01));
        assert(!shared_session.optional_live(f.now + 2)); // Optional writes have a shorter guard than session retention.
        assert(!shared_session.optional_live(std::nan("")));
    }
    {
        fixture f; f.bind(); f.attach(select_throttle, f.authority.session);
        assert(gate.blocked == (select_all & ~select_throttle));
        f.pending(); const auto barrier = gate.barrier;
        auto prep = f.command(session_kind::prepare, select_throttle, transfer);
        assert(shared_session.receive(prep, f.now));
        assert(gate.applying && gate.barrier == barrier + 1); // Fresh drain proof even though throttle is already masked.
        f.confirm(); assert(shared_session.status().result == session_result::succeeded);
        f.commit(writer_owner::remote); f.authority.attached |= select_throttle; f.state();
        auto input = f.input(); assert(shared_session.receive(input, f.now)); assert(fake_sim::transmissions.size() == 2);
        assert(!shared_session.receive(input, f.now));
        f.pending(transfer2); prep = f.command(session_kind::prepare, select_throttle, transfer2); prep.flags = session_incoming;
        assert(shared_session.receive(prep, f.now)); f.confirm();
        auto status = shared_session.status(); assert(status.result == session_result::pending);
        const auto proof = status.ack;
        assert(shared_session.receive(f.command(session_kind::heartbeat), f.now)); assert(shared_session.status().ack == proof);
        f.tick(0.31); assert(shared_session.status().result == session_result::succeeded);
        f.commit(writer_owner::local); f.attach(select_throttle, transfer2);
        assert(gate.blocked == (select_all & ~select_throttle));
        input.command = ++f.next; assert(!shared_session.receive(input, f.now)); // Queued old-owner sample cannot execute.
        assert(fake_sim::transmissions.size() == 2);
    }
    {
        fixture f; f.bind(); f.remote(); f.pending();
        auto prep = f.command(session_kind::prepare, select_throttle, transfer); prep.flags = session_incoming;
        pickup.targets[9] = pickup.targets[12] = 0.8;
        assert(shared_session.receive(prep, f.now)); f.confirm(); f.tick(0.4);
        assert(shared_session.status().result == session_result::pending);
        pickup.sample(9, 9830, f.now); pickup.sample(12, 9830, f.now); f.tick(); f.tick(0.31);
        assert(shared_session.status().result == session_result::succeeded);
        f.authority.pending = 0; std::memset(f.authority.pending_transfer[1], 0, sizeof(f.authority.pending_transfer[1])); f.state(); // Cancel completed preparation.
        f.pending(transfer2); f.commit(writer_owner::local);
        assert(!shared_session.receive(f.command(session_kind::attach, select_throttle, transfer), f.now));
        assert(shared_session.status().code == session_error::not_prepared && gate.blocked == select_all);
    }
    {
        fixture f; f.bind(); f.pending();
        auto prep = f.command(session_kind::prepare, select_throttle, transfer); prep.flags = session_incoming;
        assert(shared_session.receive(prep, f.now)); f.confirm(); f.tick(0.31);
        assert(shared_session.status().result == session_result::succeeded);
        ++f.authority.generation[1]; f.authority.pending = 0;
        std::memset(f.authority.pending_transfer[1], 0, sizeof(f.authority.pending_transfer[1])); f.state();
        // Cancellation and same-owner commit both advance the generation. Only
        // the actual committing ticket can preserve this prepared attachment.
        assert(!shared_session.receive(f.command(session_kind::attach, select_throttle, transfer), f.now));
        assert(shared_session.status().code == session_error::not_prepared && gate.blocked == select_all);
    }
    {
        fixture f; f.bind(); f.remote(); f.pending();
        auto prep = f.command(session_kind::prepare, select_throttle, transfer); prep.flags = session_incoming | session_force;
        pickup.targets[9] = pickup.targets[12] = 0.95;
        assert(shared_session.receive(prep, f.now)); f.confirm();
        assert(shared_session.status().result == session_result::succeeded); f.commit(writer_owner::local);
        assert(shared_session.receive(f.command(session_kind::attach, select_throttle, transfer), f.now));
        assert(gate.applying); f.confirm(); assert(gate.blocked == (select_all & ~select_throttle));
    }
    {
        fixture f; f.bind(); f.remote(); f.pending();
        auto prep = f.command(session_kind::prepare, select_throttle, transfer); prep.flags = session_incoming;
        assert(shared_session.receive(prep, f.now)); f.confirm(); f.tick(0.2);
        const char* tolerance = "5|session-test|2|tolerance|all|20";
        receive_command(tolerance, static_cast<unsigned>(std::strlen(tolerance)), nullptr);
        f.tick(0.11); assert(shared_session.status().result == session_result::pending);
        f.tick(0.31); assert(shared_session.status().result == session_result::succeeded);
    }
    {
        fixture f; f.bind();
        assert(shared_session.receive(f.command(session_kind::attach, select_throttle, f.authority.session), f.now));
        f.tick(0.31); assert(gate.applying); f.tick(8.1);
        assert(shared_session.status().phase == session_phase::ended && shared_session.holding());
        f.confirm(); assert(gate.applying); // Old unmask confirms, then immediately schedules its cancellation mask.
        f.confirm(); assert(gate.blocked == select_all && shared_session.status().result == session_result::closed);
        assert(!remote_writer.active());
    }
    {
        fixture f; f.bind();
        assert(shared_session.receive(f.command(session_kind::attach, select_throttle, f.authority.session), f.now));
        f.tick(0.31); const auto abandoned = shared_session.status().ack; assert(gate.applying);
        ++f.authority.revision; f.authority.pending = select_throttle;
        std::snprintf(f.authority.pending_transfer[1], sizeof(f.authority.pending_transfer[1]), "%s", transfer);
        auto state = f.command(session_kind::state); assert(shared_session.receive(state, f.now));
        f.confirm(); assert(gate.applying && shared_session.status().ack != abandoned);
        f.confirm(); assert(gate.blocked == select_all && shared_session.status().ack == state.command);
    }
    {
        fixture f; f.bind(false); f.remote();
        assert(!shared_session.receive(f.input(), f.now) && fake_sim::transmissions.empty()); // Guest cannot run the physics-host writer.
    }
    {
        fixture f; f.bind(); f.authority.attached |= select_throttle;
        ++f.authority.revision; assert(!shared_session.receive(f.command(session_kind::state), f.now));
        assert(shared_session.status().phase == session_phase::ended && gate.blocked == select_all);
        assert(session_valid(shared_session.status())); // A host snapshot cannot skip the local native attachment proof.
    }
    {
        fixture f; f.arm();
        auto automatic_bind = f.command(session_kind::bind); automatic_bind.automatic = 1;
        assert(!shared_session.receive(automatic_bind, f.now) && !gate.blocked);
        f.tick(60.1);
        assert(shared_session.status().phase == session_phase::idle && !gate.blocked && !shared_session.holding());
        assert(!shared_session.receive(f.command(session_kind::bind), f.now));
    }
    {
        fixture f; f.arm(); auto retired = f.command(session_kind::bind);
        f.arm(); assert(std::strcmp(retired.consent, f.authority.consent));
        assert(!shared_session.receive(retired, f.now) && !gate.applying); // Repeated EFB seed still creates fresh native consent.
        const auto before = shared_session.status(); shared_session.initialize(session); const auto after = shared_session.status();
        assert(after.serial > before.serial && after.phase == session_phase::idle); // Same-aircraft reload cannot rewind status freshness.
        f.arm(); assert(std::strcmp(retired.consent, f.authority.consent));
        auto bind = f.command(session_kind::bind); pickup.target_time = f.now - 2;
        assert(!shared_session.receive(bind, f.now) && !gate.applying && !gate.blocked);
    }
    {
        fixture f; f.bind(); f.remote();
        auto packet = f.input(); packet.command = ++f.next;
        std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_CLIENT_DATA) + sizeof(packet));
        auto* data = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(bytes.data());
        data->dwID = SIMCONNECT_RECV_ID_CLIENT_DATA; data->dwRequestID = data->dwDefineID = control_bridge_transport::session_command_id;
        std::memcpy(&data->dwData, &packet, sizeof(packet));
        const auto length = static_cast<DWORD>(reinterpret_cast<char*>(&data->dwData) - reinterpret_cast<char*>(data) + sizeof(packet));
        session_packet decoded{}; assert(!native_bridge.receive_session(data, length - 1, decoded));
        assert(native_bridge.receive_session(data, length, decoded));
        assert(shared_session.receive(decoded, f.now) && fake_sim::transmissions.size() == 2);
        assert(native_bridge.publish_session(shared_status()));
        assert(fake_sim::client_data[control_bridge_transport::session_status_id].size() == sizeof(session_packet));
    }
    {
        fixture f; f.bind();
        f.authority.automatic = 1; f.state();
        const auto proof = shared_session.status();
        auto feedback = f.command(session_kind::feedback); feedback.automatic_waiting = select_throttle;
        assert(shared_session.receive(feedback, f.now));
        assert(shared_session.status().automatic && shared_session.status().automatic_waiting == select_throttle);
        assert(shared_session.status().ack == proof.ack && gate.blocked == proof.blocked);
        auto wrong = f.command(session_kind::feedback); wrong.automatic = 0;
        assert(!shared_session.receive(wrong, f.now));
        auto stale = f.command(session_kind::feedback); --stale.revision;
        assert(!shared_session.receive(stale, f.now) && shared_session.status().ack == proof.ack);
        assert(!shared_session.queue_intent(select_throttle, session_intent::auto_off));
        assert(shared_session.queue_intent(select_quadrant, session_intent::auto_off));
        auto automatic = shared_session.status(); assert(automatic.intent_action == session_intent::auto_off);
        auto automatic_ack = f.command(session_kind::heartbeat); automatic_ack.kind = session_kind::intent_ack; automatic_ack.sequence = automatic.intent_id;
        assert(shared_session.receive(automatic_ack, f.now));
        assert(shared_session.status().automatic); // Click delivery is not a policy acknowledgement.
        f.authority.automatic = 0; f.state();
        assert(!shared_session.status().automatic && !shared_session.status().automatic_waiting);
    }
    {
        fixture f; f.bind(); const auto proof = shared_session.status();
        const char* click = "5|efb-test|1|set|quadrant|me";
        receive_command(click, static_cast<unsigned>(std::strlen(click)), nullptr);
        auto head = shared_session.status();
        assert(head.intent_id && head.intent_selection == select_quadrant && head.intent_action == session_intent::me);
        assert(head.intent_waiting == select_quadrant && gate.blocked == select_all && !gate.applying);
        for (unsigned index = 0; index < 7; ++index) assert(shared_session.queue_intent(select_throttle, session_intent::other));
        assert(!shared_session.queue_intent(select_throttle, session_intent::force)); // Bounded; rejected click cannot alter pending feedback.
        assert(shared_session.status().intent_other == select_throttle);
        auto ack = f.command(session_kind::heartbeat); ack.kind = session_kind::intent_ack; ack.sequence = head.intent_id + 1;
        assert(!shared_session.receive(ack, f.now)); // Cannot skip an unseen request.
        ack.command = ++f.next; ack.sequence = head.intent_id; assert(shared_session.receive(ack, f.now));
        const auto next = shared_session.status();
        assert(next.intent_id > head.intent_id && next.intent_selection == select_throttle && next.intent_action == session_intent::other);
        assert(next.ack == proof.ack && next.result == proof.result && next.selection == proof.selection); // UI ack is not a mask proof.
        assert(gate.blocked == select_all && !gate.applying && fake_sim::transmissions.empty());
        assert(shared_session.queue_intent(select_throttle, session_intent::cancel));
        assert(!(shared_session.status().intent_other & select_throttle));
        const char* leave = "5|efb-test|2|leave"; receive_command(leave, static_cast<unsigned>(std::strlen(leave)), nullptr);
        const auto ended = shared_session.status(); assert(ended.phase == session_phase::ended);
        assert(!ended.intent_id && !ended.intent_waiting && !ended.intent_other && gate.blocked == select_all);
    }
    {
        fixture f; f.bind();
        auto feedback = f.command(session_kind::feedback); feedback.seat_revision = 1; feedback.seat = 2;
        assert(session_valid(feedback) && shared_session.receive(feedback, f.now));
        const auto blocked = gate.blocked, barrier = gate.barrier;
        const char* text = "5|seat-test|1|seat|request|1|0";
        command parsed{}; assert(parse_command(text, std::strlen(text), parsed) && parsed.operation == action::seat);
        receive_command(text, static_cast<unsigned>(std::strlen(text)), nullptr);
        auto status = shared_session.status();
        assert(session_valid(status) && status.intent_action == session_intent::seat_request && !status.intent_selection && status.intent_seat_revision == 1);
        assert(!shared_session.queue_seat_intent(session_intent::seat_accept, 1, 0));
        auto ack = f.command(session_kind::heartbeat); ack.kind = session_kind::intent_ack; ack.sequence = status.intent_id;
        assert(shared_session.receive(ack, f.now));
        feedback = f.command(session_kind::feedback); feedback.seat_revision = 2; feedback.seat = 2; feedback.seat_pending = 2; feedback.seat_request = 5;
        assert(shared_session.receive(feedback, f.now));
        assert(!shared_session.queue_seat_intent(session_intent::seat_accept, 1, 5));
        assert(!shared_session.queue_seat_intent(session_intent::seat_accept, 2, 4));
        assert(shared_session.queue_seat_intent(session_intent::seat_accept, 2, 5));
        status = shared_session.status(); assert(session_valid(status) && status.intent_seat_request == 5 && status.seat_pending == 2);
        auto rollback = feedback; rollback.command = ++f.next; rollback.seat_revision = 1;
        assert(!shared_session.receive(rollback, f.now));
        assert(gate.blocked == blocked && gate.barrier == barrier && fake_sim::transmissions.empty());
        const char* invalid[] = {"5|s|1|seat|request|0|0", "5|s|1|seat|accept|2|0", "5|s|1|seat|request|2|3", "5|s|1|seat|request|4294967296|0", "4|s|1|seat|request|1|0"};
        for (const auto value : invalid) assert(!parse_command(value, std::strlen(value), parsed));
        // Exercise the exported frame callback and dispatch path, not only the
        // standalone camera adapter. Pending seat requests keep the current view.
        fake_sim::object_writes.clear();
        SIMCONNECT_RECV_SYSTEM_STATE setup{}; setup.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE; setup.dwRequestID = 0x78000001u;
        dispatch(&setup, sizeof(setup), nullptr);
        auto camera_sample = [](double option) {
            std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * 4);
            auto* data = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data()); data->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA;
            data->dwRequestID = control_camera::read_request; data->dwDefineCount = 4;
            double values[] = {2, 1, option, 5}; std::memcpy(&data->dwData, values, sizeof(values));
            const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&data->dwData) - reinterpret_cast<char*>(data) + sizeof(values));
            dispatch(data, size, nullptr);
        };
        camera_sample(2); Update_StandAlone(0);
        assert(fake_sim::object_writes.size() == 1 && fake_sim::object_writes.back().definition == control_camera::write_definition);
        SIMCONNECT_RECV_SYSTEM_STATE applied{}; applied.dwID = SIMCONNECT_RECV_ID_SYSTEM_STATE; applied.dwRequestID = fake_sim::request_id;
        dispatch(&applied, sizeof(applied), nullptr);
        for (unsigned index = 0; index < 3; ++index) camera_sample(5);
        Update_StandAlone(0);
        assert(camera.phase == camera_phase::confirmed && camera.seat == 2);
        assert(gate.blocked == blocked && gate.barrier == barrier && fake_sim::transmissions.empty());
        std::cout << "PASS actual module frame selects the confirmed seat, dispatch verifies camera read-back, and masks remain unchanged.\n";
        feedback = f.command(session_kind::feedback); feedback.seat_revision = 3; feedback.seat = 2; feedback.seat_pending = 4; feedback.seat_request = 5;
        assert(shared_session.receive(feedback, f.now));
        assert(!shared_session.queue_seat_intent(session_intent::seat_accept, 3, 5));
        assert(shared_session.queue_seat_intent(session_intent::seat_decline, 3, 5));
    }
    {
        fixture f; f.bind(); const auto before = shared_session.status();
        f.tick(1.5);
        auto packet = f.command(session_kind::heartbeat); packet.kind = session_kind::barometer; packet.flags = 0; packet.axes = 0;
        for (auto& position : packet.positions) position = -1;
        session_barometer body{}; body.epoch = body.preparation = body.revision = body.seat_revision = body.seat = 1;
        body.action = barometer_action::gate; body.mask = 3; barometer_payload(packet, body);
        assert(session_valid(packet) && !shared_session.receive(packet, f.now));
        const auto rejected = shared_session.status();
        assert(rejected.ack == before.ack && rejected.result == before.result && rejected.blocked == before.blocked);
        assert(rejected.barometer_gate_ack == barometer_ack(packet.command, barometer_result::unsupported));
        f.tick(6.51); assert(shared_session.status().code == session_error::expired); // Unsupported instrument RPC is not a heartbeat.
        assert(fake_sim::transmissions.empty());
    }
    std::cout << "Native sessions: consent/replay fence, bind/mask confirmation, initial attach, bidirectional pair handover, fresh preparation barriers, alignment hold, cancellation, force proof, lease loss, superseding state, guest isolation, bounded EFB intentions, revision-bound seat confirmation and actual packet transport passed. No actual simulator writes.\n";
}
#endif
