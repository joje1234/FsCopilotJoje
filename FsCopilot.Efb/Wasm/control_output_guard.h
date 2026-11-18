#pragma once
#include "control_output_check.h"

namespace fsc_controls {
class control_output_guard {
public:
    static constexpr DWORD definition = 0x7400, request = 0x7401;
    static constexpr DWORD engine_definition = 0x7402, engine_request = 0x7403;
    output_check_context values{};
    bool engines_off(unsigned count, double now) const {
        if (!count || count > 4 || engines_at_ < 0 || now < engines_at_ || now - engines_at_ > .25) return false;
        for (unsigned engine = 0; engine < count; ++engine) if (engines_[engine] != 0) return false;
        return true;
    }
    void initialize(HANDLE connection) {
        available_ = !!connection; calls_ = 0; values = output_check_context{}; engines_at_ = -1;
        const char* names[] = {"SIM ON GROUND", "GROUND VELOCITY", "BRAKE PARKING POSITION", "AUTOPILOT MASTER"};
        for (unsigned i = 0; i < 4 && available_; ++i) record(connection, SimConnect_AddToDataDefinition(connection, definition, names[i], i == 1 ? "Knots" : "Bool", SIMCONNECT_DATATYPE_FLOAT64));
        if (available_) record(connection, SimConnect_RequestDataOnSimObject(connection, request, definition, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 0, 0));
        for (unsigned engine = 1; engine <= 4 && available_; ++engine) {
            char name[64]{}; std::snprintf(name, sizeof(name), "GENERAL ENG COMBUSTION:%u", engine);
            record(connection, SimConnect_AddToDataDefinition(connection, engine_definition, name, "Bool", SIMCONNECT_DATATYPE_FLOAT64));
        }
        if (available_) record(connection, SimConnect_RequestDataOnSimObject(connection, engine_request, engine_definition, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD_VISUAL_FRAME, 0, 0, 0, 0));
    }
    bool observe(const SIMCONNECT_RECV_SIMOBJECT_DATA* data, DWORD size, double now) {
        if (!data || (data->dwRequestID != request && data->dwRequestID != engine_request)) return false;
        const auto offset = reinterpret_cast<const char*>(&data->dwData) - reinterpret_cast<const char*>(data);
        if (!available_ || data->dwDefineCount != 4 || size < offset + 4 * sizeof(double)) return true;
        double raw[4]{}; std::memcpy(raw, &data->dwData, sizeof(raw));
        for (const auto value : raw) if (!std::isfinite(value)) return true;
        if (data->dwRequestID == engine_request) { std::memcpy(engines_, raw, sizeof(raw)); engines_at_ = now; return true; }
        values.ground = raw[0]; values.knots = raw[1]; values.parking_brake = raw[2]; values.ap = raw[3]; values.observed = now;
        return true;
    }
    bool exception(const SIMCONNECT_RECV_EXCEPTION& error) {
        for (unsigned i = 0; i < calls_; ++i) if (error.dwSendID && packets_[i] == error.dwSendID) { available_ = false; values.observed = -1; return true; }
        return false;
    }
private:
    bool available_ = false; unsigned calls_ = 0; DWORD packets_[10]{};
    double engines_[4]{}, engines_at_ = -1;
    void record(HANDLE connection, HRESULT result) {
        DWORD sent = 0;
        if (FAILED(result) || calls_ == 10 || FAILED(SimConnect_GetLastSentPacketID(connection, &sent)) || !sent) { available_ = false; return; }
        packets_[calls_++] = sent;
    }
};
}
