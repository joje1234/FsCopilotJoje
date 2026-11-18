#pragma once
#include <SimConnect.h>
#include <cmath>
#include <cstdint>
#include <cstring>

namespace fsc_controls {
struct altimeter_setting { uint32_t pressure16 = 0; bool standard = false; };
inline bool altimeter_valid(altimeter_setting value) { return value.pressure16 >= 8000 && value.pressure16 <= 19200; }
inline bool altimeter_equal(altimeter_setting a, altimeter_setting b) { return a.pressure16 == b.pressure16 && a.standard == b.standard; }
enum class altimeter_result { idle, pending, confirmed, failed };
enum class altimeter_operation { apply, observe };

// SDK mechanics only. The caller must fence session/seat ownership and confirm
// local input gates before invoking begin(). Instrument commands also require
// a verified native input provider; the default Baron setup remains read-only.
class control_altimeters {
public:
    static constexpr DWORD read_definition = 0x6a00, read_request = 0x6a01, set_event = 0x6a02, standard_event = 0x6a03;
    altimeter_setting values[2]{};
    uint64_t serial = 0;
    altimeter_result result = altimeter_result::idle;
    bool initialize(HANDLE connection) {
        connection_ = connection; read_available_ = true; output_failed_ = outputs_mapped_ = false;
        calls_ = 0; checkpoint_ = generation_ = 0; reset_context();
        const char* names[] = {"KOHLSMAN SETTING MB EX1:1", "KOHLSMAN SETTING STD:1", "KOHLSMAN SETTING MB EX1:2", "KOHLSMAN SETTING STD:2"};
        for (unsigned index = 0; index < 4; ++index)
            if (!record(SimConnect_AddToDataDefinition(connection_, read_definition, names[index], index % 2 ? "Bool" : "Millibars", SIMCONNECT_DATATYPE_FLOAT64), true)) return false;
        return record(SimConnect_RequestDataOnSimObject(connection_, read_request, read_definition, SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 2, 0), true) && checkpoint(true);
    }
    void reset_context() { serial = after_checkpoint_ = 0; observed_ = -1; values[0] = values[1] = altimeter_setting{}; result = altimeter_result::idle; matches_ = 0; }
    bool fresh(double now) const { return read_available_ && serial && std::isfinite(now) && observed_ >= 0 && now >= observed_ && now - observed_ <= 1; }
    bool checkpoint_pending() const { return checkpoint_ != 0; }
    bool ready_for_output(double now) const { return fresh(now) && !checkpoint_ && serial > after_checkpoint_ && !output_failed_ &&
        result != altimeter_result::pending && result != altimeter_result::failed; }
    bool observe(const SIMCONNECT_RECV_SIMOBJECT_DATA* data, DWORD size, double now) {
        if (!data || data->dwRequestID != read_request) return false;
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (data->dwDefineCount != 4 || size < offset + sizeof(double) * 4 || !std::isfinite(now) || serial == UINT64_MAX) return true;
        double received[4]{}; std::memcpy(received, &data->dwData, sizeof(received));
        altimeter_setting next[2]{};
        for (unsigned index = 0; index < 2; ++index) {
            const auto pressure = received[index * 2], standard = received[index * 2 + 1];
            if (!std::isfinite(pressure) || pressure < 500 || pressure > 1200 || (standard != 0 && standard != 1)) return true;
            next[index] = {static_cast<uint32_t>(std::lround(pressure * 16)), standard != 0};
        }
        values[0] = next[0]; values[1] = next[1]; observed_ = now; ++serial;
        if (result == altimeter_result::pending) matches_ = altimeter_equal(values[selected_], desired_) ? matches_ + 1 : 0;
        return true;
    }
    bool begin(unsigned instrument, altimeter_setting desired, double now, altimeter_operation mode = altimeter_operation::apply) {
        if (instrument < 1 || instrument > 2 || !altimeter_valid(desired) || !ready_for_output(now) ||
            (mode != altimeter_operation::apply && mode != altimeter_operation::observe)) return false;
        selected_ = instrument - 1; desired_ = desired; matches_ = 0; deadline_ = now + 2; result = altimeter_result::pending;
        const auto current = values[selected_];
        if (mode == altimeter_operation::observe) return true; // Confirm the owner's local input; never rewind its cockpit knob.
        if (altimeter_equal(current, desired)) return true; // Equal state never emits a relative or absolute event.
        if (!outputs_mapped_) {
            if (!record(SimConnect_MapClientEventToSimEvent(connection_, set_event, "KOHLSMAN_SET"), false) ||
                !record(SimConnect_MapClientEventToSimEvent(connection_, standard_event, "BAROMETRIC_STD_PRESSURE"), false)) return false;
            outputs_mapped_ = true;
        }
        // EX1 preserves tuned QNH while STD is selected. Set the tuned value
        // first when needed, then enter STD explicitly for this index only.
        if ((current.pressure16 != desired.pressure16 || !desired.standard) &&
            !record(SimConnect_TransmitClientEvent_EX1(connection_, SIMCONNECT_OBJECT_ID_USER, set_event, SIMCONNECT_GROUP_PRIORITY_DEFAULT,
                SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY, desired.pressure16, instrument, 0, 0, 0), false)) return false;
        if (desired.standard && !record(SimConnect_TransmitClientEvent_EX1(connection_, SIMCONNECT_OBJECT_ID_USER, standard_event,
            SIMCONNECT_GROUP_PRIORITY_DEFAULT, SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY, instrument, 0, 0, 0, 0), false)) return false;
        return checkpoint(false);
    }
    bool supersede() {
        if (result != altimeter_result::pending || output_failed_) return false;
        // Leave queued SDK calls/checkpoints intact. A replacement may start
        // only after their acknowledgement, and a partial failure stays failed.
        result = altimeter_result::idle; matches_ = 0; return true;
    }
    void tick(double now) {
        if (result != altimeter_result::pending) return;
        if (!read_available_ || output_failed_ || !std::isfinite(now) || now >= deadline_) { result = altimeter_result::failed; return; }
        if (!checkpoint_ && fresh(now) && matches_ >= 3) result = altimeter_result::confirmed;
    }
    bool confirm(DWORD request) {
        if (!checkpoint_ || request != checkpoint_) return false;
        calls_ = 0; checkpoint_ = 0; after_checkpoint_ = serial; return true;
    }
    bool exception(const SIMCONNECT_RECV_EXCEPTION& error) {
        if (!error.dwSendID) return false;
        for (unsigned index = 0; index < calls_; ++index) if (packets_[index] == error.dwSendID) {
            if (read_call_[index]) read_available_ = false;
            else { output_failed_ = true; result = altimeter_result::failed; }
            if (result == altimeter_result::pending) result = altimeter_result::failed;
            return true;
        }
        return false;
    }
private:
    HANDLE connection_{};
    bool read_available_ = false, outputs_mapped_ = false, output_failed_ = false, read_call_[16]{};
    DWORD packets_[16]{}, checkpoint_ = 0, generation_ = 0;
    unsigned calls_ = 0, selected_ = 0, matches_ = 0;
    altimeter_setting desired_{};
    uint64_t after_checkpoint_ = 0;
    double observed_ = -1, deadline_ = 0;
    bool record(HRESULT call, bool read) {
        DWORD id = 0;
        if (FAILED(call) || calls_ == 16 || FAILED(SimConnect_GetLastSentPacketID(connection_, &id)) || !id) {
            if (read) read_available_ = false;
            else { output_failed_ = true; result = altimeter_result::failed; }
            return false;
        }
        packets_[calls_] = id; read_call_[calls_++] = read; return true;
    }
    bool checkpoint(bool read) {
        if (generation_ == 0x00ffffff) { output_failed_ = true; result = altimeter_result::failed; return false; }
        checkpoint_ = 0x7a000000u + ++generation_;
        return record(SimConnect_RequestSystemState(connection_, checkpoint_, "Sim"), read);
    }
};
}
