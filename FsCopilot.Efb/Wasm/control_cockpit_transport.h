#pragma once
#include "control_cockpit_protocol.h"

namespace fsc_controls {
// Independent SDK area and failure state: Cockpit replies cannot replace the main
// steering/world packet or renew its lease. This creates no input masks.
class control_cockpit_transport {
public:
    static constexpr DWORD status_id = 0x7130, command_id = 0x7131;
    bool available = false;
    bool initialize(HANDLE connection, const char* prefix = "") {
        connection_ = connection; available = false; calls_ = count_ = cut_ = 0; checkpoint_ = generation_ = 0;
        const char* names[] = {cockpit_status_name, cockpit_command_name}; const DWORD ids[] = {status_id, command_id};
        for (unsigned index = 0; index < 2; ++index) {
            char name[128]{};
            if (std::snprintf(name, sizeof(name), "%s%s", prefix, names[index]) >= static_cast<int>(sizeof(name))) return false;
            const auto id = ids[index];
            if (!record(SimConnect_MapClientDataNameToID(connection_, name, id)) ||
                !record(SimConnect_CreateClientData(connection_, id, sizeof(cockpit_packet), SIMCONNECT_CREATE_CLIENT_DATA_FLAG_DEFAULT), true) ||
                !record(SimConnect_AddToClientDataDefinition(connection_, id, 0, sizeof(cockpit_packet)))) return false;
        }
        available = record(SimConnect_RequestClientData(connection_, command_id, command_id, command_id, SIMCONNECT_CLIENT_DATA_PERIOD_ON_SET));
        return available;
    }
    bool receive(const SIMCONNECT_RECV* data, DWORD size, cockpit_packet& value) const {
        if (!available || !data || data->dwID != SIMCONNECT_RECV_ID_CLIENT_DATA || size < sizeof(SIMCONNECT_RECV_CLIENT_DATA)) return false;
        const auto* packet = reinterpret_cast<const SIMCONNECT_RECV_CLIENT_DATA*>(data);
        const auto offset = reinterpret_cast<const char*>(&packet->dwData) - reinterpret_cast<const char*>(data);
        if (packet->dwRequestID != command_id || packet->dwDefineID != command_id || size < offset + sizeof(value)) return false;
        cockpit_packet decoded{}; std::memcpy(&decoded, &packet->dwData, sizeof(decoded));
        if (!cockpit_valid(decoded) || (decoded.kind != cockpit_kind::apply && decoded.kind != cockpit_kind::report)) return false;
        value = decoded; return true;
    }
    bool publish(cockpit_packet value) {
        if (!available || value.kind != cockpit_kind::status || !cockpit_valid(value)) return false;
        if (count_ >= 126 || FAILED(SimConnect_SetClientData(connection_, status_id, status_id, SIMCONNECT_CLIENT_DATA_SET_FLAG_DEFAULT, 0, sizeof(value), &value))) {
            available = false; return false;
        }
        DWORD sent = 0;
        if (FAILED(SimConnect_GetLastSentPacketID(connection_, &sent)) || !sent) { available = false; return false; }
        packets_[count_++] = sent;
        if (count_ >= 16 && !checkpoint_) {
            if (generation_ == 0x00ffffff) { available = false; return false; }
            checkpoint_ = 0x7d000000u + ++generation_;
            if (FAILED(SimConnect_RequestSystemState(connection_, checkpoint_, "Sim")) ||
                FAILED(SimConnect_GetLastSentPacketID(connection_, &sent)) || !sent) { available = false; return false; }
            packets_[count_++] = sent; cut_ = count_;
        }
        return true;
    }
    bool confirm(DWORD request) {
        if (!checkpoint_ || request != checkpoint_) return false;
        for (unsigned index = cut_; index < count_; ++index) packets_[index - cut_] = packets_[index];
        count_ -= cut_; cut_ = 0; checkpoint_ = 0; return true;
    }
    bool exception(const SIMCONNECT_RECV_EXCEPTION& error) {
        if (!error.dwSendID) return false;
        for (unsigned index = 0; index < calls_; ++index) if (configuration_[index] == error.dwSendID) {
            if (create_[index] && error.dwException == SIMCONNECT_EXCEPTION_ALREADY_CREATED) return true;
            available = false; return true;
        }
        for (unsigned index = 0; index < count_; ++index) if (packets_[index] == error.dwSendID) { available = false; return true; }
        return false;
    }
private:
    HANDLE connection_{};
    DWORD configuration_[7]{}, packets_[128]{}, checkpoint_ = 0, generation_ = 0;
    bool create_[7]{};
    unsigned calls_ = 0, count_ = 0, cut_ = 0;
    bool record(HRESULT call, bool create = false) {
        DWORD sent = 0;
        if (FAILED(call) || calls_ == 7 || FAILED(SimConnect_GetLastSentPacketID(connection_, &sent)) || !sent) return false;
        configuration_[calls_] = sent; create_[calls_++] = create; return true;
    }
};
}
