#pragma once
#include <cstdio>
#include <new>
#include "control_cockpit_protocol.h"
#include "control_definition_profile.h"
namespace fsc_controls {
// Read-only diagnostics over the exact ordinary-sync catalog and readbacks.
// It has no executor, network sender, ownership or hardware mapping access.
class control_instrument_monitor {
public:
    struct change { unsigned index=0; uint64_t sequence=0; double before=0, after=0; };
    static constexpr unsigned page_size=16, history_limit=12;
    change recent[history_limit]{};
    unsigned recent_count=0, page=0;
    uint64_t sequence=0;
    void reset(){new(this) control_instrument_monitor{};}
    void observe(const definition_profile* profile,const cockpit_packet& values) {
        if(profile_!=profile||std::strcmp(native_,values.native)) {
            reset();profile_=profile;
            std::snprintf(native_,sizeof(native_),"%s",values.native);
        }
        if(!profile||values.count!=profile->cockpit_count||values.count>cockpit_limit)return;
        for(unsigned i=0;i<values.count;++i) {
            const bool valid=!!(values.valid[i/32]&(1u<<(i%32)));
            if(valid&&std::isfinite(values.values[i])) {
                if(known_[i]&&std::abs(values.values[i]-previous_[i])>profile->cockpit[i].tolerance&&sequence<9007199254740991ull) {
                    unsigned slot=recent_count;
                    for(unsigned j=0;j<recent_count;++j)if(recent[j].index==i){slot=j;break;}
                    const change event{i,++sequence,previous_[i],values.values[i]};
                    if(slot==recent_count&&recent_count<history_limit)++recent_count;
                    if(slot>=history_limit)slot=history_limit-1;
                    for(unsigned j=slot;j>0;--j)recent[j]=recent[j-1];
                    recent[0]=event;previous_[i]=values.values[i];
                } else if(!known_[i])previous_[i]=values.values[i];
                known_[i]=true;
            } else known_[i]=false; // A returning read is a baseline, not a knob turn.
        }
    }
    void next_page(unsigned count){page=count?(page+page_size>=count?0:page+page_size):0;}
private:
    const definition_profile* profile_=nullptr;
    char native_[24]{};
    bool known_[cockpit_limit]{};
    double previous_[cockpit_limit]{};
};
}
