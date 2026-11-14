#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace fsc_controls
{
enum class action { read, set, restore };
enum class group { steering, quadrant };

struct command
{
    char client[25]{};
    uint32_t request = 0;
    action operation = action::read;
    group target = group::steering;
    bool blocked = false;
};

// A deliberately small protocol, independent of the shared-cockpit packet format.
// 1|client-id|request-id|read
// 1|client-id|request-id|restore
// 1|client-id|request-id|set|steering-or-quadrant|me-or-mock
inline bool parse_command(const char* data, size_t size, command& result)
{
    if (!data || size == 0 || size > 128) return false;
    if (data[size - 1] == '\0') --size; // CommBus callers may include the terminator.
    if (size == 0 || size >= 128) return false;
    char buffer[128]{};
    std::memcpy(buffer, data, size);
    const char* fields[6]{};
    size_t count = 1;
    fields[0] = buffer;
    for (size_t i = 0; i < size; ++i)
    {
        if (buffer[i] == '\0') return false;
        if (buffer[i] != '|') continue;
        if (count == 6) return false;
        buffer[i] = '\0';
        fields[count++] = buffer + i + 1;
    }
    if ((count != 4 && count != 6) || std::strcmp(fields[0], "1") != 0) return false;
    const size_t client_size = std::strlen(fields[1]);
    if (client_size == 0 || client_size > 24) return false;
    for (size_t i = 0; i < client_size; ++i)
    {
        const char c = fields[1][i];
        if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
    }
    uint32_t request = 0;
    if (*fields[2] == '\0') return false;
    for (const char* p = fields[2]; *p; ++p)
    {
        if (*p < '0' || *p > '9') return false;
        const uint32_t digit = static_cast<uint32_t>(*p - '0');
        if (request > (UINT32_MAX - digit) / 10) return false;
        request = request * 10 + digit;
    }
    if (request == 0) return false;
    command parsed{};
    std::memcpy(parsed.client, fields[1], client_size);
    parsed.request = request;
    if (count == 4)
    {
        if (std::strcmp(fields[3], "read") == 0) parsed.operation = action::read;
        else if (std::strcmp(fields[3], "restore") == 0) parsed.operation = action::restore;
        else return false;
    }
    else
    {
        if (std::strcmp(fields[3], "set") != 0) return false;
        parsed.operation = action::set;
        if (std::strcmp(fields[4], "steering") == 0) parsed.target = group::steering;
        else if (std::strcmp(fields[4], "quadrant") == 0) parsed.target = group::quadrant;
        else return false;
        if (std::strcmp(fields[5], "mock") == 0) parsed.blocked = true;
        else if (std::strcmp(fields[5], "me") != 0) return false;
    }
    result = parsed;
    return true;
}
}
