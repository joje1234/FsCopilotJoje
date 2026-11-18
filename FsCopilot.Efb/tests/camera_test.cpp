#include <cassert>
#include <iostream>
#include <vector>
#include <cstdlib>
#include "../Wasm/control_camera.h"
using namespace fsc_controls;
namespace {
void sample(control_camera& camera, double now, double state = 2, double type = 1, double option = 2, double maximum = 5) {
    std::vector<unsigned char> bytes(sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + sizeof(double) * 4);
    auto* packet = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(bytes.data());
    packet->dwRequestID = control_camera::read_request; packet->dwDefineCount = 4;
    double values[] = {state, type, option, maximum}; std::memcpy(&packet->dwData, values, sizeof(values));
    const auto size = static_cast<DWORD>(reinterpret_cast<char*>(&packet->dwData) - reinterpret_cast<char*>(packet) + sizeof(values));
    assert(camera.observe(packet, size, now));
}
control_camera ready() {
    fake_sim::object_writes.clear(); fake_sim::fail_object_write = false;
    control_camera camera; assert(camera.initialize(static_cast<HANDLE>(1)));
    assert(camera.confirm(fake_sim::request_id)); return camera;
}
}
int main() {
#ifdef _WIN32
    _set_error_mode(_OUT_TO_STDERR); _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
#endif
    {
        auto camera = ready(); camera.request(2); sample(camera, 0, 3); camera.tick(0);
        assert(camera.phase == camera_phase::waiting && fake_sim::object_writes.empty());
        sample(camera, .1); camera.tick(.1);
        assert(camera.phase == camera_phase::applying && fake_sim::object_writes.size() == 1);
        const auto& write = fake_sim::object_writes.back();
        assert(write.definition == control_camera::write_definition && write.bytes.size() == sizeof(double) * 2);
        double target[2]{}; std::memcpy(target, write.bytes.data(), sizeof(target)); assert(target[0] == 1 && target[1] == 5);
        sample(camera, .2, 2, 1, 5); sample(camera, .3, 2, 1, 5); sample(camera, .4, 2, 1, 5); camera.tick(.4);
        assert(camera.phase == camera_phase::applying); // Read-back cannot replace the SDK checkpoint.
        assert(camera.confirm(fake_sim::request_id)); camera.tick(.4); assert(camera.phase == camera_phase::confirmed);
        sample(camera, .5, 3); camera.tick(.5); camera.request(2); camera.tick(.5);
        assert(fake_sim::object_writes.size() == 1); // Do not continually recenter the user's camera.
        camera.request(1); sample(camera, .6, 2, 1, 5); camera.tick(.6);
        assert(fake_sim::object_writes.size() == 2);
        assert(camera.confirm(fake_sim::request_id));
        for (unsigned index = 0; index < 3; ++index) sample(camera, .7 + index * .05);
        camera.tick(.9); assert(camera.phase == camera_phase::confirmed && camera.seat == 1);
    }
    {
        auto camera = ready(); camera.request(1);
        for (unsigned index = 0; index < 3; ++index) { sample(camera, index * .1); camera.tick(index * .1); }
        assert(camera.phase == camera_phase::confirmed && fake_sim::object_writes.empty());
    }
    {
        auto camera = ready(); camera.request(2); sample(camera, 0, 2, 1, 2, 4); camera.tick(0);
        assert(camera.phase == camera_phase::unavailable && fake_sim::object_writes.empty());
    }
    {
        auto camera = ready(); camera.request(2); sample(camera, 0); camera.tick(0);
        assert(camera.confirm(fake_sim::request_id)); sample(camera, .5); camera.tick(1.01);
        assert(camera.phase == camera_phase::unavailable && fake_sim::object_writes.size() == 1);
        camera.tick(2); assert(fake_sim::object_writes.size() == 1);
    }
    {
        auto camera = ready(); camera.request(2); sample(camera, 0); fake_sim::fail_object_write = true; camera.tick(0);
        assert(camera.phase == camera_phase::unavailable && fake_sim::object_writes.empty()); fake_sim::fail_object_write = false;
    }
    {
        auto camera = ready(); camera.request(2); sample(camera, 0); camera.tick(0);
        SIMCONNECT_RECV_EXCEPTION error{}; error.dwSendID = fake_sim::last_packet;
        assert(camera.exception(error) && camera.phase == camera_phase::unavailable);
    }
    {
        auto camera = ready(); sample(camera, 0); camera.reset_context(); camera.request(2); camera.tick(.1);
        assert(fake_sim::object_writes.empty()); // Previous-aircraft observations cannot apply a new seat.
        sample(camera, 0); camera.tick(2); assert(fake_sim::object_writes.empty());
    }
    assert(fake_sim::transmissions.empty());
    std::cout << "Camera checks passed: default Pilot/Copilot views, external-view deferral, SDK checkpoint and three read-backs, no recenter loop, stale context, unavailable view, ignored write, SDK failure and reverse seat change. No simulator access.\n";
}
