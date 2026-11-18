#pragma once
#include <SimConnect.h>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace fsc_controls {
enum class guidance_setting : uint32_t { master, heading, altitude, vertical_speed, heading_hold, altitude_hold,
    vertical_hold, nav_hold, approach_hold, backcourse, flight_level_change, yaw_damper, flight_director };
enum class guidance_result { idle, pending, confirmed, failed };
enum class guidance_operation { apply, observe };
struct guidance_targets { uint32_t heading = 1, altitude = 1, vertical_speed = 1; };
struct guidance_values {
    uint32_t switches = 0, nav = 0, slots[3]{};
    double heading = 0, altitude = 0, vertical_speed = 0;
    bool gps = false;
};
inline bool guidance_valid(guidance_setting setting, double value) {
    if (static_cast<uint32_t>(setting) > 12 || !std::isfinite(value)) return false;
    if (setting == guidance_setting::heading) return value >= 0 && value < 360;
    if (setting == guidance_setting::altitude) return value >= -2000 && value <= 100000;
    if (setting == guidance_setting::vertical_speed) return value >= -12000 && value <= 12000;
    return value == 0 || value == 1;
}
inline bool guidance_matches(const guidance_values& state, guidance_setting setting, double value) {
    if (!guidance_valid(setting, value)) return false;
    if (setting == guidance_setting::heading) {
        const double difference = std::fabs(state.heading - value);
        return (difference < 180 ? difference : 360 - difference) <= .012; // Half of the SDK EX1 step, including wrap.
    }
    if (setting == guidance_setting::altitude) return std::fabs(state.altitude - value) <= 1;
    if (setting == guidance_setting::vertical_speed) return std::fabs(state.vertical_speed - value) <= 1;
    const uint32_t masks[] = {1, 0, 0, 0, 2, 4, 8, 16, 32 | 64 | 128 | 256, 512, 1024, 2048, 4096};
    return ((state.switches & masks[static_cast<uint32_t>(setting)]) != 0) == (value != 0);
}
inline void guidance_assign(guidance_values& state, guidance_setting setting, double value) {
    if (setting == guidance_setting::heading) { state.heading = value; return; }
    if (setting == guidance_setting::altitude) { state.altitude = value; return; }
    if (setting == guidance_setting::vertical_speed) { state.vertical_speed = value; return; }
    const uint32_t masks[] = {1, 0, 0, 0, 2, 4, 8, 16, 32 | 64 | 128 | 256, 512, 1024, 2048, 4096};
    const auto mask = masks[static_cast<uint32_t>(setting)];
    if (!value) state.switches &= ~mask;
    else state.switches |= setting == guidance_setting::approach_hold ? 32u : mask;
}

