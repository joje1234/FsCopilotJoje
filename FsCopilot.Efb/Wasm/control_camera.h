#pragma once
#include <SimConnect.h>
#include <cmath>
#include <cstdint>
#include <cstring>

namespace fsc_controls {
enum class camera_phase : uint32_t { idle, waiting, applying, confirmed, unavailable };

// Local viewpoint only. Seat authority is negotiated separately. Read-back and
// SDK errors never change aircraft controls, ownership, physics or freeze state.
class control_camera {
public:
    static constexpr DWORD read_definition = 0x6800, read_request = 0x6801, write_definition = 0x6802;
    camera_phase phase = camera_phase::idle;
    uint32_t seat = 0;
    bool initialize(HANDLE connection) {
        connection_ = connection; available_ = true; calls_ = 0; checkpoint_ = 0; generation_ = 0; reset_context();
        const char* names[] = {"CAMERA STATE", "CAMERA VIEW TYPE AND INDEX:0", "CAMERA VIEW TYPE AND INDEX:1", "CAMERA VIEW TYPE AND INDEX MAX:1"};
        for (const auto name : names)
            if (!record(SimConnect_AddToDataDefinition(connection_, read_definition, name, "Number", SIMCONNECT_DATATYPE_FLOAT64))) return false;
        for (unsigned index = 1; index <= 2; ++index)
            if (!record(SimConnect_AddToDataDefinition(connection_, write_definition, names[index], "Number", SIMCONNECT_DATATYPE_FLOAT64))) return false;
        return record(SimConnect_RequestDataOnSimObject(connection_, read_request, read_definition, SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 2, 0)) && checkpoint();
    }
    void reset_context() { seat = 0; phase = camera_phase::idle; observed_ = -1; matches_ = 0; wrote_ = false; }
    void request(uint32_t desired) {
        if ((desired != 1 && desired != 2) || desired == seat) return;
        seat = desired; phase = available_ ? camera_phase::waiting : camera_phase::unavailable;
        matches_ = 0; wrote_ = false;
    }
    bool observe(const SIMCONNECT_RECV_SIMOBJECT_DATA* data, DWORD size, double now) {
        if (!data || data->dwRequestID != read_request) return false;
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (data->dwDefineCount != 4 || size < offset + sizeof(values_) || !std::isfinite(now)) return true;
        double values[4]{}; std::memcpy(values, &data->dwData, sizeof(values));
        for (const auto value : values) if (!std::isfinite(value) || value < 0 || value != std::floor(value)) return true;
        std::memcpy(values_, values, sizeof(values)); observed_ = now;
        if (seat && (phase == camera_phase::waiting || phase == camera_phase::applying)) {
            if (values_[0] == 2 && values_[1] == 1 && values_[2] == option()) ++matches_;
            else matches_ = 0;
        }
        return true;
    }
    void tick(double now) {
        if (!seat || phase == camera_phase::confirmed || phase == camera_phase::unavailable) return;
        if (!available_) { phase = camera_phase::unavailable; return; }
        if (wrote_ && now >= deadline_) { phase = camera_phase::unavailable; return; }
        if (!std::isfinite(now) || observed_ < 0 || now < observed_ || now - observed_ > 1 || checkpoint_) return;
        if (matches_ >= 3) { phase = camera_phase::confirmed; return; }
        if (values_[0] == 2 && values_[1] == 1 && values_[2] == option()) return;
        if (wrote_ || values_[0] != 2) return; // Keep an external/drone view until the user returns to the cockpit.
        if (values_[3] < option()) { phase = camera_phase::unavailable; return; }
        // The SDK's Pilot category uses sparse default indices: 2 Pilot, 5 Copilot.
        // A reported maximum is only a bound; success still requires read-back.
        double target[] = {1, static_cast<double>(option())};
        matches_ = 0; wrote_ = true; deadline_ = now + 1; phase = camera_phase::applying;
        if (!record(SimConnect_SetDataOnSimObject(connection_, write_definition, SIMCONNECT_OBJECT_ID_USER, 0, 0, sizeof(target), target)) || !checkpoint())
            phase = camera_phase::unavailable;
    }
    bool confirm(DWORD request) {
        if (!checkpoint_ || request != checkpoint_) return false;
        calls_ = 0; checkpoint_ = 0; return true;
    }
    bool exception(const SIMCONNECT_RECV_EXCEPTION& error) {
        if (!error.dwSendID) return false;
        for (unsigned index = 0; index < calls_; ++index) if (packets_[index] == error.dwSendID) {
            available_ = false; if (seat) phase = camera_phase::unavailable; return true;
        }
        return false;
    }
private:
    HANDLE connection_{};
    bool available_ = false, wrote_ = false;
    unsigned calls_ = 0, matches_ = 0;
    DWORD checkpoint_ = 0, generation_ = 0, packets_[16]{};
    double values_[4]{}, observed_ = -1, deadline_ = 0;
    unsigned option() const { return seat == 1 ? 2u : 5u; }
    bool record(HRESULT result) {
        DWORD id = 0;
        if (FAILED(result) || calls_ == 16 || FAILED(SimConnect_GetLastSentPacketID(connection_, &id)) || !id) {
            available_ = false; return false;
        }
        packets_[calls_++] = id; return true;
    }
    bool checkpoint() {
        if (generation_ == 0x00ffffff) { available_ = false; return false; }
        checkpoint_ = 0x78000000u + ++generation_;
        return record(SimConnect_RequestSystemState(connection_, checkpoint_, "Sim"));
    }
};
}
