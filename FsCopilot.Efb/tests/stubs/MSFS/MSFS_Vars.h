#pragma once
#include <cstring>
using FsUnitId = int;
using FsLVarId = int;
using FsAVarId = int;
using FsBVarId = int;
enum eFsVarParamType { FsVarParamTypeInteger };
struct FsVarParamVariant { eFsVarParamType type{}; unsigned int intValue = 0; };
struct FsVarParamArray { unsigned size = 0; FsVarParamVariant* array = nullptr; };
constexpr int FS_INVALID_UNIT = -1, FS_VAR_INVALID_ID = -1;
enum FsVarError : unsigned { FS_VAR_ERROR_NONE = 0, FS_VAR_ERROR_FAIL = 0xffffffffu };
namespace fake_vars {
struct registry {
    double value[2]{};
    bool percent_storage[2] = {true, true}, available[2] = {true, true}, failed[2]{};
    bool invalid_unit = false;
    unsigned gets = 0, lookups = 0;
    unsigned sets = 0;
    double brakes[2]{};
    double steering[4] = {.5, .5, .5, .5}; // Normalized tiller and three trim states.
    double pressure[2] = {1013.25,1013.25};
    double autopilot[13]{};
    int autopilot_failed = -1;
    bool brake_failed[2]{}, brake_available[2] = {true, true};
    bool set_failed[2]{}, ignore_set = false;
    void (*on_set)(unsigned, double) = nullptr;
    double bvars[3]{};
    bool bvar_available[3] = {true, true, true}, bvar_failed[3]{};
    unsigned bvar_gets = 0;
};
inline registry& state() { static registry value; return value; }
constexpr const char* autopilot_names[] = {"AUTOPILOT MASTER","AUTOPILOT HEADING LOCK","AUTOPILOT ALTITUDE LOCK","AUTOPILOT VERTICAL HOLD",
    "AUTOPILOT NAV1 LOCK","AUTOPILOT APPROACH HOLD","AUTOPILOT BACKCOURSE HOLD","AUTOPILOT FLIGHT LEVEL CHANGE","AUTOPILOT YAW DAMPER",
    "AUTOPILOT FLIGHT DIRECTOR ACTIVE","AUTOPILOT HEADING LOCK DIR","AUTOPILOT ALTITUDE LOCK VAR","AUTOPILOT VERTICAL HOLD VAR"};
}
inline FsUnitId fsVarsGetUnitId(const char* name) {
    if (fake_vars::state().invalid_unit) return FS_INVALID_UNIT;
    return !std::strcmp(name, "Percent") ? 1 : !std::strcmp(name, "Position 32k") ? 2 : !std::strcmp(name,"Millibars") ? 3 :
        !std::strcmp(name, "Bool") ? 4 : !std::strcmp(name, "Number") ? 5 :
        (!std::strcmp(name,"Percent over 100") || !std::strcmp(name,"Percent Over 100")) ? 6 : !std::strcmp(name,"Percent scaler 16k") ? 7 :
        !std::strcmp(name,"Degrees") ? 8 : !std::strcmp(name,"Feet") ? 9 : !std::strcmp(name,"Feet per minute") ? 10 : FS_INVALID_UNIT;
}
inline FsBVarId fsVarsGetBVarId(const char* name, bool callable) {
    if (callable) return FS_VAR_INVALID_ID;
    const char* names[] = {"ELECTRICAL_Battery_1", "LIGHTING_NAV_1", "LIGHTING_STROBE_1"};
    for (int i = 0; i < 3; ++i) if (!std::strcmp(name, names[i]) && fake_vars::state().bvar_available[i]) return i;
    return FS_VAR_INVALID_ID;
}
inline FsVarError fsVarsBVarGet(FsBVarId id, FsUnitId unit, double* result) {
    auto& s = fake_vars::state(); ++s.bvar_gets;
    if (id < 0 || id >= 3 || !result || s.bvar_failed[id] || unit != (id ? 4 : 5)) return FS_VAR_ERROR_FAIL;
    *result = s.bvars[id]; return FS_VAR_ERROR_NONE;
}
inline FsAVarId fsVarsGetAVarId(const char* name) {
    for(int i=0;i<13;++i)if(!std::strcmp(name,fake_vars::autopilot_names[i]))return 10+i;
    if (!std::strcmp(name,"KOHLSMAN SETTING MB EX1")) return 2;
    const char* steering[] = {"STEER INPUT CONTROL", "ELEVATOR TRIM PCT", "AILERON TRIM PCT", "RUDDER TRIM PCT"};
    for (unsigned i = 0; i < 4; ++i) if (!std::strcmp(name,steering[i])) return static_cast<int>(3+i);
    for (unsigned axis = 0; axis < 2; ++axis)
        if (!std::strcmp(name, axis ? "BRAKE RIGHT POSITION EX1" : "BRAKE LEFT POSITION EX1") && fake_vars::state().brake_available[axis]) return static_cast<int>(axis);
    return FS_VAR_INVALID_ID;
}
inline FsVarError fsVarsAVarGet(FsAVarId id, FsUnitId unit, FsVarParamArray params, double* result) {
    if(id>=10 && id<23 && result) {
        const int index=id-10;const int expected=index<10?4:index==10?8:index==11?9:10;
        if(unit!=expected || index==fake_vars::state().autopilot_failed ||
            (index>=9 ? params.size!=1 || !params.array || params.array[0].intValue!=1 : params.size!=0))return FS_VAR_ERROR_FAIL;
        *result=fake_vars::state().autopilot[index];return FS_VAR_ERROR_NONE;
    }
    if (id>=3 && id<=6 && !params.size && result) {
        const int expected[] = {6,7,6,1};
        if(unit != expected[id-3]) return FS_VAR_ERROR_FAIL;
        const double scale[] = {1,16384,1,100};
        *result=(fake_vars::state().steering[id-3]*2-1)*scale[id-3];return FS_VAR_ERROR_NONE;
    }
    if (id==2 && unit==3 && params.size==1 && params.array && params.array[0].intValue>=1 && params.array[0].intValue<=2 && result) {
        *result=fake_vars::state().pressure[params.array[0].intValue-1]; return FS_VAR_ERROR_NONE;
    }
    if (id < 0 || id > 1 || unit != 2 || params.size || !result || fake_vars::state().brake_failed[id]) return FS_VAR_ERROR_FAIL;
    *result = fake_vars::state().brakes[id] * 32768; return FS_VAR_ERROR_NONE;
}
inline FsLVarId fsVarsGetLVarId(const char* name) {
    ++fake_vars::state().lookups;
    for (unsigned engine = 0; engine < 2; ++engine) {
        const char* expected = engine ? "BKSQ_MixtureLeverPosition_2" : "BKSQ_MixtureLeverPosition_1";
        if (!std::strcmp(name, expected) && fake_vars::state().available[engine]) return static_cast<int>(engine);
    }
    return FS_VAR_INVALID_ID;
}
inline FsVarError fsVarsLVarGet(FsLVarId id, FsUnitId unit, double* result) {
    ++fake_vars::state().gets;
    if (id < 0 || id > 1 || unit != 1 || !result || fake_vars::state().failed[id]) return FS_VAR_ERROR_FAIL;
    *result = fake_vars::state().value[id] * (fake_vars::state().percent_storage[id] ? 1 : 100);
    return FS_VAR_ERROR_NONE;
}
inline FsVarError fsVarsLVarSet(FsLVarId id, FsUnitId unit, double value) {
    auto& state = fake_vars::state(); ++state.sets;
    if (id < 0 || id > 1 || unit != 1 || state.set_failed[id]) return FS_VAR_ERROR_FAIL;
    if (!state.ignore_set) {
        state.value[id] = value / (state.percent_storage[id] ? 1 : 100);
        if (state.on_set) state.on_set(static_cast<unsigned>(id), value / 100);
    }
    return FS_VAR_ERROR_NONE;
}
