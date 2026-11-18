#include <cassert>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <vector>
#include "../Wasm/control_guidance.h"
using namespace fsc_controls;
namespace {
void sample(control_guidance& ap, double now, uint32_t switches = 0, double heading = 90, double altitude = 5000, double vs = 500, double slot = 1) {
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * control_guidance::value_count);
    auto* packet = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
    packet->dwRequestID = control_guidance::read_request; packet->dwDefineCount = control_guidance::value_count;
    double values[control_guidance::value_count]{};
    for (unsigned index = 0; index < control_guidance::switch_count; ++index) values[index] = (switches & (1u << index)) ? 1 : 0;
    values[13] = heading; values[14] = altitude; values[15] = vs; values[16] = 1; values[17] = 2;
    values[18] = values[19] = values[20] = slot;
    std::memcpy(&packet->dwData, values, sizeof(values));
    const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&packet->dwData) - reinterpret_cast<char*>(packet) + sizeof(values));
    assert(ap.observe(packet, size, now));
}
control_guidance ready(guidance_targets targets = guidance_targets{}) {
    fake_sim::transmissions.clear(); fake_sim::names.clear(); fake_sim::definitions.clear();
    fake_sim::fail_transmission_after = -1; fake_sim::fail_configuration = false;
    control_guidance ap; assert(ap.initialize(static_cast<HANDLE>(1), targets));
    assert(fake_sim::transmissions.empty() && fake_sim::names.empty());
    assert(ap.confirm(fake_sim::request_id)); sample(ap, 0); return ap;
}
void confirm(control_guidance& ap, uint32_t switches, double heading = 90, double altitude = 5000, double vs = 500, double now = .1) {
    for (unsigned index = 0; index < 3; ++index) sample(ap, now + index * .05, switches, heading, altitude, vs);
    ap.tick(now + .15); assert(ap.result == guidance_result::confirmed);
}
}
int main() {
#ifdef _WIN32
    _set_error_mode(_OUT_TO_STDERR); _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
#endif
    {
        auto ap = ready(); assert(ap.fresh(0) && !ap.fresh(1.01));
        const auto& definitions = fake_sim::definitions[control_guidance::read_definition];
        assert(definitions.size() == 21 && definitions[13].first == "AUTOPILOT HEADING LOCK DIR:1");
        assert(definitions[14].first == "AUTOPILOT ALTITUDE LOCK VAR:1" && definitions[15].second == "Feet per minute");
        sample(ap, .1, 8191, 360, -500, -1000);
        assert(ap.values.switches == 8191 && ap.values.heading == 0 && ap.values.altitude == -500 && ap.values.vertical_speed == -1000);
        assert(ap.values.gps && ap.values.nav == 2 && ap.values.slots[0] == 1);
        assert(fake_sim::transmissions.empty());
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::master, 1, 0));
        const auto tx = fake_sim::transmissions.back();
        assert(fake_sim::names[tx.event] == "AUTOPILOT_ON" && tx.priority == SIMCONNECT_GROUP_PRIORITY_DEFAULT && tx.flags == SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY);
        assert(!ap.begin(guidance_setting::master, 1, 0));
        for (unsigned index = 0; index < 3; ++index) sample(ap, .01 + index * .01, 1);
        ap.tick(.04); assert(ap.result == guidance_result::pending); assert(ap.confirm(fake_sim::request_id));
        ap.tick(.05); assert(ap.result == guidance_result::pending);
        confirm(ap, 1); assert(fake_sim::transmissions.size() == 1);
        assert(ap.begin(guidance_setting::master, 0, .25)); assert(fake_sim::names[fake_sim::transmissions.back().event] == "AUTOPILOT_OFF");
        assert(ap.confirm(fake_sim::request_id)); confirm(ap, 0, 90, 5000, 500, .3);
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::heading, 90, 0)); confirm(ap, 0);
        assert(fake_sim::transmissions.empty());
        assert(ap.begin(guidance_setting::heading, 359.995, .25));
        const auto tx = fake_sim::transmissions.back(); assert(fake_sim::names[tx.event] == "AP_HEADING_BUG_SET_EX1" && tx.raw == 16384 && tx.extra[0] == 1);
        assert(ap.confirm(fake_sim::request_id)); confirm(ap, 0, 0, 5000, 500, .3);
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::altitude, -1000, 0));
        auto tx = fake_sim::transmissions.back(); assert(fake_sim::names[tx.event] == "AP_ALT_VAR_SET_ENGLISH" && static_cast<int32_t>(tx.raw) == -1000 && tx.extra[0] == 1);
        assert(ap.confirm(fake_sim::request_id)); confirm(ap, 0, 90, -1000);
        assert(ap.begin(guidance_setting::vertical_speed, -1500, .25)); tx = fake_sim::transmissions.back();
        assert(fake_sim::names[tx.event] == "AP_VS_VAR_SET_ENGLISH" && static_cast<int32_t>(tx.raw) == -1500 && tx.extra[0] == 1);
        assert(ap.confirm(fake_sim::request_id)); confirm(ap, 0, 90, -1000, -1500, .3);
    }
    {
        const guidance_setting settings[] = {guidance_setting::heading_hold, guidance_setting::altitude_hold, guidance_setting::vertical_hold,
            guidance_setting::nav_hold, guidance_setting::approach_hold, guidance_setting::backcourse, guidance_setting::flight_level_change, guidance_setting::yaw_damper};
        const char* on[] = {"AP_HDG_HOLD_ON", "AP_ALT_HOLD_ON", "AP_VS_ON", "AP_NAV1_HOLD_ON", "AP_APR_HOLD_ON", "AP_BC_HOLD_ON", "FLIGHT_LEVEL_CHANGE_ON", "YAW_DAMPER_ON"};
        const char* off[] = {"AP_HDG_HOLD_OFF", "AP_ALT_HOLD_OFF", "AP_VS_OFF", "AP_NAV1_HOLD_OFF", "AP_APR_HOLD_OFF", "AP_BC_HOLD_OFF", "FLIGHT_LEVEL_CHANGE_OFF", "YAW_DAMPER_OFF"};
        const uint32_t bits[] = {2, 4, 8, 16, 64, 512, 1024, 2048};
        for (unsigned index = 0; index < 8; ++index) {
            auto ap = ready(); assert(ap.begin(settings[index], 1, 0));
            assert(fake_sim::names[fake_sim::transmissions.back().event] == on[index]);
            assert(ap.confirm(fake_sim::request_id)); confirm(ap, bits[index]);
            assert(ap.begin(settings[index], 0, .25)); assert(fake_sim::names[fake_sim::transmissions.back().event] == off[index]);
            assert(ap.confirm(fake_sim::request_id)); confirm(ap, 0, 90, 5000, 500, .3);
        }
    }
    {
        auto ap = ready(); assert(!ap.begin(guidance_setting::flight_director, 0, 0));
        assert(!ap.begin(guidance_setting::flight_director, 1, 0));
        assert(ap.begin(guidance_setting::flight_director, 1, 0, guidance_operation::observe));
        confirm(ap, 4096); assert(fake_sim::transmissions.empty());
    }
    {
        auto ap = ready(); sample(ap, 0, 0, 90, 5000, 500, 0);
        assert(!ap.begin(guidance_setting::heading, 90, 0) && !ap.begin(guidance_setting::altitude, 6000, 0));
        assert(!ap.begin(guidance_setting::vertical_speed, 500, 0, guidance_operation::observe));
        assert(fake_sim::transmissions.empty());
        auto zero = ready({0, 0, 0}); sample(zero, 0, 0, 90, 5000, 500, 0);
        assert(zero.begin(guidance_setting::heading, 100, 0)); assert(fake_sim::transmissions.back().extra[0] == 0);
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::heading, 100, 0)); assert(ap.confirm(fake_sim::request_id));
        sample(ap, .1, 0, 100, 5000, 500, 2); ap.tick(.1); assert(ap.result == guidance_result::failed);
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::heading, 100, 0)); const auto older = fake_sim::request_id;
        assert(ap.supersede() && !ap.begin(guidance_setting::heading, 90, 0)); assert(ap.confirm(older));
        assert(!ap.begin(guidance_setting::heading, 90, 0)); sample(ap, .01, 0, 100);
        assert(ap.begin(guidance_setting::heading, 90, .01)); assert(fake_sim::transmissions.size() == 2);
        assert(ap.confirm(fake_sim::request_id)); confirm(ap, 0);
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::master, 1, 0)); const auto older = fake_sim::request_id;
        // Off must be sent even though the cached state still says Off: On is queued.
        assert(ap.begin(guidance_setting::master, 0, .01)); assert(fake_sim::transmissions.size() == 2);
        assert(fake_sim::names[fake_sim::transmissions.back().event] == "AUTOPILOT_OFF");
        assert(!ap.begin(guidance_setting::master, 0, .01) && !ap.confirm(older));
        assert(ap.confirm(fake_sim::request_id)); confirm(ap, 0);
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::heading, 100, 0));
        const auto packet = fake_sim::transmissions.back().packet; assert(ap.supersede());
        SIMCONNECT_RECV_EXCEPTION error{}; error.dwSendID = packet;
        assert(ap.exception(error) && ap.result == guidance_result::failed && !ap.begin(guidance_setting::master, 0, .1));
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::master, 1, 0)); assert(ap.supersede());
        assert(ap.begin(guidance_setting::master, 0, .01));
        assert(fake_sim::transmissions.size() == 2 && fake_sim::names[fake_sim::transmissions.back().event] == "AUTOPILOT_OFF");
        assert(ap.supersede() && !ap.begin(guidance_setting::master, 0, .01)); // Off is already queued.
    }
    {
        auto ap = ready(); fake_sim::fail_transmission_after = 0;
        assert(!ap.begin(guidance_setting::master, 1, 0) && ap.result == guidance_result::failed);
        fake_sim::fail_transmission_after = -1;
        assert(!ap.begin(guidance_setting::heading, 100, .1));
    }
    {
        auto ap = ready(); assert(ap.begin(guidance_setting::master, 1, 0));
        assert(ap.confirm(fake_sim::request_id));
        for (unsigned index = 0; index < 25; ++index) sample(ap, index * .1);
        ap.tick(2.4); assert(ap.result == guidance_result::failed && fake_sim::transmissions.size() == 1);
    }
    {
        auto ap = ready(); const auto serial = ap.serial;
        sample(ap, .1, 0, std::numeric_limits<double>::quiet_NaN()); assert(ap.serial == serial && !ap.fresh(.1));
        sample(ap, .2); assert(ap.fresh(.2)); assert(!ap.begin(guidance_setting::heading, 100, 1.21));
        assert(!ap.begin(static_cast<guidance_setting>(99), 1, .2));
        assert(!ap.begin(guidance_setting::master, 2, .2)); ap.reset_context(); assert(!ap.fresh(.2));
    }
    {
        control_guidance ap; assert(ap.initialize(static_cast<HANDLE>(1)));
        const auto read_packet = fake_sim::last_packet;
        sample(ap, 0); SIMCONNECT_RECV_EXCEPTION error{}; error.dwSendID = read_packet;
        assert(ap.exception(error) && !ap.fresh(0) && ap.result == guidance_result::failed);
        sample(ap, .1); assert(!ap.fresh(.1) && !ap.begin(guidance_setting::master, 1, .1));
        control_guidance unsupported; assert(!unsupported.initialize(static_cast<HANDLE>(1), {4, 1, 1}));
    }
    std::cout << "Guidance SDK checks passed: read-only startup; explicit on/off, indexed targets and EX1 heading wrap; no FD toggle fallback; selected-slot checks; checkpoint and three later readbacks; supersession and queued AP-on disconnect; late failure/staleness refusal. SDK is simulated; no aircraft profile or input gates verified.\n";
}
