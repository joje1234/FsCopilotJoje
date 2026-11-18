#pragma once
#include <SimConnect.h>
#include <cstdio>
#include "control_bridge_protocol.h"
#include "control_session_protocol.h"

namespace fsc_controls {
// Discovery stays separate from the consent-bound session command area.
// Optional SDK failures are classified without releasing hardware masks.
class control_bridge_transport {
public:
    static constexpr DWORD status_id = 0x7100, command_id = 0x7101, reply_id = 0x7102;
    static constexpr DWORD session_status_id = 0x7110, session_command_id = 0x7111;
    bool available = false;
    bool initialize(HANDLE connection, const char* prefix = "") {
        connection_ = connection; available = false; calls_ = 0;
        publications_count_ = checkpoint_cut_ = 0; checkpoint_ = 0;
        const char* names[] = {bridge_status_name, bridge_command_name, bridge_reply_name, session_status_name, session_command_name};
        const DWORD ids[] = {status_id, command_id, reply_id, session_status_id, session_command_id};
        for (unsigned index = 0; index < 5; ++index) {
            const DWORD id = ids[index];
            char name[128]{};
            if (std::snprintf(name, sizeof(name), "%s%s", prefix, names[index]) >= static_cast<int>(sizeof(name))) return false;
            if (!record(SimConnect_MapClientDataNameToID(connection_, name, id), false) ||
                !record(SimConnect_CreateClientData(connection_, id, bridge_packet_size, SIMCONNECT_CREATE_CLIENT_DATA_FLAG_DEFAULT), true) ||
                !record(SimConnect_AddToClientDataDefinition(connection_, id, 0, bridge_packet_size), false)) return false;
        }
        available = record(SimConnect_RequestClientData(connection_, command_id, command_id, command_id,
            SIMCONNECT_CLIENT_DATA_PERIOD_ON_SET), false) && record(SimConnect_RequestClientData(connection_, session_command_id,
                session_command_id, session_command_id, SIMCONNECT_CLIENT_DATA_PERIOD_ON_SET), false);
        return available;
    }
    bool publish(bridge_packet packet) {
        if (!available || !bridge_valid(packet) || (packet.kind != bridge_kind::status && packet.kind != bridge_kind::reply)) return false;
        return publish_bytes(packet.kind == bridge_kind::status ? status_id : reply_id, &packet, sizeof(packet));
    }
    bool publish_session(session_packet packet) {
        if (!available || !session_valid(packet) || packet.kind != session_kind::status) return false;
        return publish_bytes(session_status_id, &packet, sizeof(packet));
    }
private:
    bool publish_bytes(DWORD id, void* packet, DWORD size) {
        if (publications_count_ >= 126) { available = false; return false; }
        if (FAILED(SimConnect_SetClientData(connection_, id, id, SIMCONNECT_CLIENT_DATA_SET_FLAG_DEFAULT, 0, size, packet))) {
            available = false; return false;
        }
        DWORD sent = 0; (void)SimConnect_GetLastSentPacketID(connection_, &sent);
        publications_[publications_count_++] = sent;
        // Keep every optional call until an ordered SDK barrier acknowledges
        // it. If the barrier stops arriving, stop publishing before the bounded
        // table fills rather than lose the ability to classify an IPC error.
        if (publications_count_ >= 16 && !checkpoint_) {
            if (checkpoint_generation_ >= 0x00ffffff) { available = false; return false; }
            checkpoint_ = 0x74000000u + ++checkpoint_generation_;
            if (FAILED(SimConnect_RequestSystemState(connection_, checkpoint_, "Sim"))) { available = false; return false; }
            DWORD call = 0; (void)SimConnect_GetLastSentPacketID(connection_, &call);
            publications_[publications_count_++] = call; checkpoint_cut_ = publications_count_;
        }
        return true;
    }
public:
    bool confirm(DWORD request) {
        if (!checkpoint_ || request != checkpoint_) return false;
        for (size_t index = checkpoint_cut_; index < publications_count_; ++index) publications_[index - checkpoint_cut_] = publications_[index];
        publications_count_ -= checkpoint_cut_; checkpoint_cut_ = 0; checkpoint_ = 0;
        return true;
    }
    bool receive(const SIMCONNECT_RECV* data, DWORD size, bridge_packet& hello) const {
        if (!available || !data || data->dwID != SIMCONNECT_RECV_ID_CLIENT_DATA || size < sizeof(SIMCONNECT_RECV_CLIENT_DATA)) return false;
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_CLIENT_DATA*>(data);
        const auto offset = reinterpret_cast<const char*>(&packet->dwData) - reinterpret_cast<const char*>(data);
        if (packet->dwRequestID != command_id || packet->dwDefineID != command_id || size < offset + sizeof(hello)) return false;
        bridge_packet value{}; std::memcpy(&value, &packet->dwData, sizeof(value));
        if (!bridge_valid(value) || (value.kind != bridge_kind::hello && value.kind != bridge_kind::backend)) return false;
        hello = value; return true;
    }
    bool exception(const SIMCONNECT_RECV_EXCEPTION& error) {
        if (!error.dwSendID) return false;
        for (size_t index = 0; index < calls_; ++index) if (setup_[index].packet == error.dwSendID) {
            // Areas persist until MSFS exits; a module reload reuses them.
            if (!(setup_[index].creation && error.dwException == SIMCONNECT_EXCEPTION_ALREADY_CREATED)) available = false;
            return true;
        }
        for (size_t index = 0; index < publications_count_; ++index) if (publications_[index] == error.dwSendID) { available = false; return true; }
        return false;
    }
    bool receive_session(const SIMCONNECT_RECV* data, DWORD size, session_packet& command) const {
        if (!available || !data || data->dwID != SIMCONNECT_RECV_ID_CLIENT_DATA || size < sizeof(SIMCONNECT_RECV_CLIENT_DATA)) return false;
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_CLIENT_DATA*>(data);
        const auto offset = reinterpret_cast<const char*>(&packet->dwData) - reinterpret_cast<const char*>(data);
        if (packet->dwRequestID != session_command_id || packet->dwDefineID != session_command_id || size < offset + sizeof(command)) return false;
        session_packet value{}; std::memcpy(&value, &packet->dwData, sizeof(value));
        if (!session_valid(value) || value.kind == session_kind::status) return false;
        command = value; return true;
    }
private:
    struct setup_call { DWORD packet; bool creation; } setup_[20]{};
    DWORD publications_[128]{};
    size_t calls_ = 0, publications_count_ = 0, checkpoint_cut_ = 0;
    DWORD checkpoint_ = 0, checkpoint_generation_ = 0;
    HANDLE connection_{};
    bool record(HRESULT result, bool creation) {
        if (FAILED(result) || calls_ >= 20) return false;
        DWORD packet = 0; (void)SimConnect_GetLastSentPacketID(connection_, &packet);
        setup_[calls_++] = {packet, creation}; return true;
    }
};
}
