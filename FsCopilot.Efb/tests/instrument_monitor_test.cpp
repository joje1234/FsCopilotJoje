#define _CRT_SECURE_NO_WARNINGS
#include <cassert>
#include <cstdio>
#include "../Wasm/control_instrument_monitor.h"
using namespace fsc_controls;
int main(){
    const cockpit_definition entries[]={{"A:HEADING","degrees",true,.02},{"L:SWITCH","Number",false,.001}};
    definition_profile profile{"test","adapter",2,nullptr,0,entries,2,"abc"};
    cockpit_packet values{};values.count=2;std::strcpy(values.native,"123");values.valid[0]=3;values.values[0]=100;
    control_instrument_monitor monitor;monitor.observe(&profile,values);assert(!monitor.recent_count);
    values.values[0]=100.01;monitor.observe(&profile,values);assert(!monitor.recent_count);
    values.values[0]=100.03;monitor.observe(&profile,values);assert(monitor.recent_count==1&&monitor.recent[0].before==100&&monitor.recent[0].after==100.03);
    values.values[0]=100.1;monitor.observe(&profile,values);assert(monitor.recent_count==1&&monitor.sequence==2);
    values.valid[0]=1;monitor.observe(&profile,values);values.values[1]=1;values.valid[0]=3;monitor.observe(&profile,values);
    assert(monitor.sequence==2);
    values.values[1]=0;monitor.observe(&profile,values);assert(monitor.recent_count==2&&monitor.recent[0].index==1);
    std::strcpy(values.native,"456");monitor.observe(&profile,values);assert(!monitor.sequence&&!monitor.recent_count);
    cockpit_definition many[32]{};for(auto& entry:many)entry={"A:TEST","Number",true,.001};
    definition_profile larger{"test","adapter",2,nullptr,0,many,32,"def"};values.count=32;values.valid[0]=UINT32_MAX;
    monitor.observe(&larger,values);for(unsigned i=0;i<32;++i)values.values[i]+=10;monitor.observe(&larger,values);
    assert(monitor.recent_count==12&&monitor.sequence==32&&monitor.recent[0].index==31&&monitor.recent[11].index==20);
    monitor.next_page(32);assert(monitor.page==16);monitor.next_page(32);assert(!monitor.page);
    monitor.observe(nullptr,values);assert(!monitor.sequence&&!monitor.recent_count&&!monitor.page);
    std::puts("PASS read-only instrument monitor: exact catalog, precision thresholds, baseline/reconnect/profile fences, coalesced bounded activity and catalog paging. No SDK or network access.");
}
