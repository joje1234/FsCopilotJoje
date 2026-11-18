#define FSC_SESSION_FIXTURE_ONLY
#define FSC_CONNECTION_EPHEMERAL
#include "session_test.cpp"

int main() {
    const char* good = "5|connection-test|1|connect|127.0.0.1|3600|pilot001|copil001|host|copilot";
    command parsed{}; assert(parse_command(good, std::strlen(good), parsed));
    assert(parsed.operation == action::connect && parsed.connection.role == 1 && parsed.connection.seat == 2);
    assert(connection_valid(parsed.connection));
    const char* malformed[] = {
        "5|test|1|connect|bad/host|3600|pilot001|copil001|host|pilot",
        "5|test|1|connect|localhost|65536|pilot001|copil001|host|pilot",
        "5|test|1|connect|localhost|0|pilot001|copil001|host|pilot",
        "5|test|1|connect|localhost|3600|pilot001|pilot001|host|pilot",
        "5|test|1|connect|localhost|3600|PILOT001|copil001|host|pilot",
        "5|test|1|connect|localhost|3600|pilot001|copil001|bad|pilot",
        "5|test|1|connect|localhost|3600|pilot001|copil001|host|bad",
        "4|test|1|connect|localhost|3600|pilot001|copil001|host|pilot",
        "5|test|1|connect|localhost|3600|pilot001|copil001|host|pilot|extra"
    };
    for (const auto* value : malformed) { command invalid{}; assert(!parse_command(value, std::strlen(value), invalid)); }
    for (const auto* value : {"-host", "host-", "host..local", ".host", "host.", "a:b", "bad\"host"}) assert(!connection_host(value, std::strlen(value) + 1));
    const char* prefix = "artifacts/efb-connection/tests/saved-connection";
    const char* first = "artifacts/efb-connection/tests/saved-connection.1.bin";
    const char* second = "artifacts/efb-connection/tests/saved-connection.0.bin";
    std::remove(first); std::remove(second);
    auto preferences = parsed.connection; assert(connection_write(prefix, preferences));
    ++preferences.revision; preferences.role = 2; preferences.seat = 1; assert(connection_write(prefix, preferences));
    connection_settings restored{}; assert(connection_read(prefix, restored) && !std::memcmp(&restored, &preferences, sizeof(restored)));
    // A partial newer slot retains the previous valid configuration.
    auto* corrupt = std::fopen(second, "wb"); assert(corrupt); assert(std::fwrite("partial", 1, 7, corrupt) == 7); assert(!std::fclose(corrupt));
    assert(connection_read(prefix, restored) && restored.revision == 1 && restored.role == 1 && restored.seat == 2);
    connection_file bad{}; bad.settings = preferences; bad.checksum = connection_hash(bad); bad.settings.port = 9;
    corrupt = std::fopen(second, "wb"); assert(corrupt); assert(std::fwrite(&bad, 1, sizeof(bad), corrupt) == sizeof(bad)); assert(!std::fclose(corrupt));
    assert(connection_read(prefix, restored) && restored.revision == 1);
    assert(!connection_write("artifacts/efb-connection/absent/path/settings", preferences));
    std::remove(first); std::remove(second);
    {
        fixture f; const auto barrier = gate.barrier;
        receive_command(good, static_cast<unsigned>(std::strlen(good)), nullptr);
        assert(connection_configuration.revision == 1 && connection_configuration.seat == 2);
        assert(!gate.blocked && gate.barrier == barrier && shared_session.status().phase == session_phase::idle);
        auto observed = bridge_status(); assert(observed.connection_api == 1 && observed.configuration.revision == 1 && bridge_valid(observed));
        bridge_packet report{}; report.kind = bridge_kind::backend; report.connection_api = 1; report.capabilities = 0;
        std::snprintf(report.native, sizeof(report.native), "%s", session);
        std::snprintf(report.backend, sizeof(report.backend), "%s", f.authority.backend);
        report.request = 1; report.configuration.revision = 1; report.backend_phase = 2; assert(bridge_valid(report));
        auto send = [&](bridge_packet value) {
            std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_CLIENT_DATA) + sizeof(value));
            auto* packet = reinterpret_cast<SIMCONNECT_RECV_CLIENT_DATA*>(bytes.data()); packet->dwID = SIMCONNECT_RECV_ID_CLIENT_DATA;
            packet->dwRequestID = packet->dwDefineID = control_bridge_transport::command_id;
            std::memcpy(&packet->dwData, &value, sizeof(value));
            const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&packet->dwData) - reinterpret_cast<char*>(packet) + sizeof(value));
            bridge_dispatch(packet, size, nullptr);
        };
        send(report); assert(live_backend_phase(seconds()) == 2 && !gate.blocked && gate.barrier == barrier);
        auto stale = report; stale.backend_phase = 5; send(stale); assert(backend_phase == 2);
        stale.request = 2; stale.configuration.revision = 2; send(stale); assert(backend_phase == 2);
        assert(!live_backend_phase(backend_reported + 3.1));
        f.bind(); const auto proof = shared_session.status(); report.request = 2; report.backend_phase = 5; send(report);
        assert(backend_phase == 5 && shared_session.status().ack == proof.ack && gate.blocked == select_all);
        receive_command(good, static_cast<unsigned>(std::strlen(good)), nullptr); assert(connection_configuration.revision == 1);
        f.tick(2.01); assert(shared_session.status().phase == session_phase::ended && gate.blocked == select_all);
        assert(fake_sim::transmissions.empty());
    }
    std::cout << "Connection settings: strict parser, independent role/seat, alternating durable slots, corruption fallback, no consent/mask changes, scoped backend heartbeat and lease isolation passed.\n";
}
