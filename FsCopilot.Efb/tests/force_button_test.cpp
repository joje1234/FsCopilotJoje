#define _CRT_SECURE_NO_WARNINGS
#include <cassert>
#include <cstring>
#include "../Wasm/control_force_button.h"
using namespace fsc_controls;
int main() {
    const char* prefix="artifacts/cockpit-recovery/tests/force-binding-test";
    char path[600]{};
    for(unsigned i=0;i<2;++i){std::snprintf(path,sizeof(path),"%s.%u.bin",prefix,i);std::remove(path);}
    control_force_button binding;binding.load(prefix);binding.initialize(1,0);
    SIMCONNECT_RECV_CONTROLLERS_LIST list{};list.dwArraySize=1;
    auto& device=list.rgData[0];std::strcpy(device.DeviceName,"Test Yoke");device.DeviceId=2;device.ProductId=31;device.CompositeID=9;
    assert(binding.controllers(&list,sizeof(list)));assert(binding.count==1&&!binding.ready());
    assert(binding.listen(2,1));assert(binding.listening());
    assert(binding.confirm(fake_sim::request_id));bool fire=false;
    assert(binding.event(0x8000+10,fire)&&!fire&&binding.captured());
    binding.event(0x8000+10,fire);assert(!fire);
    binding.event(0x8000+11,fire);assert(!fire&&!binding.listening()&&binding.settings.button==5);
    const auto confirmed=binding.confirm(fake_sim::request_id);
    if(!confirmed||!binding.ready()){std::fprintf(stderr,"Force assignment: request=%u saved=%u ready=%u error=%s\n",fake_sim::request_id,binding.settings.enabled,binding.ready(),binding.error);}
    assert(confirmed&&binding.ready());
    binding.event(0x8000+10,fire);assert(fire);
    binding.event(0x8000+10,fire);assert(!fire); // No repeat while held.
    binding.event(0x8000+11,fire);assert(!fire);
    binding.event(0x8000+10,fire);assert(fire);
    control_force_button restarted;restarted.load(prefix);assert(restarted.settings.enabled&&restarted.settings.button==5);
    restarted.initialize(1,20);device.DeviceId=7;assert(restarted.controllers(&list,sizeof(list)));assert(restarted.confirm(fake_sim::request_id));
    assert(restarted.ready()&&!restarted.armed());
    restarted.event(0x8000+10,fire);assert(!fire); // A held button during load must not force.
    restarted.event(0x8000+11,fire);assert(!fire&&restarted.armed());
    restarted.event(0x8000+10,fire);assert(fire);
    restarted.tick(31);list.dwArraySize=0;assert(restarted.controllers(&list,sizeof(list)));assert(!restarted.ready());
    restarted.event(0x8000+10,fire);assert(!fire);
    restarted.tick(42);list.dwArraySize=1;device.DeviceId=9;assert(restarted.controllers(&list,sizeof(list)));assert(restarted.confirm(fake_sim::request_id)&&restarted.ready());
    assert(restarted.listen(9,43));assert(restarted.confirm(fake_sim::request_id));restarted.tick(74);assert(!restarted.listening());
    assert(restarted.clear());control_force_button cleared;cleared.load(prefix);assert(!cleared.settings.enabled);
    // A torn newest slot must fall back to the intact previous saved binding.
    std::snprintf(path,sizeof(path),"%s.%u.bin",prefix,static_cast<unsigned>(restarted.settings.revision%2));
    auto* file=std::fopen(path,"wb");assert(file);std::fputc(1,file);std::fclose(file);
    force_button_settings recovered{};assert(force_button_read(prefix,recovered)&&recovered.enabled);
    for(unsigned i=0;i<2;++i){std::snprintf(path,sizeof(path),"%s.%u.bin",prefix,i);std::remove(path);}
    std::puts("PASS saved Force button: assignment release, held-button suppression, one action per edge, restart/replug resolution, timeout, clear and torn-slot recovery. SDK simulated.");
}