// SDK mechanics, not an aircraft input-gate or shared-session authority proof.
// Production uses initialize/observe only. A fenced adapter must verify its
// input routes, target slots and single host AP engine before calling begin.
// FD output deliberately refuses: the public SDK documents a toggle, and no
// explicit aircraft-specific FD setter has been verified here.
class control_guidance {
public:
    static constexpr DWORD read_definition = 0x6b00, read_request = 0x6b01, first_output = 0x6b10;
    static constexpr unsigned switch_count = 13, value_count = 21;
    guidance_values values{};
    uint64_t serial = 0;
    guidance_result result = guidance_result::idle;
    bool initialize(HANDLE connection, guidance_targets targets = guidance_targets{}) {
        connection_ = connection; targets_ = targets; read_available_ = true; output_failed_ = false;
        calls_ = 0; checkpoint_ = generation_ = 0; output_checkpoint_ = disconnect_queued_ = false;
        std::memset(mapped_, 0, sizeof(mapped_)); reset_context();
        if (targets.heading > 3 || targets.altitude > 3 || targets.vertical_speed > 3) { read_available_ = false; return false; }
        const char* names[] = {"AUTOPILOT MASTER", "AUTOPILOT HEADING LOCK", "AUTOPILOT ALTITUDE LOCK", "AUTOPILOT VERTICAL HOLD",
            "AUTOPILOT NAV1 LOCK", "AUTOPILOT APPROACH HOLD", "AUTOPILOT APPROACH ARM", "AUTOPILOT APPROACH CAPTURED",
            "AUTOPILOT GLIDESLOPE HOLD", "AUTOPILOT BACKCOURSE HOLD", "AUTOPILOT FLIGHT LEVEL CHANGE", "AUTOPILOT YAW DAMPER",
            "AUTOPILOT FLIGHT DIRECTOR ACTIVE:1"};
        for (const auto* name : names)
            if (!record(SimConnect_AddToDataDefinition(connection_, read_definition, name, "Bool", SIMCONNECT_DATATYPE_FLOAT64), true)) return false;
        const char* targets_names[] = {"AUTOPILOT HEADING LOCK DIR", "AUTOPILOT ALTITUDE LOCK VAR", "AUTOPILOT VERTICAL HOLD VAR"};
        const char* units[] = {"Degrees", "Feet", "Feet per minute"};
        const uint32_t slots[] = {targets.heading, targets.altitude, targets.vertical_speed};
        for (unsigned index = 0; index < 3; ++index) {
            char name[80]{}; std::snprintf(name, sizeof(name), "%s:%u", targets_names[index], slots[index]);
            if (!record(SimConnect_AddToDataDefinition(connection_, read_definition, name, units[index], SIMCONNECT_DATATYPE_FLOAT64), true)) return false;
        }
        const char* sources[] = {"GPS DRIVES NAV1", "AUTOPILOT NAV SELECTED", "AUTOPILOT HEADING SLOT INDEX", "AUTOPILOT ALTITUDE SLOT INDEX", "AUTOPILOT VS SLOT INDEX"};
        for (unsigned index = 0; index < 5; ++index)
            if (!record(SimConnect_AddToDataDefinition(connection_, read_definition, sources[index], index ? "Number" : "Bool", SIMCONNECT_DATATYPE_FLOAT64), true)) return false;
        return record(SimConnect_RequestDataOnSimObject(connection_, read_request, read_definition, SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 2, 0), true) && checkpoint(true);
    }
    void reset_context() {
        serial = after_checkpoint_ = 0; observed_ = -1; values = guidance_values{}; matches_ = 0;
        result = output_failed_ ? guidance_result::failed : guidance_result::idle;
    }
    bool fresh(double now) const { return read_available_ && serial && std::isfinite(now) && observed_ >= 0 && now >= observed_ && now - observed_ <= 1; }
    bool checkpoint_pending() const { return checkpoint_ != 0; }
    guidance_targets targets() const { return targets_; }
    bool ready_for_output(double now) const { return fresh(now) && !checkpoint_ && serial > after_checkpoint_ && !output_failed_ &&
        result != guidance_result::pending && result != guidance_result::failed; }
    bool observe(const SIMCONNECT_RECV_SIMOBJECT_DATA* data, DWORD size, double now) {
        if (!data || data->dwRequestID != read_request) return false;
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (data->dwDefineCount != value_count || size < offset + sizeof(double) * value_count || !std::isfinite(now) ||
            now < 0 || now < observed_ || serial == UINT64_MAX) { invalidate(); return true; }
        double received[value_count]{}; std::memcpy(received, &data->dwData, sizeof(received));
        for (const auto value : received) if (!std::isfinite(value)) { invalidate(); return true; }
        guidance_values next{};
        for (unsigned index = 0; index < switch_count; ++index) {
            if (received[index] != 0 && received[index] != 1) { invalidate(); return true; }
            if (received[index]) next.switches |= 1u << index;
        }
        if (received[13] < 0 || received[13] > 360 || !guidance_valid(guidance_setting::altitude, received[14]) ||
            !guidance_valid(guidance_setting::vertical_speed, received[15]) || (received[16] != 0 && received[16] != 1) ||
            received[17] < 0 || received[17] > 2 || received[17] != std::floor(received[17])) { invalidate(); return true; }
        next.heading = received[13] == 360 ? 0 : received[13]; next.altitude = received[14]; next.vertical_speed = received[15];
        next.gps = received[16] != 0; next.nav = static_cast<uint32_t>(received[17]);
        for (unsigned index = 0; index < 3; ++index) {
            const double slot = received[18 + index];
            if (slot < 0 || slot > 4 || slot != std::floor(slot)) { invalidate(); return true; }
            next.slots[index] = static_cast<uint32_t>(slot);
        }
        values = next; observed_ = now; ++serial;
        if (result == guidance_result::pending) {
            if (!slot_matches(desired_)) result = guidance_result::failed;
            else if (!checkpoint_) matches_ = guidance_matches(values, desired_, desired_value_) ? matches_ + 1 : 0;
        }
        return true;
    }
    bool begin(guidance_setting setting, double desired, double now, guidance_operation operation = guidance_operation::apply) {
        const bool disconnect = setting == guidance_setting::master && desired == 0 && operation == guidance_operation::apply &&
            ((result == guidance_result::pending && (desired_ != guidance_setting::master || desired_value_ != 0)) ||
                (checkpoint_ && output_checkpoint_ && !disconnect_queued_)) && fresh(now) && !output_failed_;
        if (!guidance_valid(setting, desired) || !(ready_for_output(now) || disconnect) ||
            (operation != guidance_operation::apply && operation != guidance_operation::observe)) return false;
        if (!slot_matches(setting) || (operation == guidance_operation::apply && !route(setting, desired))) return false;
        const bool same = guidance_matches(values, setting, desired);
        desired_ = setting; desired_value_ = desired; matches_ = 0; deadline_ = now + 2; result = guidance_result::pending;
        const bool explicit_off = setting == guidance_setting::master && desired == 0;
        if (operation == guidance_operation::observe || (same && !disconnect && !explicit_off)) return true;
        const unsigned key = static_cast<unsigned>(setting) * 2 + (desired != 0 ? 1 : 0);
        const DWORD event = first_output + key;
        if (!mapped_[key]) {
            if (!record(SimConnect_MapClientEventToSimEvent(connection_, event, route(setting, desired)), false)) return false;
            mapped_[key] = true;
        }
        DWORD raw = 0, slot = 0;
        if (setting == guidance_setting::heading) { raw = static_cast<DWORD>(std::lround(desired * 16384 / 360)); slot = targets_.heading; }
        if (setting == guidance_setting::altitude) { raw = static_cast<DWORD>(static_cast<int32_t>(std::lround(desired))); slot = targets_.altitude; }
        if (setting == guidance_setting::vertical_speed) { raw = static_cast<DWORD>(static_cast<int32_t>(std::lround(desired))); slot = targets_.vertical_speed; }
        if (!record(SimConnect_TransmitClientEvent_EX1(connection_, SIMCONNECT_OBJECT_ID_USER, event, SIMCONNECT_GROUP_PRIORITY_DEFAULT,
            SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY, raw, slot, 0, 0, 0), false)) return false;
        disconnect_queued_ = explicit_off;
        return checkpoint(false);
    }
    bool supersede() {
        if (result != guidance_result::pending || output_failed_) return false;
        result = guidance_result::idle; matches_ = 0; return true;
    }
    void tick(double now) {
        if (result != guidance_result::pending) return;
        if (!fresh(now) || output_failed_ || now >= deadline_) { result = guidance_result::failed; return; }
        if (!checkpoint_ && serial > after_checkpoint_ && matches_ >= 3) result = guidance_result::confirmed;
    }
    bool confirm(DWORD request) {
        if (!checkpoint_ || request != checkpoint_) return false;
        calls_ = 0; checkpoint_ = 0; output_checkpoint_ = disconnect_queued_ = false;
        after_checkpoint_ = serial; matches_ = 0; return true;
    }
    bool exception(const SIMCONNECT_RECV_EXCEPTION& error) {
        if (!error.dwSendID) return false;
        for (unsigned index = 0; index < calls_; ++index) if (packets_[index] == error.dwSendID) {
            if (read_call_[index]) read_available_ = false; else output_failed_ = true;
            result = guidance_result::failed; return true;
        }
        return false;
    }
private:
    HANDLE connection_{};
    guidance_targets targets_{};
    bool read_available_ = false, output_failed_ = false, output_checkpoint_ = false, disconnect_queued_ = false, mapped_[26]{}, read_call_[32]{};
    DWORD packets_[32]{}, checkpoint_ = 0, generation_ = 0;
    unsigned calls_ = 0, matches_ = 0;
    uint64_t after_checkpoint_ = 0;
    double observed_ = -1, deadline_ = 0, desired_value_ = 0;
    guidance_setting desired_ = guidance_setting::master;
    void invalidate() { observed_ = -1; matches_ = 0; if (result == guidance_result::pending) result = guidance_result::failed; }
    bool slot_matches(guidance_setting setting) const {
        return !((setting == guidance_setting::heading && values.slots[0] != targets_.heading) ||
            (setting == guidance_setting::altitude && values.slots[1] != targets_.altitude) ||
            (setting == guidance_setting::vertical_speed && values.slots[2] != targets_.vertical_speed));
    }
    const char* route(guidance_setting setting, double desired) const {
        const char* on[] = {"AUTOPILOT_ON", "AP_HEADING_BUG_SET_EX1", "AP_ALT_VAR_SET_ENGLISH", "AP_VS_VAR_SET_ENGLISH",
            "AP_HDG_HOLD_ON", "AP_ALT_HOLD_ON", "AP_VS_ON", "AP_NAV1_HOLD_ON", "AP_APR_HOLD_ON", "AP_BC_HOLD_ON",
            "FLIGHT_LEVEL_CHANGE_ON", "YAW_DAMPER_ON", nullptr};
        const char* off[] = {"AUTOPILOT_OFF", "AP_HEADING_BUG_SET_EX1", "AP_ALT_VAR_SET_ENGLISH", "AP_VS_VAR_SET_ENGLISH",
            "AP_HDG_HOLD_OFF", "AP_ALT_HOLD_OFF", "AP_VS_OFF", "AP_NAV1_HOLD_OFF", "AP_APR_HOLD_OFF", "AP_BC_HOLD_OFF",
            "FLIGHT_LEVEL_CHANGE_OFF", "YAW_DAMPER_OFF", nullptr};
        if (!slot_matches(setting)) return nullptr;
        return desired != 0 ? on[static_cast<uint32_t>(setting)] : off[static_cast<uint32_t>(setting)];
    }
    bool record(HRESULT call, bool read) {
        DWORD id = 0;
        if (FAILED(call) || calls_ == 32 || FAILED(SimConnect_GetLastSentPacketID(connection_, &id)) || !id) {
            if (read) read_available_ = false; else output_failed_ = true;
            result = guidance_result::failed; return false;
        }
        packets_[calls_] = id; read_call_[calls_++] = read; return true;
    }
    bool checkpoint(bool read) {
        if (generation_ == 0x00ffffff) { output_failed_ = true; result = guidance_result::failed; return false; }
        checkpoint_ = 0x7b000000u + ++generation_;
        output_checkpoint_ = !read;
        return record(SimConnect_RequestSystemState(connection_, checkpoint_, "Sim"), read);
    }
};
}
