#pragma once
#include <map>
#include <string>
#include <vector>
#include "MSFS/MSFS_WindowsTypes.h"
constexpr DWORD SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE = 10000000;
constexpr DWORD SIMCONNECT_GROUP_PRIORITY_HIGHEST = 1;
constexpr DWORD SIMCONNECT_GROUP_PRIORITY_STANDARD = 1900000000;
constexpr DWORD SIMCONNECT_GROUP_PRIORITY_DEFAULT = 2000000000;
constexpr DWORD SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY = 0x10;
enum { SIMCONNECT_RECV_ID_EXCEPTION = 1, SIMCONNECT_RECV_ID_OPEN = 2, SIMCONNECT_RECV_ID_QUIT = 3,
    SIMCONNECT_RECV_ID_EVENT = 4, SIMCONNECT_RECV_ID_EVENT_FILENAME = 6, SIMCONNECT_RECV_ID_SIMOBJECT_DATA = 8, SIMCONNECT_RECV_ID_SYSTEM_STATE = 15, SIMCONNECT_RECV_ID_CLIENT_DATA = 16, SIMCONNECT_RECV_ID_EVENT_EX1 = 29, SIMCONNECT_RECV_ID_CONTROLLERS_LIST = 40 };
constexpr unsigned SIMCONNECT_EXCEPTION_ALREADY_CREATED = 29;
constexpr unsigned SIMCONNECT_CREATE_CLIENT_DATA_FLAG_DEFAULT = 0, SIMCONNECT_CLIENT_DATA_SET_FLAG_DEFAULT = 0, SIMCONNECT_CLIENT_DATA_PERIOD_ON_SET = 3;
constexpr unsigned SIMCONNECT_DATATYPE_FLOAT64 = 4;
constexpr unsigned SIMCONNECT_PERIOD_VISUAL_FRAME = 2;
constexpr DWORD SIMCONNECT_OBJECT_ID_USER = 0;
struct SIMCONNECT_RECV { DWORD dwID; };
struct SIMCONNECT_RECV_SYSTEM_STATE : SIMCONNECT_RECV { DWORD dwRequestID; DWORD dwInteger; float fFloat; char szString[260]; };
struct SIMCONNECT_RECV_EVENT : SIMCONNECT_RECV { DWORD uEventID; DWORD dwData; };
struct SIMCONNECT_RECV_EVENT_EX1 : SIMCONNECT_RECV { DWORD uEventID; DWORD dwData0; DWORD dwData1; DWORD dwData2; DWORD dwData3; DWORD dwData4; };
struct SIMCONNECT_RECV_EVENT_FILENAME : SIMCONNECT_RECV_EVENT {};
struct SIMCONNECT_RECV_EXCEPTION : SIMCONNECT_RECV { DWORD dwException; DWORD dwSendID; DWORD dwIndex; };
struct SIMCONNECT_RECV_SIMOBJECT_DATA : SIMCONNECT_RECV { DWORD dwRequestID; DWORD dwObjectID; DWORD dwDefineID; DWORD dwFlags; DWORD dwentrynumber; DWORD dwoutof; DWORD dwDefineCount; DWORD dwData; };
struct SIMCONNECT_RECV_CLIENT_DATA : SIMCONNECT_RECV_SIMOBJECT_DATA {};
struct SIMCONNECT_VERSION_BASE_TYPE { unsigned short Major,Minor,Revision,Build; };
struct SIMCONNECT_CONTROLLER_ITEM {char DeviceName[256];unsigned DeviceId,ProductId,CompositeID;SIMCONNECT_VERSION_BASE_TYPE HardwareVersion;};
struct SIMCONNECT_RECV_CONTROLLERS_LIST : SIMCONNECT_RECV {DWORD dwRequestID,dwArraySize,dwEntryNumber,dwOutOf;SIMCONNECT_CONTROLLER_ITEM rgData[1];};
namespace fake_sim
{
    static std::map<DWORD, std::map<DWORD, bool>> groups;
    static std::map<DWORD, std::string> names;
    static std::map<DWORD, std::vector<std::pair<std::string, std::string>>> definitions;
    static std::map<DWORD, DWORD> priorities;
    static std::map<DWORD, HANDLE> group_handles;
    static std::map<DWORD, std::vector<unsigned char>> client_data;
    static HANDLE next_handle = 0;
    static DWORD request_id = 0;
    static std::vector<DWORD> system_requests;
    static bool fail_configuration = false;
    static unsigned closes = 0;
    static DWORD last_packet = 0;
    static std::map<DWORD, DWORD> mapping_packets;
    static std::map<DWORD, DWORD> notification_packets;
    static unsigned mapping_calls = 0;
    static unsigned dispatch_registrations = 0;
    struct transmission { DWORD event, priority, flags, raw, extra[4], packet; };
    static std::vector<transmission> transmissions;
    static int fail_transmission_after = -1;
    struct object_write { DWORD definition, object; std::vector<unsigned char> bytes; };
    static std::vector<object_write> object_writes;
    static bool fail_object_write = false;
}
inline HRESULT SimConnect_Open(HANDLE* handle, const char*, void*, DWORD, void*, DWORD) { *handle = ++fake_sim::next_handle; return 0; }
inline HRESULT SimConnect_Close(HANDLE handle) {
    ++fake_sim::closes;
    for (auto entry = fake_sim::group_handles.begin(); entry != fake_sim::group_handles.end();) {
        if (entry->second == handle) { fake_sim::groups.erase(entry->first); entry = fake_sim::group_handles.erase(entry); }
        else ++entry;
    }
    return 0;
}
inline HRESULT SimConnect_MapClientEventToSimEvent(HANDLE, DWORD id, const char* name) { ++fake_sim::last_packet; ++fake_sim::mapping_calls; fake_sim::mapping_packets[id] = fake_sim::last_packet; fake_sim::names[id] = name?name:""; return 0; }
inline HRESULT SimConnect_TransmitClientEvent_EX1(HANDLE, DWORD, DWORD event, DWORD priority, DWORD flags,
    DWORD raw, DWORD extra1, DWORD extra2, DWORD extra3, DWORD extra4) {
    if (fake_sim::fail_transmission_after == 0) return -1;
    if (fake_sim::fail_transmission_after > 0) --fake_sim::fail_transmission_after;
    ++fake_sim::last_packet;
    fake_sim::transmissions.push_back({event, priority, flags, raw, {extra1, extra2, extra3, extra4}, fake_sim::last_packet});
    return 0;
}
inline HRESULT SimConnect_GetLastSentPacketID(HANDLE, DWORD* id) { *id = fake_sim::last_packet; return 0; }
inline HRESULT SimConnect_SubscribeToSystemEvent(HANDLE, DWORD, const char*) { ++fake_sim::last_packet; return 0; }
inline HRESULT SimConnect_CallDispatch(HANDLE, void (*)(SIMCONNECT_RECV*, DWORD, void*), void*) { ++fake_sim::dispatch_registrations; return 0; }
inline HRESULT SimConnect_ClearNotificationGroup(HANDLE, DWORD id)
{
    if (fake_sim::fail_configuration || !fake_sim::groups.count(id)) return -1;
    ++fake_sim::last_packet;
    // Clearing membership does not unmap the client's registered event IDs.
    fake_sim::groups[id].clear(); return 0;
}
inline HRESULT SimConnect_AddClientEventToNotificationGroup(HANDLE handle, DWORD group, DWORD event, bool mask)
{
    if (fake_sim::fail_configuration || !fake_sim::names.count(event)) return -1;
    ++fake_sim::last_packet;
    fake_sim::notification_packets[event] = fake_sim::last_packet;
    fake_sim::group_handles[group] = handle;
    fake_sim::groups[group][event] = mask; return 0;
}
inline HRESULT SimConnect_SetNotificationGroupPriority(HANDLE, DWORD id, DWORD priority) { ++fake_sim::last_packet; fake_sim::priorities[id] = priority; return 0; }
inline HRESULT SimConnect_SetInputGroupPriority(HANDLE,DWORD,DWORD){++fake_sim::last_packet;return 0;}
inline HRESULT SimConnect_SetInputGroupState(HANDLE,DWORD,DWORD){++fake_sim::last_packet;return 0;}
inline HRESULT SimConnect_ClearInputGroup(HANDLE,DWORD){++fake_sim::last_packet;return 0;}
inline HRESULT SimConnect_EnumerateControllers(HANDLE){++fake_sim::last_packet;return 0;}
namespace fake_sim { struct input_mapping { DWORD packet, down; std::string definition; };static std::vector<input_mapping> input_mappings; }
inline HRESULT SimConnect_MapInputEventToClientEvent_EX1(HANDLE,DWORD,const char* definition,DWORD down,DWORD,DWORD,DWORD,bool){++fake_sim::last_packet;fake_sim::input_mappings.push_back({fake_sim::last_packet,down,definition});return fake_sim::fail_configuration?-1:0;}
inline HRESULT SimConnect_RequestSystemState(HANDLE, DWORD id, const char*) { ++fake_sim::last_packet; fake_sim::request_id = id; fake_sim::system_requests.push_back(id); return 0; }
inline HRESULT SimConnect_AddToDataDefinition(HANDLE, DWORD definition, const char* name, const char* unit, unsigned) {
    ++fake_sim::last_packet; fake_sim::definitions[definition].push_back({name, unit}); return 0;
}
inline HRESULT SimConnect_ClearDataDefinition(HANDLE, DWORD definition) { ++fake_sim::last_packet; fake_sim::definitions.erase(definition); return 0; }
inline HRESULT SimConnect_RequestDataOnSimObject(HANDLE, DWORD, DWORD, DWORD, unsigned, DWORD, DWORD, DWORD, DWORD) { ++fake_sim::last_packet; return 0; }
inline HRESULT SimConnect_MapClientDataNameToID(HANDLE, const char*, DWORD) { ++fake_sim::last_packet; return 0; }
inline HRESULT SimConnect_CreateClientData(HANDLE, DWORD, DWORD, unsigned) { ++fake_sim::last_packet; return 0; }
inline HRESULT SimConnect_AddToClientDataDefinition(HANDLE, DWORD, DWORD, DWORD) { ++fake_sim::last_packet; return 0; }
inline HRESULT SimConnect_RequestClientData(HANDLE, DWORD, DWORD, DWORD, unsigned) { ++fake_sim::last_packet; return 0; }
inline HRESULT SimConnect_SetClientData(HANDLE, DWORD id, DWORD, unsigned, DWORD, DWORD size, void* data) {
    ++fake_sim::last_packet;
    const auto* bytes = reinterpret_cast<const unsigned char*>(data);
    fake_sim::client_data[id] = std::vector<unsigned char>(bytes, bytes + size); return 0;
}
inline HRESULT SimConnect_SetDataOnSimObject(HANDLE, DWORD definition, DWORD object, DWORD, DWORD, DWORD size, void* data) {
    if (fake_sim::fail_object_write) return -1;
    ++fake_sim::last_packet;
    const auto* bytes = reinterpret_cast<const unsigned char*>(data);
    fake_sim::object_writes.push_back({definition, object, std::vector<unsigned char>(bytes, bytes + size)}); return 0;
}
