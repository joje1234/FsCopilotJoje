#pragma once
#include <map>
#include <string>
#include "MSFS/MSFS_WindowsTypes.h"
constexpr DWORD SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE = 10000000;
enum { SIMCONNECT_RECV_ID_SYSTEM_STATE, SIMCONNECT_RECV_ID_EVENT, SIMCONNECT_RECV_ID_EVENT_FILENAME, SIMCONNECT_RECV_ID_EXCEPTION, SIMCONNECT_RECV_ID_QUIT };
struct SIMCONNECT_RECV { DWORD dwID; };
struct SIMCONNECT_RECV_SYSTEM_STATE : SIMCONNECT_RECV { DWORD dwRequestID; };
struct SIMCONNECT_RECV_EVENT : SIMCONNECT_RECV { DWORD uEventID; DWORD dwData; };
struct SIMCONNECT_RECV_EVENT_FILENAME : SIMCONNECT_RECV_EVENT {};
struct SIMCONNECT_RECV_EXCEPTION : SIMCONNECT_RECV { DWORD dwException; DWORD dwSendID; DWORD dwIndex; };
namespace fake_sim
{
    static std::map<DWORD, std::map<DWORD, bool>> groups;
    static std::map<DWORD, std::string> names;
    static std::map<DWORD, DWORD> priorities;
    static DWORD request_id = 0;
    static bool fail_configuration = false;
    static unsigned closes = 0;
}
inline HRESULT SimConnect_Open(HANDLE* handle, const char*, void*, DWORD, void*, DWORD) { *handle = reinterpret_cast<HANDLE>(1); return 0; }
inline HRESULT SimConnect_Close(HANDLE) { ++fake_sim::closes; fake_sim::groups.clear(); return 0; }
inline HRESULT SimConnect_MapClientEventToSimEvent(HANDLE, DWORD id, const char* name) { fake_sim::names[id] = name; return 0; }
inline HRESULT SimConnect_SubscribeToSystemEvent(HANDLE, DWORD, const char*) { return 0; }
inline HRESULT SimConnect_CallDispatch(HANDLE, void (*)(SIMCONNECT_RECV*, DWORD, void*), void*) { return 0; }
inline HRESULT SimConnect_ClearNotificationGroup(HANDLE, DWORD id)
{
    if (fake_sim::fail_configuration || !fake_sim::groups.count(id)) return -1;
    fake_sim::groups[id].clear(); return 0;
}
inline HRESULT SimConnect_AddClientEventToNotificationGroup(HANDLE, DWORD group, DWORD event, bool mask)
{
    if (fake_sim::fail_configuration) return -1;
    fake_sim::groups[group][event] = mask; return 0;
}
inline HRESULT SimConnect_SetNotificationGroupPriority(HANDLE, DWORD id, DWORD priority) { fake_sim::priorities[id] = priority; return 0; }
inline HRESULT SimConnect_RequestSystemState(HANDLE, DWORD id, const char*) { fake_sim::request_id = id; return 0; }
