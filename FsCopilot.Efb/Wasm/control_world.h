#pragma once
#include "control_session_timing.h"
#include <SimConnect.h>
#include <cstdint>
#include <cstring>
#include <cmath>

namespace fsc_controls {
constexpr size_t world_value_count = 12;
// Position/attitude in radians and feet; earth velocity in ft/s; angular
// velocity in rad/s. Use explicit units rather than the SimVar name's suffix.
constexpr const char* world_names[] = {"PLANE LATITUDE", "PLANE LONGITUDE", "PLANE ALTITUDE", "PLANE PITCH DEGREES",
    "PLANE BANK DEGREES", "PLANE HEADING DEGREES TRUE", "VELOCITY WORLD X", "VELOCITY WORLD Y", "VELOCITY WORLD Z",
    "ROTATION VELOCITY BODY X", "ROTATION VELOCITY BODY Y", "ROTATION VELOCITY BODY Z"};
constexpr const char* world_units[] = {"Radians", "Radians", "Feet", "Radians", "Radians", "Radians",
    "Feet per second", "Feet per second", "Feet per second", "Radians per second", "Radians per second", "Radians per second"};
constexpr const char* world_freeze_names[] = {"IS LATITUDE LONGITUDE FREEZE ON", "IS ALTITUDE FREEZE ON", "IS ATTITUDE FREEZE ON"};
constexpr const char* world_freeze_events[] = {"FREEZE_LATITUDE_LONGITUDE_SET", "FREEZE_ALTITUDE_SET", "FREEZE_ATTITUDE_SET"};
inline bool world_valid(const double* values) {
    if (!values) return false;
    for (size_t index = 0; index < world_value_count; ++index) if (!std::isfinite(values[index])) return false;
    constexpr double pi = 3.14159265358979323846;
    return std::abs(values[0]) <= pi / 2 + 0.000001 && std::abs(values[1]) <= pi + 0.000001 &&
        std::abs(values[3]) <= pi + 0.000001 && std::abs(values[4]) <= pi * 2 + 0.000001 &&
        std::abs(values[5]) <= pi * 2 + 0.000001;
}

// Owns only world reads/sets and the three guest freezes. All calls use the
// module's one SDK connection. Failed output stops the session and retains
// freezes; only explicit local recovery restores the prior freeze settings.
class control_world {
public:
    static constexpr DWORD read_definition = 0x6400, read_request = 0x6401, write_definition = 0x6402, first_freeze_event = 0x6500;
    double values[world_value_count]{};
    uint64_t serial = 0;
    uint32_t frozen = 0;
    bool available = false;
    void initialize(HANDLE connection) { connection_ = connection; available = !!connection; }
    bool start() {
        if (configured_) return available;
        if (!connection_) return false;
        configured_ = true;
        for (size_t index = 0; index < world_value_count; ++index) {
            if (!record(SimConnect_AddToDataDefinition(connection_, read_definition, world_names[index], world_units[index], SIMCONNECT_DATATYPE_FLOAT64)) ||
                !record(SimConnect_AddToDataDefinition(connection_, write_definition, world_names[index], world_units[index], SIMCONNECT_DATATYPE_FLOAT64))) return false;
        }
        for (unsigned index = 0; index < 3; ++index) if (
            !record(SimConnect_AddToDataDefinition(connection_, read_definition, world_freeze_names[index], "Bool", SIMCONNECT_DATATYPE_FLOAT64)) ||
            !record(SimConnect_MapClientEventToSimEvent(connection_, first_freeze_event + index, world_freeze_events[index]))) return false;
        return record(SimConnect_RequestDataOnSimObject(connection_, read_request, read_definition, SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 1, 0)) && checkpoint();
    }
    bool observe(const SIMCONNECT_RECV_SIMOBJECT_DATA* data, DWORD size, double now) {
        if (!data || data->dwRequestID != read_request) return false;
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (data->dwDefineCount != world_value_count + 3 || size < offset + sizeof(double) * (world_value_count + 3)) return true;
        double received[world_value_count + 3]{}; std::memcpy(received, &data->dwData, sizeof(received));
        if (!world_valid(received) || !std::isfinite(now)) return true;
        uint32_t mask = 0;
        for (unsigned index = 0; index < 3; ++index) {
            if (received[world_value_count + index] != 0 && received[world_value_count + index] != 1) return true;
            if (received[world_value_count + index]) mask |= 1u << index;
        }
        std::memcpy(values, received, sizeof(values)); frozen = mask; observed_ = now; ++serial; return true;
    }
    bool fresh(double now) const { return available && serial && observed_ >= 0 && now >= observed_ && now - observed_ <= 1; }
    bool begin(bool host, double now) {
        if (!fresh(now) || (host && owned_)) return false;
        host_ = host; active_ = true; received_ = now; recovering_ = false;
        if (host) return true;
        if (!owned_) original_ = frozen;
        owned_ = true; freeze_confirmed_ = false;
        return freeze(7);
    }
    bool ready(double now) const { return fresh(now) && active_ && (host_ || (owned_ && frozen == 7 && freeze_confirmed_)); }
    bool live(double now) const {
        return available && active_ && observed_ >= 0 && now >= observed_ && now-observed_ < session_recovery_seconds &&
            (host_ || (frozen_owned() && now >= received_ && now-received_ < session_recovery_seconds));
    }
    void arm_stream(double now) { received_ = now; }
    bool write(const double* next, double now) {
        if (host_ || !ready(now) || !world_valid(next)) return false;
        received_ = now;
        double copy[world_value_count]{}; std::memcpy(copy, next, sizeof(copy));
        if (calls_ + 2 > 256) { available = false; return false; }
        if (!record(SimConnect_SetDataOnSimObject(connection_, write_definition, SIMCONNECT_OBJECT_ID_USER, 0, 0, sizeof(copy), copy))) return false;
        return calls_ < 32 || checkpoint();
    }
    void end() { active_ = false; }
    bool owned() const { return owned_; }
    bool frozen_owned() const { return owned_ && frozen == 7 && freeze_confirmed_; }
    bool recovering() const { return recovering_; }
    bool recover(double now) {
        if (!owned_) return true;
        if (!recovering_) {
            recovering_ = true; freeze_confirmed_ = false;
            // A rejected world-data write must not prevent a later explicit
            // recovery from attempting to restore our own freezes.
            available = !!connection_;
            if (!freeze(original_)) return false;
        }
        if (!fresh(now) || !freeze_confirmed_ || frozen != original_) return false;
        owned_ = recovering_ = active_ = false; return true;
    }
    void reset_context() { active_ = owned_ = recovering_ = freeze_pending_ = false; serial = 0; observed_ = -1; }
    void cancel_recovery() { recovering_ = false; }
    bool confirm(DWORD request) {
        if (!checkpoint_ || request != checkpoint_) return false;
        for (size_t index = cut_; index < calls_; ++index) packets_[index - cut_] = packets_[index];
        calls_ -= cut_; cut_ = 0; checkpoint_ = 0;
        if (freeze_pending_) { freeze_confirmed_ = true; freeze_pending_ = false; }
        return true;
    }
    bool exception(const SIMCONNECT_RECV_EXCEPTION& error) {
        if (!error.dwSendID) return false;
        for (size_t index = 0; index < calls_; ++index) if (packets_[index] == error.dwSendID) { available = false; return true; }
        return false;
    }
private:
    HANDLE connection_{};
    bool configured_ = false, host_ = true, active_ = false, owned_ = false, recovering_ = false;
    bool freeze_confirmed_ = false, freeze_pending_ = false;
    uint32_t original_ = 0;
    double observed_ = -1, received_ = -1;
    DWORD checkpoint_ = 0, generation_ = 0, packets_[256]{};
    size_t calls_ = 0, cut_ = 0;
    bool record(HRESULT result) {
        if (FAILED(result) || calls_ == 256) { available = false; return false; }
        DWORD id = 0;
        if (FAILED(SimConnect_GetLastSentPacketID(connection_, &id)) || !id) { available = false; return false; }
        packets_[calls_++] = id; return true;
    }
    bool checkpoint(bool force = false) {
        if (checkpoint_ && !force) return true;
        if (generation_ == 0x00ffffff) { available = false; return false; }
        checkpoint_ = 0x77000000u + ++generation_;
        if (!record(SimConnect_RequestSystemState(connection_, checkpoint_, "Sim"))) return false;
        cut_ = calls_; return true;
    }
    bool freeze(uint32_t mask) {
        if (calls_ + 4 > 256) { available = false; return false; }
        for (unsigned index = 0; index < 3; ++index) if (!record(SimConnect_TransmitClientEvent_EX1(connection_, SIMCONNECT_OBJECT_ID_USER,
            first_freeze_event + index, SIMCONNECT_GROUP_PRIORITY_DEFAULT, SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY,
            (mask >> index) & 1u, 0, 0, 0, 0))) return false;
        freeze_pending_ = true; return checkpoint(true);
    }
};
}
