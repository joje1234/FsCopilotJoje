// Read-only Windows SDK check of the production world's definitions/reader.
// Never calls begin(), write() or recover(), so no freeze/control is changed.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <SimConnect.h>
#include <chrono>
#include <cstdio>
#include "../Wasm/control_world.h"
namespace {
fsc_controls::control_world world;
const auto started = std::chrono::steady_clock::now();
bool failed = false, opened = false;
double now() { return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count(); }
void CALLBACK dispatch(SIMCONNECT_RECV* data, DWORD size, void*) {
    if (data->dwID == SIMCONNECT_RECV_ID_OPEN) { opened = true; if (!world.start()) failed = true; }
    else if (data->dwID == SIMCONNECT_RECV_ID_SIMOBJECT_DATA) world.observe(reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(data), size, now());
    else if (data->dwID == SIMCONNECT_RECV_ID_SYSTEM_STATE) world.confirm(reinterpret_cast<SIMCONNECT_RECV_SYSTEM_STATE*>(data)->dwRequestID);
    else if (data->dwID == SIMCONNECT_RECV_ID_EXCEPTION) {
        const auto* error = reinterpret_cast<SIMCONNECT_RECV_EXCEPTION*>(data);
        std::printf("SDK error %lu send %lu index %lu\n", error->dwException, error->dwSendID, error->dwIndex);
        failed = true; world.exception(*error);
    }
}
}
int main() {
    HANDLE connection{};
    if (FAILED(SimConnect_Open(&connection, "FS Copilot read-only world definitions", nullptr, 0, nullptr, 0))) return 2;
    world.initialize(connection);
    while (!failed && now() < 8 && world.serial < 3) { SimConnect_CallDispatch(connection, dispatch, nullptr); Sleep(10); }
    const bool passed = opened && !failed && world.serial >= 3 && world.fresh(now());
    if (passed) std::printf("PASS actual native world reader: 12 position/attitude/velocity values, 3 freeze states, %llu fresh observations, frozen=%u. No aircraft, freeze or hardware/control writes.\n",
        static_cast<unsigned long long>(world.serial), world.frozen);
    SimConnect_Close(connection); return passed ? 0 : 1;
}
