#include <cassert>
#include <cstdlib>
#include <iostream>
#include <vector>
#include "../Wasm/control_altimeters.h"
using namespace fsc_controls;
namespace {
void sample(control_altimeters& altimeters, double now, double first = 1000, bool std1 = false, double second = 1010, bool std2 = false) {
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * 4);
    auto* packet = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
    packet->dwRequestID = control_altimeters::read_request; packet->dwDefineCount = 4;
    double values[] = {first, std1 ? 1.0 : 0.0, second, std2 ? 1.0 : 0.0}; std::memcpy(&packet->dwData, values, sizeof(values));
    const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&packet->dwData) - reinterpret_cast<char*>(packet) + sizeof(values));
    assert(altimeters.observe(packet, size, now));
}
control_altimeters ready() {
    fake_sim::transmissions.clear(); fake_sim::fail_transmission_after = -1;
    control_altimeters altimeters; assert(altimeters.initialize(static_cast<HANDLE>(1)));
    assert(altimeters.confirm(fake_sim::request_id)); sample(altimeters, 0); return altimeters;
}
void confirmed(control_altimeters& altimeters, double first, bool std1, double second, bool std2) {
    for (unsigned index = 0; index < 3; ++index) sample(altimeters, .1 + index * .05, first, std1, second, std2);
    altimeters.tick(.3); assert(altimeters.result == altimeter_result::confirmed);
}
}
int main() {
#ifdef _WIN32
    _set_error_mode(_OUT_TO_STDERR); _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
#endif
    {
        auto altimeters = ready(); assert(altimeters.begin(1, {16000, false}, 0));
        confirmed(altimeters, 1000, false, 1010, false); assert(fake_sim::transmissions.empty());
        assert(altimeters.begin(2, {16200, false}, .3));
        assert(fake_sim::transmissions.size() == 1);
        const auto& event = fake_sim::transmissions[0];
        assert(event.event == control_altimeters::set_event && event.raw == 16200 && event.extra[0] == 2);
        assert(event.priority == SIMCONNECT_GROUP_PRIORITY_DEFAULT && event.flags == SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY);
        for (unsigned index = 0; index < 3; ++index) sample(altimeters, .4 + index * .1, 1000, false, 1012.5, false);
        altimeters.tick(.61); assert(altimeters.result == altimeter_result::pending);
        assert(altimeters.confirm(fake_sim::request_id)); altimeters.tick(.61); assert(altimeters.result == altimeter_result::confirmed);
        assert(altimeters.values[0].pressure16 == 16000 && altimeters.values[1].pressure16 == 16200);
    }
    {
        auto altimeters = ready(); sample(altimeters, 0, 999.125, true);
        assert(altimeters.values[0].pressure16 == 15986 && altimeters.values[0].standard);
        assert(altimeters.begin(1, {16000, true}, 0) && fake_sim::transmissions.size() == 2);
        assert(fake_sim::transmissions[0].event == control_altimeters::set_event && fake_sim::transmissions[0].extra[0] == 1);
        assert(fake_sim::transmissions[1].event == control_altimeters::standard_event && fake_sim::transmissions[1].raw == 1);
        assert(altimeters.confirm(fake_sim::request_id)); confirmed(altimeters, 1000, true, 1010, false);
        const auto count = fake_sim::transmissions.size();
        assert(altimeters.begin(1, {16000, true}, .3));
        for (unsigned index = 0; index < 3; ++index) sample(altimeters, .4 + index * .1, 1000, true);
        altimeters.tick(.61); assert(altimeters.result == altimeter_result::confirmed && fake_sim::transmissions.size() == count);
        assert(altimeters.begin(1, {16000, false}, .61));
        assert(fake_sim::transmissions.back().event == control_altimeters::set_event && fake_sim::transmissions.back().raw == 16000);
    }
    {
        auto altimeters = ready(); assert(altimeters.begin(2, {16160, true}, 0));
        assert(fake_sim::transmissions.size() == 1 && fake_sim::transmissions[0].event == control_altimeters::standard_event && fake_sim::transmissions[0].raw == 2);
        assert(altimeters.confirm(fake_sim::request_id)); confirmed(altimeters, 1000, false, 1010, true);
    }
    {
        auto altimeters = ready(); assert(altimeters.begin(1, {16016, false}, 0));
        assert(altimeters.confirm(fake_sim::request_id));
        for (unsigned index = 0; index < 3; ++index) sample(altimeters, .1 + index * .1, 1000, false, 1001, false);
        altimeters.tick(2.01); assert(altimeters.result == altimeter_result::failed);
        assert(!altimeters.begin(1, {16016, false}, .4) && fake_sim::transmissions.size() == 1); // Wrong-index/ignored output cannot pass or loop.
    }
    {
        auto altimeters = ready(); fake_sim::fail_transmission_after = 1;
        assert(!altimeters.begin(1, {16016, true}, 0) && altimeters.result == altimeter_result::failed && fake_sim::transmissions.size() == 1);
        assert(!altimeters.begin(1, {16000, false}, .1)); fake_sim::fail_transmission_after = -1;
    }
    {
        auto altimeters = ready(); assert(!altimeters.begin(0, {16000, false}, 0) && !altimeters.begin(3, {16000, false}, 0));
        assert(!altimeters.begin(1, {7999, false}, 0) && !altimeters.begin(1, {19201, false}, 0));
        assert(!altimeters.begin(1, {16000, false}, 1.1)); altimeters.reset_context();
        assert(!altimeters.begin(1, {16000, false}, 0) && fake_sim::transmissions.empty());
    }
    {
        auto altimeters = ready(); assert(altimeters.begin(1, {16016, false}, 0));
        SIMCONNECT_RECV_EXCEPTION error{}; error.dwSendID = fake_sim::transmissions.back().packet;
        assert(altimeters.exception(error) && altimeters.result == altimeter_result::failed && altimeters.fresh(0));
    }
    {
        auto altimeters = ready(); assert(altimeters.begin(1, {16016, false}, 0, altimeter_operation::observe));
        sample(altimeters, .1, 1002); altimeters.tick(.1);
        assert(fake_sim::transmissions.empty() && altimeters.result == altimeter_result::pending);
        assert(altimeters.supersede() && altimeters.begin(1, {16032, false}, .1, altimeter_operation::observe));
        for (unsigned index = 0; index < 3; ++index) sample(altimeters, .2 + index * .05, 1002);
        altimeters.tick(.31); assert(altimeters.result == altimeter_result::confirmed && fake_sim::transmissions.empty());
    }
    {
        auto altimeters = ready(); assert(altimeters.begin(2, {16176, false}, 0));
        const auto older = fake_sim::request_id;
        assert(altimeters.supersede() && !altimeters.begin(2, {16192, false}, 0));
        assert(fake_sim::transmissions.size() == 1 && altimeters.confirm(older));
        assert(!altimeters.begin(2, {16192, false}, 0)); // A checkpoint alone does not refresh the cached pressure.
        sample(altimeters, .01, 1000, false, 1011, false);
        assert(altimeters.begin(2, {16192, false}, .01) && fake_sim::transmissions.size() == 2);
        assert(altimeters.confirm(fake_sim::request_id)); confirmed(altimeters, 1000, false, 1012, false);
    }
    {
        auto altimeters = ready(); assert(altimeters.begin(1, {16016, false}, 0));
        const auto packet = fake_sim::transmissions.back().packet; assert(altimeters.supersede());
        SIMCONNECT_RECV_EXCEPTION error{}; error.dwSendID = packet;
        assert(altimeters.exception(error) && altimeters.result == altimeter_result::failed);
        assert(!altimeters.begin(1, {16000, false}, 0, altimeter_operation::observe));
    }
    {
        auto altimeters = ready(); assert(altimeters.begin(2, {16176, false}, 0));
        assert(altimeters.supersede() && altimeters.confirm(fake_sim::request_id));
        // The cached 1010 reading predates the queued 1011 write. Returning to
        // 1010 must wait for a new reading and then emit the restoring event.
        assert(!altimeters.begin(2, {16160, false}, 0));
        sample(altimeters, .01, 1000, false, 1011);
        assert(altimeters.begin(2, {16160, false}, .01));
        assert(fake_sim::transmissions.size() == 2 && fake_sim::transmissions.back().raw == 16160);
        assert(altimeters.confirm(fake_sim::request_id)); confirmed(altimeters, 1000, false, 1010, false);
    }
    std::cout << "Altimeter SDK checks passed: independent indexed absolute output, equal-state no-op, retained QNH in STD, explicit STD entry/exit, checkpoints and three fresh read-backs, ignored/wrong-index output, partial failure, stale context refusal, observe-only owner confirmation and superseding without bypassing queued SDK calls. SDK implementations are local; no simulator access.\n";
}
