#pragma once
#include <string>
#include <vector>
enum FsCommBusBroadcastFlags { FsCommBusBroadcast_JS };
namespace fake_sim { static std::vector<std::string> messages; }
inline void fsCommBusRegister(const char*, void (*)(const char*, unsigned int, void*), void*) {}
inline void fsCommBusUnregisterOneEvent(const char*, void (*)(const char*, unsigned int, void*), void*) {}
inline bool fsCommBusCall(const char*, const char* data, unsigned int size, FsCommBusBroadcastFlags)
{
    fake_sim::messages.emplace_back(data, size - 1);
    return true;
}
