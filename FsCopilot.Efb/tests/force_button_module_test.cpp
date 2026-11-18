#define _CRT_SECURE_NO_WARNINGS
#define FSC_TEST_SILENT_TRACE
#define FSC_CONNECTION_EPHEMERAL
#define FSC_PROFILE_ROOT "artifacts/cockpit-recovery/tests/force-module-"
#include <cassert>
#include "../Wasm/control_test.cpp"
void confirm_gate(){SIMCONNECT_RECV_SYSTEM_STATE p{};p.dwID=SIMCONNECT_RECV_ID_SYSTEM_STATE;p.dwRequestID=first_barrier+gate.barrier;dispatch(&p,sizeof(p),nullptr);}
void button(bool down){SIMCONNECT_RECV_EVENT p{};p.dwID=SIMCONNECT_RECV_ID_EVENT;p.uEventID=0x8000+(down?6:7);dispatch(&p,sizeof(p),nullptr);}
int main(){
    const char* prefix=FSC_PROFILE_ROOT "force-steering-button";
    force_button_settings saved{};saved.revision=100;saved.enabled=1;saved.button=3;saved.product=77;std::strcpy(saved.name,"Force test");
    assert(force_button_write(prefix,saved));
    module_init();assert(start_requested);
    SIMCONNECT_RECV opened{};opened.dwID=SIMCONNECT_RECV_ID_OPEN;dispatch(&opened,sizeof(opened),nullptr);confirm_gate();assert(gate.ready);
    SIMCONNECT_RECV_CONTROLLERS_LIST devices{};devices.dwID=SIMCONNECT_RECV_ID_CONTROLLERS_LIST;devices.dwArraySize=1;
    devices.rgData[0].DeviceId=4;devices.rgData[0].ProductId=77;std::strcpy(devices.rgData[0].DeviceName,"Force test");dispatch(&devices,sizeof(devices),nullptr);
    SIMCONNECT_RECV_SYSTEM_STATE configured{};configured.dwID=SIMCONNECT_RECV_ID_SYSTEM_STATE;configured.dwRequestID=fake_sim::request_id;dispatch(&configured,sizeof(configured),nullptr);
    assert(force_button.ready());
    assert(gate.assign_mock(select_all,seconds()));confirm_gate();assert(gate.blocked==select_all);
    button(true);assert(gate.blocked==select_all&&!gate.applying);
    button(false);button(true);assert(gate.applying);confirm_gate();
    assert(gate.blocked==select_quadrant&&gate.ready&&!gate.applying);
    const auto revision=gate.revision;button(true);assert(gate.revision==revision);
    assert(gate.assign_mock(select_all,seconds()));confirm_gate();
    const auto maps_before=fake_sim::input_mappings.size();
    assert(force_button.listen(4,seconds()));
    const auto attempted=fake_sim::input_mappings;
    for(size_t i=maps_before;i<attempted.size();++i)if((attempted[i].down-0x8000)/2>=32) {
        SIMCONNECT_RECV_EXCEPTION rejected{};rejected.dwID=SIMCONNECT_RECV_ID_EXCEPTION;rejected.dwException=3;rejected.dwIndex=1;rejected.dwSendID=attempted[i].packet;
        dispatch(&rejected,sizeof(rejected),nullptr);
    }
    assert(force_button.listening()&&gate.ready&&gate.blocked==select_all);
    configured.dwRequestID=fake_sim::request_id;dispatch(&configured,sizeof(configured),nullptr);
    button(true);button(false);
    assert(!force_button.listening()&&gate.blocked==select_all);
    configured.dwRequestID=fake_sim::request_id;dispatch(&configured,sizeof(configured),nullptr);
    assert(force_button.ready());
    button(true);confirm_gate();assert(gate.blocked==select_quadrant);
    // A fatal optional rejection must not recursively enqueue more cleanup.
    assert(force_button.listen(4,seconds()));
    SIMCONNECT_RECV_EXCEPTION rejected{};rejected.dwID=SIMCONNECT_RECV_ID_EXCEPTION;rejected.dwException=3;rejected.dwSendID=fake_sim::last_packet;
    dispatch(&rejected,sizeof(rejected),nullptr);
    const auto cleanup_end=fake_sim::last_packet;
    assert(!force_button.listening()&&!force_button.ready()&&gate.ready&&gate.blocked==select_quadrant);
    for(unsigned i=0;i<5000;++i) {
        rejected.dwSendID=cleanup_end-(i%2);dispatch(&rejected,sizeof(rejected),nullptr);
        assert(fake_sim::last_packet==cleanup_end&&gate.ready&&gate.blocked==select_quadrant);
    }
    assert(!error[0]);
    module_deinit();
    char path[600]{};for(unsigned slot=0;slot<2;++slot){std::snprintf(path,sizeof(path),"%s.%u.bin",prefix,slot);std::remove(path);}
    std::puts("PASS production-module hardware Force: valid capture with rejected unused buttons, Steering-only takeover, optional-failure isolation and 5000 cleanup errors without recursive SDK calls or lost control discovery. SDK simulated.");
}
