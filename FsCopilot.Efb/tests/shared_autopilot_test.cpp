#define _CRT_SECURE_NO_WARNINGS
#include <cassert>
#include <iostream>
#include "../Wasm/control_cockpit.h"
using namespace fsc_controls;
namespace {
unsigned calls=0, slot=0;double result=0;
bool execute(const char*) {++calls;fake_vars::state().autopilot[slot]=result;return true;}
unsigned entry(const char* name) {
    const auto& profile=definition_profiles[0];
    for(unsigned i=0;i<profile.cockpit_count;++i)if(!std::strcmp(profile.cockpit[i].name,name))return i;
    assert(false);return 0;
}
}
int main() {
    control_cockpit channel;session_packet session{};session.phase=session_phase::active;
    std::strcpy(session.backend,"abc");std::strcpy(session.session,"1234");session.flags=session_host;
    auto& ap=fake_vars::state().autopilot;ap[10]=90;ap[11]=5000;ap[12]=500;
    double now=1;channel.observe(&definition_profiles[0],"abc",session,now);
    unsigned indices[13]{};
    for(unsigned i=0;i<13;++i) {
        char name[128]{};std::snprintf(name,sizeof(name),i>=9?"A:%s:1":"A:%s",fake_vars::autopilot_names[i]);
        indices[i]=entry(name);assert(channel.known(indices[i]) && channel.status().values[indices[i]]==ap[i]);
        assert(definition_profiles[0].cockpit[indices[i]].shared);
    }
    uint64_t command_id=0;
    auto apply=[&](unsigned axis,double desired,double observed,const char* expression) {
        cockpit_packet command{};command.kind=cockpit_kind::apply;command.command=++command_id;command.read_serial=channel.status().serial;command.index=indices[axis];
        std::strcpy(command.native,"abc");std::strcpy(command.backend,"abc");std::strcpy(command.session,"1234");
        std::strcpy(command.profile,channel.status().profile);std::strcpy(command.expression,expression);command.values[0]=desired;command.values[1]=observed;
        return channel.apply(command,session,now+.001,execute);
    };
    // Actual current SDK readback still fences a stale toggle or rotary plan.
    ap[0]=1;assert(!apply(0,1,0,"(>K:AUTOPILOT_ON)") && !calls);
    now+=.05;channel.observe(&definition_profiles[0],"abc",session,now);
    assert(apply(0,1,1,"(>K:AUTOPILOT_ON)") && !calls && channel.status().result==cockpit_result::succeeded);
    slot=0;result=0;assert(apply(0,0,1,"(>K:AUTOPILOT_OFF)") && calls==1);
    for(unsigned i=0;i<3;++i){now+=.05;channel.observe(&definition_profiles[0],"abc",session,now);}
    assert(channel.status().result==cockpit_result::succeeded && !ap[0]);
    slot=10;result=7760*360.0/16384; // Independent SDK quantization of 170.5 degrees.
    assert(apply(10,170.5,90,"1 7760 (>K:2:AP_HEADING_BUG_SET_EX1)"));
    for(unsigned i=0;i<3;++i){now+=.05;channel.observe(&definition_profiles[0],"abc",session,now);}
    assert(channel.status().result==cockpit_result::succeeded && std::abs(channel.status().values[indices[10]]-170.5)<.022);
    assert(ap[11]==5000 && ap[12]==500 && fake_vars::state().pressure[0]==1013.25 && fake_vars::state().pressure[1]==1013.25);
    // The matching guest path does not require an AP-owner adapter either.
    session.flags=0;slot=12;result=-700;assert(apply(12,-700,500,"1 -700 (>K:2:AP_VS_VAR_SET_ENGLISH)"));
    for(unsigned i=0;i<3;++i){now+=.05;channel.observe(&definition_profiles[0],"abc",session,now);}
    assert(channel.status().result==cockpit_result::succeeded && ap[12]==-700);
    fake_vars::state().autopilot_failed=10;now+=.05;channel.observe(&definition_profiles[0],"abc",session,now);
    assert(!channel.known(indices[10]) && !apply(10,180,result,"1 8192 (>K:2:AP_HEADING_BUG_SET_EX1)"));
    assert(channel.known(indices[0]) && channel.known(indices[11]));
    std::cout<<"Shared AP native scalar path passed: all mode/selector getters and indexed units, either participant, stale-readback fence, quantized heading confirmation, independent barometers and per-reading failure. SDK/executor simulated; no MSFS access.\n";
}
