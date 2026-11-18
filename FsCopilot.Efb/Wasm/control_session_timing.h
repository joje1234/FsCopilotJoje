#pragma once
namespace fsc_controls {
// Keep in step with NativeSessionTiming. These leases retain identity/masks;
// they do not change the one-second write freshness checks or packet TTLs.
constexpr double session_recovery_seconds = 8;
}
