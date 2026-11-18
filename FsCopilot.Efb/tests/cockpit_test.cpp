#define _CRT_SECURE_NO_WARNINGS
#include <cassert>
#include <iostream>
#include <vector>
#include "../Wasm/control_cockpit.h"
using namespace fsc_controls;
namespace { unsigned calls=0; bool execute(const char* expression){++calls;return !std::strcmp(expression,"1 16224 (>K:2:KOHLSMAN_SET)");} }
namespace {
unsigned switch_calls = 0;
bool execute_switch(const char* expression) {
    const char* setters[] = {"(>B:ELECTRICAL_Battery_1_Toggle)", "(>B:LIGHTING_NAV_1_Toggle)", "(>B:LIGHTING_STROBE_1_Toggle)"};
    for (unsigned i = 0; i < 3; ++i) if (!std::strcmp(expression, setters[i])) {
        ++switch_calls; fake_vars::state().bvars[i] = 1 - fake_vars::state().bvars[i]; return true;
    }
    return false;
}
}
int main() {
    control_cockpit channel;session_packet session{};session.phase=session_phase::active;
    std::strcpy(session.backend,"abc");std::strcpy(session.session,"1234");
    channel.observe(&definition_profiles[0],"abc",session,1);
    const auto state=channel.status();assert(cockpit_valid(state)&&state.serial&&state.count>100);
    unsigned left=0,right=0;
    for(unsigned i=0;i<state.count;i++) {
        if(!std::strcmp(definition_profiles[0].cockpit[i].name,"A:KOHLSMAN SETTING MB EX1:1"))left=i;
        if(!std::strcmp(definition_profiles[0].cockpit[i].name,"A:KOHLSMAN SETTING MB EX1:2"))right=i;
    }
    assert(left!=right&&channel.known(left)&&channel.known(right)&&state.values[left]==1013.25);
    cockpit_packet command{};command.kind=cockpit_kind::apply;command.command=1;command.read_serial=state.serial;command.index=left;
    std::strcpy(command.native,"abc");std::strcpy(command.backend,"abc");std::strcpy(command.session,"1234");std::strcpy(command.profile,state.profile);
    std::strcpy(command.expression,"1 16224 (>K:2:KOHLSMAN_SET)");command.values[0]=1014;command.values[1]=1013.25;
    auto wrong=command;std::strcpy(wrong.session,"5678");assert(!channel.apply(wrong,session,1.01,execute)&&calls==0);
    assert(!channel.apply(command,session,std::numeric_limits<double>::quiet_NaN(),execute)&&calls==0);
    assert(channel.apply(command,session,1.01,execute)&&calls==1&&channel.status().result==cockpit_result::pending);
    assert(!channel.apply(command,session,1.02,execute));
    fake_vars::state().pressure[0]=1014;
    for(unsigned i=1;i<=3;i++)channel.observe(&definition_profiles[0],"abc",session,1.02+i*.05);
    assert(channel.status().result==cockpit_result::succeeded&&channel.status().values[right]==1013.25);
    command.command=2;command.read_serial=channel.status().serial;command.values[1]=1000;
    assert(!channel.apply(command,session,1.2,execute)&&calls==1&&channel.status().result==cockpit_result::rejected);
    command.command=3;command.values[1]=1014;fake_vars::state().pressure[0]=1015;
    assert(!channel.apply(command,session,1.21,execute)&&calls==1); // A knob changed after the last IPC snapshot.
    const auto serial=channel.status().serial;channel.reset();channel.observe(&definition_profiles[0],"abc",session,1.25);assert(channel.status().serial>serial);
    auto report=command;report.kind=cockpit_kind::report;report.command=4;report.read_serial=channel.status().serial;report.linked=1;
    assert(channel.apply(report,session,1.251,execute)&&calls==1);
    assert(channel.request_resync(session,1.26)&&channel.status().resync_request==1);
    assert(!channel.request_resync(session,1.27));
    report.command=5;report.resync_phase=3;report.checked=2;report.resync_ack=0;
    assert(channel.apply(report,session,1.28,execute)&&channel.status().resync_phase==1);
    report.command=6;report.resync_ack=1;report.resync_phase=2;report.checked=1;report.corrected=1;report.resync_pending=1;
    std::strcpy(report.issues,"Unavailable: test instrument");
    assert(channel.apply(report,session,1.29,execute)&&channel.status().resync_phase==2&&calls==1);
    auto* wire=std::fopen("artifacts/cockpit-recovery/tests/native-cockpit-v2.bin","wb");assert(wire);
    assert(std::fwrite(&channel.status(),1,sizeof(cockpit_packet),wire)==sizeof(cockpit_packet));std::fclose(wire);
    session.phase=session_phase::ended;channel.observe(&definition_profiles[0],"abc",session,1.3);
    command.command=3;command.read_serial=channel.status().serial;assert(!channel.apply(command,session,1.31,execute)&&calls==1);
    assert(channel.status().resync_request==0&&channel.status().resync_phase==0&&!channel.request_resync(session,1.31));
    {
        control_cockpit switches; session.phase = session_phase::active;
        const char* names[] = {"B:ELECTRICAL_Battery_1", "B:LIGHTING_NAV_1", "B:LIGHTING_STROBE_1"};
        const char* setters[] = {"(>B:ELECTRICAL_Battery_1_Toggle)", "(>B:LIGHTING_NAV_1_Toggle)", "(>B:LIGHTING_STROBE_1_Toggle)"};
        unsigned indices[3]{};
        for (unsigned n = 0; n < 3; ++n) for (unsigned i = 0; i < definition_profiles[0].cockpit_count; ++i)
            if (!std::strcmp(names[n], definition_profiles[0].cockpit[i].name)) indices[n] = i;
        fake_vars::state().bvars[1] = 1;
        switches.observe(&definition_profiles[0], "abc", session, 2);
        for (unsigned n = 0; n < 3; ++n) assert(switches.known(indices[n]) && switches.status().values[indices[n]] == (n == 1 ? 1 : 0));
        auto change = command; change.command = 1; change.read_serial = switches.status().serial;
        change.index = indices[0]; change.values[0] = 1; change.values[1] = 0; std::strcpy(change.expression, setters[0]);
        assert(switches.apply(change, session, 2.01, execute_switch) && switch_calls == 1);
        for (unsigned n = 1; n <= 3; ++n) switches.observe(&definition_profiles[0], "abc", session, 2.01 + n * .05);
        assert(switches.status().result == cockpit_result::succeeded && switches.status().values[indices[0]] == 1);
        change.command = 2; change.read_serial = switches.status().serial;
        assert(!switches.apply(change, session, 2.17, execute_switch) && switch_calls == 1); // Stale input-event state cannot toggle twice.
        change.command = 3; change.values[1] = 1;
        assert(switches.apply(change, session, 2.18, execute_switch) && switch_calls == 1); // Equal state never executes a toggle.
        change.command = 4; change.index = indices[1]; change.values[0] = 0; std::strcpy(change.expression, setters[1]);
        assert(switches.apply(change, session, 2.19, execute_switch) && switch_calls == 2);
        for (unsigned n = 1; n <= 3; ++n) switches.observe(&definition_profiles[0], "abc", session, 2.19 + n * .05);
        assert(switches.status().result == cockpit_result::succeeded && switches.status().values[indices[1]] == 0);
        fake_vars::state().bvar_available[0] = false; fake_vars::state().bvar_failed[1] = true;
        fake_vars::state().bvars[2] = std::numeric_limits<double>::quiet_NaN();
        switches.observe(&definition_profiles[0], "abc", session, 2.35);
        for (const auto i : indices) assert(!switches.known(i)); // Missing/failed/nonfinite input events are unavailable, never fabricated OFF.
        change.command = 5; change.read_serial = switches.status().serial;
        assert(!switches.apply(change, session, 2.36, execute_switch) && switch_calls == 2);
        assert(fake_vars::state().bvar_gets > 0);
    }
    auto corrupt=state;corrupt.reserved[0]=1;assert(!cockpit_valid(corrupt));
    std::cout<<"Ordinary cockpit native checks passed: typed indexed pressure, separate altimeters, identity/session/readback fences, replay refusal, confirmation, monotonic reload and no ended-session execution. SDK simulated.\n";
}
