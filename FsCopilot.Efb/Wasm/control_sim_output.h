#pragma once
#include <SimConnect.h>
#include "control_output.h"
#include "control_events.h"

namespace fsc_controls {
// Uses mappings already installed by the four control masks. A transmission
// starts below our hardware mask, so it cannot be learned again as hardware.
// Nothing is transmitted by initialize(), confirm(), or exception().
class control_sim_output {
public:
    bool available = false;
    size_t failed_axis() const { return failed_.axis; }
    const char* failed_target() const { return failed_.target; }
    const char* failed_operation() const { return failed_.operation; }
    void initialize(HANDLE connection, DWORD first_event, DWORD priority = SIMCONNECT_GROUP_PRIORITY_DEFAULT) {
        connection_ = connection; first_event_ = first_event; priority_ = priority;
        // Only named priorities below our masks. Adjacent priority numbers are
        // unsafe: SimConnect moves groups when the requested slot is occupied.
        available = !!connection && (priority == SIMCONNECT_GROUP_PRIORITY_DEFAULT || priority == SIMCONNECT_GROUP_PRIORITY_STANDARD);
        calls_ = cut_ = 0; checkpoint_ = 0;
    }
    void stop() { available = false; }
    DWORD request_checkpoint() {
        if (!available || checkpoint_ || generation_ >= 0x00ffffff) return 0;
        checkpoint_ = 0x75000000u + ++generation_;
        if (!record(SimConnect_RequestSystemState(connection_, checkpoint_, "Sim"))) return 0;
        cut_ = calls_; return checkpoint_;
    }
    bool write(const output_action* actions, size_t count) {
        if (!available || !connection_ || !actions || !count || count > axis_count) return false;
        DWORD ids[axis_count]{};
        // Validate every event before the first SDK call, including bounded
        // strings. Leave room to track the complete batch and its checkpoint.
        if (calls_ + count + 1 > capacity) { available = false; return false; }
        for (size_t item = 0; item < count; ++item) {
            if (!std::memchr(actions[item].event, 0, sizeof(actions[item].event))) return false;
            bool found = false;
            for (size_t event = 0; event < event_count; ++event) if (!std::strcmp(actions[item].event, events[event].name)) {
                ids[item] = first_event_ + static_cast<DWORD>(event); found = true; break;
            }
            if (!found) return false;
        }
        for (size_t item = 0; item < count; ++item) {
            if (!record(SimConnect_TransmitClientEvent_EX1(connection_, SIMCONNECT_OBJECT_ID_USER, ids[item],
                priority_, SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY,
                static_cast<DWORD>(actions[item].raw), 0, 0, 0, 0), actions[item].axis, actions[item].event, "Transmit event")) return false;
        }
        return checkpoint_if_needed();
    }
    bool confirm(DWORD request) {
        if (!checkpoint_ || request != checkpoint_) return false;
        for (size_t index = cut_; index < calls_; ++index) { packets_[index - cut_] = packets_[index]; details_[index - cut_] = details_[index]; }
        calls_ -= cut_; cut_ = 0; checkpoint_ = 0; return true;
    }
    bool exception(const SIMCONNECT_RECV_EXCEPTION& error) {
        if (!error.dwSendID) return false;
        for (size_t index = 0; index < calls_; ++index) if (packets_[index] == error.dwSendID) {
            // Stop remote output while preserving the existing hardware masks.
            failed_ = details_[index];
            available = false; return true;
        }
        return false;
    }
protected:
    static constexpr size_t capacity = 256;
    HANDLE connection_{};
    DWORD first_event_ = 0, checkpoint_ = 0, generation_ = 0, packets_[capacity]{};
    DWORD priority_ = SIMCONNECT_GROUP_PRIORITY_DEFAULT;
    size_t calls_ = 0, cut_ = 0;
    struct call_detail { size_t axis = axis_count; char target[80]{}; const char* operation = "SDK call"; };
    call_detail details_[capacity]{}, failed_{};
    bool record(HRESULT result, size_t axis = axis_count, const char* target = "", const char* operation = "SDK call") {
        call_detail detail{}; detail.axis = axis; detail.operation = operation;
        std::snprintf(detail.target, sizeof(detail.target), "%s", target);
        failed_ = detail;
        if (FAILED(result) || calls_ >= capacity) { available = false; return false; }
        DWORD sent = 0;
        if (FAILED(SimConnect_GetLastSentPacketID(connection_, &sent)) || !sent) { available = false; return false; }
        packets_[calls_] = sent; details_[calls_++] = detail; return true;
    }
    bool checkpoint_if_needed() {
        if (calls_ >= 32 && !checkpoint_) {
            if (generation_ >= 0x00ffffff) { available = false; return false; }
            checkpoint_ = 0x75000000u + ++generation_;
            if (!record(SimConnect_RequestSystemState(connection_, checkpoint_, "Sim"))) return false;
            cut_ = calls_;
        }
        return true;
    }
};
}
