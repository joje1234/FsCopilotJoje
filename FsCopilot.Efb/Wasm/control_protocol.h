#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>
#include "control_connection.h"

namespace fsc_controls
{
enum class action { read, set, restore, tolerance, learn, force, advanced, automatic, join, leave, cancel, connect, seat, instruments_local, output_check, wizard_start, wizard_cancel, resync, force_button, reset_setup };
enum class group { steering, quadrant, throttle, propeller, mixture, all };
inline uint32_t group_selection(group value) {
    return value == group::steering ? 1u : value == group::quadrant ? 14u : value == group::throttle ? 2u : value == group::propeller ? 4u : value == group::mixture ? 8u : value == group::all ? 15u : 0u;
}
inline const char* group_name(group value) {
    return value == group::steering ? "steering" : value == group::quadrant ? "quadrant" : value == group::throttle ? "throttle" : value == group::propeller ? "propeller" : value == group::all ? "all" : "mixture";
}
constexpr unsigned default_tolerance_percent = 10;

struct command
{
    char client[25]{};
    uint32_t request = 0;
    action operation = action::read;
    group target = group::steering;
    bool blocked = false;
    unsigned tolerance = default_tolerance_percent;
    bool enabled = false;
    connection_settings connection{};
    uint32_t seat_action = 0, seat_revision = 0, seat_request = 0;
    uint32_t button_action = 0, button_device = 0;
    char instrument_session[33]{};
};

// A deliberately small protocol, independent of the shared-cockpit packet format.
// 2|client-id|request-id|read
// 2|client-id|request-id|restore (explicit immediate override)
// 2|client-id|request-id|set|steering-or-quadrant|me-or-mock
// 2|client-id|request-id|force|steering-or-quadrant|me
// 2|client-id|request-id|tolerance|steering-or-quadrant|1-or-2-or-3-or-5-or-10
// 2|client-id|request-id|learn|steering-or-quadrant|reset
inline bool parse_command(const char* data, size_t size, command& result)
{
    if (!data || size == 0 || size > 384) return false;
    if (data[size - 1] == '\0') --size; // CommBus callers may include the terminator.
    if (size == 0 || size >= 384) return false;
    char buffer[384]{};
    std::memcpy(buffer, data, size);
    const char* fields[10]{};
    size_t count = 1;
    fields[0] = buffer;
    for (size_t i = 0; i < size; ++i)
    {
        if (buffer[i] == '\0') return false;
        if (buffer[i] != '|') continue;
        if (count == 10) return false;
        buffer[i] = '\0';
        fields[count++] = buffer + i + 1;
    }
    if ((count != 4 && count != 6 && count != 7 && count != 10) || (std::strcmp(fields[0], "2") && std::strcmp(fields[0], "3") && std::strcmp(fields[0], "4") && std::strcmp(fields[0], "5"))) return false;
    const bool extended = std::strcmp(fields[0], "2") != 0;
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
    if (count == 7) {
        if (std::strcmp(fields[0], "5") || std::strcmp(fields[3], "seat")) return false;
        parsed.operation = action::seat;
        parsed.seat_action = !std::strcmp(fields[4], "request") ? 1u : !std::strcmp(fields[4], "accept") ? 2u :
            !std::strcmp(fields[4], "decline") ? 3u : !std::strcmp(fields[4], "cancel") ? 4u : 0u;
        if (!parsed.seat_action) return false;
        uint32_t* numbers[] = {&parsed.seat_revision, &parsed.seat_request};
        for (unsigned index = 0; index < 2; ++index) {
            if (!fields[5 + index][0]) return false;
            for (const char* p = fields[5 + index]; *p; ++p) {
                if (*p < '0' || *p > '9') return false;
                const auto digit = static_cast<uint32_t>(*p - '0');
                if (*numbers[index] > (UINT32_MAX - digit) / 10) return false;
                *numbers[index] = *numbers[index] * 10 + digit;
            }
        }
        if (!parsed.seat_revision || ((parsed.seat_action == 1) != !parsed.seat_request)) return false;
    }
    else if (count == 10) {
        if (std::strcmp(fields[0], "5") || std::strcmp(fields[3], "connect") ||
            std::strlen(fields[4]) >= sizeof(parsed.connection.relay) || std::strlen(fields[6]) >= sizeof(parsed.connection.local) ||
            std::strlen(fields[7]) >= sizeof(parsed.connection.peer)) return false;
        parsed.operation = action::connect; parsed.connection.revision = 1;
        std::memcpy(parsed.connection.relay, fields[4], std::strlen(fields[4]));
        std::memcpy(parsed.connection.local, fields[6], std::strlen(fields[6]));
        std::memcpy(parsed.connection.peer, fields[7], std::strlen(fields[7]));
        uint32_t port = 0; if (!fields[5][0]) return false;
        for (const char* p = fields[5]; *p; ++p) { if (*p < '0' || *p > '9' || port > 6553) return false; port = port * 10 + static_cast<unsigned>(*p - '0'); }
        parsed.connection.port = port;
        parsed.connection.role = !std::strcmp(fields[8], "host") ? 1 : !std::strcmp(fields[8], "guest") ? 2 : 0;
        parsed.connection.seat = !std::strcmp(fields[9], "pilot") ? 1 : !std::strcmp(fields[9], "copilot") ? 2 : 0;
        if (!connection_valid(parsed.connection)) return false;
    }
    else if (count == 4)
    {
        if (std::strcmp(fields[3], "read") == 0) parsed.operation = action::read;
        else if (std::strcmp(fields[3], "restore") == 0) parsed.operation = action::restore;
        else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "join")) parsed.operation = action::join;
        else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "leave")) parsed.operation = action::leave;
        else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "wizard-start")) parsed.operation = action::wizard_start;
        else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "wizard-cancel")) parsed.operation = action::wizard_cancel;
        else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "reset-setup")) parsed.operation = action::reset_setup;
        else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "resync-cockpit")) parsed.operation = action::resync;
        else return false;
    }
    else if (count == 6 && !std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "force-button")) {
        parsed.operation=action::force_button;
        parsed.button_action=!std::strcmp(fields[4],"listen")?1u:!std::strcmp(fields[4],"cancel")?2u:!std::strcmp(fields[4],"clear")?3u:0u;
        if(!parsed.button_action||!fields[5][0])return false;
        for(const char* p=fields[5];*p;++p){if(*p<'0'||*p>'9'||parsed.button_device>(UINT32_MAX-static_cast<unsigned>(*p-'0'))/10)return false;parsed.button_device=parsed.button_device*10+static_cast<unsigned>(*p-'0');}
        if(parsed.button_action!=1&&parsed.button_device)return false;
    }
    else if (count == 6 && !std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "instruments")) {
        if (std::strcmp(fields[4], "local")) return false;
        const auto length = std::strlen(fields[5]);
        if (std::strcmp(fields[5], "0")) {
            if (length != 32) return false;
            for (const char* p = fields[5]; *p; ++p) if (!((*p >= '0' && *p <= '9') || (*p >= 'a' && *p <= 'f'))) return false;
        }
        std::memcpy(parsed.instrument_session, fields[5], length); parsed.operation = action::instruments_local;
    }
    else
    {
        if (std::strcmp(fields[4], "steering") == 0) parsed.target = group::steering;
        else if (std::strcmp(fields[4], "quadrant") == 0) parsed.target = group::quadrant;
        else if (extended && !std::strcmp(fields[4], "throttle")) parsed.target = group::throttle;
        else if (extended && !std::strcmp(fields[4], "propeller")) parsed.target = group::propeller;
        else if (extended && !std::strcmp(fields[4], "mixture")) parsed.target = group::mixture;
        else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[4], "all") && !std::strcmp(fields[3], "tolerance")) parsed.target = group::all;
        else return false;
        if (std::strcmp(fields[3], "set") == 0) {
            parsed.operation = action::set;
            if (std::strcmp(fields[5], "mock") == 0) parsed.blocked = true;
            else if (std::strcmp(fields[5], "me") != 0) return false;
        } else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "check-output") && !std::strcmp(fields[5], "start") && parsed.target != group::quadrant) {
            parsed.operation = action::output_check;
        } else if (!std::strcmp(fields[0], "5") && !std::strcmp(fields[3], "cancel") && !std::strcmp(fields[5], "pending")) {
            parsed.operation = action::cancel;
        } else if (!std::strcmp(fields[3], "force") && !std::strcmp(fields[5], "me")) {
            parsed.operation = action::force;
        } else if (std::strcmp(fields[3], "tolerance") == 0) {
            parsed.operation = action::tolerance;
            if (!std::strcmp(fields[5], "1")) parsed.tolerance = 1;
            else if (!std::strcmp(fields[5], "2")) parsed.tolerance = 2;
            else if (!std::strcmp(fields[5], "3")) parsed.tolerance = 3;
            else if (!std::strcmp(fields[5], "5")) parsed.tolerance = 5;
            else if (!std::strcmp(fields[5], "10")) parsed.tolerance = 10;
            else if (!std::strcmp(fields[5], "20")) parsed.tolerance = 20;
            else if (!std::strcmp(fields[5], "30")) parsed.tolerance = 30;
            else return false;
        } else if (!std::strcmp(fields[3], "learn") && !std::strcmp(fields[5], "reset")) parsed.operation = action::learn;
        else if (extended && ((!std::strcmp(fields[3], "advanced") && parsed.target == group::quadrant) ||
            ((!std::strcmp(fields[0], "4") || !std::strcmp(fields[0], "5")) && !std::strcmp(fields[3], "auto") && parsed.target == group::quadrant))) {
            parsed.operation = !std::strcmp(fields[3], "advanced") ? action::advanced : action::automatic;
            if (!std::strcmp(fields[5], "on")) parsed.enabled = true;
            else if (std::strcmp(fields[5], "off")) return false;
        }
        else return false;
    }
    result = parsed;
    return true;
}
}
