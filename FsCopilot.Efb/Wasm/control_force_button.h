#pragma once
#include <SimConnect.h>
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <cstddef>
#include <new>

namespace fsc_controls {
#pragma pack(push,1)
struct force_button_settings {
    uint64_t revision=0;
    uint32_t enabled=0, product=0, composite=0, button=0;
    unsigned short hardware[4]{};
    char name[256]{};
};
struct force_button_file {
    uint32_t magic=0x31424646,version=1;
    force_button_settings settings{};
    uint32_t checksum=0;
};
#pragma pack(pop)
inline bool force_button_valid(const force_button_settings& s) {
    return s.revision && s.enabled<=1 && s.button<128 && std::memchr(s.name,0,sizeof(s.name)) && (!s.enabled||s.name[0]);
}
inline uint32_t force_button_hash(const force_button_file& f) {
    uint32_t hash=2166136261u;const auto* p=reinterpret_cast<const unsigned char*>(&f);
    for(size_t i=0;i<offsetof(force_button_file,checksum);++i)hash=(hash^p[i])*16777619u;
    return hash;
}
inline bool force_button_read(const char* prefix,force_button_settings& result) {
    bool found=false;
    for(unsigned slot=0;slot<2;++slot) {
        char path[600]{};std::snprintf(path,sizeof(path),"%s.%u.bin",prefix,slot);
        auto* file=std::fopen(path,"rb");if(!file)continue;
        force_button_file value{};const bool complete=std::fread(&value,1,sizeof(value),file)==sizeof(value);
        const bool exact=std::fgetc(file)==EOF&&!std::ferror(file);std::fclose(file);
        if(complete&&exact&&value.magic==0x31424646&&value.version==1&&force_button_valid(value.settings)&&value.checksum==force_button_hash(value)&&
           (!found||value.settings.revision>result.revision)){result=value.settings;found=true;}
    }
    return found;
}
inline bool force_button_write(const char* prefix,const force_button_settings& settings) {
    if(!force_button_valid(settings))return false;
    force_button_file value{};value.settings=settings;value.checksum=force_button_hash(value);
    char path[600]{};std::snprintf(path,sizeof(path),"%s.%u.bin",prefix,static_cast<unsigned>(settings.revision%2));
    auto* file=std::fopen(path,"wb");if(!file)return false;
    const bool complete=std::fwrite(&value,1,sizeof(value),file)==sizeof(value);
    const bool flushed=std::fflush(file)==0;const bool closed=std::fclose(file)==0;
    force_button_settings verified{};
    return complete&&flushed&&closed&&force_button_read(prefix,verified)&&!std::memcmp(&verified,&settings,sizeof(settings));
}

// Independent, non-masking input group: it remains live while Steering is held.
// DeviceId is ephemeral. Persist the SDK identity, then resolve it uniquely.
class control_force_button {
    static constexpr DWORD input_group=0x7240,notification_group=0x7241,first_event=0x8000;
    DWORD barrier_=0x7e000000;
    HANDLE connection_{};
    char prefix_[512]{};
    enum class call_kind { setup, button, discovery, cleanup };
    struct sdk_call { DWORD packet=0, generation=0; call_kind kind=call_kind::setup; unsigned button=128; };
    sdk_call packets_[2048]{};unsigned packet_cursor_=0;
    bool registered_[128]{}, rejected_[128]{};
    call_kind failed_kind_=call_kind::setup;
    unsigned failed_button_=128;
    DWORD mapped_device_=0;
    bool mapped_=false,confirmed_=false,enumerating_=false,pressed_=false,armed_=false,input_exists_=false,notification_exists_=false;
    bool listening_=false,captured_=false;
    unsigned captured_button_=0;
    SIMCONNECT_CONTROLLER_ITEM selected_{};
    double last_enumeration_=-100,listen_at_=0;
    bool record(HRESULT result,call_kind kind=call_kind::setup,unsigned button=128) {
        if(FAILED(result))return false;
        DWORD packet=0;if(SUCCEEDED(SimConnect_GetLastSentPacketID(connection_,&packet)))packets_[packet_cursor_++%2048]={packet,barrier_,kind,button};
        return true;
    }
    void stop() {
        // Clear the local flag BEFORE sending cleanup. A rejection of cleanup
        // must never enqueue more cleanup and recursively flood SimConnect.
        const bool had_input=input_exists_;input_exists_=false;
        mapped_=confirmed_=pressed_=armed_=false;
        if(connection_&&had_input){record(SimConnect_SetInputGroupState(connection_,input_group,0),call_kind::cleanup);record(SimConnect_ClearInputGroup(connection_,input_group),call_kind::cleanup);}
    }
    bool map(DWORD device,bool capture) {
        if(barrier_==0x7effffff){error="Reload the aircraft before assigning another button.";return false;}++barrier_;
        stop();mapped_device_=device;mapped_=true;
        bool ok=true;
        if(notification_exists_){notification_exists_=false;ok=record(SimConnect_ClearNotificationGroup(connection_,notification_group));}
        std::memset(rejected_,0,sizeof(rejected_));
        const unsigned begin=capture?0:settings.button,end=capture?128:begin+1;
        for(unsigned button=begin;ok&&button<end;++button) {
            const DWORD down=first_event+button*2,up=down+1;
            if(!registered_[button]) {
                ok=record(SimConnect_MapClientEventToSimEvent(connection_,down,nullptr))&&record(SimConnect_MapClientEventToSimEvent(connection_,up,nullptr));
                registered_[button]=ok;
            }
            ok=ok&&record(SimConnect_AddClientEventToNotificationGroup(connection_,notification_group,down,false))&&record(SimConnect_AddClientEventToNotificationGroup(connection_,notification_group,up,false));notification_exists_=true;
            char definition[80]{};std::snprintf(definition,sizeof(definition),"joystick:%u:button:%u",static_cast<unsigned>(device),button);
            if(ok){input_exists_=true;rejected_[button]=!record(SimConnect_MapInputEventToClientEvent_EX1(connection_,input_group,definition,down,0,up,0,false),call_kind::button,button);}
        }
        ok=ok&&record(SimConnect_SetNotificationGroupPriority(connection_,notification_group,SIMCONNECT_GROUP_PRIORITY_HIGHEST))&&
            record(SimConnect_SetInputGroupPriority(connection_,input_group,SIMCONNECT_GROUP_PRIORITY_HIGHEST))&&
            record(SimConnect_SetInputGroupState(connection_,input_group,1))&&record(SimConnect_RequestSystemState(connection_,barrier_,"Sim"));
        if(!ok){stop();error="Cannot map the Force takeover button.";}return ok;
    }
    static bool same(const SIMCONNECT_CONTROLLER_ITEM& device,const force_button_settings& s) {
        return !std::strcmp(device.DeviceName,s.name)&&device.ProductId==s.product&&device.CompositeID==s.composite&&
            !std::memcmp(&device.HardwareVersion,s.hardware,sizeof(s.hardware));
    }
    void resolve() {
        if(listening_||!settings.enabled)return;
        const SIMCONNECT_CONTROLLER_ITEM* match=nullptr;unsigned matches=0;
        for(unsigned i=0;i<count;++i)if(same(devices[i],settings)){match=&devices[i];++matches;}
        if(matches!=1){stop();error=matches?"Saved controller identity is ambiguous; assign it again.":"Saved controller is disconnected.";return;}
        if(!mapped_||mapped_device_!=match->DeviceId){error="";map(match->DeviceId,false);}
    }
public:
    // Reconstruct in static storage; assignment from a temporary puts the
    // packet history/controller list on MSFS's limited callback stack.
    void reset(){new(this) control_force_button{};}
    force_button_settings settings{};
    SIMCONNECT_CONTROLLER_ITEM devices[16]{};
    unsigned count=0;
    const char* error="";
    bool listening()const{return listening_;}
    bool captured()const{return captured_;}
    bool ready()const{return mapped_&&confirmed_&&!listening_&&settings.enabled;}
    bool armed()const{return ready()&&armed_;}
    void load(const char* prefix) {std::snprintf(prefix_,sizeof(prefix_),"%s",prefix);(void)force_button_read(prefix_,settings);}
    void initialize(HANDLE connection,double now) {connection_=connection;last_enumeration_=now-20;tick(now);}
    void tick(double now) {
        if(listening_&&now-listen_at_>30){cancel();error="Button assignment timed out; try again.";}
        if(connection_&&now-last_enumeration_>=10&&!listening_) {
            last_enumeration_=now;enumerating_=true;
            if(!record(SimConnect_EnumerateControllers(connection_),call_kind::discovery)){enumerating_=false;error="Controller discovery is unavailable.";}
        }
    }
    bool controllers(const SIMCONNECT_RECV_CONTROLLERS_LIST* data,DWORD size) {
        const auto offset=reinterpret_cast<const char*>(data->rgData)-reinterpret_cast<const char*>(data);
        if(!enumerating_||size<static_cast<size_t>(offset))return false;
        const auto capacity=(size-static_cast<size_t>(offset))/sizeof(SIMCONNECT_CONTROLLER_ITEM);
        if(data->dwArraySize>capacity||data->dwArraySize>16){enumerating_=false;stop();error="Controller discovery returned too many devices.";return true;}
        // The SDK returns one variable-length list, not independently actionable entries.
        count=0;
        for(unsigned i=0;i<data->dwArraySize;++i)if(std::memchr(data->rgData[i].DeviceName,0,256))devices[count++]=data->rgData[i];
        enumerating_=false;resolve();return true;
    }
    bool listen(DWORD device,double now) {
        const SIMCONNECT_CONTROLLER_ITEM* selected=nullptr;
        for(unsigned i=0;i<count;++i)if(devices[i].DeviceId==device)selected=&devices[i];
        if(!selected||!connection_){error="Select a connected controller.";return false;}
        selected_=*selected;listening_=true;captured_=false;listen_at_=now;error="";
        if(!map(device,true)){listening_=false;return false;}return true;
    }
    void cancel(){listening_=captured_=false;stop();resolve();}
    bool clear() {
        if(settings.revision==UINT64_MAX)return false;
        auto next=settings;++next.revision;next.enabled=0;
        if(!force_button_write(prefix_,next)){error="Cannot save the button binding.";return false;}
        settings=next;listening_=captured_=false;stop();error="";return true;
    }
    bool confirm(DWORD request){
        if(request<0x7e000000||request>0x7effffff)return false;
        if(request==barrier_) {
            bool supported=false;for(unsigned i=0;i<128;++i)if(!rejected_[i])supported=true;
            if(mapped_&&((listening_&&!supported)||(!listening_&&rejected_[settings.button]))) {
                stop();listening_=captured_=false;error="No supported button mapping was found on this controller.";
            } else confirmed_=mapped_;
        }
        return true;
    }
    const char* failed_operation()const{return failed_kind_==call_kind::button?"Map controller button":failed_kind_==call_kind::discovery?"Enumerate controllers":failed_kind_==call_kind::cleanup?"Cleanup button input":"Configure button input";}
    unsigned failed_button()const{return failed_button_;}
    bool exception(const SIMCONNECT_RECV_EXCEPTION& exception){
        for(unsigned i=0;exception.dwSendID&&i<2048;++i)if(packets_[i].packet==exception.dwSendID){
            const auto call=packets_[i];failed_kind_=call.kind;failed_button_=call.button;
            if(call.kind==call_kind::cleanup)return true;
            if(call.kind==call_kind::discovery){enumerating_=false;error="Controller discovery is unavailable; control learning is unchanged.";return true;}
            if(call.generation!=barrier_)return true;
            if(call.kind==call_kind::button&&listening_) {
                // Devices expose different button counts. Reject only this
                // candidate; valid candidates must remain available to learn.
                rejected_[call.button]=true;
                if(captured_&&captured_button_==call.button)captured_=false;
                bool supported=false;for(unsigned j=0;j<128;++j)if(!rejected_[j])supported=true;
                if(supported)return true;
            }
            stop();listening_=captured_=false;error="Simulator rejected the Force button mapping; assign it again.";return true;
        }
        return false;
    }
    // Returns true only on a fresh, armed down edge. Learning press/release cannot force.
    bool event(DWORD id,bool& fire) {
        if(id<first_event||id>=first_event+256)return false;
        fire=false;if(!mapped_||!confirmed_)return true;
        const unsigned button=(id-first_event)/2;const bool down=(id-first_event)%2==0;
        if(rejected_[button])return true;
        if(listening_) {
            if(down&&!captured_){captured_=true;captured_button_=button;}
            else if(!down&&captured_&&button==captured_button_) {
                if(settings.revision==UINT64_MAX){cancel();error="Binding revision exhausted.";return true;}
                force_button_settings next{};next.revision=settings.revision+1;next.enabled=1;next.button=button;
                next.product=selected_.ProductId;next.composite=selected_.CompositeID;std::memcpy(next.hardware,&selected_.HardwareVersion,sizeof(next.hardware));std::memcpy(next.name,selected_.DeviceName,256);
                if(!force_button_write(prefix_,next)){cancel();error="Cannot save the button binding.";return true;}
                settings=next;listening_=captured_=false;stop();resolve();armed_=true; // The assignment release is already observed.
            }
        } else if(button==settings.button) {
            if(!down){pressed_=false;armed_=true;}
            else if(!pressed_){pressed_=true;fire=armed_;}
        }
        return true;
    }
    void dispose(){stop();connection_={};}
};
}
