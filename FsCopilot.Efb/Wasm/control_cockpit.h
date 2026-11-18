#pragma once
#include <cstdlib>
#include <limits>
#include <MSFS/MSFS_Vars.h>
#include "control_cockpit_protocol.h"
#include "control_definition_profile.h"
#include "control_session_protocol.h"
namespace fsc_controls {
class control_cockpit {
public:
    using executor = bool (*)(const char*);
    void reset() { const auto serial=packet_.serial; *this = control_cockpit{}; packet_.serial=serial; }
    void observe(const definition_profile* profile, const char* native, const session_packet& session, double now) {
        if (profile_ != profile || std::strcmp(packet_.native, native)) { reset(); profile_ = profile; }
        if (std::strcmp(packet_.session, session.session) || std::strcmp(packet_.backend, session.backend) || session.phase != session_phase::active) {
            pending_ = false; received_ = packet_.command = 0; packet_.result = cockpit_result::idle;
            packet_.linked = 0; reported_ = 0;
            packet_.backend[0] = packet_.session[0] = 0;
            packet_.resync_request=packet_.resync_ack=0;packet_.resync_phase=0;
            packet_.checked=packet_.corrected=packet_.resync_pending=packet_.failed=packet_.unavailable=0;packet_.issues[0]=0;
        }
        std::snprintf(packet_.native, sizeof(packet_.native), "%s", native);
        packet_.active = session.phase == session_phase::active ? 1 : 0;
        if (packet_.active) {
            std::snprintf(packet_.backend, sizeof(packet_.backend), "%s", session.backend);
            std::snprintf(packet_.session, sizeof(packet_.session), "%s", session.session);
        }
        if (!profile || !profile->cockpit || profile->cockpit_count > cockpit_limit || !profile->cockpit_identity) return;
        packet_.count = static_cast<uint32_t>(profile->cockpit_count);
        std::snprintf(packet_.profile, sizeof(packet_.profile), "%s", profile->cockpit_identity);
        std::memset(packet_.valid, 0, sizeof(packet_.valid));
        for (unsigned index = 0; index < packet_.count; ++index) {
            double value = 0;
            if (read(profile->cockpit[index], value)) { packet_.values[index] = value; packet_.valid[index / 32] |= 1u << (index % 32); }
        }
        if (packet_.serial != UINT64_MAX) ++packet_.serial;
        observed_ = now;
        if (now - reported_at_ > 1.5) packet_.linked = 0;
        if (pending_) {
            if (!packet_.active || now > deadline_) { packet_.result = cockpit_result::failed; pending_ = false; }
            else {
                const bool matched = known(packet_.index) && std::abs(packet_.values[packet_.index] - desired_) <= profile_->cockpit[packet_.index].tolerance;
                matches_ = matched ? matches_ + 1 : 0;
                if (matches_ >= 3) { packet_.result = cockpit_result::succeeded; pending_ = false; }
            }
        }
    }
    const cockpit_packet& status() const { return packet_; }
    bool request_resync(const session_packet& session,double now) {
        if(!packet_.active || !packet_.linked || session.phase!=session_phase::active ||
            std::strcmp(packet_.session,session.session) || std::strcmp(packet_.backend,session.backend) ||
            !std::isfinite(now) || now<observed_ || now-observed_>.25 || now-reported_at_>1.5 || packet_.resync_request==UINT64_MAX ||
            packet_.resync_phase==1 || packet_.resync_phase==2)return false;
        ++packet_.resync_request;packet_.resync_phase=1;
        packet_.checked=packet_.corrected=packet_.resync_pending=packet_.failed=packet_.unavailable=0;packet_.issues[0]=0;
        return true;
    }
    bool known(unsigned index) const { return index < packet_.count && !!(packet_.valid[index / 32] & (1u << (index % 32))); }
    bool apply(const cockpit_packet& command, const session_packet& session, double now, executor execute) {
        if (!cockpit_valid(command) || (command.kind != cockpit_kind::apply && command.kind != cockpit_kind::report) ||
            (command.kind == cockpit_kind::apply && (pending_ || !execute)) ||
            !packet_.active || session.phase != session_phase::active || !profile_ || !std::isfinite(now) ||
            std::strcmp(command.native, packet_.native) || std::strcmp(command.backend, session.backend) ||
            std::strcmp(command.session, session.session) || std::strcmp(command.profile, packet_.profile) ||
            command.command <= (command.kind == cockpit_kind::report ? reported_ : received_) || command.read_serial > packet_.serial || packet_.serial - command.read_serial > 4 ||
            now < observed_ || now - observed_ > .25 || (command.kind == cockpit_kind::apply && !known(command.index))) return false;
        if (command.kind == cockpit_kind::report) {
            reported_=command.command;reported_at_=now;packet_.linked=command.linked;
            if(command.resync_ack==packet_.resync_request && command.resync_phase) {
                packet_.resync_ack=command.resync_ack;packet_.resync_phase=command.resync_phase;
                packet_.checked=command.checked;packet_.corrected=command.corrected;packet_.resync_pending=command.resync_pending;
                packet_.failed=command.failed;packet_.unavailable=command.unavailable;std::memcpy(packet_.issues,command.issues,sizeof(packet_.issues));
            }
            return true;
        }
        received_ = packet_.command = command.command; packet_.index = command.index;
        packet_.result = cockpit_result::rejected;
        if ((session.flags & session_host) && !profile_->cockpit[command.index].shared) return false;
        // The helper planned the vanilla expression against this readback.
        // A local rotary change invalidates that plan instead of replaying an old toggle.
        double current=0;
        if (!read(profile_->cockpit[command.index],current) || std::abs(command.values[1]-current)>.000001) return false;
        for (const char* c = command.expression; *c; ++c) if (*c < ' ' || *c > '~') return false;
        desired_ = command.values[0];
        if (std::abs(desired_-current)<=profile_->cockpit[command.index].tolerance) { packet_.result = cockpit_result::succeeded; return true; }
        if (!execute(command.expression)) { packet_.result = cockpit_result::failed; return false; }
        pending_ = true; matches_ = 0; deadline_ = now + 1.5; packet_.result = cockpit_result::pending;
        return true;
    }
private:
    const definition_profile* profile_ = nullptr;
    cockpit_packet packet_{};
    uint64_t received_ = 0, reported_ = 0;
    double observed_ = -1, deadline_ = 0, desired_ = 0, reported_at_ = -2;
    bool pending_ = false;
    unsigned matches_ = 0;
    static bool read(const cockpit_definition& d, double& value) {
        const auto unit = fsVarsGetUnitId(d.units);
        if (unit == FS_INVALID_UNIT) return false;
        if (!std::strncmp(d.name, "B:", 2)) {
            // Read the existing input-event value, not a callable preset.
            // Setters still come from the vanilla YAML expression unchanged.
            const auto id = fsVarsGetBVarId(d.name + 2, false);
            return id != FS_VAR_INVALID_ID && fsVarsBVarGet(id, unit, &value) == FS_VAR_ERROR_NONE && std::isfinite(value);
        }
        if (!std::strncmp(d.name, "L:", 2)) {
            const auto id = fsVarsGetLVarId(d.name + 2);
            return id != FS_VAR_INVALID_ID && fsVarsLVarGet(id, unit, &value) == FS_VAR_ERROR_NONE && std::isfinite(value);
        }
        if (std::strncmp(d.name, "A:", 2)) return false;
        char name[160]{}; if (std::strlen(d.name + 2) >= sizeof(name)) return false;
        std::snprintf(name, sizeof(name), "%s", d.name + 2);
        FsVarParamVariant parameter{}; FsVarParamArray parameters{};
        auto* colon = std::strrchr(name, ':');
        if (colon) {
            char* end = nullptr; const auto index = std::strtoul(colon + 1, &end, 10);
            if (!colon[1] || *end || index > UINT32_MAX) return false;
            *colon = 0; parameter.type = FsVarParamTypeInteger; parameter.intValue = static_cast<unsigned>(index);
            parameters.size = 1; parameters.array = &parameter;
        }
        const auto id = fsVarsGetAVarId(name);
        return id != FS_VAR_INVALID_ID && fsVarsAVarGet(id, unit, parameters, &value) == FS_VAR_ERROR_NONE && std::isfinite(value);
    }
};
}
