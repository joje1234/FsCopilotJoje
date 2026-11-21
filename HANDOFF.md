# FS Copilot EFB plan and Windows handoff

## Installed 0.9.12: reconnect and automatic helper startup - 2026-11-21

The user authorized fixing switch/deicing loss after rejoining and automatic
helper startup, including retiring older EFB helper processes. MSFS was closed
at preparation. Keep saved setup; no Reset/Relearn. Candidate root:
`artifacts/rejoin-command-scope`. The separate vanilla-fixes repository is untouched.

Concrete reconnect defect: session-created definition bridges restart commands
at 1, but both persistent Windows SDK clients retained a connection-wide sent
counter. Those clients now scope successful dispatch counters to immutable
native/backend/session identity, and suppress retired-session drop callbacks.
Current write gates, expiry, ownership and within-session replay refusal remain.
The production-linked Windows tests fail before the fix at new-session command
1 and pass after it across two rejoins without recreating the SDK connection.
Ordinary cockpit/AP/trims real-relay checks pass with SDK boundaries simulated.
No actual two-PC MSFS validation of this correction has occurred.

The helper now holds an exclusive per-user file lease across async Main.
Duplicate same-version launches exit before updating latest-log.txt or opening
SDK clients. Positively identified older FsCopilot.Efb processes in this Windows
session are replaced after rechecking PID, start time, path and process name.
Newer/unknown/uninspectable or unstoppable processes cause this launch to exit.
Never stop vanilla FsCopilot.exe. Production guard tests use injected peer
processes and real Windows file sharing, without stopping actual processes.

The test kit provides Enable/Disable automatic startup.cmd and Configure
startup.ps1. With MSFS closed they atomically update only the helper Launch.Addon
in the simulator's exe.xml, backing up the original and preserving unrelated
entries. No registry or scheduled task is needed. The extracted helper folder
must remain in place. XML fixture tests cover deduplication, backups,
idempotence, removal, malformed/disabled refusal and validation-only behavior.
The Windows helper publishes with zero warnings/errors. Includes all 0.9.11
control-value isolation fixes and its identical checked native WASM.

With MSFS confirmed closed by CIM and installation guards, the matching package
and helper were installed. The production guard actually replaced the previous
0.9.10 process. Exactly one 0.9.12 helper runs, PID 26092, executable
`artifacts/rejoin-command-scope/bundle/Helper/FsCopilot.Efb.exe`. A real second
launch exited 0 without stopping it or changing latest-log.txt. The 36 protected
kit files, seven installed package files and matching helper/definitions pass.
All six saved .bin files remain hash-identical. No settings reset occurred.

MSFS automatic startup is configured in
`C:/Users/jorge/AppData/Roaming/Microsoft Flight Simulator 2024/exe.xml`, pointing
to that exact helper. Original XML backup is `exe.xml.fscopilot-20261121-121402-8eadd7.bak`
beside it. Receipts: stage `installation.json`, `backup.json`,
`live-helper/helper-process.json`, `automatic-startup.json`, `duplicate-check.json`.
Authoritative successful install log: `tests/installation-complete.log`.
The first install's Start-Process exit-code assertion was falsely negative;
that attempt rolled back before startup XML changed. The verifier now owns the
duplicate process handle directly; actual helper duplicate handling was correct.

Frozen friend ZIP: `artifacts/rejoin-command-scope/test-kit/fscopilot-0.9.12-two-player-test.zip`,
33,311,642 bytes, SHA256 `A390F8003C56B3C345A7F86E7F6BDC44D818131AA07B9E9CCC6AC9BC18C420EC`.
Friend must install the complete matching kit with MSFS closed and run its
Enable automatic startup.cmd once, then keep that extracted folder in place.
Do not overwrite frozen ZIPs or previous working rollback copies. Actual
next-MSFS-start and two-PC rejoin/engine-start validation remain required.

## 0.9.11 control-value isolation candidate; not installed - 2026-11-20

The user reports 0.9.10 shared controls worked on both PCs until independently
moving left/right engine levers with the cockpit mouse during engine start.
They have three physical levers. Their original guest helper log was supplied
as `C:/Users/jorge/Downloads/helper-20261120-215121-4448.log`. Copies of both
helper logs, host native log, an unchanged-production validator reproduction and
a four-second read-only engine capture are preserved under
`artifacts/session-engine-start-diagnostic`. No real control writes were made.

Host/guest agree on the final world sequence 76807 (39820 received frames).
Host diagnostics show missing RPM target 13 then 10; the guest then reports
host updates delayed and native Ended/Closed/NativeFailed with held masks and
freezes. Later its local native observations stop. The original invalid raw
RPM number was not logged, and the later local native failure is not conclusively
explained by these helper logs. Both helpers were 0.9.10. This was not evidence
that separate engine values or saved learning disappeared.

The production world validator AND Windows send gate rejected the entire fresh
physics/control packet if any required target was unknown. Both defects were
reproduced locally. 0.9.11 carries explicit unknown control markers without
stopping physics; the native guest skips only incomplete groups. Held groups
cannot provide stale aligned-pickup proofs. Invalid raw readings are isolated
across all 21 slots, saved hardware scales survive outliers/alternate SET echoes,
and invalid engine-count samples cannot replace a verified schema. Ordinary
instrument reads are bounded per slot; reappearing guest telemetry becomes a
baseline instead of a synthetic knob request. New diagnostics name unavailable
and recovered axes and log native raw values/units on transitions.

All changes are generic; vanilla definitions, setter routes, ownership, actual
loss-of-link safeguards, rates and stored setup signatures are retained. Join
behavior revision 3 prevents old/new helper peers from establishing a session.
Both PCs also need the matching native package; use the kit preflight. See
`FsCopilot.Efb/CONTROL_VALUE_AUDIT.md` for the complete audit and limitations.

Local evidence: `artifacts/control-value-isolation/tests` contains the 21-slot /
1–4-engine normalization matrix, production C++ group/world tests, 41 native
contracts, 1772 loader/Jint/definition checks, 89 ownership scenarios, and relay
tests using both actual C++ modules plus C# backend with captured SDK I/O. Faults
in all 21 slots and nine seconds of sustained unknown controls keep the same
session/masks/physics stream and resume on fresh data. Real stream/heartbeat
loss still expires. Ordinary cockpit/AP/trims relay checks pass. These are local
checks, not actual two-PC simulator validation of 0.9.11.

Candidate root is `artifacts/control-value-isolation`, with fresh `build`,
`publish`, frozen bundle/ZIP and matching `test-kit` after final verification.
Do not overwrite frozen ZIPs. **Nothing is installed/switched/reset:** 0.9.10
remains installed, and the separate vanilla-fixes repository/package is untouched.
Next: install on both PCs only with MSFS fully closed, preserve saved setup and
the working rollback, then repeat separate left/right mouse movements and the
engine-start sequence. Capture the new raw warning if a legitimate feather/
endpoint position is outside the currently verified range; do not guess a new
aircraft range or write a clamped/remembered replacement value.

## Installed 0.9.10: startup stack correction, live confirmation pending - 2026-11-18

**Current installed package/helper is 0.9.10.** User-tested working baseline
remains 0.9.8 (all four output checks passed). 0.9.9 failed to initialize in
MSFS: EFB stayed Waiting for bridge, all controls Waiting for simulator. All
seven installed files matched the frozen candidate. The native log had no new
initialization entry; helper repeatedly reported discovery SDK errors 25/31.
Its log is preserved at `artifacts/bridge-startup/tests/live-0.9.9-helper-startup-failure.log`.

The compiled module_init frame grew from 48,272 bytes (0.9.8) to 78,160
(0.9.9): assignment from large temporary Force/monitor objects. It now uses
placement construction directly in existing static storage via reset(), also
for monitor profile changes. 0.9.10 module_init is 34,528 bytes; largest
detected frame 35,088. No settings, control policy, output mapping or update
rate changes. This removes a concrete regression consistent with the MSFS
SDK's large-frame stack-overflow guidance; **no MSFS callstack was captured,
so actual root-cause/startup confirmation still requires the simulator test**.
Node/WASI import-shim initialization runs both old/new versions, demonstrating
that shim execution alone does not reproduce MSFS's native recompilation.

New `FsCopilot.Efb/tests/wasm_stack.test.cjs` inspects the actual SDK-produced
WASM and rejects detected frames >=64 KiB; build.ps1 runs it before packaging.
It rejects frozen 0.9.9 and passes 0.9.10. Windows WASM/helper build (104
warnings, zero errors), all 24 C++ suites/40 native contracts, 36 JS checks,
DOM/input/navigation/package and actual max-status tests pass. UI/ordinary
protocols are otherwise unchanged; no new two-PC validation. Logs under
`artifacts/bridge-startup/tests/` include `old-stack-failure.log`, `stack.log`,
`build.log`, `native.log` and `ui.log`.

After the user confirmed MSFS fully closed, elevated CIM and installer guards
confirmed closure. `artifacts/bridge-startup/install-and-switch.ps1` installed
all seven verified package files and switched the known helper. Exactly one
matching helper runs, **PID 36000**, executable
`artifacts/bridge-startup/bundle/Helper/FsCopilot.Efb.exe`, working directory
`artifacts/bridge-startup/live-helper`; startup log confirms 0.9.10. Four
learning/output .bin files remain hash-identical; no reset/Relearn. The 32-file
kit and installed package/helper/definitions preflight passes:
`live-helper/reports/setup-20261118-222709-dab1bf.json` in that stage.
Receipts: `installation.json`, `backup.json`, `live-helper/helper-process.json`.
Helper log: `%LOCALAPPDATA%/FsCopilot/Efb/Logs/helper-20261118-222707-36000.log`.

Verified 0.9.9 rollback/settings copy:
`artifacts/bridge-startup/installed-backup-0.9.9-before-0.9.10-20261118-222706-73010d/`.
**0.9.9 is the failed startup version.** The working 0.9.8 rollback remains
`artifacts/input-monitor/installed-backup-0.9.8-before-0.9.9-20261118-220744-77c267/`,
and its frozen candidate/kit remain intact under `artifacts/nosewheel-sign`.
Do not remove the working fallback.

Frozen 0.9.10 candidate:
`artifacts/bridge-startup/fscopilot-efb-0.9.10-candidate.zip`, 33,272,331 bytes,
SHA256 `F91474BE449B6A810199A6C5AA4EC8B01A6DC15D290244ACDFCE2C127BE6AA24`.
Friend kit:
`artifacts/bridge-startup/test-kit/fscopilot-0.9.10-two-player-test.zip`,
33,296,709 bytes, SHA256
`9F1C8379BCB9536FACBEAE06679565ABB572BB31946D56AE926EBB32B7175159`.
Do not overwrite frozen ZIPs. Next: restart MSFS into parked Baron, open EFB,
confirm Waiting for bridge clears and hardware drives EFB indicators without
Reset/Relearn. Only then retry Force assignment and the read-only monitor.
Actual 0.9.10 startup/Force/monitor and two-PC behavior remain unverified.

## Installed 0.9.9; live Force/monitor tests next - 2026-11-18

The user confirmed MSFS fully closed. Elevated CIM and repeated installer
guards confirmed closure. `artifacts/input-monitor/install-and-switch.ps1`
completed. All seven package files match frozen 0.9.9; all 32 kit files and the
matching helper/complete definitions pass preflight. Exactly one helper runs:
**PID 40360**, `artifacts/input-monitor/bundle/Helper/FsCopilot.Efb.exe`, working
directory `artifacts/input-monitor/live-helper`; startup log confirms 0.9.9.
Known helper 28548 was replaced. Do not launch an older/vanilla helper alongside it.

All four current learning/output .bin files were backed up and hash-verified
unchanged. No reset/Relearn. Seven rollback payloads match frozen 0.9.8:
`artifacts/input-monitor/installed-backup-0.9.8-before-0.9.9-20261118-220744-77c267/`
(`package` and `work-closed`). Receipts: `installation.json`, `backup.json`,
`live-helper/helper-process.json`; full log `install-run.log`. Checker report:
`live-helper/reports/setup-20261118-220747-b1d822.json` in that stage.
Helper log: `%LOCALAPPDATA%/FsCopilot/Efb/Logs/helper-20261118-220746-40360.log`.

User-tested prior baseline is 0.9.8 with all four local wizard checks passed.
**Actual 0.9.9 Force assignment and instrument-monitor tests are pending.**
Next restart into the parked Baron, keep learning, retry assignment under
Setup/Steering and confirm readouts remain available. Then Setup > Instrument
sync monitor > Open monitor, change heading/altimeter/light and inspect Covered
names/directions or search. Local coverage cannot prove delivery without a
peer. Network/EFB rates are unchanged (~30Hz/10Hz); no timing changes were
made in response to the user's question. Both PCs need full matching kits
before shared testing. The prepared-candidate notes below are historical.

## 0.9.9 ready: Force-button isolation and local instrument monitor - 2026-11-18

**Installed is 0.9.8 with helper PID 28548**, despite the historical ready
notes below. Its successful installation is recorded in
`artifacts/nosewheel-sign/installation.json` and `live-helper/helper-process.json`.
All seven package files and four preserved settings files were verified. The
0.9.7 rollback is `installed-backup-0.9.7-before-0.9.8-20261118-212947-2831fd`
under that stage. The user now confirms the Nosewheel/wizard works; actual
native log reaches saved output mask 15 (all four groups). Do not reset learning.

The user's new Force-button assignment attempt caused thousands of
"Optional Force button mapping failed" messages followed by general SDK error
3 (send 10343, index 1), after which discovery/readouts became unavailable.
The old log cannot identify the first rejected call. The exception handler
unconditionally called stop(), which sent cleanup on each rejected cleanup
again. The bounded packet history eventually lost attribution and the generic
handler failed the controls. Actual evidence is preserved in
`artifacts/input-monitor/tests/live-0.9.8-force-failure.log`.

0.9.9 makes input cleanup idempotent, tags optional calls by generation/kind/
button, ignores cleanup rejections without sending more calls, registers local
event IDs once per connection and permits valid button candidates when higher
unsupported indices are rejected. Fatal optional failures stop only the binding.
Precise SDK send/index/operation/button diagnostics replace the generic log.
Regression tests retain ready controls and masks across valid capture with
rejected candidates and 5000 cleanup-error callbacks. The SDK stub's previous
ClearNotificationGroup incorrectly deleted client registrations; it now only
clears membership, matching the API. Actual 0.9.9 hardware binding is untested.

Setup adds **Instrument sync monitor > Open monitor**, entirely read-only.
It observes the exact ordinary-sync catalog/readbacks, with 12 coalesced recent
definition changes, before/after values, Both pilots vs Host-to-copilot direction,
searchable catalog and Pause/Clear activity. Existing policy labels are generated
into native metadata (e.g. Left/Right altimeter). Catalog pages are bounded at
16 entries per existing UI status. Baselines, invalid/reappearing reads and
profile/native changes cannot manufacture user-input events. Invalid optional
monitor data does not reject the main control status. Coverage means configured
and locally observable, never proof of peer delivery. GTN touches/event-only
controls and missing readable definitions are outside it. No monitor executor,
SDK writes, synthetic peer, ownership changes or network sender exists.

The user separately asked about yoke update frequency and explicitly requested
no changes. Answer given: native shared observations target ~30Hz; EFB display
10Hz; effective network delivery has not been measured and can be lower due to
frame rate/latency; remote physical values have no interpolation. **No rate,
timer, smoothing or physical-output behavior was changed for that question.**

0.9.9 Windows WASM/helper build passes (104 warnings, zero errors); 24 C++
suites plus 40 native contracts, 36 JS controller checks, DOM/input/navigation/
package and actual max native-status (16 catalog + 12 activity rows) pass.
Chrome passes existing 64 layouts/36 insets plus three monitor layouts.
Focused ConnectionOnly relay/production-native physical/mixed-brake tests pass
with SDK fixtures. Logs: `artifacts/input-monitor/tests/`; UI screenshot:
`artifacts/input-monitor/ui/instrument-monitor.png`. No actual 0.9.9 simulator
or two-PC validation has happened. Both PCs need a full matching update.

Frozen candidate: `artifacts/input-monitor/fscopilot-efb-0.9.9-candidate.zip`,
33,272,579 bytes, SHA256
`6CDFAF781953504852F1953911ED80A90ACC229BFDB7359F901CB71095C92D84`.
Friend kit: `artifacts/input-monitor/test-kit/fscopilot-0.9.9-two-player-test.zip`,
33,296,160 bytes, SHA256
`1792F97A112133DE9161E5456AF09B1B5A93025C84F518BC3FD7455A1378FC9B`.
32 protected kit files pass; installer ValidateOnly confirms the named 0.9.8
destination. Frozen archives must not be overwritten. Prepared installer:
`artifacts/input-monitor/install-and-switch.ps1`, expecting the nosewheel-sign
helper receipt, backing up 0.9.8/all .bin files and preserving current learning.

**Installation still pending:** elevated CIM found MSFS running PID 40840.
An async question asks the user to fully close it. Do not replace loaded WASM.
After closure, install, verify rollback/settings/helper and update this status.
Next local tests: keep learning, no Reset/Relearn; assign Force button and
verify Steering-only force plus continued control readouts, then use the monitor
for heading, altimeter and a light switch. Test card LOCAL F1/M1 documents this.
Ordinary barometers/AP/trims policy and GTN guidance limitations are unchanged.

## 0.9.8 Nosewheel sign correction ready; MSFS closure needed - 2026-11-18

**Installed remains 0.9.7 / helper 30928.** The user's new Steering wizard
failure is confirmed in actual `controls-test.log`: Nosewheel requested
51.0000%, observed 48.9990%, restored=1, repeated across retries. This is
opposite direction, not insufficient learning. Read-only process check found
MSFS running (PID 36084). No live output probe or package replacement was done.
The same log records successful Reset setup and new saved roll/pitch/engine
learning; **preserve these settings for 0.9.8, no further reset/Relearn.**

The complete Baron tree/modules and vanilla apply_surfaces were checked.
`Definitions/modules/flight_controls.yaml` now sets tiller using
`${-value * 16384} (>K:AXIS_STEERING_SET)`. A generic compiler regex accepts
negation plus constant scaling; sampled absolute-affine validation remains.
Generated adapter is `definitions-v1-bd6fddd4200998ca5cd3b8fd`. No aircraft-only
runtime branch, omitted probe, widened tolerance or changed ownership policy.
The native output-check fixture previously derived fake readback from the
same definition; it now models the observed SDK sign independently. The old
mapping fails that regression (`tests/before.log`); corrected mapping passes.
C#/C++ setter checks cover endpoints, center, both signs and the 51% probe.
The relay harness also models the independent sign. These are local tests,
not actual 0.9.8 simulator evidence.

0.9.8 Windows WASM/helper built (104 warnings, zero errors), 1248 definition
checks and all 23 C++ suites / 40 native contracts pass. UI controller (36),
DOM/input/native-status/package tests pass. 32 protected kit files verify.
First full relay run timed out in SharedStartupTests' native handover; its
repeat passed completely (`network-retry.log`) before installation. No network
runtime timeout was changed for this definition fix. Logs and actual 0.9.7
evidence: `artifacts/nosewheel-sign/tests/`.

Frozen candidate: `artifacts/nosewheel-sign/fscopilot-efb-0.9.8-candidate.zip`,
33,250,683 bytes, SHA256
`F72CD18CC236136FF74C75EA64775087EEC362CA4C66B3C65F3EBF78193788CD`.
Friend kit: `artifacts/nosewheel-sign/test-kit/fscopilot-0.9.8-two-player-test.zip`,
33,273,571 bytes, SHA256
`FA74A623416FE4931B07DE46F49B815C378A5AB5D56F1E71EE902B2AB93C36D7`.
Do not overwrite frozen archives. Installer ValidateOnly passed against the
named installed 0.9.7 destination. `install-and-switch.ps1` in that stage
expects the known reset-setup helper receipt, backs up 0.9.7 and all current
.bin settings, and requires MSFS closed before replacing files/switching helper.
It is prepared but has not run. Next live test after installation: parked,
engines/AP OFF, brake ON, Setup wizard; no Reset/Relearn. Keep hardware still
for output verification and report exact group/axis/message if it stops.

## User-requested fresh setup reset completed - 2026-11-18

After installing 0.9.7, the user explicitly requested deletion of their saved
settings for a fresh wizard experience. Elevated CIM confirmed MSFS closed.
All four pickup/output .bin files were copied, hash-verified, then deleted
with literal paths. No .bin settings remain in the active package's WASM work
directory. No saved connection or Force-button files existed. Logs and the
route diagnostic were kept. Helper 30928 remains running; package is unchanged.
Backup: `artifacts/reset-setup/user-settings-reset-20261118-193322-69c2d1/`.
Receipt: `artifacts/reset-setup/settings-reset.json`.
Next: start the parked Baron, engines/AP OFF, brake ON, then Setup wizard.
No Reset setup/Relearn click is needed first. The preserved-settings statements
below describe the installation before this explicitly requested reset.

## Installed 0.9.7: shared trims and live setup reset - 2026-11-18

**Current installed package/helper: 0.9.7.** An elevated CIM check confirmed
MSFS was closed. `artifacts/reset-setup/install-and-switch.ps1` installed all
seven hash-verified files and switched only the known 0.9.6 helper. Exactly
one matching helper runs: PID **30928**, executable
`artifacts/reset-setup/bundle/Helper/FsCopilot.Efb.exe`, working directory
`artifacts/reset-setup/live-helper`. Its startup log confirms 0.9.7. The initial
disconnected message is while MSFS is closed, not a live test result.

All four saved .bin files remain hash-identical. No reset was performed.
The verified seven-file 0.9.6 rollback and closed-state settings backup are in
`artifacts/reset-setup/installed-backup-0.9.6-before-0.9.7-20261118-203049-a41b77/`.
Receipts: `artifacts/reset-setup/{installation.json,backup.json}` and
`live-helper/helper-process.json`. Full preflight passes all 32 protected kit
files, seven installed files and the running helper/complete definitions:
`live-helper/reports/setup-20261118-203052-96ba8b.json`.

The user's latest policy removes ALL trims from Steering. Elevator, aileron
and rudder trims are ordinary bidirectional shared instruments through vanilla
YAML/getters and absolute setters. Neither pilot's trim events are masked.
Trim roles, learning, alignment and wizard probes are excluded. Steering is
fixed roll/pitch, rudder, tiller and both brakes; takeover aligns only roll/pitch.
Wire slots 6..8 remain reserved so engine indices do not change.

Setup now has **Reset setup > Clear setup**, with explicit confirmation and
native prerequisite text. It clears this aircraft's learned controls, output
proofs and brake-response selection in both saved generations without restarting
MSFS. Connection preferences, tolerance, Force binding, ownership and aircraft
positions stay intact. It requires a parked aircraft, brake ON, AP OFF, all
groups Me, no shared session or active wizard/output check. Reset is a data
operation and may run with engines ON; the wizard still requires engines OFF.
It cannot repair a failed SDK output adapter. Retransmitted reset requests do
not erase newly learned inputs again; storage failure/retry is tested.

The actual 0.9.6 Steering wizard failure was SDK error 20 in the first output
batch. Its old log did not identify the specific rejected call. The definition's
tiller setter now uses AXIS_STEERING_SET with conversion before SDK argument
rounding; the trim SimVar writer is out of the physical batch. New SDK-failure
logs identify send/index, axis, operation and target. **The revised output path
has not yet passed an actual MSFS wizard check. Do not call the failure fixed
until the user tests it.**

Ordinary scalar protocol v2 now acknowledges individual peer proposals. An
older receipt cannot roll back a newer local rotary/trim adjustment. Pending
writes remain protected from snapshots and actual host input remains
authoritative. No-op receipts/snapshots agree. Both PCs need the complete
matching helper/package/definitions; do not mix versions.

Validation: Windows WASM/helper build succeeds (104 warnings, zero errors).
1242 real YAML/Jint definition checks; 23 C++ suites plus 40 native contracts;
full local-relay suite including AP, three trims both ways and rotary stress;
36 JS checks, DOM/embedded-input/navigation/package/native-status checks and
64 browser layouts/36 EFB insets pass. Logs are in
`artifacts/reset-setup/tests/` (final UI/network/browser logs end in `-final`).
The default native-status fixture path is old; use
`artifacts/altimeter-native-command/tests/native-test.log` for the current run.
All SDK/input tests are simulated; no new two-PC simulator validation occurred.

Frozen candidate: `artifacts/reset-setup/fscopilot-efb-0.9.7-candidate.zip`,
33,251,205 bytes, SHA256
`6F90F87445E3A557E2151DA416E5CDA457C25262200079ACEFD816C6689ED15A`.
Friend kit: `artifacts/reset-setup/test-kit/fscopilot-0.9.7-two-player-test.zip`,
33,273,262 bytes, SHA256
`0463A29F71492326512CFC2EDFDC2E112A44FA9179346CE3AF28F8588C1173CB`.
Do not regenerate/overwrite these frozen archives. Detailed steps are in the
kit's START HERE.txt and `artifacts/reset-setup/GUIDE.txt`.

**Next actual test:** restart into the parked Baron, engines/AP OFF, brake ON,
all controls Me. Open Setup > Reset setup > Clear setup, then Setup wizard
without restarting MSFS again. It must finish four named checks without trim
steps. Stop at the exact failure if Steering still fails; inspect the new
native error attribution before another patch. Then both PCs update fully and
test P1-P8/A1-A4, especially trims both ways under each Steering owner.
Do not automatically wipe settings on installation.

AP and both separate barometers remain normal shared instruments. GTN750
Premium is required on both PCs. The user validated a local Premium route
read; copying routes and selecting remote GTN guidance remain pending. GTN
screens stay local; current guidance comes from the physics host. Only Baron
EFB control/cockpit profiles are configured. Older records below are historical.

## Installed 0.9.6, settings preserved - 2026-11-18

The user confirmed MSFS was fully closed. An elevated CIM check and the
installer's repeated process guards confirmed closure before replacing files.
`artifacts/shared-ap/install-and-switch.ps1` completed successfully. All seven
installed package files match the frozen 0.9.6 candidate; all 32 protected kit
files pass. Exactly one matching helper is running: **PID 34920**, executable
`artifacts/shared-ap/bundle/Helper/FsCopilot.Efb.exe`, working directory
`artifacts/shared-ap/live-helper`. Known 0.9.4 helper PID 30772 was stopped.
Do not start an older helper/vanilla exe alongside this one.

All four existing .bin files were backed up and hash-verified unchanged:
two `pickup-885d39af-2` learning slots and two matching `-output` slots.
No connection-preference or force-button .bin files existed in this backup;
none were deleted. **No reset was performed; do not reset or press Relearn.**
The seven rollback package files also match the frozen 0.9.4 candidate hashes.
Backup: `artifacts/shared-ap/installed-backup-0.9.4-before-0.9.6-20261118-191528-8be07f/`,
with `package` and `work-closed`. Existing 0.9.4/0.9.5 frozen kits remain intact.

Receipts: `artifacts/shared-ap/installation.json`, `backup.json`,
`live-helper/helper-process.json`; full run log is `install-run.log`.
Passing full checker report:
`live-helper/reports/setup-20261118-191531-a42e72.json` within that stage.
New helper log: `%LOCALAPPDATA%/FsCopilot/Efb/Logs/helper-20261118-191530-34920.log`.
It confirms 0.9.6 startup. The initial native-disconnected message is while
MSFS is closed; actual simulator/AP behavior has not been validated yet.

The user was told installation is finished and MSFS can restart. Next: parked
Baron, engines/AP OFF, parking brake ON. Check all fixed Steering rows already
exist and run the wizard for any missing output results, without Relearn.
Only roll/pitch and engine levers should need manual learning/alignment;
pedals, brakes, tiller and trim use automatic simulator state. Return tested
groups to Me. Both PCs need the full matching 0.9.6 kit before the next shared
P1-P7/A1-A4 tests. Friend kit and detailed scope are recorded below. GTN source
selection and route copying remain pending; current guidance uses the flight
host. Both independent barometers retain normal shared operation.

The candidate-ready/installation-waiting notes below are historical.

Latest wizard question: the user asks whether stored settings need resetting
to run it again. Read-only native-log inspection after the 0.9.6 restart shows
saved shared-output mask **14** (Throttle/RPM/Mixture valid; Steering needed),
with all fixed axes learned. 0.9.6 wizard skips saved groups and has no rerun-all
button. No files have been cleared. An async question asks whether the button
is disabled or immediately finishes, and for its prerequisite text. Diagnose
that before wiping anything; if a full recheck is requested, clear only output
proofs with MSFS closed and backups, preserving pickup calibration/settings.

## 0.9.6 shared AP candidate ready; installation awaiting MSFS closure - 2026-11-18

**Installed remains 0.9.4.** Full 0.9.6 is built/frozen under
`artifacts/shared-ap`, including the uninstalled 0.9.5 Steering fixes. The user
asked to continue the plan and now requires PMS50 GTN750 Premium for the
supported setup. They confirm Premium is active on BOTH PCs. Their read-only
route inspector now shows points matching the GTN plan while the simulator
EFB has no plan. This is actual local route-read evidence; no route was copied,
no exact response/source identity was supplied and remote guidance is untested.

Both pilots can now adjust standard AP modes/FD and heading/altitude/VS through
the ordinary YAML/core path. There is no AP owner. The aircraft cockpit policy
opts into sharedAutopilot; other profiles default to AP exclusion. Corrected
Baron definitions remove duplicate heading mappings and invalid VS variable /
obsolete altitude/VS key names. Reuse absolute on/off patterns from the vanilla
AS_G1000_NXi module and standard events in YAML. Heading uses indexed SDK fine
precision with data-declared 0.022-degree readback tolerance; other scalars keep
0.000001. Current-readback and same-revision replay checks remain strict.
FD retains its equality-guarded toggle. Neither indexed barometer changed.
See `FsCopilot.Efb/VANILLA_REUSE_AUDIT.md` for sources and exception rationale.

The actual ordinary-cockpit relay test had not been included in the default
full run; it is now included. It exposed stale host snapshot feedback during
new copilot switch proposals. The generic model now withholds pending setter
values from snapshots and ignores initialized duplicate host revisions.
Explicit resync still uses Retry; small fractional knob movement accumulates
against a separate input anchor without resending unchanged proposals.

Validation: 1238 actual YAML/Jint/model checks; 23 C++ suites and 40 native
contracts; focused ordinary and complete default real-relay loopback pass,
including either-pilot AP modes, fractional heading, indexed altitude/signed
VS, coupled NAV/HDG convergence, no settled echo, five exact vanilla switch
writes, both independent barometers and explicit recovery. Legacy exclusive
AP-owner tests use fake adapters and do not implement remote GTN guidance.
35 controller plus DOM/input/navigation/package checks pass. Browser passes
64 layouts/36 EFB insets. WASM: 0 errors/104 existing warnings; self-contained
helper publishes with ProductVersion 0.9.6. No actual two-PC AP validation.
Logs: `artifacts/shared-ap/tests`. These native/relay tests simulate SDK,
executor and hardware input. User route-read evidence is separate.

Frozen full candidate:
`artifacts/shared-ap/fscopilot-efb-0.9.6-candidate.zip`, 33,239,494 bytes,
SHA256 `EA5160EA4A4622C26F64337E5EE90D7B016F184639F8714AD91DD6F5AA9C9376`.
Matching full friend kit:
`artifacts/shared-ap/test-kit/fscopilot-0.9.6-two-player-test.zip`,
33,260,322 bytes, SHA256
`0583AEEF249F0AF48CC20D08C617A873BFC454017355772C66E268950796C7BC`.
All 32 protected kit files and seven staged package hashes pass. Installer
ValidateOnly passes against the actual named installed 0.9.4 target without
changing it. Do not overwrite frozen 0.9.4/0.9.5/0.9.6 artifacts.

An async question asks the user to fully close MSFS for installation; no reply
has arrived yet. A subsequent elevated CIM check confirms one actual
`FlightSimulator2024.exe` process is still running, with the expected 0.9.4
helper PID 30772 at its recorded shared-recovery path. No installation or
helper switch has run. **Do not replace the loaded WASM or reset settings.** Once
closure is reported, run `artifacts/shared-ap/install-and-switch.ps1` elevated.
It guards actual MSFS closure, validates all 27 payloads, backs up 0.9.4 and all
current .bin files without deleting them, stops only helpers whose exact path
matches the recorded 0.9.4 helper, starts the matching 0.9.6 helper hidden and
runs the full kit/process checker. A post-install helper failure rolls back
while MSFS remains closed. Prepared receipts go under that stage/live-helper.
Use the elevated guard: restricted empty process listings do not prove closure.

Next live tests: restart parked Baron, engines/AP OFF, parking brake ON; keep
learning, no Relearn/reset. Run any missing wizard results (Steering expanded
from 0.9.4; valid engine checks remain). Both PCs need the complete 0.9.6 kit.
Use START HERE's P1-P7 then A1-A4 for selectors/modes before taxi/manual flight.
Custom KAS297 arming, CWS/test and pitch-mode UP/DN still need verification;
standard VS sharing is not evidence for them. Keep the actual local/source
mode activation separate from a peer-sync failure in reports.

GTN screens remain local. **Guidance currently comes from the physics host's
navigator.** Source selection, route copying, active-leg/direct-to transfer and
remote GTN guidance remain pending. Premium's local EFB route integration alone
does not establish a cross-PC guidance API. Continue investigating this after
the parked AP checks; do not enable overlapping ordinary and experimental
exclusive AP writers. Both barometers remain normally shared with no AP/seat
restrictions.

Aircraft portability answer given to the user: runtime is largely reusable,
but only the Baron has the role/cockpit profiles in the generated EFB catalog.
Vanilla YAML alone does not enable another aircraft. Full include-tree mapping,
group applicability (especially jets/fixed pitch), cockpit policy, generated
catalog and simulator validation are needed. Differences belong in data;
no general compatibility or tested two-PC flight is claimed.

## Active work and agreed AP design - 2026-11-18

Installed baseline remains **0.9.4**, with the user's requested reset already
completed below. Do not reset again. The current Steering candidate is 0.9.5
under `artifacts/fixed-steering`; it is not installed. Supported Steering now
declares all nine controls from definitions before first use. Pedals, brakes,
tiller and trim use actual cockpit demand through vanilla getters/setters,
with no manual discovery/learning/alignment. Wizard hardware prompts cover
yoke roll/pitch and engine levers only; the complete parked output diagnostic
still runs automatically. Optional brake response selection uses actual output
readback and is saved in dual-slot format v2, with v1 engine-proof migration.
Old hardware events cannot change membership or output routes during a session.
Details and exceptions are in `FsCopilot.Efb/VANILLA_REUSE_AUDIT.md`.

The user clarified AP behavior: either pilot can operate heading bug, selected
altitude/VS and every AP button, and resulting states must sync as in vanilla.
**No AP-control ownership lock.** Only which pilot's GTN supplies navigation
guidance is an explicit source choice. Both independent barometers retain
ordinary shared operation and the corrected indexed absolute setters; do not
bind either to a seat/AP owner. GTN screen presses/pages remain local. This
supersedes earlier exclusive AP-owner proposals. Production AP/selector sync
is currently disabled; remote GTN guidance and Lite route copying remain
unimplemented/unverified. Revisit vanilla AP definitions and avoid double
writers before enabling the ordinary or guidance paths. Recording the design
does not implement it.

The installed version supports manual-flight testing, not a claim of complete
state synchronization or a validated two-PC flight. Continue named parked
checks before taxi/manual flight. Full AP/GTN-assisted flight remains pending.

### 0.9.5 prepared, with settings and installed package untouched

1197 checks pass using the actual YAML loader/Jint. All 22 C++ suites and 40
native contracts pass, including the automatic left-brake response through the
production module/writer, restored baseline, saved response/proof after restart
with zero output calls, and exact v1-to-v2 storage migration. Full loopback relay
tests pass first-use trim sharing through all three vanilla setters, non-owner
blocking, the unchanged nine-control Steering list/session GUID, button versus
curved-axis brake demand, yoke-only pickup and existing recovery regressions.
They use simulated SDK/input providers; production AP ownership tests remain
fake fixtures and do not implement the newly agreed shared-AP policy.

The full relay run caught an existing recovery-logging defect: resetting
observations to `TimeSpan.MinValue` could overflow age subtraction during
disconnect. Age calculation now treats missing observations as expired before
retention checks/logging; the same complete run passes. No exception swallowing
or relaxation of fresh-write/ownership checks was added.

35 controller checks plus DOM/input/navigation/package tests pass. Chrome passes
64 layouts and 36 EFB insets. Windows WASM builds with 0 errors/104 existing
warnings and the matching self-contained helper publishes. Logs are under
`artifacts/fixed-steering/tests`. Whitespace checks pass. Actual hardware and
two-PC MSFS validation are still pending.

Frozen verified full candidate:
`artifacts/fixed-steering/fscopilot-efb-0.9.5-candidate.zip`, 33,236,687 bytes,
SHA256 `78ACCFC56D5488CCD77F0027E1318DD933C4FFDD8A15406E9162EB1C153A9128`.
Matching full friend kit:
`artifacts/fixed-steering/test-kit/fscopilot-0.9.5-two-player-test.zip`,
33,255,888 bytes, SHA256
`7DDBDD311D36044E7B57B3EBDB794933F32D0DF729582CCC9F579141C7378C93`.
All 32 protected kit files pass. The seven package hashes were checked in the
staged kit, not installed to the real Community folder. Installer ValidateOnly
passes against the named installed 0.9.4 target without changing any files.
Both frozen 0.9.4 ZIP hashes still match. Do not overwrite frozen artifacts.

When MSFS is confirmed fully closed, the prepared installer is
`artifacts/fixed-steering/install-0.9.5.ps1`; it verifies/backups 0.9.4 and saves
all current .bin files unchanged. Switch only the known matching 0.9.4 helper
to the new bundle helper, record receipts and verify full file/process checks.
Use an elevated closure check/installer guard; restricted empty process lists
are insufficient. **Do not reset learning or settings again.** User has not
reported closing MSFS for this candidate. The current matching helper remains
the 0.9.4 one recorded below. Friend needs the complete matching 0.9.5 kit.

Before enabling the newly agreed AP behavior, audit vanilla's AP subtree and
the ordinary catalog exclusions. The Baron currently has duplicate heading-bug
definitions (one default setter and one indexed key setter); enabling both as
ordinary catalog entries would be ambiguous. Resolve in definitions. Reuse the
existing loader/setters with equality/echo suppression, verify selected-altitude
and UP/DN units/indexes, and avoid simultaneous ordinary and guidance writers.
Do not require a verified exclusive-AP input adapter just to share AP buttons.
Remote GTN guidance needs its own capability investigation; synchronizing AP
button states does not transport the selected pilot's local navigation signals.

## Installed 0.9.4 and reset setup for a fresh wizard — 2026-11-18

The user explicitly requested installing the new candidate and wiping their
settings, superseding the earlier hold while testing 0.9.1. An elevated process
check confirmed MSFS was closed. The old 0.9.1 helper PID 31572 was stopped after
verifying its exact executable path. All 27 frozen candidate payload hashes
were checked, the seven Community files were installed and verified, and the
matching 0.9.4 helper is now running as **PID 30772** at
`artifacts/shared-recovery/bundle/Helper/FsCopilot.Efb.exe` with working directory
`artifacts/shared-recovery/live-helper`. Do not start the old helper again.

The requested reset backed up, verified and removed all six saved .bin files:
two control-learning slots, two shared-output validation slots and two
connection-setting slots. No force-button files existed. A post-start check
confirms **zero saved .bin files remain**. Hardware/MSFS bindings and logs were
not changed. Connection codes/role/seat settings must be entered again. The
user can choose `jorge001` again after setup, with a different friend's code.

Verified rollback package and original settings are under
`artifacts/shared-recovery/installed-backup-0.9.1-before-0.9.4-20261118-163621-43ace3/`
(`package` and `work-closed`). All six saved-file hashes still match that backup.
Receipts: `artifacts/shared-recovery/installation.json`, `settings-reset.json`,
`live-helper/helper-process.json`; orchestration is `install-and-reset.ps1`.
The installation receipt records the explicit subsequent settings reset.

Full checker passes protected kit files, all seven installed package hashes,
and exactly one matching helper with its complete definitions. Report:
`artifacts/shared-recovery/live-helper/reports/setup-20261118-163654-cfbe8b.json`.
The new log is `%LOCALAPPDATA%/FsCopilot/Efb/Logs/helper-20261118-163622-30772.log`
and shows the expected 0.9.4 startup/version. Simulator connection is not yet
available because MSFS is closed. These checks establish installation/reset,
not actual simulator or two-PC validation of 0.9.4.

Next: user restarts the parked Baron with engines OFF, AP OFF and parking brake
ON, then EFB → FS Copilot → Setup → Setup wizard. It should start completely
fresh without pressing Relearn. Let the wizard manage owners/Auto. Confirm all
four named output checks and return to Me, then restore shared connection
preferences. Friend also needs the complete matching 0.9.4 kit before joining.
Do not clear settings again unless explicitly requested. Actual switch sync,
yoke-only pickup, brake-axis behavior and short-pause recovery still need tests.

## 0.9.4 development and local validation (installed above)

The user explicitly requested recoverable errors/warnings without ending the
shared session unless necessary. The earlier accidental drops were explicit
one-second native-observation watchdogs, not uncaught exceptions. Recovery is
now coordinated across the native helper/service/authority and C++ session,
writer and guest-world retention. No aircraft-specific rule was added.

Keep the existing one-second data freshness, current-readback/ownership/barrier
proofs and 250-ms input/world queue TTL. After a simulator/world gap, transfers
and new shared writes pause; peer heartbeats warn after three seconds. Retain
the same session identity/masks/guest freeze within an eight-second window.
Fresh same-identity data resumes automatically; cached positions are not
replayed. Only lease heartbeat/End commands may use retained primary status.
Optional cockpit/AP send gates reject retained non-fresh data. Actual transport
loss, changed identities, critical unconfirmed SDK/mask operations and sustained
gaps still end the session with explicit recovery/fresh consent required.
New join negotiation remains strict: a regression test caught an unintended
grace extension to retained join data; it was corrected before final checks.

Helper logs now include INF/WRN/ERR levels. WRN [Shared recovery] records entry
and observation/session ages; INF records recovery wait duration. Unexpected
terminal [Shared ended/stopped] errors retain their specific cause. Normal
helper disposal uses INF. The EFB uses existing Active + NativeUnavailable
status for "Updates delayed. Shared session retained; control changes are
waiting." No IPC schema/seat/barometer ownership change was needed.

Validation: all 22 C++ suites and 40 native bridge/session scenarios pass;
full real-relay loopback passes temporary heartbeat/world gaps, retained local
SDK updates, same-session recovery, unchanged guest masks/freeze, strict
retained-data join rejection, persistent loss expiry and existing control,
instrument, AP-fake and recovery regressions. A focused C++ writer check
retains at 7.9 seconds, expires at 8, and rejects stale writes during retention.
35 JS plus DOM/input/navigation/package tests pass; Chrome preview and 64
layouts/36 EFB insets pass. WASM builds with 0 errors/104 existing warnings;
matching self-contained helper publish succeeds. Underlying SDK/input routes
are simulated in these tests. Actual two-PC simulator stall recovery remains
unverified. No claim that the two earlier real stalls' underlying cause is fixed.

Frozen full candidate:
`artifacts/shared-recovery/fscopilot-efb-0.9.4-candidate.zip`, 33,233,401 bytes,
SHA256 `59D8B5180E23F25ABA31223387EB1E856C9656BC58AC6F17C05C9AB51CE1505A`.
Complete friend kit:
`artifacts/shared-recovery/test-kit/fscopilot-0.9.4-two-player-test.zip`,
33,252,484 bytes, SHA256
`558F1E99C286004BC3B5B41C5E06DD3F214736767137C3FFD11509A29754741A`.
All 32 protected kit files pass integrity. Installer ValidateOnly passes against
the named installed 0.9.1 target. Build/test logs are under
`artifacts/shared-recovery/tests`; complete guide and prepared installer are in
that stage. Earlier frozen 0.9.1/0.9.2/0.9.3 artifacts are retained.

**Still not installed:** respect the user's explicit "Still testing the previous
version" answer. Keep installed 0.9.1/helper and current learning/checks/codes.
When testing ends and MSFS is fully closed, use
`artifacts/shared-recovery/install-0.9.4.ps1`, which backs up package/.bin files,
verifies all seven hashes and preserves settings; switch to that bundle's
matching Helper. Both PCs must update completely. Restricted empty process
lists do not establish closure; keep the installer's closed-simulator guard.
No separate 0.9.2/0.9.3 installation is needed. Next real tests are parked
battery/NAV/STROBE/TAXI changes both ways, missing Steering wizard proof on the
friend, yoke-only pickup, and the kit's R4 two-second pause on each PC.
AP selectors/ownership remain pending, GTN pages stay local, and independent
barometers retain normal sharing. The deliberate 23:47 disconnect is not a bug.

## 0.9.3 (superseded by 0.9.4): missing battery/light mappings — 2026-11-17

The user reports the previous-version two-PC shared connection worked, but
battery/lights did not mirror. The native ordinary catalog and helper profile
admitted only A/L getters, dropping all 11 existing B-variable electrical/light
definitions. Vanilla's expression watcher handles these; the Baron YAML and
its includes already contain the correct getters/setters. Generic B getters
are now admitted outside the existing AP/GTN exclusions and read through
`fsVarsGetBVarId(name,false)` / `fsVarsBVarGet`. Exact vanilla setters, equality
suppression, current-readback validation and confirmation are retained. No
aircraft-specific switch table was added. See the new audit section.

Checked: 1189 actual definition/Jint checks; full native C++ and 39 bridge
contracts; full real-relay loopback with new host/friend battery and NAV-light
changes, strobe changes, exact five toggle writes and no echo; 35 JS checks,
DOM/input/navigation/package tests; Windows WASM 0 errors/104 existing warnings
and matching self-contained helper publish. Native SDK calls are simulated in
these tests; actual 0.9.3 two-PC switch behavior remains unverified. This update
also includes the pending 0.9.2 yoke-only takeover and brake-axis candidate.

Frozen complete candidate:
`artifacts/cockpit-input-events/fscopilot-efb-0.9.3-candidate.zip`, 33,231,067 bytes,
SHA256 `A3F85E724373F72F85674C9A9F243287DA0BC2E4691C75C2AA9B2B9541756350`.
Complete friend kit:
`artifacts/cockpit-input-events/test-kit/fscopilot-0.9.3-two-player-test.zip`,
33,249,142 bytes, SHA256
`9359EC9D8A67BA642892BEF7651240A0704F4BDB82CC4410BB154882D658EF54`.
Its file checker passes all 32 protected files. Installer ValidateOnly passes
against the named installed 0.9.1 package. Prior frozen kits remain unchanged.

**Do not install yet:** the user explicitly answered that they are still
testing the previous version. Installed package/helper remain 0.9.1. Preserve
current learning, saved checks and connection codes. Both PCs need the full
matching update when they finish testing; no separate 0.9.2 install is needed.
Wait for closure before replacing the loaded WASM. Prepared installer:
`artifacts/cockpit-input-events/install-0.9.3.ps1`; then switch to its matching
bundle Helper, preserving profiles and verifying hashes. Restricted empty
process lists do not establish closure; retain the elevated installer's guard.

**Accidental disconnections:** host helper log
`helper-20261117-192644-31572.log` records Native observations timed out at
23:16:27 and 23:27:17 Oslo time. The one-second local native freshness limit
fired first; local peer disconnect/relay leave followed. No learned-layout
failure is recorded for those drops. This establishes the trigger, not why
fresh simulator/SDK observations stopped. Do not change timeout policy without
investigating pause/stall/transport behavior. User confirms the remote close
at 23:47:29 was deliberate; no friend-log request is needed for that incident.
The 23:29:39 event was a later join-negotiation timeout, not an active-flight
drop. Relevant redacted lines are at
`artifacts/cockpit-input-events/tests/session-endings.txt`.

**Instrument status clarification:** both indexed QNH/barometer setters are
already in the normal shared definition path, with no pilot/AP ownership
restriction. Heading bug, selected altitude/VS and AP modes are excluded from
ordinary sync; guidance observation/transport tests exist, but production AP
input capture/ownership is not enabled. These selectors are still pending and
0.9.3 does not enable them. Keep GTN pages local. Do not describe the AP fake
tests as actual shared AP operation in the simulator.

## 0.9.2 candidate (superseded by 0.9.3): yoke-only takeover — 2026-11-17

The user's latest instruction is explicit: only roll/pitch must align before
Steering takeover. Rudder, brakes, nosewheel and trim still transfer/block with
the group, without matching positions. Local native runtime and shared helper
authority now implement that rule; UI counts only the two yoke axes and removes
pedal/brake alignment prompts. The wizard no longer requires pedal centering or
brake release. It still reads input and verifies actual output separately.

The friend's final wizard error is an output mapping failure, not alignment:
left brake requested 1.1895/1.4863/1.3809% and observed .3789/.4746/.4395%.
Preserve the local button user's working left route. Optional definition data
selects the nonlinear response for an observed absolute-axis source; the right
curve is unchanged. Full reasoning, empirical limitation and vanilla/include
audit are in `FsCopilot.Efb/VANILLA_REUSE_AUDIT.md`. No output tolerance is relaxed.
Only a changed effective Steering route loses its saved output proof; valid
engine checks and all saved learning/settings are kept. Both PCs need the
complete matching 0.9.2 helper/definitions/package.

Checked: 1168 definition/Jint checks; native C++ and 39 bridge/session contracts;
complete real-relay loopback including mixed button/curved-axis users, pickup
both ways with mismatched pedals, blocked non-owner input, and yoke mismatch
still waiting; 35 JS tests plus DOM/input/navigation/package tests; Chrome
preview and 64 layouts/36 EFB insets. The complete production-module SDK-fake
output check reproduces the 1.1895% case and confirms restoration. Windows WASM
build has 0 errors/104 existing SDK/legacy warnings; matching self-contained
helper publish succeeds. All tests above are local; no actual two-PC simulator
validation of this candidate has occurred.

Frozen full candidate: `artifacts/yoke-only-takeover/fscopilot-efb-0.9.2-candidate.zip`
(27 payloads, 33,229,516 bytes; SHA256
`BB951D68AB1E35F49925C9F3F7CEFB9ED7E46728CE3344967143E312102550C6`).
Complete friend kit: `artifacts/yoke-only-takeover/test-kit/fscopilot-0.9.2-two-player-test.zip`
(33,246,687 bytes; SHA256
`FFF400A309EE276B37B1A6B780FC434CB81247CB9B3506F308595963714AE01C`).
Kit integrity passes all 32 protected files. Frozen 0.9.1 bundles are retained.
Build/tests and receipts are in `artifacts/yoke-only-takeover`.

**Installation pending:** MSFS was last running (PID 27784); the user has been
asked to close it. Installed baseline remains 0.9.1 with helper PID 31572 at
`artifacts/efb-input-compat/bundle/Helper/FsCopilot.Efb.exe`. Prepared installer
`artifacts/yoke-only-takeover/install-0.9.2.ps1` verifies the named 0.9.1 target,
requires closed MSFS, backs up package and current .bin files, verifies all
seven installed hashes and preserves saved files. `-ValidateOnly` passes.
Never replace the loaded module. Switch to the new matching helper only after
closed-simulator installation. Keep existing settings; do not reset/Relearn.

A later restricted process scan returned no processes, but the elevated
installer's closed-simulator guard still detected MSFS and refused installation
before making any changes. Installed version remains 0.9.1. Do not infer closure
from an empty restricted process listing; retain the installer's guard.

Next: install after closure, then friend reruns only missing wizard groups and
both test solo yoke-only pickup with mismatched pedals before resuming parked
two-PC tests. Use the new kit's START HERE and log collector. Production AP
assignment remains disabled, GTN pages remain local, barometers normally shared.

## Test-kit installation repaired — 2026-11-17

The user reported missing Community payloads and two installed copies. Inspection
confirmed that the extracted kit's Community folder was empty and both simulator
Community and Community2024 contained seven identical 0.9.1 files. The checker
correctly reported the actual missing files/ambiguity; no checker or runtime
change was needed to pass it.

All seven kit payloads were restored from the original frozen ZIP after checking
its SHA256 and each extracted file. With MSFS closed, the duplicate at
`Packages/Community/fscopilot-controls-test` was moved intact to
`artifacts/two-player-ready/duplicate-install-backup-20261117-213635-e59230/fscopilot-controls-test`.
The previously tested `Packages/Community2024/fscopilot-controls-test` remains
installed. All four currently saved learning/output .bin hashes are unchanged.
The running 0.9.1 helper (PID 31572) was retained. No simulator settings were reset.

The unchanged extracted Check setup script now passes kit integrity, installed
seven-file integrity and running-helper/complete-definition checks. Report:
`artifacts/two-player-ready/TestKit/Reports/setup-20261117-213636-0073fc.json`.
Cleanup receipt: `artifacts/two-player-ready/duplicate-install-cleanup.json`.
The original frozen 34-entry ZIP remains unchanged and complete. Personal reports
and the archived duplicate are outside that distribution.

The source START HERE instructions now explicitly use Ctrl+C / Ctrl+V to retain
the kit's own package files, require one installed copy, and distinguish duplicate
cleanup from an explicit Community-path override. This documentation clarification
is for future kit builds; the existing frozen kit/manifest were not rewritten.
For this PC, use Community2024. Next step remains the two-PC parked tests.

## Two-player test kit ready — 2026-11-17

The user confirms the 0.9.1 UI is now good and asks to prepare proper two-player
network testing. The latest real native-log run also records fresh output passes
for Steering (1), Throttle (2), RPM (4), Mixture (8), saved mask 15, and wizard
completion (phase 6). Evidence is copied to
`artifacts/two-player-ready/local-wizard-evidence.txt`. Fresh learning/output files
have been written again as intended. Do not reset or restore old calibration.
Connection settings still need to be entered after the earlier explicit reset.

**Keep the installed 0.9.1 binaries and current matching helper (PID 31572).**
No application/runtime/aircraft definitions were changed for this preparation.
The new test kit verifies unchanged original binary payloads and adds clear
installation/testing instructions, a local preflight checker, an editable test
card and an augmented log collector. Both local installed package and running
helper/complete definitions pass its actual filesystem/process preflight.
This check does not contact the simulator or public relay, and does not prove
network or aircraft behavior. `local-preflight.json` records that scope.

Send the friend **the complete kit**, not the old 0.7.x UI-only update:
`artifacts/two-player-ready/fscopilot-0.9.1-two-player-test.zip`
(34 entries; 33,243,736 bytes; SHA256
`82CC3188ADB7DB0F39C6D440E777454C70578C08FE61518951FBA47111AA0099`).
All ZIP entries were hash-verified. Personal reports, logs and settings are
excluded from the distribution. It includes the complete matching Community,
Helper and definition tree. The local user can retain the verified existing
installation/helper; the friend must update to this full matching version.

Start with `START HERE.txt` / `README.txt`. The instructions explicitly cover:
old bridge/application removal from the running test, preserving saved learning,
four wizard checks on both PCs, reciprocal random connection codes, Host/Pilot
left vs Join/Copilot right, and the initial parked join. Test cases P1-P7 exercise
steady connection, exclusive Steering, independent engine Auto, scoped Force,
both normally shared altimeters, absolute flap targets and resync from either PC.
T1 then checks combined-button vs axis brakes during slow taxi. F1-F2 cover
manual flight/gear, and R1-R3 cover Leave, helper loss, explicit recovery/rejoin
and saved setup after restart. AP stays OFF. AP ownership and GTN route copying
are not enabled. Do not treat local-colour blue on both PCs as shared ownership:
blue is Me on each PC; one owner must appear blue locally and orange remotely.

Tool sources: `FsCopilot.Efb/TestKit` and `scripts/build-test-kit.ps1`.
`Check setup.cmd` checks every protected file, all seven installed files, the
actual running helper and its definitions, extra definitions, duplicate/moved-name
old bridge packages and a running vanilla desktop application. It never starts
or stops a process or writes simulator state. Unknown/ambiguous Community paths
are reported instead of guessed; the PowerShell script accepts -CommunityPath.
`Collect test logs.cmd` combines recent helper/native logs with kit identity,
setup reports and TEST RESULTS.txt. It is local-only and excludes saved .bin data.
Collect on BOTH PCs at the first failure with the case number/local time.

Validation: full real-relay loopback + production C++ SDK-fake network suite
passes after rebuilding its harness; 1,149 actual vanilla definition checks pass.
The test-kit fixture suite exercises missing included YAML, changed installed UI,
renamed-but-still-live old bridge, editable test card, path/duplicate refusal,
and the real CMD/PowerShell/ZIP diagnostic chain with no settings exported.
An initially missing parent-process compression assembly was fixed and the whole
fixture run passes. All are local tests, not actual two-PC simulation. The public
relay has not been contacted by these checks. Actual two-PC validation is NEXT.

The kit is frozen; do not overwrite the ZIP. Personal Check/Collect outputs go
under its Reports folder and are not part of the manifest/distribution. Tooling
changes require a separately prepared kit; the existing 0.9.1 candidate remains
untouched for rollback.

## 0.9.1 input compatibility candidate installed — 2026-11-17

The user reports no hover states and no right-click menu in the actual 0.9.0 EFB.
**0.9.1 is installed locally with its matching helper (PID 31572)** while MSFS is
closed. Seven installed hashes match; active saved settings remain empty after
the explicitly requested reset below. The 0.9.0 rollback, full matching ZIP,
candidate/install/verification receipts and test logs are in
`artifacts/efb-input-compat`. Last user-tested working local baseline stays 0.7.4.
Actual 0.9.1 EFB hover/right-click/hold behavior and the fresh wizard need testing.

Input changes in `src/view.js` / `src/controls.css`:
- Hover rules are separated from optional `:focus-visible` selectors so unsupported
  selectors cannot invalidate a shared rule. Pointer movement also sets explicit
  hover attributes, including independent throttle/RPM/mixture feedback.
- When events target the SVG root, rectangle picking accounts for the 900×680
  viewBox, scale, letterboxing and swapped seat translations. Native descendant
  targeting still works. No SVG matrix/bounding-box API is required in runtime.
- Secondary pointer down/up and button bitmasks are supported alongside mouse and
  contextmenu events. A trailing compatibility click is suppressed, preventing
  the opened menu from disappearing or assigning a control.
- A 650ms primary/touch hold opens the same Force-only menu. Release cannot become
  an assignment click, even after a long hold. Movement, cancellation, unmount and
  session changes fence/cancel pending holds. Opening a menu sends no command.
- Existing Force/double-click/ownership behavior is reused. No native ownership,
  aircraft definitions or physics runtime code was changed for this UI fix. The
  packaged WASM was rebuilt and is not byte-identical to 0.9.0; do not claim that
  the binary was unchanged. Both full bundles remain available for rollback.

Local controller/DOM/navigation/packaging and new embedded-input tests pass.
All 16 production C++ SDK-fake regression suites also pass; they do not contact
MSFS. Chrome
passes 64 layouts/36 EFB insets plus explicit checks with pseudo hover removed,
SVG-root targets, pointer-only right click and primary hold. These emulate limited
input behavior and are not actual Coherent/MSFS validation. Windows build: zero
errors, 104 existing SDK/legacy warnings. Complete 27-payload ZIP:
`artifacts/efb-input-compat/fscopilot-efb-0.9.1-candidate.zip`, 33,228,037 bytes,
SHA256 `D6D0C043ACB0D49324E7EAB7EF0D8FDC3BA8C9C2A8CF9E110900794ED64EA1B6`.

The exact cause in the shipped renderer is not confirmed. The vendor's
[selector support table](https://docs.coherent-labs.com/cpp-prysm/content_development/supported_features_tables/cssselectors/)
lists hover/focus but not focus-visible; compatibility is implemented without
assuming that table identifies MSFS's exact embedded-engine version.

Next: restart into the parked Baron with engines/AP off and parking brake on.
Check seat/control hover, right-click and the press-and-hold fallback, then run
Setup wizard from scratch without Relearn. Shared connection settings must be
entered again after the reset. AP ownership/route copying remain gated/deferred.

## Fresh-wizard reset completed — 2026-11-17

The user explicitly requested removal of all saved FS Copilot EFB settings to test
the wizard from scratch. After MSFS exited, the reviewed reset script verified a
backup and removed all six whitelisted saved binaries: learning/output checks and
connection/seat preferences. No Force binding existed. MSFS hardware profiles,
package and logs were retained. Receipt: `artifacts/cockpit-recovery/settings-reset.json`;
backup: `artifacts/cockpit-recovery/settings-before-fresh-wizard-20261117-190652-b1ec9f`.
The helper restarted as PID 36924, then was replaced by the 0.9.1 helper above.
The active WASM work folder contains zero `.bin` settings files before restart.
Do not restore old calibration automatically; the next test must be a fresh wizard.

## 0.9.0 cockpit reliability candidate — 2026-11-17

This section supersedes the earlier "not implemented" notes for UI integration,
manual resync and hardware Force Steering. **0.9.0 is installed locally with the
matching helper (PID 23996)**. MSFS was closed; all seven installed hashes match
and all six saved binary files are unchanged. The 0.8.0 package/closed-state work
backup and install/verification receipts are under `artifacts/cockpit-recovery`.
The old helper is retained, stopped, in `artifacts/instruments-guidance/bundle`.
Last user-tested local baseline remains 0.7.4. No new actual simulator, two-PC or
hardware validation is claimed; the user has been asked to restart into the Baron.

Complete matching bundle: `artifacts/cockpit-recovery/fscopilot-efb-0.9.0-candidate.zip`,
27 payloads, 33,226,453 bytes, SHA256
`76CA1AAA429E8C944D6B7A3A6D49834DEFF7F686192843EB936B6A40995AA703`.
The bundle contains an explicit README/test guide. Local checks pass: 1,149 actual
vanilla definition checks, 39 native contract scenarios plus production C++ SDK
fakes, full real-relay loopback suite, 35 UI/controller checks, DOM/navigation/
packaging checks, Chrome preview and 64 layouts/36 EFB insets. Windows WASM build
has zero errors and 104 existing SDK/legacy warnings. `verification.json` records
the tested scope. Browser automation blocks preview live reload only in its own
test tab so a development refresh cannot interrupt a gesture; user preview reload
remains enabled.

- The approved blue/orange cockpit mock is integrated into the actual EFB build.
  Seat selection, individual quadrant hover, inline pedal alignment, pending
  blinking and scoped double-click Force retain the Force-only context fallback.
  Auto starts on. Wizard/output checks still pause it, and recovery/Force can
  leave it paused; Setup offers a deliberate Resume action instead of concealing
  a disabled automatic policy behind a permanently hidden toggle.
- The Baron flap YAML now maps the three valid handle indices to absolute
  FLAPS_UP / FLAPS_1 / FLAPS_DOWN. Equality and invalid detents emit nothing.
  Gear keeps its existing absolute B-event setter; historical gear failure is
  still undiagnosed. Gear testing must be airborne, allowing normal travel time.
- Setup → Cockpit synchronization → Resync cockpit from host is available in an
  active matching shared session. Either PC can request it. The host waits for
  its pending write, publishes fresh readings plus an availability bitmap, and
  the receiver reconciles using vanilla setters and the latest authority values.
  Earlier failed revisions become eligible for a fresh bounded attempt. Only
  definitions explicitly listed as retrySafe may retry, up to three attempts;
  other setters get one. A 120-second batch limit prevents endless recovery.
  Counts and named failed/unavailable entries appear on both tablets and in
  helper logs. Session/profile/read-serial/readback fences remain in force.
  No ownership, physics, calibration, AP or GTN state is reset by recovery.
- Setup → Steering → Force takeover button chooses an SDK controller and listens
  for one of its first 128 buttons. Assignment commits on release, never on the
  learning press. Dual checksummed `work/force-steering-button.0.bin` / `.1.bin`
  records persist device identity and button per PC. EX1 input uses an independent
  non-masking input group and invokes the existing Steering-only Force command.
  Saved bindings start observation without an EFB view. Missing/ambiguous devices
  disable the binding. After load/replug, a press/release arms the listener;
  subsequent presses force once per down edge. No MSFS device profile is edited.
  Actual controller-ID stability, input delivery, replug and closed-EFB behavior
  still need simulator validation. Binding failures are isolated from control masks.
- Ordinary IPC advances to V2 and recovery adds a final network packet type;
  distribute the complete matching 0.9.0 helper/definitions/package to both PCs.
  Existing learned controls and saved output-check records are preserved.

Artifacts, logs, the full ZIP, per-payload hashes, install/rollback scripts and
the explicit test guide are under `artifacts/cockpit-recovery`. Local definition,
production-module SDK-fake, C#/native IPC, real-relay loopback and browser tests
are distinct from actual simulator checks. AP ownership is still gated off;
GTN screens remain local and route copying is deferred for Lite/free.

Next actual checks: retained learning/wizard results; cockpit interactions and
double-click Force; hardware button with tablet closed and across restart/replug;
then matching two-PC altimeters/flaps, resync from either PC, normal airborne gear
and disconnect/recovery. Resume the AP input/commander milestone afterward.

## In progress — 2026-11-17: ordinary instruments, AP assignment and GTN routes

**Additional requested feature: hardware Force Steering takeover.** Add a saved,
per-PC binding in **Setup → Steering → Force takeover button**. A physical press
must invoke the existing Steering-only Force action (Me, bypass alignment),
without requiring a selected seat or an open EFB view. Other control groups
retain their owners. Use one action per press/release, not a toggle or repeating
command while held. Reuse the current local/shared Force path and its session/
membership/confirmation guards; don't build a second ownership mechanism.

Feasibility is supported by official `SimConnect_EnumerateControllers` and
`SimConnect_MapInputEventToClientEvent_EX1` (explicit joystick button down/up
notifications; the older non-EX1 mapping has a documented reconnect device-ID
bug). A custom dotted SimConnect event alone does not create a discoverable
action in MSFS's normal Controls UI. Prefer a native/SDK-backed FS Copilot Setup
binding, verify saved device identity/replug behavior, and keep reception separate
from Steering's suppressed control-event group. Do not silently alter the user's
existing MSFS device profiles or assume the browser can read devices inside EFB.
Actual WASM/SDK routing and closed-EFB behavior still need implementation/tests.
Do not add AP-disconnect side effects without settling that behavior; current
request is the same Force Steering operation as the UI.

This is added to the UI-integration/control-reliability milestone; it is **not
implemented or installed yet**. Primary SDK reference:
https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/inputevents/simconnect_mapinputeventtoclientevent_ex1/

**Latest priorities after UI acceptance:** The user likes the reviewed mock and
asks what remains, reports historical vanilla gear/flap synchronization failures,
and requests an instrument resync option. The approved UI is not installed;
Community/helper remain 0.8.0. Prioritize (1) EFB integration of approved UI,
including real double-click and Auto/wizard behavior; (2) robust ordinary-system
sync and manual recovery, with actual two-PC tests; (3) AP commander/input adapter
and host NAV/APR integration; (4) complete flight/disconnect/recovery checks and
matching distribution. GTN displays stay local and route copying is optional.

Gear/flap investigation found:
- The Baron YAML `A:FLAPS HANDLE INDEX` setter sends only one FLAPS_INCR/DECR.
  A 0→2 update therefore cannot express the full target. Equal values choose
  DECR in the underlying definition; `CockpitDefinitionProfile.Plan` already
  suppresses equality in the new EFB path, unlike vanilla's toggle-only guard.
  Use a verified absolute flap target via definitions; aircraft detent differences
  belong in data, not a Baron runtime special case. Official SDK documents
  FLAPS_UP, FLAPS_1/2/3, FLAPS_DOWN and FLAPS_SET (scaled position, not raw index):
  https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-flight-control-events/
- Gear currently uses `L:LANDING_GEAR_Gear` with `B:LANDING_GEAR_Gear_Set`, already
  an absolute setter. The cause of the user's historical gear failure is not
  proven. Verify the selector against actual gear readback and permit normal
  extension/retraction time; don't infer a toggle defect or force surface state.
- The new ordinary bridge publishes host snapshots every second and verifies
  outputs. `CockpitReplication` deliberately suppresses the same failed revision
  after an unconfirmed write. Repeating a snapshot is therefore not a complete
  user-triggered resync, and no manual resync control currently exists.

Proposed recovery behavior (not implemented): **Resync cockpit from host** in
Setup. Either participant requests a fresh host snapshot after pending writes
settle, the receiver compares real readings and applies only differences using
existing definitions, and a scoped fresh attempt permits bounded retries of
previously failed entries. Report corrected/pending/failed/unavailable items by
name. Keep both pilots' normal instrument access; the host supplies the recovery
baseline. Do not reset ownership, hardware calibration/alignment, physics, AP
commands or local GTN pages/plans. Gear/flap command reliability should use fresh
readback, confirmation, bounded repair and no replay of relative press histories.
Distinguish deliberate local adjustments from a late remote-write readback to
avoid feedback. No runtime/definition fixes or simulator writes were made during
this investigation, and no new two-PC success is claimed.

**Latest UI review, round 5:** Yokes now include a filled 28×28 center hub on
the crossbar, moving with the existing hardware/target transforms. Handles are
longer again (30 units), retaining their slight inward lean. Pedal shapes and
center hubs use a separate **6-unit** corner radius; the default remains 12.
The live mock reloaded, browser checks pass and the screenshot was inspected.
No simulator package/helper changes.

**Latest UI review, round 4:** Corner radius is now **12** (two thirds of the
previous 18) across cockpit interior shapes, bottom corners and HTML containers;
windows/top remain unchanged. Yoke handles extend four units higher and strokes
are slightly thicker (input 9, base 7, target 3). Aligned checkmark glyphs are
removed; hardware/target markers, direction arrows and Setup diagnostics remain.
The localhost mock reloaded, browser checks pass, and its screenshot was reviewed.
The installed simulator package/helper are still unchanged at 0.8.0.

**Latest UI review, round 3:** The quadrant's pair backgrounds, lever tracks and
labels now share a centered layout (equal 7-unit outer margins). Interior SVG
panels, seats and hit areas use the instrument panel's 18-unit radius; rounded
HTML containers share an 18px radius, while windows/top geometry is preserved.
The cockpit bottom now runs below the seats, with 18-unit bottom corners and a
taller SVG viewBox. Yokes have a shallower crossbar curve and subtly inward
handles. The live browser mock reloaded; existing Chrome interactions/compact
layout checks pass and the new screenshot was visually inspected. No simulator
package or helper was changed.

**Latest UI review, round 2 (supersedes the green/red layout below):** Me is now
pastel blue and Mock pastel orange, with darker seats/light hover/white selection.
The cockpit SVG is 900×650, with more space between quadrant and pilot stations.
The quadrant shell now encloses THR/RPM/MIX. Owner names and status/help lines
are hidden in the cockpit; accessible owner labels and Setup diagnostics remain.
Rudder alignment moves opposing pedal graphics, with brakes immediately beside
each pedal pair. Unknown relative hardware positions are not fabricated. The
instrument panel is empty for later AP controls. Seats still swap left/right,
now using the wider station spacing.

The browser mock always has quadrant Auto **on**, with no visible toggle; the
installed simulator's policy was not changed. Preserve the real Setup wizard's
temporary Auto pause for output checks when integrating. Visible ellipsis
buttons and per-pair/Auto/output-check menu actions are gone; the wizard remains
the setup path. A Force-only right-click/keyboard fallback remains until actual
EFB double-click behavior is validated. The menu flicker was caused by closing
on every snapshot revision; ordinary updates no longer dismiss it, while loss
of usability/native session still does.

With Me selected, double-click forces **only the clicked group**. If the first
click is still awaiting acknowledgement, one Force is queued, bounded to three
seconds and the same native/shared-session context. Exiting assignment cancels
it. Completed assignment tracking is retired so a later Auto request cannot be
cancelled as part of an earlier completed assignment. Browser tests cover real
double-clicks, delayed acknowledgements, 900 ms menu persistence, pedal movement,
label/shell bounds and Auto on; DOM tests include changed-session refusal. The
existing 64-layout/36-inset Chrome regression and controller/DOM suites pass
with intentional old-menu/old-coordinate expectations updated. This is still
the live browser review at port 4173; no simulator package/helper was installed.

**Current priority: UI review in a standalone browser mock.** The user asked to
improve nine cockpit interactions before more simulator tests. All are now in
the shared UI source: darker pastel-green Me / muted-red Mock, lighter seat
hovers, white selected-seat strokes, separate engine-pair hovers, a dim neutral
quadrant shell, requested-owner hover tint and stronger confirmed-owner fill,
500 ms on/off alignment blinking, and same-seat/outside dismissal. Assignment
starts with no seat selected. Dismissal cancels only requests initiated in that
selection; it waits for in-flight acknowledgements, drains multiple cancellations
serially, and never reverses completed ownership. These are frontend changes;
the **installed Community/helper remain 0.8.0** and simulator behavior of the new
UI is not claimed. No package was built or installed for this mock.

`FsCopilot.Efb/preview.html`, `preview.css` and `preview.js` use the actual shared
SVG/CSS/controller through a browser-only simulated transport, with mixed/waiting/
all-Me presets and hardware sliders. No CommBus/SimConnect/helper access. The
hardware panel represents physical inputs and deliberately does not dismiss
selection; ordinary page/frame clicks do. Local live-reload server **PID 8340**,
`FsCopilot.Efb/scripts/preview-server.mjs`, serves only its allowlisted files on
**http://127.0.0.1:4173/preview.html**. Process metadata is in
`artifacts/ui-preview/server-process.json`; Chrome was opened visibly for the
user. Browser refresh/source edits require no UI bundle build or MSFS restart.
The preview can also open directly as a local file. Preserve this fast review
loop before packaging UI changes.

Validation: new actual Chrome test `tests/preview.test.cjs` passes for both file
and localhost access, including separate hover fills, 250/750 ms animation
sampling, outside/same-seat cancellation and aligned completion. Extended DOM
tests pass delayed-ack/multiple-group cancellation and confirmed-owner retention.
Existing **34** controller/registration checks and the **64-layout / 36-inset**
Chrome regression pass. Screenshots/logs are under `artifacts/ui-preview`.

**Latest readback / GTN result:** The user restarted the installed 0.8.0 and
reports “No avionics route response.” They confirm **GTN750 Lite/free**, and
the simulator's own EFB plan is empty/different from the GTN plan. PMS50's
[current feature comparison](https://pms50.com/msfs/) explicitly marks MSFS 2024
EFB Sync unavailable in Lite and available in Premium. Raw public HTML was
checked because the text renderer omits its check/cross icons. Do not ask the
user to keep retrying this optional interface or imply that Premium is needed
for shared controls/AP. Keep GTN displays local and planned NAV/APR guidance on
the physics host. Route copying remains optional and unavailable in this setup;
no import/screen replay was added. Existing 0.8.0 cannot distinguish SDK manager
startup, simulator acknowledgement and absent navigator response, so its timeout
alone does not prove which stage stalled.

A subsequent **0.8.1 UI-only source draft** prefers the direct planned-route
listener in an EFB view, avoiding the SDK singleton's aircraft-GameState/initial
EFB-read prerequisites. It distinguishes the three timeout stages, records
bounded ASCII error diagnostics through the existing native diagnostic callback,
and explains that navigator edition affects route access. Errors cannot be
substituted for route data. Focused async/timeout tests, controller/registration
and DOM tests pass. **This draft is not installed/frozen**; installed Community
and helper remain 0.8.0. The unchanged helper source is also still 0.8.0.

The read-only native probe was extended and run against the real restarted
simulator: native `75d3b73`, ordinary profile
`dace69d45885effac4e83d576463fa43`, **209/218** scalar values readable, both
required barometers valid and separate (**1012.5625 / 1013.25 hPa**), all required
hardware mappings loaded, no unknown required control targets, masks 0,
Auto 14 and no shared session active. No control/route writes were performed.
The nine unavailable optional LVArs are listed in
`artifacts/instruments-guidance/post-restart-observations.json`; don't fabricate
zero values or claim complete 218-instrument coverage. Full raw SDK observation
is in `tests/post-080-read-only.log`. Actual two-PC instrument synchronization
and AP takeover remain unverified.

The user explicitly requests continuing steps **3, 4 and 5**: normal instrument
sync from vanilla definitions, independently claimable AP, and route-only GTN
sharing with local screens.
The user completed the selector capture and closed MSFS, authorizing the update.
**0.8.0 is now installed**; **0.7.4 remains the last user-tested baseline**.
All seven package hashes match, and all **six** saved binary files are unchanged.
Closed-state package/learning rollback and receipt are recorded in
`artifacts/instruments-guidance/backup.json` and `installation.json`.
Matching helper PID **25632** runs hidden from
`artifacts/instruments-guidance/bundle/Helper/FsCopilot.Efb.exe`; its process
record is `live-helper/helper-process.json`. The old path-verified helper PID
33184 was stopped. No simulator process was running during installation.
The installed 0.8.0 candidate was frozen at
`artifacts/instruments-guidance/fscopilot-efb-0.8.0-candidate.zip` (33,190,970 bytes;
SHA256 `578E7D634F1A85735A9962F0247B71BCB78C3E42B0C92C33ABE9984674D64113`).
Its 27 payloads and ZIP contents are individually verified in `candidate.json`.
It contains ordinary instrument synchronization and a read-only route diagnostic;
**AP claims and route copying remain unfinished and disabled**. No agent has
written simulator controls during this work; only passive SDK captures ran.

Ordinary-instrument source now exists:

- `CockpitDefinitionProfile` selects A/L scalar entries from the full vanilla
  tree, preserves `Definition.Set`/Jint, and excludes physical/AP writers and
  local GTN/GNS modules. Aircraft policy is in `Definitions/cockpit/*.json`.
  Both indexed barometers are required. An existing duplicate circuit-6 getter
  has two conflicting bus setters; policy explicitly withholds it pending a
  verified mapping. B/H and string getters are currently excluded and must not
  be described as complete instrument coverage.
- `CockpitReplication` serializes ordinary changes at the host. Both players
  can adjust shared instruments; there are no seat/AP-owner barometer locks.
  Revisions, input/output distinction and echo suppression prevent rotary loops.
  A real loopback test exposed dropped rapid adjustments during receipt latency;
  the fix rebases across the same peer's accepted writes while fencing newer
  physical host adjustments. Focused relay tests now pass.
- `control_cockpit*` provides a separate 8192-byte native IPC area (0x7130/31,
  capability bit 32) with typed indexed AVar/LVar reads, active session/backend/
  catalog checks, a fresh live baseline check before executing a vanilla RPN
  setter, and three matching observations. Optional writes also check the live
  physical lease before dispatch, including expiry before the next native tick.
  NaN clocks and invalid/oversized targets are refused. No physics or hardware masks are
  changed. Definition-generated catalog now includes ordinary entries/hash.
  Native status serial remains monotonic across aircraft resets. Link feedback
  expires and is separate from the experimental seat-owned barometer channel.
- `NativeCockpitClient` follows the existing optional AP transport isolation
  pattern. `DefinitionCockpitBridge` performs profile handshake, native-fenced
  writes and echo-safe replication. Runner/backend/service/authority bridge now
  accept the ordinary profile; disposal retires its fence. New helper policy
  JSON is packaged by the helper/compiler/definition/network test projects.
  Native and C# bridge capability validation accepts bit 32 (maximum mask 63).
- EFB displays ordinary altimeters without ownership restrictions when the new
  native field is present. Old experimental paths remain disabled in production.

GTN work now has `src/navigation.js`, a **read-only RouteInspector**, using the
installed SDK's FlightPlanRouteManager or `JS_LISTENER_PLANNEDROUTE` directly.
It requests avionics data, correlates request IDs, rejects conflicting responses,
keeps EFB route comparison separate, and never calls a route writer. Setup has
Read avionics route; native diagnostic callback writes bounded ASCII canonical
JSON to `work/avionics-route-last.json` without executing/importing it. This is
support investigation, **not flight-plan copying**. SDK request responses have
no provider identity; actual GTN route/export/import remains unverified.
Inspector tests and DOM checks pass, including async cancellation/unsubscription,
early/foreign replies, multiple responders and stale controls. The actual
simulator/GTN route read is still needed for this new inspector.

AP capture evidence under `artifacts/instruments-guidance/tests`:

- Early 300s/1800s captures saw no actions; the user replied after those windows.
  Do not count those as route verification. The current observer was rebuilt
  from source and accepts up to 7200 seconds; all subscriptions are non-masking.
- After the user set the heading near 170, an immediate 5s read confirmed
  `AUTOPILOT HEADING LOCK DIR:1 = 167.78320312` (file `ap-heading-170.log`).
- User completed the longer button capture. The owned PID 24664 was path-checked
  and stopped. `ap-buttons-live.log` records AP_PANEL_HEADING_HOLD, AP_ALT_HOLD,
  AP_NAV1_HOLD, AP_APR_HOLD, AP_BC_HOLD, TOGGLE_FLIGHT_DIRECTOR, AP_ALT_VAR_DEC/INC
  with argument 100, and AP_MASTER. HDG/ALT/FD/readouts change; NAV/APR/BC did
  not show corresponding mode activation while parked. This does not prove
  suppression or output. The last AP_MASTER left AP on; user then switched it
  off on request. `ap-off-confirmation.log` verifies Master=0, FD=1, ground=1,
  groundspeed about .045 knots. Do not ask that off step again unchanged.
- **Completed:** passive selector/rocker observer **PID 35328**, path
  `artifacts/instruments-guidance/live/guidance_input_capture.exe`, metadata
  `ap-selectors-observer.json`, log `tests/ap-selectors-live.log`. User completed
  the requested heading and KFC UP/DN actions with AP OFF/FD ON, then closed MSFS.
  The observer already exited; no process needed stopping. Capture records
  **303 AP_HEADING_BUG_SET_EX1**, slot 1, plus **10 AP_PITCH_REF_INC_UP** and
  **9 AP_PITCH_REF_INC_DN**. Pitch-reference readback in degrees changes by .5:
  INC_UP reduces its numeric value and INC_DN increases it in the observed mode.
  The heading InputEvent value is a normalized percentage, not degrees; prefer
  actual degree readback/key EX1 scaling. Its final FAIL label is from simulator
  shutdown (`QUIT`), with **zero SDK errors** and no writes. This is useful
  passive routing evidence, not suppression/output proof. No capture is pending.

AP ownership is **not newly implemented/enabled**. Existing protocol/coordinator
tests use simulated verified providers; production guidance gate is still null.
Important remaining issues before enabling: pitch-reference/UP-DN coverage (the
current contract has 13 settings, no pitch reference), actual input suppression,
guest AP-engine isolation, selector display/reference behavior, and UI claims.
The guidance packet has **35 reserved bytes at offset 477**, not three. They can
support versioned UI feedback/claim fields if needed. Do not weaken proof guards
or claim a provider merely because the captured keys match standard events.

Current validation: **1136** definition/core checks, **38** native C# contracts,
the full reciprocal C++/C# native runner, ordinary cockpit actual-relay test,
full existing network regression, **34** JS controller/registration checks,
new route tests, extended DOM checks, package tests and actual headless Chrome
layout/click regression all pass. SDK/native inputs in automated tests are
simulated; this is not two-PC simulator proof. The real Windows WASM build passes
with **104 SDK-header warnings / zero errors**. Published self-contained helper
dry-run exits 0 from an unrelated working directory, loads all packaged policies
and **218** ordinary entries (hash `dace69d45885effac4e83d576463fa43`), and opens
no simulator/network clients. Seven-file Community payload and matching helper
are in `artifacts/instruments-guidance/bundle`; README.txt has concrete tests.
The 0.8.0 candidate has now been installed as recorded above. The published
helper startup log confirms version/path/PID; its initial native-disconnected
message is expected while MSFS is closed. Current user prompt asks for a restart
into the parked Baron and **Setup → Flight plan → Read avionics route** after
creating a recognizable GTN plan with an intermediate waypoint. Need exact
status/airports/enroute count and comparison with the GTN. No Relearn requested.
New native ordinary-channel live read and actual two-PC instrument tests remain
pending. Preserve the tested 0.7.4 rollback.

## Latest checkpoint — 2026-11-17: 0.7.4 wizard passed the user's Baron test

The user replied **“it works!”** to the request to run Setup wizard in the parked
Baron, complete all four named checks and return tested controls to Me. Record
this as a successful user-reported actual simulator test. **0.7.4 is the current
tested local baseline.** No repeat of that test or reply to the earlier prompt
is pending. Restart reuse in 0.7.4 and the friend's matching update/two-PC test
remain unconfirmed; automated cancellation/failure tests remain simulated.

The user asked whether work continued. Clarified that the previous turn stopped
after the guide, then implemented and tested the wizard. **0.7.4 is now installed.**
Read-only process checks found MSFS closed. The guarded installer backed up
0.7.3 and closed-state saved `.bin` files, verified all seven new Community
hashes, and confirmed saved data unchanged. The path-verified owned 0.7.3 helper
PID 36980 was stopped; matching **0.7.4 helper PID 33184** runs hidden from
`artifacts/setup-wizard/bundle/Helper/FsCopilot.Efb.exe`, working directory
`artifacts/setup-wizard/live-helper`. Its process/log receipt is in that folder.
No simulator control commands or live output tests were issued during this work.

**Completed user test:** parked Baron, engines/AP OFF, parking brake ON;
EFB → FS Copilot → Setup → Setup wizard. The user confirms success. The wizard
handles Me/Mock/Auto configuration. This confirmation covers the guided flow;
no independent per-axis SDK capture was taken after the user's reply.

Native orchestration survives EFB view close/reload, skips valid saved proofs,
pauses Auto, requests ordinary aligned Me, waits for fresh hardware/stability,
masks one group, runs the existing 1% check, saves, requests aligned return and
restores original Auto after clean completion/cancellation. Cancel requests
bounded restoration before alignment. Output/storage/context failure keeps Auto
off and reports a specific group/axis. Scoped Force is an explicit user action.
Competing controls are locked; recent duplicate Start/Cancel packets are ignored.
Steering relative-event timestamps record activity without inventing positions.
The additional engine-state subscription is read-only and separate from the
existing manual output-check parked guard. Saved mappings/tolerance are retained.

The right-brake candidate correction is in the existing Baron YAML setter, using
the documented nonlinear axis curve and explicit normalized breakpoints in role
metadata. The compiler accepts bounded piecewise affine branches, executes the
actual vanilla/Jint setter and emits interpolation knots. Generic native output
and fixed-input normalization consume those knots. Runtime contains no new
Baron/right-brake correction branch. Left brake retains its observed working
affine route. Curve changes hash into only that group's output proof. The user
now confirms the guided checks pass with this installed correction.

Validation passes: 1112 vanilla YAML/Jint checks; full native contracts (37) and
C++ regression, including all 16 saved-result combinations with Auto on/off,
hardware prompts, each cancellation phase, restoration/save/output/context
failure, native command/replay integration and cached setup without output;
full actual loopback relay/client regression with production C++ sessions and
simulated SDK, including mixed relative/button/axis controls; 34 JS checks;
DOM wizard prompts/locking/cancel/view reload/error/results; Chrome 64 layouts,
36 insets and nine additional wizard layouts; maximum native JSON parsed by
production JS; Windows WASM build with zero warnings/errors; packaged helper
dry run from a foreign working directory (9 roles and matching adapter
`definitions-v1-d68ab55779773cfab91cb66b`); package tests and frozen ZIP hashes.
Automated SDK/control tests are simulated, not actual MSFS/two-PC validation.

Frozen complete bundle:
`artifacts/setup-wizard/fscopilot-efb-0.7.4-candidate.zip`, 26 payloads,
33,123,844 bytes, SHA256
`2B0067D00384F7E0A2AEE1A95E6B69E6BDA87B1415B37B67D77396BD39179ECA`.
Candidate metadata remains frozen. Installation/backup/verification and test logs
are under `artifacts/setup-wizard`. Do not rerun the baseline-specific installer
against installed 0.7.4. The friend needs the full matching bundle before the
next shared test. AP assignment/GTN route copying remain unchanged and disabled.

## Latest checkpoint — 2026-11-17: Setup wizard guide and Steering output failure

The user requests one **Setup wizard** entry that performs shared-output
configuration/checks and asks only for hardware movement once the aircraft is
ready. The implementation guide is
[`FsCopilot.Efb/SETUP_WIZARD.md`](FsCopilot.Efb/SETUP_WIZARD.md). It specifies native
orchestration, saved-result reuse, automatic Me/Mock/Auto management, per-axis
prompts, cancellation/recovery and acceptance checks. This is documentation;
no wizard has been implemented or installed. The installed package remains 0.7.3.

The earlier Steering test request has been answered: the user reports an
output-check failure. The bounded read-only observer completed with zero SDK
errors. At the 1% nudge it recorded rudder=328, EX1 left brake=326 and right
brake=104 (brake demands in Position 32k). Left brake reached about 1%, right
brake about 0.32%. Both demands and rudder returned to their original zero
values; parking brake remained on. Evidence:
`artifacts/mixed-brakes/tests/live-steering-output-observer.log`. This establishes
an output mismatch, not its root cause. Native logs report requested movement
not confirmed. Do not repeat the unchanged test request or claim Steering output
passed. Investigate the YAML setter/reference before custom runtime corrections;
keep the output-check thresholds intact.

Next implementation order: fix/validate the affected output route, then build
the wizard using existing runtime, diagnostic and validation storage. No
simulator controls, installation or helper changes were made while writing
this guide. The earlier input recognition success remains valid.

## Latest checkpoint — 2026-11-17: 0.7.3 installed; mixed rudder/brakes implemented

This supersedes the 0.7.2 notes below. The user reported rudder/brake learning
failure, asked to use fixed SDK routes, removed the Simple quadrant view, and
requested Auto on by default. These changes are implemented and installed.
MSFS was explicitly closed by the user. All seven Community hashes match and
saved `.bin` files are unchanged; rollback/closed learning are backed up under
`artifacts/mixed-brakes`. The old owned helper PID 32532 was path-verified and
stopped. Matching **0.7.3 helper PID 36980** is running from
`artifacts/mixed-brakes/bundle/Helper/FsCopilot.Efb.exe`; its process record is
`artifacts/mixed-brakes/live-helper/helper-process.json`. Do not start old helpers.

**Live 0.7.3 input check passed:** user confirms rudder and combined brake-button
input works without learning after restart, parking brake on, Steering Me.
The read-only C# SDK probe confirms native `69d28cb`, matching nine-role adapter
`definitions-v1-e121813198d55c3221c76006`, Required=Learned=0x7e37, Advanced ready,
Auto=14, Blocked=0 and no unknown required cockpit targets. Centered rudder reads
0.5; left/right brake demand reads zero. Saved mappings are loaded. Several
hardware positions are still unknown until movement; that is not lost learning.
Capabilities=27 means shared output is not enabled yet. No output-validation
cache files existed at this check. This is input recognition, not proof of new
masked output or actual two-PC sharing. Receipt: `tests/live-0.7.3-inputs.log`.

**Steering output check completed and failed:** see the latest checkpoint above
for the observed right-brake movement mismatch. The bounded read-only observer
finished; it sent no controls. No unchanged repeat of that user test is pending.

Frozen full matching update:
`artifacts/mixed-brakes/fscopilot-efb-0.7.3-candidate.zip`, **33,102,419 bytes**,
SHA256 `07918F59C7D84DB0BFC0D958B44A1A51D5A75440947581B02A681C138D7940FB`.
All 26 ZIP entries were reopened/hashed: seven Community files, the complete
Helper/Definitions folder including the new standard flight-controls module,
log collector and root instructions/licenses. Both PCs need this full update,
not the earlier 0.7.2 Community-only ZIP. Candidate creation metadata remains
frozen; `installation.json` and `verification.json` describe deployment.

Actual passive capture identified **RUDDER_AXIS_MINUS/PLUS**, not absolute
AXIS_RUDDER_SET: 67 relative events, no SDK errors. It also measured normal brake
positions at 1.0 while EX1 left/right demand was zero with parking brake on.
The capture caught the rudder segment near its end; no brake key events were
recorded in that window, so do not misrepresent the user's later button sequence
as captured. The user confirms one button for both wheels; friend uses axes.

Runtime now uses fixed definitions for rudder and both brakes, requiring no
two-position calibration. `modules/flight_controls.yaml` reuses vanilla
apply_surfaces' opposite rudder sign. Existing Baron EX1/Position 32k brake
definitions and their `value - 16384` setters are reused. The shared compiler
validates bounded absolute affine expressions and emits distinct read/output
domains. No aircraft-name condition was added to runtime. Typed AVar reads join
the existing typed LVar references; failed reads produce unknown data, not zeros.
Steering's required core is stable (0x37; total twin-engine schema 0x7e37) before
the first relative or axis event. Old roll/pitch/engine learning is retained.

On Me, actual cockpit demand combines buttons/axes/relative rudder and supplies
shared input. While held, reflected output is never treated as local hardware.
Absolute pedal positions normalize on their first event using the standard
definition, without learning. Relative commands have no absolute hardware pose;
incoming relative Steering requires centered cockpit rudder and released brakes.
UI marks the relative mode/readiness explicitly and hides the unknown hardware
dot. Both combined/separate brake buttons, absolute brake axes, relative rudder
and standard absolute rudder keys belong to the same Steering mask.

Engine controls are always separated. Native startup is Advanced/Auto on; the
Simple option is hidden and aggregate diagram clicks do not claim the quadrant.
One quadrant Auto policy still affects each engine pair independently. Shared
binding retains its proven Auto-OFF initial snapshot/masking contract; after
Active the host's selected Auto is applied once through ordinary authority.
An explicit output check pauses Auto and resumes it only after confirmed restore.
SDK/restoration failure or Force/Leave keeps it off. The unchanged engine proof
hashes survive this update; new Steering routes retire only Steering validation.

Validation passed: **1104** actual vanilla YAML/Jint checks; native C++ tests and
**37** wire contracts; new mixed-input tests; actual relay plus both production
C++ sessions with a relative/button host and absolute-pedal peer, suppression and
takeover in both directions, combined/independent wheels, neutral relative pickup
and stable layout; full network regression; **33** JS checks; rewritten DOM cases
for always-separated controls and relative indicators; Chrome 64 layouts/36
insets plus validation-text layouts; real Windows WASM build (0 warnings/errors),
actual packaged-helper dry run and ZIP hash verification. SDK calls in local
control/network tests are simulated. Actual 0.7.3 input recognition has now passed;
Baron masked output/two-PC validation is still pending.
Evidence: `artifacts/mixed-brakes/tests`.

The user asked for AP rules again. Reiterated these as **proposed/not enabled**:
one independently claimable AP commander, separate NAV/APR source initially the
physics host, independent GTN pages with intended route-data copying, fixed
pilot-side Baron AP altitude reference regardless of seat/commander, normal
shared separate barometers, and Steering owner's independent AP-off path.
Actual AP input suppression and flight-plan copying remain unverified/disabled.

## Latest checkpoint — 2026-11-17: installed 0.7.2; saved validation and navigation investigation

This section supersedes historical checkpoints below. The user went to bed and
authorized continued work. No question is pending. Do not ask them to paste the
same friend log again: they confirmed it was the whole file.

**Installed:** EFB/native 0.7.2, existing helper 0.7.0 unchanged. MSFS had exited;
the guarded installer verified all seven payloads, created a 0.7.1 rollback copy
and closed-state learning backup, and verified every saved `.bin` stayed unchanged.
Receipt/backup/verified update ZIP: `artifacts/output-validation-cache`.
Frozen ZIP: `fscopilot-efb-0.7.2-validation-update.zip`, 487,432 bytes, SHA256
`403A782A065DEA8912E12D6D1C0726F28B073E006B4460CD54096BF0D6ECF4C5`.
This ZIP updates Community only; it does not require replacing either helper.

Completed shared-output checks now persist by aircraft/engine identity and
effective per-group output/reference routes. UI/helper-only updates and unrelated
definitions preserve them. Absolute definition output does not depend on physical
axis calibration; the steering fallback does. Two checked disk copies must agree
on each pass, including after a partially written revocation. A recheck revokes
its old saved pass before any diagnostic movement; SDK output errors revoke saved
validation. Storage failures are visible. Startup performs no output writes and
does not invent current hardware positions, alignment, ownership or membership.
Setup explicitly names every missing group in all partial states and reports
complete/saved status. Core Steering rows (roll, pitch, rudder, both brakes) are
always visible, with undetected inputs marked; this UI change alone is not the
mixed brake-input fix.

Actual old 4/4 results existed only in 0.7.1 memory. A read-only attempt to capture
them after exit could not connect, so **no synthetic passes were seeded**. The
first checks in 0.7.2 establish its durable record; later restarts reuse it.
Simulator restart/persistence validation is pending. Tests passed: actual-module
restart with zero key/AVar/LVar output writes, corrupt/interrupted storage and
per-route invalidation; 37 native contracts; 33 JS checks and DOM all 16 pass masks;
real Chrome 64 layouts/36 insets plus named-output text at 420×560/760×700; full
actual-loopback network regression; real Windows WASM build, zero warnings/errors.
The browser screenshot was visually reviewed. See `verification.json` and `tests/`.

The longer **second actual two-PC session** did reach Active and lasted about
three minutes (23:18:41.527–23:21:42.329 peer close). The user confirmed it worked;
host status reached revision 18 with all four groups attached. After recovery,
host Blocked=0, Required/Learned=0x7e03, capabilities=31. Guest initiating failure
is still unconfirmed. Rudder/brake first discovery can change the required-axis
schema and invalidate the old readiness gate; source inspection supports that
hypothesis, not proof of this particular disconnect.

**Remaining immediate control fix:** stable Steering schema and independent
support for button brakes versus axis toe brakes on the two PCs, including
non-owner suppression for both. Existing event masks include BRAKES/BRAKES_LEFT/
BRAKES_RIGHT and AXIS_LEFT/RIGHT_BRAKE_SET; learning/output supports only the latter.
Use the existing vanilla `BRAKE LEFT/RIGHT POSITION EX1, Position 32k` definition
setters (`value - 16384`), rather than a Baron-specific output workaround. Do not
interpret a button's incremental pressure command as an absolute axis or guess a
release event. Only Me-side cockpit brake observations may supply button demand;
masked reflected output cannot become local hardware input. The native behavioral
fix and actual asymmetric two-PC test remain unfinished.

**AP/GTN investigation:** see the latest section in
`FsCopilot.Efb/VANILLA_REUSE_AUDIT.md`. The Baron manual explicitly identifies
the pilot-side encoding altimeter as its KFC 150 altitude reference. AP ownership
or seat swaps must not silently switch it to the right altimeter. Both altimeters
remain normally shared; seat/AP-based barometer restrictions stay retired.
PMS50 2.2.64 is confirmed installed; the provider advertises 2024 EFB route sync.
The installed 2024 Working Title SDK exposes route request/response and route-send
methods, a better first investigation path than touches or legacy GPS writes.
MSFS Planned Route data covers itinerary/procedures, not full active-leg/Direct-To/
approach execution. Runtime compatibility/source identification and receiver
readback are not yet verified. No AP claims, route writes, provider/package edits
or additional simulator output tests were enabled during this investigation.

The diagnostic-only helper 0.7.2 source/publish under `artifacts/shared-diagnostics`
has not been installed/distributed. It adds a fixed per-run log location and a
log collector; actual CMD/PowerShell/ZIP fixture tests and a fixed-path published
helper dry run from an unrelated working directory now pass. Keep both peers on
the working 0.7.0 helper until a complete matching replacement is deliberately
packaged and tested. Its codec assembly version differs.

## Active master-plan work (2026-11-16; installed baseline from 2026-11-15)

### Latest checkpoint: first direct two-PC bind reached Active, then guest disconnected

Both PCs completed their current-load **4/4** after the 0.7.1 UI update. They
then clicked Join themselves before the planned synchronized walkthrough. Host
helper logs prove direct P2P connection to friend01, Binding at 23:00:07.800 and
**Active / Shared controls connected** at 23:00:07.893. At 23:00:09.114 the peer
connection closed (RemoteConnectionClose); host ended at 23:00:10.102. The user
confirms both EFBs showed session ended after roughly one second. Do not call
actual shared controls/world/flight validated by this brief Active status.

Host read-only status: native **6491c43**, capabilities 31, saved settings
jorge001/friend01, Host/left, Auto off; session
`7f9c5802f0754bf5a4f41512864ce014` Ended, all four hardware groups held
(`Blocked=15`), no groups attached. Native result Closed, error None. Host world
values validate and all required cockpit targets are known; world reads continue.
The host log reports only peer disconnect, not the initiating guest reason.
Evidence preserved at `artifacts/first-shared-flight/verification.json` and `logs/`.

Recovery instructions already sent in commentary: stay parked; on **both PCs**
close Setup, Steering dots -> Force takeover, quadrant dots -> Force takeover;
this explicitly reconnects hardware at its current positions. Keep MSFS running
and do not retry Join yet. Read-only capture afterward still had Blocked=15, so
recovery has not yet been confirmed. Four checks remain valid without reload.

The user pasted a friend helper log ending around 22:43 with startup/restart
errors and no Connecting/Binding/Active/Ended entries. Its earlier missing
`fnx_32x.json` was before loading the Baron and is not established as this failure.
**Pending question:** friend closes/reopens their helper `log` in Notepad,
Ctrl+End, pastes the newest connection-attempt phase/error lines. No fresh Join
is needed to retrieve that evidence. Do not diagnose a timeout from timing alone.

The user clarified host was on runway with engines running, guest parked engines
off. Join code does not require matching engine-running states, so this alone is
not an established cause. For a controlled retry request both stationary, brakes
set, engines/AP/Auto off; ordinary engine/system-definition streaming is not yet
implemented. Continue independent inspection, but preserve existing fences and
timeouts until the actual initiating failure is known.

### Previous checkpoint: 0.7.1 buttons confirmed; current-load output checks

The user cannot open Flight/Your seat dropdowns in the actual EFB. The live
snapshot is Idle, ready and unblocked, so active-session locking is not the
explanation. Ordinary HTML selects were used for those fields; the actual-browser
tests previously changed selected options by script rather than clicking them.
Source now uses explicit paired buttons for Host flight / Join flight and
Pilot-left / Copilot-right, with highlighted selection. Choices remain drafts
until Save; timers preserve them, unsaved edits disable Join, and active/pending
sessions still lock edits. Keyboard navigation includes the new buttons.

Candidate: `artifacts/connection-buttons/fscopilot-efb-0.7.1-ui-update.zip`,
478,986 bytes, SHA256
`7C2759C0D321CD8CF2F6EB9837D2308A1533A496ECFABEA7BAD2FF0F6FD87EFA`.
Every ZIP payload hash is verified. Only packaged JS, manifest and layout differ
from frozen 0.7.0. **WASM is byte-identical**, and the existing 0.7.0 helper and
definitions remain compatible. App registration now reports 0.7.1 too (it had
stale 0.5.3 metadata). Original 0.7.0 ZIP stays frozen.

DOM tests now click both role/seat options and verify selection, draft persistence,
no pre-Save command, payloads and lock behavior. Actual headless Chrome mouse
clicks pass, with 64 layouts/36 EFB insets and 420x560/760x700 connection captures.
The first restricted-process browser launch could not initialize its GPU; the
same test passes outside that environment with an isolated local profile. All
33 controller/registration checks and package checks pass. Logs/screenshots are
under `artifacts/connection-buttons/tests`. After the user confirmed MSFS closed,
0.7.1 was installed at 21:42 UTC: all seven hashes verified, native byte-identical,
and all three saved-learning/settings files unchanged. The 0.7.0 rollback and
closed-state work backup are under
`connection-buttons/installed-backup-0.7.0-before-0.7.1-20261116-224220-e4c598`.
Receipt: `connection-buttons/installation.json`. Existing 0.7.0 helper stays in
use. The friend applies the small ZIP's Community folder manually with MSFS
closed. Four parked output checks reset on restart.

The user confirms the buttons work and settings are saved. A fresh read-only SDK
capture confirms native **6491c43**, codes **jorge001 / friend01**, Host role 1 /
left seat 1, **p2p.fscopilot.com:3600**, saved revision 3. Learning matches the
required schema (`0x7e03`), with Auto off and session Idle. Throttle was initially
unread after restart but now has a current hardware value; the latest capture has
only Throttle on Mock (`Blocked=2`), consistent with ongoing local tests.
The previous load's extra unlearned trim axes are absent in this load.

The user reports Join shared session is greyed out. **Capabilities are 27**, so
this load has not completed its four parked output checks; the helper reports
UnsupportedPackage for that gate. This is not evidence of a networking failure.
Pending user question: both PCs repeat all four parked checks after the UI-update
restart, moving saved controls once on Me first; return all groups to Me, report
both **4/4**, do not press Relearn. Then verify capability 31 / WaitingForJoin and
guide both to Join together. New diagnostic probe logs saved connection and
session context, built at `artifacts/connection-buttons/probe`; it sends no
control/session writes. Receipt/log: `connection-buttons/live-validation.json`
and `connection-buttons/tests/live-0.7.1-join-readiness.log`.

The newest local load is native `633b60a`, capabilities 27, all groups Me, Auto
off, session Idle. Required axes `0x7fc3` but learned `0x7e03`: extra trim axes
6/7/8 have been detected but are not learned. Do not treat the prior load's 4/4
as current readiness, and investigate these extra axes if they prevent Steering
learning/checks. Current live capture: `definition-controls/tests/live-connection-selectors.log`.

### Previous checkpoint: 0.7.0 all four live output checks passed; two-PC test pending

The user explicitly asked to continue and complete the plan. This milestone
implements physical definition selection/execution; the whole mod is not yet
finished or verified on two PCs. **0.7.0 is installed and its four parked output
checks passed**, with 0.6.9 retained for rollback.

- `FsCopilot.Definitions` now loads immutable aircraft control-role selections
  using the actual vanilla loader/setters. `Definitions/controls/*.json` points
  at existing YAML entries and declares their ranges. Missing/ambiguous roles,
  incomplete engine groups, arbitrary expressions and relative/toggle physical
  setters are rejected. Selection handles native/vanilla path separators and
  validates engine count and adapter fingerprint before shared setup.
- `FsCopilot.Definitions.Compiler` emits `control_definition_catalog.h` from that
  data. The WASM build runs it first. The catalog currently selects the target
  aircraft's six engine roles; adding/changing a physical mapping requires a
  matching package rebuild. Runtime profile upload is not implemented.
- `control_definition_profile.h`, generic cockpit reads and
  `control_definition_output.h` replace the literal production Baron selector
  and mixture names. The generated catalog contains the aircraft data. Existing
  hardware learning/storage and generic steering output remain in place.
  Throttle uses its vanilla AVar setter, RPM its absolute key setter, and mixture
  its existing LVar setter with explicit Percent units. No raw-scale guessing or
  SimConnect LVar registration is used. Complete-pair validation and existing
  session/owner/revision/generation/sequence/mask fences remain enforced.
- 1,095 actual-definition checks pass. Native checks now cover typed output,
  guarded parked checks, sustained SDK packet retirement and the actual module's
  four-group capability transition, plus the 37 C# wire/session contracts.
  Full real-loopback regression passes. Its configured EFB path now exercises
  both production C++ controllers with definition output: throttle, RPM, mixture
  and reciprocal aligned ownership. SDK/input models remain simulated.
- The Windows WASM build passes without warnings/errors. UI/controller (33),
  DOM, package and maximum native JSON checks pass. Helper/desktop build and
  helper's actual packaged-definition dry run pass. No new simulator writes have
  been performed by these tests.
- A local **Check shared output** action runs only on a complete group already
  assigned to Mock, with fresh parked/brake/AP-off observations, Auto off and no
  shared session. It requests 1% movement and restoration, with an SDK barrier
  and three fresh matching readbacks for each step. It never changes masks or
  writes after local takeover. Failure cannot certify output. Relevant hardware
  calibration changes invalidate that group's proof. All four groups must pass
  before this test build advertises remote-output capability 4. Proofs are
  in-memory per load; saved learning is unaffected. No check runs on startup.
- Native target observations now use every visual frame so parked checks and
  alignment do not consume three-frame-old positions at low simulator FPS.

Candidate, logs and installation receipt: `artifacts/definition-controls/`.
After the user confirmed MSFS closed, installation at 17:10 UTC verified all
seven payload hashes and left both saved-learning files byte-identical. The
rollback and closed-state learning backup are in
`installed-backup-0.6.9-before-0.7.0-20261116-181050-1ec8af` under that directory.
The user restarted into the parked Baron with brake set, AP and quadrant Auto
off, both groups on Me, and moved the hardware without Relearn. The live C# SDK
read-only check passes with fresh packets and the expected definition fingerprint.
All required controls are learned (`0x7e03`), with neither group blocked and Auto
off. Both hardware and cockpit mixtures report **82.8125%**. Session is Idle and
capabilities remain 27, with remote-output bit 4 off. No simulator controls were
written. Evidence: `live-validation.json` and `tests/live-0.7.0-readonly.log`.

The user ran the embedded **Mixture -> Check shared output** action and confirmed
Setup shows **1/4**. This establishes the definition-driven native LVar route's
same-state, distinct 1% movement and restoration check in the actual Baron. A
subsequent read-only SDK snapshot has the same native identity, only Mixture
blocked (`8`), Auto off and both cockpit/hardware mixtures at the original
**82.8125%**. The probe itself did not capture the movement; its restoration
snapshot and the user's embedded-check result are recorded separately in
`live-validation.json` / `tests/live-0.7.0-after-mixture.log`.

The user then confirmed the other three checks passed and Setup reached **4/4**.
A fresh read-only SDK snapshot confirms the same native instance, **capabilities
31** (remote-output bit 4 enabled), all controls back on **Me** (`Blocked=0`), Auto
off and session Idle. All required axes remain learned. The steering schema is
still `0x003` (roll/pitch): the rudder mask was previously user-tested, but rudder
output/alignment is not independently established by this four-group result.
Do not claim all optional pedal/trim axes were learned or output-tested.

The matching helper passed a bounded 5-second live startup: fresh native discovery
with capabilities 31, definitions accepted, then **WaitingForSetup**. It exited
cleanly, did not join a session, and performed no agent control writes. Logs:
`tests/live-0.7.0-after-all-checks.log` and `tests/helper-live/stdout.log`.
Current receipt: `live-validation.json`. Actual two-PC physical/world/seat
validation remains pending. The friend is available. The user was given the
verified frozen 0.7.0 ZIP to send; it includes the Community package and complete
Helper/Definitions folder, with no personal settings/learning. The friend must
install with MSFS closed, learn their own hardware and pass the four local checks.

The friend has vanilla FS Copilot installed. No deletion/uninstall is needed.
For this first shared test, keep the vanilla desktop app closed and temporarily
move its active **fscopilot-bridge** package outside Community while MSFS is
closed. Restore it later for vanilla use. Source review confirms the legacy WASM
also manages physics/freezes through FSC_CONTROL, including watchdog unfreeze;
distinct ClientData names alone do not isolate those shared simulator variables.
The new package is **fscopilot-controls-test** and both PCs should run only the
matching **Helper/FsCopilot.Efb.exe** for this test. Jorge's active Community2024
folder currently contains only fscopilot-controls-test among FS Copilot packages.
No existing files were moved/deleted by the agent during that read-only check.

The user now reports the friend passed **4/4**; no friend-side logs or direct
observations have been received. Started Jorge's matching helper for the real
shared test, PID **32532**, with working directory/logs under
`artifacts/definition-controls/tests/shared-live/` (`helper-process.json` records
the launch). It currently cannot connect: a read-only OS process check confirms
**MSFS is closed on Jorge's PC**. The pending user request is to reload the parked
Baron, repeat the four per-load output checks (saved learning remains), restore
all groups to Me and report ready. The friend should keep the existing 4/4
cockpit open. No public connection, shared Join or control write has been made.

Next connection settings can use the two lower-case eight-character codes
`jorge001` and `friend01`, each entered as the other's Friend's code. Jorge:
Host flight / Pilot-left. Friend: Join flight / Copilot-right. Both use the
existing `p2p.fscopilot.com`, port 3600; direct P2P is attempted before relay.
Do not claim the public relay supports the new sender-aware traffic; that has
not been verified. Both save settings, wait for WaitingForJoin, then Join within
the same short window while parked/AP/Auto off. First observe connection/world
state before asking either player to move controls. Actual two-PC output remains
unverified until it passes.

Logging for the first two-PC test is already enabled in the shipped build:
native `\\work\\controls-test.log` persists lifecycle, mapping load/save,
configuration, parked-output pass/failure/refusal and SDK error traces. The
helper separately writes a file named `log` (no extension) in its process working
directory via Serilog; it logs startup, discovery, backend status and transport
errors. It is normally the Helper directory when launched there, but the path is
relative, not fixed to AppContext.BaseDirectory. These are local files; there is
no automatic upload or remote access to the friend's PC, and every hardware
event is deliberately not logged. Ask for native/helper logs and exact EFB error
when diagnosing a failure. The native log from Jorge's current load was copied
to `tests/live-0.7.0-native.log`; it independently confirms all four passed
selections (8, 4, 2, 1). Receipt now includes that durable trace.

The frozen ZIP is `fscopilot-efb-0.7.0-candidate.zip` in that directory:
33,079,259 bytes, 24 payloads, SHA256
`526618C1BC40CC206B1BBEC9C23F54B06EDE1A93A141E64A7787C60DFB2B2948`.
`candidate.json` records every payload hash and `verification.json` records the
checked source hashes and limits of the offline evidence. `installation.json`
records the completed install; `backup.json` records the verified rollback and
learning backup. No simulator output test was run during installation.

Remaining broader work: ordinary vanilla cockpit/altimeter synchronization is
not yet executed by the EFB helper; its corrected YAML is available and the
separate desktop test candidate remains valid. Do not resume seat-owned barometer
work. Actual two-PC physical/world/seat validation, a verified AP input adapter
using the existing definitions, and optional GTN flight-plan exchange remain
unfinished. GTN screen interaction stays local.

### Current user direction: vanilla first; ordinary shared altimeters

The user requested an audit of all Baron-specific additions, reuse of vanilla
FS Copilot and definitions before custom aircraft code, and investigation of
the vanilla barometer bug. They then explicitly dropped pilot/copilot ownership
restrictions for altimeters if the definition bug is corrected. **This supersedes
the older seat-owned instrument plan below.** Do not add a Baron knob-mask
provider or make instrument ownership a shared-flight prerequisite.

Completed audit: [VANILLA_REUSE_AUDIT.md](FsCopilot.Efb/VANILLA_REUSE_AUDIT.md).
The literal production Baron detection/mixture names in the installed 0.6.9
headers have been replaced by the generated definition catalog in the 0.7.0
candidate above. Preserve the working explicit-unit reader and generic hardware
alignment/masking until that replacement is validated. AP/camera/pressure SDK
mechanics are largely generic. Retain necessary ownership and calibration code;
YAML does not implement hardware blocking or per-group authority.

Vanilla's actual parser reproduces the Baron's equal-value DEC bug and reverse
command after reaching a target. The PMS50 include adds another barometer writer.
Prepared workspace profile uses separate absolute indexed QNH settings and a
GTN module variant without its barometer mapping. The original shared GTN module
and all other Baron mappings are preserved. Generic core no-op fixes prevent
empty setters from falling back to a variable write or accruing echo-suppression
debt. **1,059 actual-definition checks pass**, including all 448 unaffected
entries. The desktop Release build/publish passes
with the same four pre-existing warnings. Actual two-PC symptoms/knob indices
and STD behavior are not verified by these offline checks.

The Downloads application and original desktop definitions are unchanged. A
desktop test candidate is staged in `artifacts/vanilla-reuse-audit`; it is
distinct from the EFB helper. The 0.7.0 helper now loads the physical role
selection from YAML, but ordinary cockpit definition streaming remains pending.
Native remote-output capability remains off until the explicit live checks pass.
See the audit for the legacy AP/duplicate-entry hazards.

The first reuse implementation shared `Simulation/Definitions.cs` between the
desktop, helper and definition checks, and published the YAML files. The 0.7.0
candidate now adds runtime selection, semantic roles and guarded native execution
on top of that loader. The earlier `*-shared-loader` audit builds remain frozen.

**Latest live-tested baseline: 0.6.9**, with post-restart typed mixture reads and
user-tested mixture blocking/aligned takeover confirmed without Relearn.
Its installation receipt verified all seven payload hashes. A fresh,
production-validated generation-17 profile restored
only missing mixture axes 11/14 from the pre-Relearn backup. Every other learned
axis is byte-identical to current generation 16, and the other slot is unchanged.
Receipt: `artifacts/mixture-typed-reader/installation.json`. The matching helper
remains in `artifacts/mixture-typed-reader/bundle/Helper`; it is not needed for
the local Mock test. Both 0.6.8 and 0.5.3 rollback copies remain available.
The earlier full **user-tested baseline is 0.5.3**; 0.6.8 failed mixture after restart.

The 0.5.3 visible footer Tolerance button cycles
Tight/Medium/Loose = **10/20/30%**, applied atomically to Steering and all three
engine pairs. It waits for the native reply, preserves owners and keeps the
alignment hold. Setup uses the same preset labels; legacy smaller values remain
compatible. EFB/native protocol is now **v5**; scope `all` is accepted only for
tolerance, never a global force/ownership action. The single quadrant Auto
switch and independent pair ownership from user-confirmed 0.5.1 are preserved.

0.5.3 includes the unavailable-axis bridge encoding fix described below: -10
inside the pickup implementation becomes -1 on the wire. It supersedes the
staged-only 0.5.2; **0.5.2 was never installed**. Six C++ runners, 23 controller
checks, DOM/registration/package/native-v5 status checks and 11 native/C# wire
scenarios pass. Chrome checks pass 52 layouts / 24 EFB-inset layouts with real
preset clicks/acks. C++ checks verify 15%/25%/30% boundaries, owner preservation
and the retained 300 ms hold. Windows WASM builds with zero warnings/errors.
The real SDK fixture transport verifies 10/20/30 values and expiry after its
server closes, both exits 0 and no aircraft/control writes.

All seven installed hashes, ZIP/layout and unchanged saved mappings were checked.
Build: `artifacts/native-bridge/0.5.3/`. Passed 0.5.1 and both mapping slots are
backed up at
`artifacts/native-bridge/installed-backup-0.5.1-passed-before-0.5.3-20261115-173528`.
Receipt: `artifacts/efb-control-test/verification.json`. The user restarted into
0.5.3 and confirmed the Tolerance cycle/alignment bands work. The canonical
read-only probe also passed against the actual installed WASM at the Baron:
fresh challenge/reply and three Ready observations, native `6bfe071`, two engines,
adapter `bksq-baron58p-cockpit-v1`, Auto mask 14 and blocked mask 12. Log:
`artifacts/native-bridge/installed-wasm-0.5.3-restarted-direct.log`. This required
running outside the sandbox; the sandboxed COM connection failed. No aircraft
or control writes occurred. Earlier session 34779 ended before this restart;
it is terminal. No canonical probe is currently running.

### Typed mixture reads and selective calibration recovery (0.6.9 installed; live reader passed)

The user restarted into 0.6.8 and reported full mixture travel displaying 1.0%,
with hardware dots moving but cockpit sliders not following. They pressed Relearn
and could learn other controls, but not mixture. A status-only C# probe confirms
native `6a45cf3`, API 1, capabilities 27, Me/Auto off, Idle shared session and live
AP transport with no profile. Mixture target is ~0.00464264 at roughly 46.4%
hardware travel: the raw cockpit fraction was wrongly treated as percent.
Log: `artifacts/baron-ap-input/live/0.6.8-mixture-reported.log`.

The earlier raw-100 capture was not a stable unit contract. SDK documentation
states that [SimConnect LVar definitions can change shared variable types/units](https://docs.flightsimulator.com/html/Programming_Tools/SimConnect/API_Reference/Events_And_Data/SimConnect_AddToDataDefinition.htm).
The production module now registers **only the 23 standard SimVars** in its
position frame. `control_cockpit_refs.h` looks up existing bare mixture names via
`fsVarsGetLVarId` and reads explicit **Percent** using `fsVarsLVarGet`. It never
registers, creates or writes an LVar. A missing variable/unit, read error or invalid
range remains unavailable. It does not guess the scale of values below 1.
The built WASM imports exactly the three Vars API lookup/getter functions and no
Vars registration/setter. [Vars API reference](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/wasm/vars-api/fsvarslvarget/).

Native-module and dedicated reader tests cover fraction/percent storage, full
travel, midpoint, half-percent near idle, absent variables/units and failed reads.
The production module test also switches storage representation during reads and
asserts there are no LVar SimConnect definitions. Native 37 contracts, full real
loopback regression, JS/DOM/registration/package checks and actual SDK WASM/helper
builds pass. The actual post-restart status-only probe also passes: both hardware
and cockpit mixture references read **0.48809814453125 (48.81%)**, native `689590b`,
adapter `bksq-baron58p-cockpit-v1`, two engines, Me/Auto off and Idle shared session.
Saved mixture mappings are present without Relearn. Logs:
`artifacts/mixture-typed-reader/tests/live-0.6.9-mixture-50.log` and
`live-0.6.9-mixture-50-confirmed.log` in that tests directory. No controls or
ownership were written by these probes. Package/helper version: **0.6.9**.

Current generation-16 profile is checksum-valid but its two mixture mappings are
missing after Relearn. `recover_mixture_profile.cpp` uses the production profile
reader/writer to fill only missing mixture entries from the valid pre-Relearn
generation-11 backup. It preserves every other mapping and any already learned
mixture. Its tests pass; a workspace-only preview produces generation 17, mask
18432 (axes 11/14), without changing either source. **Regenerate from current
files with MSFS closed before copying the resulting slot**; do not apply a stale
preview. After MSFS closed, recovery was regenerated from the actual current
files and generation 17 slot 1 was installed and hash-verified. Only the missing
mixtures changed; the other axes/slot were verified unchanged. The restarted
live probe confirms learned mask `0x6c03`, including both restored mixtures.
Throttle mappings are still missing (bits 9/12); do not require a whole-quadrant
aligned takeover until those are learned. The completed user check selected only
Mixture under Advanced engine controls, Auto off: Mock must block cockpit lever
movement, then Me must release after alignment without Relearn. Mixture's scoped
Force takeover is available if needed. The user answered **"Both blocking and
aligned takeover work"**. This passes local mixture pickup on 0.6.9; it does not
validate shared-flight output.

Three bounded standalone mixture-output checks failed and are not shared-flight
validation. The normal AXIS route and diagnostic-only `MIXTURE1_SET/2_SET` route
both kept the original 48.81% readout rather than producing a distinct 1% nudge;
same-state/original readback and removal of their own masks passed, with no SDK
exceptions. A priority experiment aborted on its own output echo during the
same-state step; mask removal passed, but original readback was not confirmed.
The unproven priority option was removed from production source and the probe;
the default output priority is unchanged. The installed 0.6.9 package was never
modified by these experiments. All 23 diagnostic guard/fake cases pass after
cleanup. Logs are `tests/live-0.6.9-mixture-output.log`,
`tests/live-0.6.9-mixture-set-output.log`, and
`tests/live-0.6.9-mixture-set-priority.log` under `artifacts/mixture-typed-reader`.
**Remote physical output remains disabled**. Resolve the aircraft's output route
before enabling it; local pickup success alone does not establish remote output.

Follow-up routing investigation: the SDK's documented **STANDARD** priority
passes a private-event isolation test against the live Baron (mask receives no
STANDARD output, sink receives it, zero SDK errors). The earlier mask+1 guess
cannot be used because SimConnect assigns a different slot when a requested
priority is occupied, across clients. The SDK output adapter now accepts only
DEFAULT or STANDARD; its production default remains DEFAULT, and only the
development probe opts into STANDARD. Unknown/adjacent priorities are rejected.
26 guard/SDK-fake scenarios cover the new option, ignored output and self-echo.
The subsequent bounded `MIXTURE1_SET/2_SET` STANDARD-priority check still fails
distinct 1% movement; same-state/original readback and mask removal pass with
zero SDK errors or hardware interference. Logs and receipt:
`artifacts/mixture-output-route/`. No new package was installed.

Output-plan correction after the user asked about vanilla definitions: **reuse
the existing aircraft definitions through a common adapter**, rather than adding
a new hardcoded Baron-only writer. The installed vanilla distribution at
`C:/Users/jorge/Downloads/FsCopilot_v1.2.1/Definitions/bksq-aircraft-baronpropress.yaml`
already defines `L:BKSQ_MixtureLeverPosition_1, Percent` (line 48) and engine 2
(line 51) under `master`, alongside generic engine mixture variables. With no
explicit `set`, `Definition.Set` writes the same variable and units; vanilla's
Coordinator receives those values and calls SimClient's setter. Thus vanilla
already has the intended cockpit-mixture route in configuration. Actual vanilla
two-PC mixture behavior was not retested, so this is code/config evidence, not
a new live pass. The generic-event diagnostics did not exercise that route.

The standalone EFB helper currently constructs NativeSharedSessionService and
does not load the vanilla Definitions/Coordinator. Its project also does not
package those aircraft YAML files; this repository's Definitions directory has
only shared modules, while the downloaded vanilla folder has aircraft profiles.
The original Me/Mock test intentionally avoided profiles, but shared flight
must restore aircraft mapping support. Do not start the old Coordinator alongside
native authority: its global master/shared writers could bypass group ownership.

Next: introduce a reusable mapping adapter that can use the existing definitions,
with explicit attribution to Steering, Throttle, RPM and Mixture and normalized
units. Choose the verified cockpit lever reference for custom mixtures, and
preserve per-group owner/session/generation checks at execution. Handle legacy
duplicate generic/cockpit entries deliberately, rather than writing both blindly.
Keep unsupported mappings unavailable and validate each aircraft profile. Native
typed Vars lookup/set remains a possible LVar execution backend, not a separate
Baron-only configuration system. Then prove actual movement/restoration before
enabling remote output. Do not infer success from SDK acceptance alone.
[Publisher-supported mixture interfaces](https://support.justflight.com/support/solutions/articles/17000132376-why-does-the-mixture-behave-strangely-in-the-turbocharged-version-and-i-cannot-bind-it-to-hardware-c),
[SDK priority assignment](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_API_Reference.htm),
[typed native setter](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/wasm/vars-api/fsvarslvarset/).
This adapter is a next step, not an implemented or verified feature. Evidence:
`artifacts/definition-output-review/verification.json`. No further user hardware
test is pending at this checkpoint.

The external AP capture no longer registers any LVars, and `simulator_targets`
reads only standard SimVars. Do not rerun old artifact mixture-unit probes during
validation: their registrations can affect the variable being diagnosed.
The 0.6.9 ZIP and hashes are in `artifacts/mixture-typed-reader/candidate.json`;
current source/test/recovery evidence is in that folder's `verification.json`.

### Earlier live output checks and mixture percent correction (0.6.8; failed mixture restart test)

Actual parked-Baron SDK checks now pass for **throttle and RPM**: same-state
write, distinct 1% movement, original-position readback, and removal of the
diagnostic's own temporary mask. No physics or native ownership was changed.
Remote physical-output bit 4 stays off: the embedded shared-session path,
mixture/steering output and actual two-PC flight are not established by these
standalone checks. Logs are in `artifacts/baron-ap-input/live/*-movement.log`.

The initial throttle diagnostic rejected continuous combined-axis chatter.
Tracing distinguishes `THROTTLE_AXIS_SET_EX1` raw -9622/-9492 from the separate
per-engine output events. The diagnostic now tolerates only captured events on
the saved source, within **0.45% of the initial hardware pose**. Output event
names never qualify. The limit is below the minimum distinct movement readback;
unknown routes, larger/drifting input and self-echo remain failures. Guard and
14 SDK-fake scenarios, including continuous jitter and later drift, pass.

The mixture mapping **is stored** in the actual valid generation-11 profile:
axes 11/14 both use `AXIS_MIXTURE_SET`, scale bit 1. Installed 0.5.3 instead rejects
the cockpit readout because `control_targets.h` assumed a 0..1 LVar. Current live
`BKSQ_MixtureLeverPosition_1/2` values are **100**, including when requested using
Number, Percent and Percent Over 100. Generic engine mixture reads ~82.38588%,
so it remains the wrong cockpit reference. Source now keeps the raw 0..100
percent value, validates that range, and does not guess a fraction for values
below 1. A 0.5 value means 0.5%, not half travel. Saved calibrations are untouched.
The percent interpretation is also described in [Black Square support](https://support.justflight.com/support/solutions/articles/17000132376-why-does-the-mixture-behave-strangely-in-the-turbocharged-version-and-i-cannot-bind-it-to-hardware-c).
This supersedes earlier normalized-LVar assumptions in the historical notes/tests.

`guidance_input_capture.cpp` is a bounded passive diagnostic: non-masking key
notifications, AP/cockpit observations and relevant InputEvent subscriptions only.
A three-minute capture passed with **7,454 samples and zero SDK errors**. It
enumerated 127 InputEvents and found `INSTRUMENT_HEADING_BUG`, but recorded no
button/knob interactions; actual AP input routes remain unverified. The original
three-minute user prompt is now expired. Start a new capture only when the user
is ready. [Black Square's explanation](https://community.justflight.com/topic/9399/rpn-code-for-the-up-dn-rocker-kfc-150-autopilot)
shows the UP/DN rocker also needs pitch-reference coverage, beyond the existing
heading/altitude/VS command set, before certifying a complete AP input provider.

The AP integration test now forces a lease packet to overtake a queued disconnect.
The send gate checks the observed native command counter, and the Windows SDK
worker also tracks commands it has already sent, so a known obsolete packet is
dropped before SDK and retried under the original authority/deadline. Rejected
reference/input acknowledgements no longer overwrite an SDK value acknowledgement.
The 0.5.3 rollback copy and staged 0.6.7 ZIP are retained. Installed 0.6.8 and
current validation are recorded in `artifacts/baron-ap-input/verification.json`.
The user confirmed MSFS closed, and installation verified all seven package
files plus unchanged saved learning. They have been asked to restart parked,
leave Me/Auto off and move the hardware mixture to about 50% for a read-only
check without Relearn. The earlier AP action reply arrived after its capture
window, so no AP route validation is claimed.

The read-only C# probe now accepts `--observe-session`, which sends neither
session nor AP commands and waits for current native session/advertised AP status.
It prints both cockpit and hardware mixture values plus learned/fresh masks.
The standalone output diagnostic now also recognizes the API-1 package only
with a fresh, matching-native **Idle/unbound** session observation, no consent,
owners or held world freeze, and remote-output capability still off. Guard and
22 SDK-fake scenarios cover admission, armed/active refusal, missing/stale/wrong
native/frozen status and losing the unbound context during output. No new live
write has yet run against 0.6.8. Receipt for these checks:
`artifacts/baron-ap-input/tests/output-local-session-regression.log`.

### Windows AP client and shared-session coordinator (preceding source increment)

Current development connects the native AP channel to `NativeGuidanceClient`
and `NativeGuidanceCoordinator`. The Windows client uses a separate SimConnect
connection, bounded queues and current native/authority checks immediately before
SDK sends. AP failures cannot close a healthy physical-control transport. The
coordinator runs on the shared-session actor and preserves the fixed physics
host as the only AP SDK writer. AP command ownership is independent of seats,
physical groups and navigation-source ownership. Navigation transfer is still
unavailable without an actual verified source adapter.

Guidance authority protocol **v2** negotiates the intersection of supported
settings from both adapters. The generic native writer supports bits 0..11
(4095), with numeric settings further restricted by the selected target slots;
FD output remains unsupported. New discovery capability **16** means AP transport
only. Source advertises **27** when that transport is available, with physical
remote-output bit **4 still off**. Shared-flight startup checks the required
physical capability bits independently; adding or losing the optional AP bit
cannot by itself change physical-session eligibility.

Captured AP inputs travel as attributed absolute intents. Native input capture
uses a bounded, acknowledged FIFO and a logical authoritative reference including
the current pending target. This lets a second AP press request Off while a
previous On is still queued, even though the guest's own AP engine remains Off.
Reference packets do not write simulator state or renew leases. The SDK worker
re-enters the host model fence immediately before a write (model lock before
transport lock), so a retired On cannot survive a later disconnect. Raw samples
and gate proofs cannot complete queued outputs: explicit native SDK completion
plus fresh matching readback is required.

Native AP lease refresh now preserves an asynchronous input-interception request;
it no longer restarts the adapter each refresh. Independent proof loss still
holds AP. FGA1/v1 remains a separate 512-byte area: adapter identity occupies
409..472, reference acknowledgement 473..476, and the reserved tail is 477..511.
Actions 6/7 acknowledge captured input and update its logical reference.

The installed 0.5.3 package and staged 0.6.7 ZIP remain unchanged. The Baron still
has **no verified production AP input provider**, so no AP masks or shared AP
writers can start there. EFB AP claim/recovery controls, actual Baron input-route
validation, remote physical-output validation and two-PC flight remain required.
GTN pages/touches remain local; flight-plan-only exchange is still unimplemented.

The user restarted and parked the Baron. A ten-second actual SDK read-only check
passed with **162 samples**, identified `bksq-aircraft-baronpropress`, and observed
PMS50 GTN running with version value 2002064. It performed no aircraft/control
writes and does not prove AP input interception or GTN/WTT navigation transfer.
Log: `artifacts/guidance-helper/tests/baron-guidance-readonly.csv`.
The freshly published C# probe also passed a fresh discovery challenge and three
current status packets against installed 0.5.3 (`711d3c9`, capabilities 11,
blocked 0, Auto 0). No session commands or aircraft/control writes were sent.
Log: `artifacts/guidance-helper/tests/baron-helper-readonly.log`.

Current validation and build receipt: `artifacts/guidance-helper/verification.json`.
The ownership runner passes **89 scenarios** and the native runner **37 contracts**.
The AP relay integration uses actual local relay clients and both production C++
controllers with simulated SDK/input providers; no simulator is used by that test.
The full loopback/native regression passes with the AP integration included in
the default runner. Actual SDK WASM rebuild, Windows helper/probe publication and
the helper's explicit dry run pass. Installed package and staged ZIP hashes match
their earlier receipts. Next: actual aircraft AP input routes/provider and EFB
AP controls, plus the parked remote physical-output check and two-PC validation.

### Earlier fenced native AP command/status channel (source only; not installed)

The actual WASM now creates separate **512-byte FGA1/v1** AP command/status areas:
`FSC_CONTROLS_GUIDANCE_COMMAND_V1` and `FSC_CONTROLS_GUIDANCE_STATUS_V1`.
`control_guidance_protocol.h` and `NativeGuidanceWire.cs` define the same canonical
wire format. The separate transport cannot replace the existing steering/world
status or its acknowledgements. AP SDK transport failures stop AP work without
changing physical group ownership or closing a healthy physical session.

`control_session` validates the native/backend/session/consent binding, native
epoch, current physical revision and immutable physics role before accepting an
AP command. A guest also requires its owned world freeze. Queued steering-owner
AP disconnects carry steering owner/generation and are rechecked immediately
before SDK dispatch. AP commands have a counter independent of the physical
channel; neither AP commands nor polls renew the physical lease. An AP gate also
expires independently after one second, cannot be revived by retained backend
packets, and requires fresh local recovery/join after a hold.

`control_guidance_channel.h` requires an actual aircraft `guidance_input_gate`
provider. Its proof covers all input routes, native suppression/capture and a
single authoritative AP engine; a backend declaration supplies none of these.
Gate completion requires provider confirmation and three later AP samples.
Gate and value jobs have independent acknowledgements, scope/sequence fences and
bounded deadlines. SDK confirmation remains checkpoint plus three later matching
readbacks. Source verification is rechecked before queued NAV/APR or AP-on into
an armed navigation mode can reach SDK output. The authority model also refuses
AP-on when navigation remains armed without a verified source.

The status reports a supported-setting mask separately from verified input
routes. The generic writer supports settings 0..11 subject to matching target
slots; FD output remains unsupported. Gate readiness and supported settings are
separate from verified NAV readiness and its 64-character source fingerprint.
The future helper/coordinator must use those actual native fields rather than
treating a coarse command capability as proof of every setting or NAV support.
Preparation on this native wire is an opaque increasing local token; the helper
still needs to map the ownership model's preparation GUID to that token.

AP holds cancel queued work while retaining input masks. Full local recovery
waits for queued SDK work to drain, releases known AP masks through the provider,
then requires three later samples. Known mask release remains possible after a
profile loses verification. A local-only latch rejects delayed backend AP work;
only fresh local join consent clears it. No AP write or input mask is enabled in
the default Baron: production supplies no verified provider. The Windows helper
does not yet read/send this optional channel, and no AP discovery capability or
EFB AP action is advertised. The existing physical/instrument wire sizes and
installed capabilities remain unchanged.

Offsets: binding strings 16/40/80/120; native epoch 160, physical revision 168,
AP command 176; command/navigation epochs 184/192, preparation 200, guidance
revision 208, value sequence 216; action/setting 224/228, value 232,
capture/physics role 240/244, reader serial 248; gate/value acks 256/260,
status serial 264; flags/support/masks/slots/modes 272..292, targets 296..312,
GPS/radio/error 320/324/328; steering generation/owner 332/340, NAV fingerprint
344..408, zero reserved tail 409..511. String tails and booleans are canonical.

Validation: **87 ownership/session cases**, **37 native contracts**, production
C++ AP channel/SDK tests, reciprocal C++/C# binary fixtures, native maximum JSON
parsing and the full real loopback relay/native regression pass. Tests cover
no-provider refusal through the actual WASM dispatcher, isolated AP SDK failure,
readback, replay/scope rejection, lost NAV source before dispatch, late steering
ownership change, independent leases and local recovery. Input routes and SDK
are simulated. Windows WASM and helper build with zero warnings/errors. Receipt:
`artifacts/guidance-channel/verification.json`.

Installed 0.5.3 and the staged 0.6.7 ZIP remain unchanged; no live SDK AP capture
or writes occurred. MSFS is closed. Next: optional Windows AP client and native
guidance coordinator (including supported-setting negotiation), then EFB claims;
actual Baron input/source validation, remote physical output and two-PC flight
checks remain required before completion.

### Earlier native AP reader/SDK increment (source only; not installed)

`control_guidance.h` now provides the native AP reader and SDK output mechanics.
At this earlier increment, the WASM initialized only its read path, dispatched its samples and
exceptions independently of physical control ownership, and publishes validated
AP observations in EFB status. These include raw mode switches, indexed heading/
altitude/vertical-speed targets, selected slots, GPS/radio source and a freshness
serial. `outputAvailable` stays false. The newer fenced channel above supplies
the native command path; no AP input masking or AP UI action is enabled. Shared AP startup still
requires the missing verified aircraft input/output adapter.

The SDK mechanics use explicit on/off events and absolute indexed targets.
Heading uses EX1 precision with a circular 0.012-degree readback tolerance; the
C# authority now uses that same tolerance, including north wrap. Numeric output
and confirmation refuse a selected slot different from the configured observed
slot; current production observations explicitly watch slot 1. A profile must
verify the actual Baron slots before command support is enabled.

Output confirmation requires an SDK checkpoint and three later matching samples.
Superseding queued work preserves its checkpoint/error tracking and requires a
later observation before an ordinary replacement. An explicit AP disconnect can
follow a queued AP-on immediately, including after supersession; it emits Off
even when the cached state still says Off. A second Off cannot duplicate that
queued output. Stale/malformed readings, changed slots, SDK errors and ignored
commands cannot produce successful confirmation. The generic FD route documented
by the SDK is a toggle; this adapter implements no FD setter and refuses FD
output rather than replaying a toggle. Owner-side observation can still confirm
the actual FD state. [SDK AP events](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-autopilot-and-flight-assistance-events/).

The installed Baron is streamed at
`Packages/StreamedPackages/fs24-justflight-aircraft-baronpro`; the relevant cockpit
code is packaged in `.fsarchive` files. Readable installed files did not establish
the complete input routes. The legacy YAML contains AP toggle writers; it is not
proof of the current aircraft routes and must not run alongside the new shared
authority. No streamed aircraft files or GTN files were modified.

Validation for this increment: Windows WASM and helper build with zero warnings/
errors; **86 ownership/session cases** (22 guidance cases), **31 native contracts**,
the new strict C++ guidance SDK checks, **32 JavaScript checks**, actual native
maximum JSON parsing, DOM and package checks pass. The isolated real AP relay
case also passes. SDK/input-gate proofs are simulated. The prior full relay/
native regression is recorded in `artifacts/flight-guidance/`; this increment's
logs and source receipt are in `artifacts/guidance-native/verification.json`.
Installed 0.5.3 and the staged 0.6.7 ZIP remain unchanged. No new candidate was
installed and no live SDK capture or AP output test was run. MSFS is closed.
Next: actual Baron/PMS50 observation and verified input routes, then the fenced
native guidance command/status transport and EFB integration. Native remote
control output and two-PC flight checks remain required for the full mod.

### AP/guidance authority development (2026-11-16; source only, not installed)

The ownership model now has separate **AP commander** and **navigation source**
roles, both initially held by the physics host. Commands are absolute targets or
explicit on/off requests; GTN touches and rotary increments are not replicated.
A command-owner claim masks/captures the old owner's input before attaching the
successor. It preserves the physics host, physical groups, seats and altimeter
revision. The steering owner can request AP disconnect with the current steering
generation; disconnect retires queued AP-on work and waits for actual off
readback. SDK dispatch failure, stale input proof or lost readback holds guidance
without changing physical group ownership.

`GuidanceProtocol.cs`, `ControlAuthorityGuidance.cs` and
`GuidanceAuthorityReplica.cs` provide the policy and validated snapshots.
Attributed wire messages and `ControlAuthorityNetworkHost` publish the separate
stream. Both peers must prove input suppression, a matching command adapter and
a single host AP engine before activation. Network declarations are not evidence
of actual Baron input blocking. Production does **not** start this policy yet:
there is no verified native AP input/output adapter, EFB AP control or GTN
flight-plan exchange. Installed 0.5.3 and the staged 0.6.7 ZIP remain unchanged.

Navigation transfer is separately capability gated, initially requires AP off,
and commits only after the host applies the requested source and the new owner
confirms the same route fingerprint. A fingerprint identifies state; it does not
implement a complete route transfer. Cancelling after route application or with
pending output holds guidance instead of claiming a successful rollback. NAV/APR
cannot arm without an authoritative ready source. Without a verified transfer
adapter, either pilot may claim AP commands while the physics host retains NAV/
APR guidance. GTN screen pages stay local.

The installed PMS50 package was found in `Packages/Community/`, not
`Community2024/`; its manifest reports **2.2.64**. PMS50's current integration guide
v1.58 documents WTT external-source integration with a local master instrument.
It does not establish a complete cross-PC snapshot/resync interface; treating it
as one would be an inference. Investigate complete flight-plan export/import,
active leg, procedures, approach and missed-approach state before advertising
flight-plan-only synchronization. [PMS50 integrator guide](https://pms50.com/fs2020/gtn750/integration.pdf).
The SDK's `FlightPlanLoad` loads a `.PLN`; it does not prove a custom GTN/WTT
import or complete readback. [SDK FlightPlanLoad](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/flights/simconnect_flightplanload/).

Validation: **85 ownership/session scenarios**, including 21 new guidance cases,
pass. The full real loopback relay/native regression passes, including a new AP
commander handover, absolute target/readback, stale-owner rejection and scoped
stop. Avionics/gate proofs in the AP network case are explicit test fixtures;
this does not validate the Baron AP. The Windows helper builds with zero
warnings/errors. `FsCopilot.Network.Tests/run.ps1 -GuidanceOnly` runs the isolated
relay case without building a native harness.

A strict actual-SDK read-only diagnostic is ready at
`artifacts/flight-guidance/live/guidance_state_probe.exe --seconds=30` (1..120).
It observes AP modes, slot-1 targets, selected slots/radio source and GTN runtime
variables. No input masks, events, aircraft writes or flight-plan loads occur.
Variable references: [SDK AP variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-autopilot-and-assistant-variables/).
The SDK build and invalid-duration rejection pass; **no live capture was run**
because MSFS is closed. Next: capture actual Baron/PMS50 behavior, then implement
and verify the native input/output adapter before exposing AP claims. The pending
remote-output and two-PC checks remain required. Logs and receipt:
`artifacts/flight-guidance/verification.json`.

### EFB instrument status and local recovery (0.6.7 candidate; not installed)

Setup now shows separate instrument readings, confirmed seat owners, actual
native input state and synchronization/recovery status. Me/other owner rows use
the existing pastel colors. Unsupported mappings keep neutral indexed labels.
Shared status requires both a fresh backend declaration and matching native
profile/gate/input proof; a declaration alone cannot show Shared or released
inputs. Actual Chrome input tests and 420x560 screenshots cover Held, Releasing
and Local in the scrollable Setup panel. Installed 0.5.3 remains unchanged.

**Use local altimeters** is a direct EFB CommBus action scoped to the displayed
shared-session GUID. It stops only instrument work, waits for queued SDK output
and native input-release/read-back confirmation, and leaves physical ownership
and physics untouched. An input adapter may release its known masks after its
aircraft tuning profile becomes unsupported; pressure writes remain forbidden.
Recovery failure leaves inputs held with an explicit retry state. A local-only
native latch rejects delayed backend pressure/gate commands until a fresh join.

Instrument protocol v3 adds explicit Local policy: either attributed member can
retire instrument synchronization without ending the shared flight. Pending
pressures cannot commit or revive afterward. Confirmed seat changes continue
with local instruments, independently of physical hosting and control groups.
The other pilot retains their own held inputs until choosing local recovery.
Local pressure changes then stay local on each simulator.

New native kind 13 carries descriptive instrument UI feedback in a typed
96-byte command body. It is binding/control/seat fenced, expires after one
second, and cannot change input masks or renew the physical-control lease/proof.
Native discovery independently reports local-only, recovering, held and input
proof flags (64/128/256/512). These describe actual native state separately from
the backend's requested phase. The helper and WASM must be kept together.

Checks: 64 ownership/session scenarios, 31 native contracts, 31 JavaScript
scenarios, DOM checks, real Chrome layouts/insets and input clicks, strict C++
session/SDK cases, and the full actual loopback relay suite. The integrated
native test explicitly loses its tuning profile, uses instrument-only recovery
while flight controls remain Active, swaps seats with Local policy and verifies
that local knobs no longer echo. SDK/input providers remain simulated.
Receipt: `artifacts/instrument-ui/verification.json`.

Production still has no verified Baron knob input provider and leaves
instrument sync unavailable. Actual Baron pressure/rotary/camera mapping,
remote native output, two-PC flight and AP/NAV/APR/GTN work remain on the master
plan. No real simulator or installed package writes were made for this increment.

### Backend/native instrument coordinator (0.6.6; not installed)

`NativeAltimeterCoordinator` now runs on the physical coordinator's actor through
`NativeControlAuthorityBridge`. Every native command uses
`NativeAuthorityCoordinator.QueueBarometer` and its existing global counter.
Attributed availability messages require instrument protocol v2 and matching
native adapter IDs on both peers before any startup masking. Production still
has no verified Baron instrument adapter, reports transport only, and does not
start instrument authority. Installed 0.5.3 remains unchanged.

The host first acquires both native input masks and seeds authority from fresh
read-back. Both peers seed/confirm both indexed pressures behind mask 3. Protocol
v2 then enters Attaching before Active: owners' inputs can be released only after
both masked baselines have been proven. A new owner's movement while its final
Active state is delayed is accepted and preserved, rather than overwritten by
the old baseline. The deterministic relay test delays this exact state exchange.

Host-own input uses native Observe; remote-owner input uses indexed absolute
Apply, and the guest mirrors only its non-owned instrument. QNH and STD remain
separate. Pressure activity does not change physical-control revisions. Host
dispatch is claimed under the model lock. A proven pre-execution queue drop
uses a separate retry fence, which rejects retired pending values and preserves
the original three-second deadline. Native acks and pressure observations may
arrive separately; confirmation waits for fresh matching pressure data.

Adapter loss holds only instruments. Explicit session leave plus one scoped
Me/Force action now waits for both native instrument-input and prior-physics
read-back, then releases only the requested hardware group. Production
instrument-only recovery/status UI remains unfinished; do not activate a Baron
profile until that flow and all actual knob routes are verified.
Native Stop cancels pressure work that has not reached the SDK, preserves both
input holds and prevents backend packets from restarting held instrument work.

Checks: 63 ownership/session scenarios, 29 native contracts, strict C++ native
session/SDK/world/connection tests, and the full real loopback relay/client suite.
New end-to-end cases use both actual C++ controllers/SDK adapters with explicitly
simulated input providers. They cover dropped queue work, delayed seat activation,
QNH/STD preservation, initial physics-host seating on either side, unsupported
peer refusal before masking, adapter-loss isolation and one-click scoped recovery.
These do not establish actual Baron input masking or two-PC behavior.
Receipt: `artifacts/altimeter-coordinator/verification.json`.

Startup diagnostics also reproduced an initial empty peer notification ending a
newly connected guest before its first bind. The bridge now distinguishes that
initial collection from a disconnect after confirmed host presence. The native
relay test deliberately prepends an empty peer collection; existing heartbeat,
world expiry and later disconnect behavior remain bounded. This is a backend
notification race, not evidence about earlier real SDK connection failures.

Next: expose instrument status and instrument-only local recovery in the EFB,
verify the actual Baron indexed pressures and all custom knob routes, then
complete the pending native remote-output and two-PC checks. AP/NAV/APR guidance
authority, local GTN policy and flight-plan-only options remain on the master plan.

### Native altimeter RPC integration (0.6.5; not installed)

Typed Gate/Seed/Apply/Observe/Stop instrument commands now run through the bound
native session, with independent packed gate/value acknowledgements in the
existing 1024-byte wire. Native validation checks consent, session, control
revision, seat revision, instrument epoch, preparation and per-index sequence.
Instrument traffic cannot renew the physical-control lease or replace handover
proofs. The Windows writer checks the current native seat and adapter metadata
again after queueing. Production supplies no verified Baron input-gate provider,
so it advertises transport only and refuses instrument writes as unsupported.
Installed 0.5.3 remains unchanged.

Focused C# contracts and strict C++ tests cover cross-language bytes, initial
masked baselines, per-seat indexed ownership, sequence replay, simultaneous
gate/value work and stale completions. Actual native-session tests also cover
an epoch/seat change only behind both input masks, expired traffic, held-input
rejoin refusal and explicit recovery with fresh input/pressure read-back. Losing
the provider during recovery restores the hold. These input proofs are simulated.
The SDK adapter now requires a fresh
pressure sample after an older write checkpoint before starting a replacement;
a regression returns to the original pressure without mistaking cached data for
an equal-state no-op. These use simulated SDK calls, not actual Baron controls.

Next: add the backend instrument coordinator using the existing shared command
counter and connect attributed authority requests to native acknowledgements.
Production has no verified Baron input-gate provider. Actual
knob mapping, simulator validation and two-PC checks remain required before
enabling synchronization. Instrument recovery currently participates in explicit
session recovery; an instrument-only recovery UI remains to be connected before
activation. Working-check receipt:
`artifacts/altimeter-native-command/verification.json`.

### Seat-owned altimeter authority core (0.6.4; not installed)

The **0.6.4 development candidate** added the authority core, separate from
installed **0.5.3**. Authority protocol is **v5** (the existing EFB command protocol
remains v5). `ControlAuthorityAltimeters.cs` shares the control session's lock:
left/index 1 follows the pilot seat, right/index 2 the copilot seat, independently
of physics hosting, steering, quadrant and AP ownership. Actual Baron index and
input-route verification is still pending; this logical mapping is not live proof.

Instrument state has its own revision stream. Tuning a pressure does not advance
the control/world revision. A host seed becomes active only after both peers'
matching adapter, input-mask and pressure-baseline proofs. Settings retain tuned
QNH in 1/16 mbar and STD separately. Requests carry session, owner epoch,
preparation token and per-instrument sequence. Old-owner, duplicate, cancelled-
preparation and unauthorized requests are rejected; rapid inputs coalesce.
Only a fresh host read-back commits a pending value. Dispatch is claimed once
before the SDK callback; a partial/throwing callback holds instrument authority
without silently ending or transferring physical flight-control ownership.

An accepted seat swap now waits when barometer sync is active: it drains accepted
pressure work, requires both native input gates to mask both knobs, and requires
a fresh host pressure snapshot. Only then are both seats committed, preserving
each pressure and STD setting. New owners wait for new baseline/gate proofs.
Cancellation changes the preparation token so retired frames/proofs cannot
revive. Cached gate samples do not renew readiness; sample serials must advance.
Instrument gate proofs expire after one second, preparation after six seconds,
and pending pressure requests after three seconds. These are model deadlines,
not evidence of real simulator timing.

`ControlAuthorityNetworkHost` publishes control and instrument snapshots from one
locked capture, including close/recovery paths. `AuthorityAltimeter*` messages
and `AltimeterAuthorityReplica` provide attributed request/confirmation/state
transport. The real loopback test exercises old/new seat owners and preserved
QNH/STD, with explicit simulated native gate/completion proofs. It is not a
native barometer end-to-end test. Production does not call StartAltimeters yet;
no production native/UI command calls the SDK barometer setter. Setup continues
to report local altimeters and synchronization unavailable. Capability 4 stays
off (production capabilities 11).

SDK mechanics now include **observe-only** confirmation of an owner's existing
local movement, plus superseding a pending read-back without bypassing queued
SDK checkpoints or clearing failures. This is essential: do not write an older
host request back onto the host owner's knob, or mirror old canonical state over
a guest owner's newer local input. Initial seeding may apply canonical values
only with all instrument input masked. Current SDK functions remain exposed only
to tests, pending native command/gate integration.

The EFB shows accepted seat swaps as waiting (native SeatPending 3/4), with scoped
cancel/decline. Seat graphics stay at the confirmed side. Shared JSON now includes
the actual shared-session ID and monotonic native status serial. The controller
rejects delayed seat displays across stop/rejoin and seat-revision rollback.

Validation: 59 ownership/session scenarios, 25 native contracts, 29 JS checks,
strict C++ session/native/world/connection/altimeter runners, current native JSON,
DOM/package and actual Chrome checks pass. The full actual loopback suite also
passes legacy compatibility, physical native control/Auto/world/loss paths and
the new instrument-authority transport case. No startup timeout occurred in this
increment; the earlier 0.6.3 timeout remains unexplained, not fixed by this work.
WASM and matched helper builds pass. Artifacts/receipt:
`artifacts/altimeter-authority/verification.json`; staged bundle:
`artifacts/altimeter-authority/fscopilot-efb-0.6.4-candidate.zip`.

Next implement the consent-bound native instrument command/ack path and sidecar
coordinator on the existing bridge actor, using the shared command-ID allocator.
Keep its acknowledgements separate from control-mask proofs. Use observe-only
for the physics host's owned input, absolute apply for a remote-owned instrument,
and seed only behind confirmed input masks. Supersede old pending SDK waits when
newer owner input arrives. Add actual per-knob profile/gate proof, hold/recovery
feedback and the full C++ module/relay test before enabling production writes.
Then verify actual Baron indices/STD/independence, output/camera and two-PC flight.
No new package is installed; the simulator restart response remains pending.

### Indexed altimeter SDK layer (preceding source; not installed)

This preceding increment produced a **0.6.3 development candidate**, separate from installed
**0.5.3**. `control_altimeters.h` reads indexed **tuned QNH** using `KOHLSMAN
SETTING MB EX1:1/2`, plus each index's STD flag. The usual MB variable returns
standard pressure in STD mode; it cannot preserve the underlying tuned pressure.
The read-only capture now records both MB variants, both STD flags, both Baron
altimeter LVars and four camera variables (12 values). Actual Baron index/knob
identification remains unverified.

The native observation packet's former last 24 reserved bytes now carry two
pressures in **1/16 mbar**, a two-bit STD mask, a fresh flag and a separate sample
serial. C++ and C# validate the same ranges and reject observation data on Hello
or backend UI-report packets. This is read-only metadata and grants no authority.
The real module/fixture-to-C# test preserves different pressures and STD flags
without producing simulator events. Setup shows the two readings under
**Altimeters · local**, explicitly reporting synchronization unavailable.

The SDK adapter also implements an absolute indexed setter: no INC/DEC replay,
no event on equal state, explicit index parameters, retained tuned QNH separate
from STD, an SDK checkpoint and three fresh matching read-backs. Failed, ignored
or wrong-index-only output cannot report success or repeat commands indefinitely.
Partial SDK failures stop the operation. This setter is tested with local SDK
implementations **but is not exposed by any production session/UI command**.
Production uses the reader only. Capability 4 remains off (capabilities 11).
Requested-index read-back does not prove that an aircraft leaves its other index
unchanged; the actual Baron must pass that separate independence check.

Do not call the barometer issue fixed: seat-owned instrument authority, confirmed
state replication, stale-owner/write fencing, preservation across seat swaps and
verified per-knob local input gates still need integration. Never enable the
setter directly from raw peer observations. Common INC/DEC events do not prove
which Baron knob was used, and no live custom knob/InputEvent route is verified.
The existing legacy/YAML relative barometer routes are unchanged; the dedicated
helper continues to exclude those legacy writers.

Validation: strict C++ altimeter/native/session/world/connection runners, 25
native contracts, 27 JS checks, DOM/package/current maximum native JSON and actual
Chrome checks pass. The native/relay rerun passes observation transport, seat/
Auto/control regressions, heartbeat/world-stream expiry, startup and connection
recovery. Its first attempt timed out during heartbeat-fixture initial attachment;
the preserved log is `artifacts/altimeter-sdk/tests/native-network-first-timeout.log`.
The rerun passed after adding host/peer/native diagnostics; no root cause or
production timeout fix is claimed. Keep this as an unresolved startup concern.

The WASM, matched 0.6.3 helper and real-SDK 12-variable capture build successfully.
No simulator, installed package or saved control profiles were written. Latest
receipt: `artifacts/altimeter-sdk/verification.json`; staged bundle:
`artifacts/altimeter-sdk/fscopilot-efb-0.6.3-candidate.zip`. New module/helper must
be kept together because older helpers reject nonzero formerly reserved bytes.
The simulator restart response remains pending. Next integrate the seat-owned
barometer transaction path with explicit native confirmation, then verify the
actual Baron references/input gates and proceed with live output/two-PC checks.

SDK evidence: [pressure and STD variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-system-variables/),
[indexed absolute pressure/STD events](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-instrumentation-events/).

### Seat requests and local camera selection (preceding source; not installed)

This preceding increment produced a **0.6.2 candidate**, separate from installed **0.5.3**. Shared
Setup offers **Swap seats**; the other player accepts or declines, and the
requester can cancel. Requests expire after 15 seconds. The physics host confirms
both seat assignments together. Neither a seat request nor a committed swap
changes physics hosting, group owners/generations, Auto, masks, alignment or
control input sequences. Disconnect cancels a pending request without swapping.
The saved seat preference is for the next join; it is not rewritten by a swap.

Authority protocol **v4** includes the current unique pilot/copilot assignments,
a separate seat revision and one pending request. Both initial and changed seats
are authoritative; host-right/guest-left negotiation remains supported. The
EFB-to-native FIFO carries the expected revision and ticket, so a requester cannot
self-approve and an old dialog cannot accept a replacement request. Session wire
offsets 976–1015 carry seat feedback and intent metadata; eight reserved bytes
remain. These UI feedback packets do not renew native leases or prove attachment.
Connection API 1 now permits the Windows reader to map the existing session
areas while remote-output capability 4 remains unadvertised.

`control_camera.h` reads local camera metadata and applies default Pilot/Copilot
view options on a saved initial preference or confirmed seat change. It defers
while outside the cockpit, requires an SDK checkpoint and three matching fresh
read-backs, and does not continually recenter the view. Unsupported/ignored
requests or SDK failures report an unconfirmed view in Setup and leave aircraft
controls untouched. The Baron's actual default views still need live validation;
this is not a completed simulator camera test. Separate altimeter ownership is
still unfinished and must follow the current authoritative seats when added.

Validation: 50 ownership/session scenarios, 24 native contracts, 26 JS checks,
DOM/package/current maximum native JSON checks, strict C++ camera/session/world/
native/connection runners, and the full actual loopback relay/native suite pass.
The relay test drives real EFB commands through both C++ controllers for request,
accept, decline and reverse swaps while preserving masks and group generations.
Real Chrome passes 64 layouts, 36 EFB insets and actual seat confirmation clicks
at 420×560. The WASM and dedicated helper build with zero warnings/errors.

Artifacts and receipt: `artifacts/seat-authority/verification.json`. The staged
bundle is `artifacts/seat-authority/fscopilot-efb-0.6.2-candidate.zip`. No new
package has been installed. Pending live work remains the read-only restart
check, bounded accepted-baseline output check, embedded native output/camera
validation and actual two-PC flight. Capability 4 stays off until real output
evidence supports it; this seat increment does not change that gate.

### Bounded output diagnostic and cockpit identification (preceding checks; not installed)

`FsCopilot.Efb/tests/run-output-check.ps1` builds a separate desktop SDK diagnostic
for the accepted solo 0.5.3 module. The guard and all twelve scenarios using local
SDK implementations pass, including ignored commands, hardware recapture, partial
writes and failed mask cleanup. Both live executables compile with `/W3 /WX`.
The script does not run either executable against MSFS.

The output check loads the actual saved aircraft mappings, requires both groups
on Me, Auto off, fresh parked state and parking brake on, and masks only one
engine pair in its own SDK connection. Same-state mode checks acceptance; the
separate one-percent nudge mode requires distinct movement and original-position
read-back. It confirms removal of its own mask and closes the connection on all
paths. A failed SDK write may prevent confirmed position restoration and must
remain a failed result. This does not test the embedded shared-session path and
does not enable capability 4.

The second executable only captures the two indexed barometric settings, their
standard-pressure flags, the two Baron altimeter LVars and camera view metadata.
Knob-to-index correlation and actual Baron seat views remain unverified. No new
instrument writer, instrument ownership or camera feature has been added.

Usage: `FsCopilot.Efb/tests/OUTPUT_CHECK.md`. Evidence and binary/source hashes:
`artifacts/native-output-check/verification.json`. No live output test or cockpit
capture has run in this increment; installed 0.5.3 remains unchanged. The prior
simulator restart request is still pending. Next repeat read-only SDK discovery,
then perform the scoped live output and embedded-module checks before enabling
shared output or proceeding to actual two-PC validation.

### EFB connection setup and standalone helper (preceding source; not installed)

This preceding increment produced a **0.6.1 candidate**. Installed remains the user-passed **0.5.3**;
capability 4 is still unadvertised. EFB Setup now provides local/friend codes,
Host/Join, an independent left/right seat preference, and server/port settings.
Native confirmation updates the form; 30 Hz observations preserve unsaved edits.
Join is disabled while the displayed values differ from saved values. Pending
or active sessions and retained shared control masks lock connection edits.
Saved seat preferences move the Me/Other graphics to the corresponding side;
actual camera positioning and in-flight seat swaps remain unfinished.

`control_connection_storage.h` stores only preferences in alternating checksummed
`connection-settings.0.bin` / `.1.bin` in the WASM work directory. It restores
neither ownership nor join consent and leaves learned-control files separate.
Discovery wire API extension 1 uses offsets 808–999 for preferences and helper
status. Old packets with zero extension remain valid. Backend-kind UI reports
are fenced by native instance, settings revision, source and sequence; their
three-second expiry is independent of native leases/proofs. They cannot write
controls, renew a lease or arm consent. All session commands remain on the
existing consent-bound session channel.

New `FsCopilot.Efb.Backend` publishes **FsCopilot.Efb.exe**, a dedicated background
helper with no Avalonia/legacy controller/rotary/GTN writers. It starts without
development options and uses `EfbConnectionBackend` to own/drain the configured
network and session service. The ordinary desktop app retains its existing
default; its explicit `--efb-backend` mode also accepts settings from the tablet.
A process guard prevents duplicate normal helpers. Default port 3600 uses
HybridNetwork; custom ports use RelayNetwork. The updated attributed relay or a
direct attributed connection is required; actual public-server compatibility
and two-PC connectivity remain unverified.

The helper and WASM build with zero warnings/errors. Managed and native SDK DLLs
must ship externally alongside the helper. The actual loopback/native harness
checks EFB Save → helper/network → explicit Join → native Auto/output, independent
seat/physics role, held-state edits, SDK loss/reconnection feedback, explicit local
recovery and drained reconfiguration. C++ persistence/parser/report tests and
controller/DOM tests pass. Real Chrome checks include 64 cockpit layouts, 36 EFB
insets, actual typing, saved/draft behavior and reachable 420×560 connection Setup.

The preceding live SDK check is **not passed**: MSFS PID 25700 existed, but
both the published helper and independent read-only C++ reader failed SDK Open
(the latter exits 2). The earlier successful parked world check remains valid
historical evidence, not proof of the new helper or native module in MSFS.
Duplicate-instance handling and bounded helper startup/exit pass; no network
session or actual control writes were attempted by these checks. A simulator
restart is needed before repeating live compatibility and scoped output checks.

Artifacts: `artifacts/efb-connection/`; consolidated receipt:
`artifacts/efb-connection/verification.json`; screenshots:
`artifacts/efb-connection/browser/connection-760x700.png` and `connection-420x560.png`.
Next prepare a scoped actual Baron output/embedded WASM check, obtain a simulator
restart, then enable capability 4 only with supporting real output evidence and
continue two-PC validation. The full master goal remains active, including seat/
camera swaps, separate altimeters, controlled systems, AP/guidance authority and
GTN flight-plan investigation. No new candidate has been installed.

### Shared quadrant Auto (preceding source; not installed)

Current source/candidate is **0.6.0**; installed remains the user-passed **0.5.3**.
The shared EFB now exposes the single quadrant Auto switch. Either player can
change one host-confirmed policy; throttle, RPM and mixture still acquire
ownership independently. Backend pickup reads only calibrated, fresh local
hardware positions and requires strictly greater than 2% cumulative movement
from an anchor. First samples, exact 2%, cockpit/world changes and Steering never
arm it. Existing tolerance and 300 ms native proofs remain in effect.

Shared Auto uses authority protocol **v3** and typed automatic requests. Auto
off cancels automatic waits/transactions while preserving manual requests and
owners. A transaction already in suppression stays detached until explicit
pickup, preserving the existing cancellation rule. Scoped Cancel resets the
movement anchor; stationary hardware cannot immediately rearm the request.
Native session policy/waiting metadata occupy offsets 968/972. UI-only feedback
does not renew the lease, change masks or overwrite a native operation proof.
The EFB's mode/owner colors await confirmed native state.

The actual local relay/backend/C++-SDK-fake suite passes pickup in both
directions for all three pairs, independent owners, first sample/noise/exact-2%
and cockpit-only rejection, manual Steering, scoped Cancel/rearm and Auto-off
preservation of manual pickup. World/lease loss and existing takeover checks
still pass. Ownership contracts: 46; native wire contracts: 21; UI/registration:
24 plus DOM; nine strict C++ runners. WASM builds with zero warnings/errors;
published backend retains four pre-existing warnings. Artifacts and complete
receipts: `artifacts/shared-auto/verification.json`.

This has not yet run as a shared session in the actual Baron or across two PCs.
Capability 4 remains unadvertised. Next finish normal EFB connection setup and
prepare a bounded real-Baron remote-axis/native-module test before enabling it.
Seats/camera, per-seat altimeters, controlled aircraft systems, AP/guidance
authority and GTN flight-plan investigation remain in the active master plan.

### Fixed-host world and cockpit synchronization (preceding source; not installed)

The backend/native path now synchronizes the fixed physics host's world state
and canonical cockpit targets. `control_world.h` registers 12 explicit-unit
position/attitude/earth-velocity/body-angular-velocity values plus three freeze
readbacks. Bind confirms all hardware masks before starting guest freezes; native
binding completes only after a fresh SDK checkpoint and frozen readback. The host
is never frozen or written by a guest world frame. Physics authority remains fixed
when steering or engine ownership changes.

`SharedWorldPacket` is a bounded 334-byte encoded packet on the attributed actual
transport. It is the modern backend's sole sequenced/unreliable stream. Host
snapshots carry session/revision/sequence/generations, 12 world values and 21
normalized cockpit targets. Both backend and native recheck binding/role/epochs
and scope at execution; queued world samples expire in 250 ms. The native guest
writes physics through `SimConnect_SetDataOnSimObject` and mirrors cockpit targets
only while the corresponding hardware is masked and not actively locally owned.
Initial, pending and canceled pickup targets therefore stay canonical; confirmed
local hardware is never overwritten. Mirror events still bypass the hardware
observer at DEFAULT priority. No direct target-copy fixture remains.

World fields use session-packet bytes 864..959, world observation serial 960..967,
with 56 reserved bytes remaining. Command kind 10 is World; status flags 16/32/64
mean world mode, fresh world data and confirmed owned guest freeze. Packet size
remains 1,024 bytes. World packets neither renew the native lease nor replace a
handover acknowledgement. One-second world-stream expiry retains guest freezes
and hardware masks even if ordinary session heartbeats continue.

Leave/loss retains the guest freeze. Explicit scoped Me/Force restores only this
module's prior freeze settings, waits for both SDK checkpoint and readback, then
continues the selected hardware action. Other groups stay masked. Write errors
end the session without release; partial batches cannot replay. Existing external
freeze settings are preserved. Shared configuration timeout no longer invokes the
solo fail/release path. The native-only writer recovery regression is preserved.

The real-source relay/native suite now follows host altitude through the actual
C++ SDK write adapter, corrects injected guest drift, exercises independent
takeover/owner return and verifies world loss despite live heartbeats, guest
Force recovery, rejoin and replay fences. The SDK fake captures the simulator
readbacks; manual cockpit mirroring was removed. Nine strict C++ runners, 20
native wire scenarios, 44 ownership scenarios, 24 JS/registration checks, DOM and
native-v5 status pass. WASM builds with zero warnings/errors. Desktop Release
builds/publishes with zero errors and four existing warnings.

**Actual simulator evidence:** the read-only world probe received four fresh
observations of all 15 values with zero errors. After the user restarted and
confirmed the Baron was parked, the bounded SDK ground test briefly froze it,
wrote its exact current 12-value state once, and restored all three freeze flags
to zero, with zero SDK errors and no hardware masks or axis writes. Its initial
0.01-knot guard rejected 0.04172 knots of parking drift without writes; the guard
now allows up to 0.1 knots and still requires fresh on-ground/stopped observations.
An earlier attempt could not connect after MSFS stopped and made no changes.
This proves the production native world's SDK reader/setter/freeze/recovery APIs;
it does not prove two-PC flight or the installed WASM session path.

Current artifacts: `artifacts/native-world/candidate/` and
`artifacts/native-world/backend/`; receipt: `artifacts/native-world/verification.json`.
Installed 0.5.3 remains unchanged and capability 4 remains unadvertised. Shared
quadrant Auto is now implemented above. Next finish EFB connection configuration and prepare a concrete
candidate install and actual Baron remote-axis/WASM/two-PC validation. Controlled
aircraft-system sync, seats/camera, separate altimeters, AP/guidance authority and
GTN flight-plan investigation remain part of the full active goal.

### Backend startup and published SDK client (preceding source increment; not installed)

`NativeSharedSessionService` now owns fresh EFB consent, actual relay connection,
metadata negotiation, native coordinator lifetime and explicit rejoin. It pins
native/backend/aircraft/adapter/engine/schema identity and checks observation and
session serial freshness independently. A consumed consent key is native + token:
an SDK reconnect's new backend GUID cannot make it reusable. Replayed retained
packets cannot keep a negotiation alive. Ending/disconnecting retains native
hardware masks and requires another explicit EFB Join for a new session.

The actual desktop executable has an explicit `--efb-backend` startup path. It
constructs neither vanilla `Coordinator` nor `MasterSwitch`, so legacy axis,
rotary/GTN interaction and physics-host-switch writers are absent in this mode.
Normal desktop startup keeps its existing path. The backend requires explicit
relay host/port, local/peer codes, host/guest role and pilot/copilot seat; it does
not choose or contact a public relay automatically. Physics role and seat remain
independent. `--efb-dry-run` validates options without constructing simulator or
network clients; `--run-seconds` provides bounded diagnostic runs. EFB connection
configuration/default-launch UX is still pending; these are development options.

The self-contained Windows publication is in `artifacts/shared-startup/backend/`.
Actual published dry-run/invalid-options launches exit 0/1 as expected. An initial
publication exposed a missing native SDK dependency: the referenced native
Windows project now copies `SimConnect.dll` to publish and excludes it from the
single-file bundle. The corrected executable reached live installed 0.5.3
discovery and correctly refused shared binding for capability 11. It ran four
seconds, exited 0, and did not write aircraft controls or change masks.

The Windows session client now requests session areas only after fresh native
discovery advertises remote output (4); isolated test prefixes still map their
known fixture areas. Thus installed observation-only packages are readable even
with a session-enabled client, without absent-area SDK errors/reconnect loops.
The actual isolated SDK session fixture still passes bind/state/committed-ticket/
input and expiry: both exits 0, three replies, 33 packets, zero SDK failures.
Failure diagnostics now include exception type and HRESULT.

The full real-source relay suite passes, including automatic EFB-consent startup,
remote native throttle output, Leave/retained masks, fresh rejoin, old-session
rejection, unsupported capability refusal, stale native replay and changed-backend
consent replay. These native executions use the SDK fake. No world/physics writer
is attached yet; the tests do not establish two-PC flight. Release builds/publishes
have no errors and four existing UI/async/Windows-sound warnings. Trimming is
disabled because authority JSON DTOs and Avalonia views need runtime reflection.
Receipt and logs: `artifacts/shared-startup/verification.json`.

Installed 0.5.3 remains unchanged; capability 4 remains unadvertised. Fixed-host
native world and cockpit synchronization is now connected above. Next add shared Auto, EFB connection
setup, controlled aircraft-system synchronization and real Baron/two-PC checks.
Seat/camera swaps, separate altimeters, AP/guidance authority and GTN flight-plan
investigation remain required by the full active goal.

### EFB shared-control requests (preceding source increment; not installed)

The EFB now routes actual Me/Other, scoped Force and Cancel commands through the
native module into `NativeAuthorityCoordinator`. In shared mode a click only
queues an intention; it never unmasks hardware. A bounded eight-entry native
FIFO publishes its head in session status (offset 840). New command `IntentAck`
consumes exactly that head without replacing mask/attachment proofs or renewing
the lease. The backend routes each intention once and can retry its ack.

Setup now has Join/Cancel join/Leave shared session. Join is a local CommBus
action that arms fresh native consent and leaves hardware unchanged. Leave
ends the native session, retains confirmed masks and causes the backend to end
authority. Join does not launch a Windows process; the explicit backend mode
above must already be running. The installed 0.5.3 UI is unchanged.

Shared graphics distinguish confirmed ownership from temporary hardware
suppression, show waiting outlines and use Other pilot labels. Native attachment
is still required before input becomes active. Ended sessions show scoped
recovery guidance. Solo learning/Auto/presets retain their existing behavior.
Shared Auto and relearning are unavailable until the shared policy is integrated;
do not enable solo Auto as a substitute for shared authority.

Cancellation now also pauses a committed incoming attachment. It supersedes an
in-flight native operation with State and discards a late attachment proof;
hardware remains masked until another explicit Me request. Either participant
can cancel a selected pending transfer. The actual relay/native test catches
the interval after commit but before attachment, verifies retained suppression,
then resumes through a new EFB request. All ownership requests in the native
integration scenario now enter via the real CommBus command parser, including
initial Join, independent pairs, bidirectional return and Force.

Eight strict C++ runners, 19 native wire scenarios, 44 ownership scenarios,
24 JS/registration checks, DOM integration and the full real-source loopback
network suite pass. Headless Chrome passes 64 layouts and 36 EFB inset layouts,
including actual Join/Leave clicks and pending owner colors. Its runner now
rejects debugger closure/command timeout; an interrupted browser run can no
longer silently return success. Chrome required execution outside the sandbox
because its sandboxed GPU subprocess crashed. WASM builds with zero warnings/
errors. Current candidate: `artifacts/efb-session/candidate/`; verification:
`artifacts/efb-session/verification.json`. This remains SDK-fake/native + browser
evidence, not actual Baron remote-write or two-PC flight validation.

Backend session startup/negotiation is now implemented above. Next add
the single shared quadrant Auto policy, fixed-host world/physics replication and
filtering of overlapping legacy writers. Capability 4 remains unadvertised.
Seat/camera changes, separate altimeters, AP/guidance ownership and GTN flight-plan
investigation remain required by the full active goal.

### Network-to-native coordinator (earlier source increment; not installed)

`FsCopilot.Native/NativeAuthorityCoordinator.cs` now drives native binding,
state synchronization, preparation and attachment from real four-group authority
snapshots. `FsCopilot/Network/NativeControlAuthorityBridge.cs` connects it to the
actual attributed transport and `INativeSessionClient` (implemented by the Windows
SDK client). Native callbacks and host authority work share one actor. Accepted
remote values cross the host's final `TryApplyAcceptedInput` fence immediately
before the native queue. Local host hardware is not retransmitted into the host.

Frames use actual cockpit targets, not cached hardware positions. Local pickup
intents wait for alignment before requesting a network transfer. Outgoing
suppression runs promptly, while an unaligned incoming pair never occupies the
native operation and starves another pair. A newer state supersedes a pending
operation. Proofs match binding, ack, revision, ticket and epochs and are consumed
once. Committed intents wait for attachment rather than creating repeated
transfers. Required learned host output axes and local hardware learning are
checked before binding, so joining cannot strand an unlearned control in masked mode.

Atomic transfer requests/snapshots now carry explicit `Force`. Only the requesting
new owner can force their own takeover. Both suppression proofs and committed
attachment are still required. The prepared native ticket scopes the alignment
bypass to that transfer and selected group. Native status adds AP engagement as
status-only flag 8. Steering samples/output are suspended while the relevant AP
is engaged; the native writer rechecks the host AP at execution. Engine pairs
remain independent. This is an interlock, not AP/NAV/APR/GTN synchronization.
Native transport runs at 30 Hz in shared mode; EFB display and solo remain 10 Hz.

**Lifecycle race fixed:** negotiation and network host share an authority object.
Negotiation could revoke the peer first, making the host's conditional callback
skip publication and leaving the native coordinator active. Two-group and
four-group hosts now publish external revision changes on their actor tick.
The relay test deliberately revokes authority before socket disconnection and
verifies replica delivery. Backends also exchange source/session/sequence-checked
`AuthorityAlive` every 500 ms; 3 s expiry ends shared control even with a live
relay socket. A test drops only those heartbeats and verifies retained masks.
Native lease heartbeats remain independent.

End-to-end tests use the real relay, real negotiation, both real backend bridges
and two binary subprocesses containing the production C++ WASM controller/output
adapter with the SDK fake. They verify initial attachment, throttle pickup while
RPM waits, cancellation/recovery, independent mixtures, host/peer/host return,
scoped Force, atomic whole-quadrant transfer, remote roll/pitch/rudder, AP
interlock, native loss and scoped EFB Force recovery. Only cockpit targets are
mirrored by the fixture; aircraft/world replication is not implemented by it.
No actual MSFS aircraft controls are written, and this is not two-PC flight
evidence. `FsCopilot.Network.Tests/run.ps1 -DotNetPath <dotnet.exe>` builds the
harness and runs all actual-source tests; `-NativeOnly` selects coordinator cases.

The full network suite, 44 ownership/session scenarios, 18 native/session wire
scenarios and eight C++ runners pass. Native v5 JS status remains compatible.
WASM/native Windows/probe builds report zero warnings/errors; desktop Release
has zero errors and the same 25 legacy UI/reflection/trimming warnings. Candidate:
`artifacts/native-coordinator/candidate/`; logs/receipt in its parent directory.
Installed 0.5.3 remains unchanged.

With the user back in the Baron, the installed default/read-only client again
passed fresh discovery and three Ready observations from native `6bfe071`.
The rebuilt queued-client SDK fixture also passed bind/state/committed-ticket/
input transport and expiry: both processes exited 0, three discovery replies,
32 packets and zero SDK failures. It used isolated diagnostic ClientData only;
no aircraft controls or hardware masks were changed. Logs:
`artifacts/native-coordinator/live/`. All seven installed payload hashes still
match the accepted 0.5.3 artifact. Consolidated source/build/test receipt:
`artifacts/native-coordinator/verification.json`.

EFB join/intent routing and explicit backend startup are now implemented above.
Add the single shared Auto policy/per-pair
movement intents and seat-aware labels/colors. Connect fixed-host world/physics replication
and exclude overlapping legacy axis/GTN/rotary writers. Verify actual Baron
remote output and real two-PC operation. Capability 4 remains unadvertised in the
real package. Seat/camera swaps, separate altimeters, AP/guidance authority and
flight-plan-only GTN investigation remain part of the full active goal.

### Consent-bound native session candidate (current source; not installed)

The next increment adds `control_session_protocol.h` / `control_session.h` and
`NativeSessionWire.cs` / `NativeSessionSendGate.cs`. Separate 1,024-byte session
status/command areas accept Bind, State, Prepare, Attach, Input, End and Heartbeat.
The production native dispatcher routes them to the writer. Discovery remains
Hello-only and cannot join or change controls. Local CommBus event
`FSC_CONTROLS_SESSION_JOIN` accepts a 32-hex seed; native code derives a fresh
consent token using that seed, instance identity and a monotonic arm counter.
The seed alone cannot be used as the binding token. Consent expires after 60 s.
The arm counter and status serial survive aircraft resets; old binding/consent
is invalidated. The EFB join/intent routing and coordinator are implemented above;
backend startup and connection orchestration remain unfinished.

Bind first confirms all four masks through a fresh SDK barrier. Native incoming
preparation then requires current alignment for 300 ms; outgoing preparation
always drains preceding SDK writes through a new barrier, even for an already
masked pair. Attach needs the prepared ticket and committed generation, another
alignment hold (unless the prepared Force flag applies), and native confirmation.
Heartbeat renews a 2 s lease without replacing the pending operation's ack.
End/expiry/transport failure retains masks and never acknowledges an abandoned
attachment. After an in-flight unmask drains, it restores the prior confirmed
mask. Explicit local Me/Force recovery disables solo Auto.
Tolerance changes restart the managed alignment hold as well as the solo hold.

**Authority correction:** cancellation advances a generation too. Added immutable
`AtomicGroupState.CommittedTransfer`, set on commit and cleared on invalidation;
wire/replica/native checks preserve it. A canceled same-owner transfer cannot
reuse its prepared attachment or Force proof merely because generations match.
Native session packets carry all four pending and committed ticket identities.
The experimental atomic authority remains version 2 (never deployed); the
strict serializer now includes this field. Both peers need the new build.

`NativeControlClient(enableSessions: true)` now maps the separate session areas
and queues copied command bytes. Its SDK reader rechecks native/backend/consent,
authority/generation/owner and freshness at execution. Reconnects use a fresh
backend GUID; input expires in 250 ms, other queued commands in 1 s. Default
construction remains observations-only and works with installed 0.5.3. That
compatibility passed against the actual live Baron:
`artifacts/native-session/installed-0.5.3-compatibility.log`.

The isolated actual-MSFS SDK fixture passed bind, state, committed ticket and
input bytes through the production C++ transport/C# queued writer, then expiry
after server close: both exits 0, three discovery replies, zero SDK failures.
It only used private ClientData and read-only checkpoints, with no hardware
masks or aircraft writes. Receipt: `artifacts/native-session/live/`.
The initial fixture exposed an empty-session timestamp overflow in the Windows
reader; fixed by checking that a session exists before subtracting its timestamp.

Eight C++ runners, native v5 JS status, 17 native/session C++↔C# scenarios,
43 ownership/session scenarios and actual-source loopback relay tests pass.
Session tests include local consent replay, initial attach, bidirectional pair
handover, same-owner cancellation, tolerance hold reset, lease cancellation of
an in-flight unmask, superseding states and guest output isolation. Windows WASM
and the native Windows/probe projects build with zero warnings/errors. Desktop
Release builds with zero errors and 25 warnings in legacy UI/reflection/trimming
code; do not claim a warning-free desktop Release. Candidate:
`artifacts/native-session/candidate/FsCopilotControls.wasm`; it is not installed.
The passed 0.5.3 package remains installed. Capability 4 remains unadvertised.

The coordinator and EFB request routing are now implemented above; backend
startup/connection orchestration remains. Map
`Pending` and `CommittedTransfer` GUIDs using `N` format, and use the fresh
connection's `NativeBridgeState.Backend` plus native-issued consent. Initial
local attach uses the negotiated session GUID as its ticket. Serialize native
operations, send heartbeats while waiting for alignment, and match proofs by
Ack/Result/ticket/epochs (not the latest received Command). Consume each proof
once. Higher-authority State supersedes a pending native operation; a still-valid
transfer must then be resubmitted. Schedule currently aligned pairs so one
unaligned pair cannot starve independent Auto/manual pickups. Feed accepted
network frames through the host's final `TryApplyAcceptedInput` fence into the
native queue. Use actual cockpit targets for owned lever values; hardware is for
Auto intent/alignment. Replicate the single quadrant Auto policy through host
authority. Add local seat-aware owner colors, shared pending feedback and loss
recovery, then install an increment for real native-write/two-player checks.
Physics/world replication, legacy sync filtering, seat/camera swaps and
AP/baro/GTN authority remain incomplete; the full master goal is still active.

### Native remote-output candidate (not installed or network-enabled)

After that live check, added `control_remote_writer.h` and
`control_sim_output.h`, integrated into the WASM lifecycle/error dispatcher.
The source now differs from the accepted installed 0.5.3 artifact. Separate
candidate: `artifacts/native-writer/candidate/FsCopilotControls.wasm`; build log
in that directory reports zero warnings/errors. Do not replace the accepted
0.5.3 build or imply multiplayer is enabled.

The executor begins only with all four confirmed hardware masks, no pending
pickup, fresh cockpit data, a fresh binding and an initially detached snapshot.
It rechecks native/backend/session/epoch, authority revision, per-group generation
and sequence, attached remote ownership, complete negotiated axes and actual
native mask at execution. Output now supports independent engine pairs and
inverts the host's learned mappings; combined bindings become per-engine events.
The actual SDK adapter sends below the hardware mask at DEFAULT priority and
tracks every call through bounded, separately identified SDK checkpoints.
This initial writer-only candidate had no command handler. The current session
candidate above adds a separate consent-bound handler; discovery still cannot
invoke it. Capability 4 remains absent pending backend and real simulator tests.

Lease loss or native-output failure stops execution and holds solo Auto suspended
without releasing hardware masks. Active shared mode rejects solo EFB ownership
changes. After the link ends, explicit local Me/Force recovery disables Auto and
affects only the selected group. Partial SDK failure can apply the first engine
before the second fails; it ends the writer and cannot replay that sequence.
There is no claim that multiple SDK transmissions execute atomically.

`remote_writer_test.cpp` includes the actual module and SDK adapter, capturing
writes in the fake. It covers independent pairs for 1-4 engines, stale/duplicate
frames, owner return handover, pending transfers, mask changes, local recovery,
schema/aircraft changes, partial and asynchronous errors and stalled checkpoints.
Output, remote-writer and existing actual-module runners pass; native v5 JS status
and all 11 C++/C# wire checks still pass. Logs: `artifacts/native-writer/tests/`.
Installed 0.5.3 hashes were rechecked unchanged after the candidate build.

That increment's next steps were native commands, local EFB consent, backend
binding and native preparation/attachment acknowledgements, now implemented
above except for the EFB join UI and authority coordinator. Every new
preparation must use a fresh SDK barrier even if masks are already set, to drain
previous-owner SDK transmissions before acknowledging handover. Never activate
the current internal `begin` through discovery or an unconsented cached command.
The single shared Auto policy must submit intents to host authority; it must not
resume solo unmasking. Real aircraft remote-write and two-player checks remain.

The user confirmed **0.4.3 works** and explicitly requested continuing until the
mod is finished. Keep the active goal and finish the integration in reviewable
increments. **0.5.0 Advanced controls are now user-confirmed working.** The user
clarified that Auto must be a single quadrant switch, while takeovers remain
independent per engine pair; **0.5.1 is now installed** with that change. **0.4.4 actual tail-rudder
suppression is user-confirmed after restart**; it is backed up as the passed local
baseline, along with accepted UI and yoke/quadrant behavior. The network code is
not yet enabled at app startup or installed into MSFS.

Additional accepted requirements while this work was active:

- Choose **Pilot / left seat** or **Copilot / right seat**, moving the local
  cockpit viewpoint automatically, and support a confirmed in-flight seat swap.
  Seat roles must be independent of physics-host, steering, quadrant and AP
  ownership. Both players cannot own the same seat. Baro ownership follows the
  pilot/left and copilot/right seat as agreed. Preserve each altimeter's current
  pressure when roles swap; do not automatically equalize them.
- Rudder pedals and their toe brakes belong to **Steering**. The existing native
  event mask already includes rudder, nosewheel, left/right brake axes and brake
  buttons; observed/saved absolute pedal axes also join alignment requirements.
  The diagram already routes pedal clicks to Steering and shows detected axes
  across the panel. Verify those readings and make this clearer in the UI; do not
  create a third ownership group or silently ignore a detected pedal at pickup.
- **Latest live report:** the user confirmed actual rudder movement is now
  blocked with Steering on Mock after restarting into 0.4.4. Toe brakes were not
  the reported failure. Two earlier read-only captures using
  `tests/pedal_capture.cpp` (90 and 300 seconds, all observer masks false) recorded
  no pedal events or changing positions. Those captures did not identify the
  exact binding; suppression is established by the subsequent hardware retest.
- The user explicitly expands the earlier two-group requirement: keep the
  simple **Steering + Engine quadrant** view by default, but add **Advanced**
  under the quadrant's three-dot menu with three independently claimable pairs:
  both throttles, both RPM/propeller controls, and both mixtures. No per-engine
  ownership was requested. The old two-group protocol is an installed baseline,
  not a final limitation; version/feature-negotiate the advanced group layout.
- **Latest Auto clarification:** provide exactly one quadrant Auto switch in the
  initial quadrant menu, available without Advanced. It enables/disables Auto
  for throttle, RPM and mixture together. No individual pair Auto modes. The
  setting is separate from ownership: a pair has exactly one owner, and the
  opposite participant can reclaim only that pair by moving and aligning their
  hardware. Pilot→copilot→pilot transfers must retain RPM/mixture owners and
  reject all input from the previous owner, including already queued frames.
  Carry the single Auto policy in authoritative shared-session state when the
  EFB/backend bridge is enabled; local Auto must then submit transfer intents,
  never independently unmask before host grant. Auto off cancels automatic
  requests without taking ownership or cancelling manual Me requests.
- Auto operates on those three engine pairs only. More than 2%
  of full-travel physical movement arms pickup, then actual alignment/tolerance,
  hold and simulator confirmation complete it. Use cumulative movement from an
  anchored physical baseline so a slow deliberate move also triggers; 1-2%
  chatter must not. Cached-position refreshes, remote writes, cockpit movement
  and echoes must never arm it. Do not repeatedly submit an already-pending
  request. Disabling Auto cancels its pending request. **No Auto for Steering.**
  Show manual/auto, pending and confirmed ownership per pair. On switching to
  advanced mode inherit the quadrant owner for all three pairs; on returning to
  simple mode handle mixed ownership explicitly, never silently seize controls.

### Latest increment: 0.5.1 (2026-11-15)

The new single Auto switch is in the main quadrant ⋯ menu in both views. Native
Auto is now strictly 0/all-three-pairs (14), with separate movement baselines,
alignment holds and mask confirmations per pair. Advanced is presentation only;
switching back to Simple preserves mixed ownership, tolerances, pending pickup
and the Auto setting. Simple displays pair owner colors/labels while mixed or
Auto is enabled. Pair Auto controls have been removed. Mixed tolerances are
shown as Per pair, and each graphic uses its own tolerance. Pending pair pickup
can still be cancelled in Simple. EFB/native protocol **v4** rejects old partial
Auto commands; legacy non-Auto commands remain accepted.

The accepted local baseline is now 0.5.0 Advanced hardware behavior, plus the
previous persistence/alignment/rudder/menu confirmations. The new 0.5.1 candidate
passes six C++ runners, 22 controller checks, DOM/package/registration/native v4
status checks, and Chrome checks across 40 layouts / 12 EFB insets. Simultaneous
pair movement/cancellation and different pair tolerances are covered. The
authority runner passes 42 scenarios, including a throttle handover to the peer
and back to the host with unchanged RPM/mixture ownership and old-owner input
rejected at the actual writer boundary.

Actual native/backend discovery is now implemented in the WASM module and
`FsCopilot.Native*`. It transports only observations/discovery, not aircraft
commands. The native module keeps a single WASM callback; optional IPC failures
are classified independently and cannot release masks. Publication tracking is
bounded by ordered SDK checkpoints. The monitor requires fresh challenge replies
and observations, rejects cached/stale/retired/conflicting instances and clones
mutable arrays. Nine C++→C# packet/monitor contract scenarios pass. The actual
production C++ transport and C# client passed a live MSFS SDK test under isolated
ClientData names: exact fixture values, fresh discovery, expiry after server
shutdown, both processes exit 0, zero SDK failures and no aircraft/control writes.
This does not prove installed WASM delivery; verify the canonical diagnostic after
the package is installed/restarted. Capabilities are **11**, deliberately missing
isolated remote input (4), so negotiated multiplayer remains disabled.

Read `FsCopilot.Native/README.md`. Build output is separate at
`artifacts/native-bridge/candidate/`; `build.ps1 -ArtifactRoot` supports this.
All seven 0.5.1 installed payload hashes match; ZIP/layout checked. Passed 0.5.0
and both mapping slots were backed up to
`artifacts/native-bridge/installed-backup-0.5.0-advanced-passed-before-0.5.1-20261115-170009`;
live saved mappings were unchanged. `artifacts/efb-control-test/verification.json`
records the installation. Logs/SDK receipt are under `artifacts/native-bridge/`,
notably `live-transport-verification.json`.

**0.5.1 user result:** after restarting and returning to the Baron cockpit, the
user says the new global Auto behavior seems to work. Record that hardware test
as passed, following the 0.5.0 Advanced confirmation.

Before the fix, actual installed WASM discovery did **not** pass: sessions 18056 (300 s) and
24185 (20 s, Ready-only) both ended with exit 1 waiting for a discovery reply.
They are terminal; do not poll them as live or restart the same 0.5.1 test.
Inspection found the reason: `control_pickup::input_position` returns **-10**
for unobserved/unavailable axes, but the versioned bridge wire requires **-1**.
The factory passed -10 through and the native publisher rejected the entire
packet, including Hello replies. Maximum-all-learned tests had missed this.

**0.5.2 staged fix, now included in installed 0.5.3:** normalize unavailable inputs to -1
in `bridge_status`, with actual-module unobserved/startup regression and a second
binary C++→C# fixture. The 0.5.1 Auto/UI behavior stays the accepted installed
baseline. The intermediate build is under `artifacts/native-bridge/0.5.2`, preserving
the installed 0.5.1 artifact directory. The stricter reader is compiled at
`artifacts/native-bridge/probe-ready/`; it requires three Ready aircraft packets.
No aircraft/control writes occur. Actual 0.5.3 WASM discovery and user preset
confirmation have now passed (see the current status above). Next implement
fenced native commands/remote input, wire EFB intents to network authority, and
replicate the single Auto policy. Seat/camera swap and AP/baro/GTN work remain.

### Installed Advanced/Auto increment 0.5.0

The native module now uses `control_runtime.h` and four confirmed masks:
Steering, throttle, propeller/RPM, mixture. The default view remains Steering plus
whole quadrant. Quadrant ⋯ → Advanced engine controls reveals separately colored
engine pairs; click a pair after selecting a seat. Its per-pair Force/Auto actions
are accessible through the same visible quadrant ⋯ menu or a pair right-click.
Setup shows three separate engine cards and scrolls. Whole-quadrant commands
still configure/align/attach all three pairs together, including mixed ownership.
Closing Advanced is refused until owners/tolerances match and pickup is finished
or cancelled. Advanced/Auto options reset for a new aircraft/session; learned
scales keep their existing alternating persistent file format.

Auto is opt-in per engine pair and never offered for Steering. Cumulative
physical movement strictly greater than 2% arms pickup against an anchored
baseline; initial readings and cockpit position updates alone do not arm it.
All required levers in that pair must align within its tolerance (default 10%)
for 300 ms before requesting unmasking; ownership waits for its native barrier.
Auto off cancels automatic pending pickup but preserves manual Me requests.
The EFB/native protocol is now v3, distinct from the .NET authority protocol v2;
native parsing retains legacy two-group commands. The maximum native status
(9 steering axes, 12 engine axes plus pair diagnostics) passes the production JS
validator. Pair-specific relearning preserves adjacent pair scales.

Six C++ runners pass under MSVC C++14 /W3 /WX. The Windows WASM Release build has
zero warnings/errors. 21 controller/registration checks, DOM/package/native status
checks and actual Chrome mouse/keyboard/scroll tests pass across 36 main layouts
and 8 simulator-inset layouts. All 177 SDK mappings passed four non-masking
rebuilds across four groups, zero failures. No physical control was transmitted
or masked by that external probe.

All seven installed payload hashes and the ZIP/layout were verified. Passed
0.4.4 and both live cache files were backed up to
`artifacts/efb-control-test/installed-backup-0.4.4-passed-before-0.5.0-20261115-155012`;
the two saved mapping files were unchanged. `verification.json` records the
receipt. The user subsequently confirmed Advanced controls work. Their revised
single Auto switch requirement supersedes the per-pair modes in 0.5.0; the new
0.5.1 hardware check remains pending.

### Shared-session integration foundation (not enabled)

`SessionNegotiation` / `SessionWire` in `FsCopilot.Ownership` implement an explicit
hello/offer/accept/commit handshake with fresh client and host attempt GUIDs,
transport-pinned participants, matching aircraft/engine/adapter/native protocol
and required capabilities. Initial seat preferences are negotiated independently
of the physics host, including host-right/guest-left. Conflicting seat choices
and incompatible configurations fail. Every session starts with both groups
detached; successful metadata negotiation is not a simulator-mask acknowledgement.
Capabilities must come from a verified bridge; tests simulate them, and the
currently installed local prototype does not advertise these network features.

Retries are idempotent without extending the deadline; old commits/offers/ends
cannot rebind a new join. Explicit termination and peer loss revoke remote
authority. `OwnershipNetworkNegotiation` runs the handshake on the actual
sender-aware INetwork stream, with replayed null/live session bindings so a
closed session cannot be replayed as active to a late subscriber. Registration
must occur before connecting peers, in the same packet order on both clients.

The contract runner now passes **41** scenarios, including advanced negotiation,
four-group authority and simulator-writer fences. The four-group session stores
its own copy of each accepted sample. `TryApplyAcceptedInput` now passes a fresh
copy of those accepted values into its writer callback, rejecting substituted
queue values, stale generations and duplicate sequences. A throwing/partially
applied writer cannot replay the same sequence. The actual relay regression also
verifies mutated queued values cannot reach the writer; these checks perform no
simulator writes. Logs: `accepted-writer-tests.log` and
`accepted-writer-network-tests.log` under artifacts/ownership-protocol.
The additional ownership checks fence simulator execution against changed
generations, newer queued samples, duplicate application and ended sessions.
`OwnershipSession.TryApplyAcceptedInput` rechecks at the actual writer queue;
`End` permanently closes both local and remote authority. Transport loss and
explicit session termination must clear live negotiated bindings.
The actual loopback relay/client runner passes the previous checks plus four
negotiation checks, including wrong-source replay, initial seat choices, feeding
the negotiated session into the real ownership host, remote frame acceptance,
explicit-end revocation and fresh join. Logs:
`artifacts/ownership-protocol/session-tests.log` and `session-network-tests.log`.
Actual desktop Release build succeeds with the same four pre-existing warnings
in SetupViewModel/UiSounds/UpdateAvailableWindow. No public relay is contacted or
deployed by these tests. No real two-player simulator session has been tested.

### Live remote-routing/camera investigation

`FsCopilot.Efb/tests/remote_route_probe.cpp` was built with SDK 1.8.2 and run
against the active Baron 58P. It masks/transmits only a private custom event,
never hardware or aircraft-control events. At HIGHEST transmission priority,
the mask observer received one event and the DEFAULT sink none; at DEFAULT
priority, the mask observer received none and the DEFAULT sink one. **PASS**,
zero SDK errors. The probe handles `SIMCONNECT_RECV_ID_EVENT_EX1`, which the
extended transmission API emits. Its initial version counted only ordinary
EVENT callbacks and incorrectly reported zero deliveries; that harness error
was fixed before recording the passing result.

This verifies a priority path past our high-priority mask without recapture; it
does not yet prove correct Baron lever/control behavior. Actual remote control
mapping/read-back still needs isolated validation, generation checks at queued
execution and native enforcement. Do not bypass every custom aircraft handler
blindly. SDK transmission documentation:
https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_TransmitClientEvent_EX1.htm

The same probe read cameras without writing: state **2** (cockpit), view type
**1**, option index **1**, pilot-view options **6** on
`SimObjects\Airplanes\bksq-aircraft-baronpropress\aircraft.CFG`. SDK documents
Pilot option 2 and Copilot option 5 within cockpit pilot-view type 1. This is a
verified read-back/candidate mapping, not a completed camera movement test.
See `artifacts/ownership-protocol/remote-route-live.log` and
https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simvars/camera-variables/.

Next: implement and test the simulator adapter and EFB/backend bridge, then
enable negotiated multiplayer behind verified native capabilities. Add the
confirmed in-flight seat-swap flow and camera application/read-back; initial seat
preferences are implemented only in the handshake so far. Preserve the accepted
two-axis-group prototype and per-instrument baro ownership design below.

### Passed rudder coverage 0.4.4

The event catalogue adds AXIS_SENSOR_RUDDER_SET, RUDDER_AXIS_MINUS,
RUDDER_AXIS_PLUS and RUDDER_CENTER, bringing it to **177**. All 177 passed the
actual SDK's non-masking check through four rebuilds, with zero failures.
AXIS_LEFT_BRAKE_LINEAR_SET / AXIS_RIGHT_BRAKE_LINEAR_SET were tried in the
isolated probe, rejected with exceptions 7/3, and excluded before packaging.
Do not re-add those aliases solely because current online SDK docs list them.

`control_test.cpp` now observes ordinary and extended EVENT_EX1 packets, with
size checks; a truncated extended packet cannot update the learner/counters.
The sensor rudder alias can learn. Steering alignment regressions independently
require fresh aligned rudder, left brake and right brake before the hold completes.
Mask changes, target adapters, persistence, per-group menus and native status
checks pass. WASM Release builds with zero warnings/errors; controller (19),
registration, package, DOM and real Chrome checks pass. Seven installed payload
hashes match, 0.4.3 is backed up under artifacts, and both saved slots are unchanged.
No actual movement was captured in `pedal-capture.log` or
`pedal-capture-extended.log`, so the exact binding remains unidentified. The user
subsequently restarted into 0.4.4 and confirmed actual tail-rudder suppression.

Additional native adapter foundation (not wired into the module yet):
`Wasm/control_output.h` inverts the already-learned aircraft scale/direction,
including nonlinear brakes. It creates per-engine event aliases from combined
hardware sources so remote engine values need not be equal, validates the whole
group frame before exposing any action, and refuses unknown/ambiguous mappings.
`tests/output_test.cpp` passes 1,010 inverse-model samples and complete-frame,
independent-engine, wrong-group/unlearned/source rejection checks. Use this in
the next authenticated native/backend adapter; no remote aircraft events are
currently injected by the installed prototype. See artifacts/ownership-protocol.

## Current increment: 0.4.3 (2026-11-15)

The user reported 0.4.2's context menu did not open inside MSFS. That version
listened only for `contextmenu`, which Coherent's public supported-event table
does not list as supported. We did not capture the simulator's event stream, so
this is the likely compatibility cause, not a measured runtime trace.

0.4.3 adds secondary mouse down/up handling (including `which`) and mouse-based
outside dismissal. Each group also has a visible three-dot action icon opened
with an ordinary left-click: beside Steering and below the quadrant. Both entry
points show the same scoped **Force takeover** menu. The global override stays
removed, and ownership still waits for native confirmation. Browser checks
suppress contextmenu while exercising actual mouse input; visible-icon, scoped
command, no-label-overlap, keyboard and stale-state tests pass. Release WASM
build passes with zero warnings/errors. Installed in Community2024 with all
seven payload hashes verified, 0.4.2 backed up, and both saved-profile files
unchanged. The user subsequently confirmed it works; 0.4.3 is the current passed
local EFB baseline.

The user also clarified the Baron has distinct pilot/left and copilot/right
altimeters. The accepted plan is now **independent per-instrument ownership by
seat**, with an optional explicit follow-guidance/link policy, instead of
automatically assigning both knobs to the AP owner. See the revised instrument
proposal below. No altimeter runtime or vanilla profile modifications have been
made. EFB menu recovery is the only new shipped behavior in this increment.

## Previous increment: 0.4.2 (2026-11-15)

The user confirmed 0.4.1 controls, Setup and mixture learning work, then reported
apparent lost learning after restart and requested removing the global immediate
override in favor of right-click **Force takeover** for only the clicked group.

Actual storage inspection found both persistent Baron slots and generation 11
with all eight expected mappings. Persistence worked; startup's independent
scale verification could stall at centres/endpoints or erase a saved scale
against delayed cockpit read-back. 0.4.2 restores mappings as learned, displaying
**Saved: move control to read position** until a matching current event arrives.
It never restores raw positions or freshness. No two-position relearning is
required on restart. AXIS/SET echo isolation and all normal alignment checks
remain. Changing bindings/inversion/devices requires explicit Relearn if the
event name is unchanged; the cache has no USB/MSFS-profile identity.

Verified actual storage:
`C:\Users\jorge\AppData\Roaming\Microsoft Flight Simulator 2024\WASM\MSFS2020\fscopilot-controls-test\work`.
Baron files are `pickup-885d39af-2.0.bin` / `.1.bin`; format remains unchanged.

The cockpit context menu sends bounded `force|steering|me` or `force|quadrant|me`
commands and cancels only that group's pending pickup. The other group's masks
and request stay intact; ownership colors wait for the simulator barrier. The
global button is gone. The old restore command remains protocol-compatible but
has no UI entry. Right-click, Shift+F10/context-menu key, Escape/outside dismissal,
offline/busy behavior and both group directions are covered by native/DOM/Chrome
tests. Release WASM build passes with zero warnings/errors. Installed in the
existing Community2024 package with seven payload hashes verified; 0.4.1 and
both saved slots are backed up in artifacts, and live saved files are unchanged.
Startup SET feedback before cache loading cannot replace a saved AXIS source.
Actual MSFS restart and EFB context-menu retest are pending. See the EFB README
for the current test steps; historical version notes below describe earlier UI.

Version **0.2.1** aligned takeover has passed the user's test in the **Black
Square Baron 58P**. The user confirmed it works and authorized continuing to the
next stage on 2026-11-14. The user selected **saving learned controls between
simulator sessions** as the next increment. The user confirmed cross-session
storage works in version 0.3.0, but reported that throttle learning remained
stuck. Version 0.3.1 fixes a source-switch reset and sets the requested default
alignment tolerance to 10% for both groups. The user has now confirmed that
everything works in 0.3.1; this is the current passed local baseline. The original experiment passed in
version 0.1.3. The user then requested Me turning yellow until hardware is within
tolerance, followed by automatic confirmed takeover. Exactly two ownership groups
remain: Steering and Engine quadrant. Multiplayer, device bindings and resync
remain later work.

The next increment is **sender-aware ownership networking**. The protocol in
`FsCopilot.Ownership` passes 13 contract scenarios. The existing direct/relay
transports now expose sender metadata, and an experimental host adapter passes
real loopback relay/client tests for transfer, accepted input and peer loss.
It is not enabled in the app startup or connected to the EFB/simulator yet.
Version **0.4.1** is the latest user-confirmed EFB control baseline. The user
accepted the 0.4.0 cockpit appearance, but Setup would not
scroll to the quadrant and the mixtures would not learn. The follow-up fixes
Setup scrolling, uses the verified Baron cockpit-mixture references and increases
native status publication from 2 Hz to 10 Hz. The user confirmed the 0.4.1
correction works. The active 0.4.2 persistence/menu increment is described above.
The ghost-quadrant/overlay idea is parked at the user's request.

The requested UI puts routine actions and alignment on one screen: no top
introductory text, select a pastel blue Me/red Mock seat then tap actual cockpit
controls, with owner-matched colors after confirmation. Quadrant hardware dots,
white cockpit marks and tolerance bands sit on the six drawn lever tracks. Yoke
input/target transforms, roll/pitch tracks and extra detected steering-axis
indicators show alignment on the diagram. Waiting adds yellow outlines and a
cancel action. Setup holds tolerance, learning/storage and diagnostics; immediate
takeover remains visible. Two ownership groups and native behavior are preserved.
Headless Chrome verifies 24 size/state combinations (600x600, 760x700, 1024x600,
420x560), plus four layouts with reserved EFB clock/navigation space, no main-view
scrolling, palette/ownership correspondence and actual
yoke/lever hit points. User must restart MSFS to check the installed EFB view.

### Follow-up fixes — 2026-11-15

- Setup has explicit top/right/bottom/left bounds and height (rather than relying
  on the `inset` shorthand), a fixed Close header and a separate flex scrolling
  body. Explicit wheel/touch handlers keep scrolling inside Setup. Real-browser
  tests scroll to the bottom mixture/quadrant diagnostics and verify updates do
  not reset the scroll position.
- Read-only live mixture capture found AXIS_MIXTURE_SET raw 16384 at full-rich,
  while generic engine mixture read-back was 75.51088%. The actual cockpit LVars
  were both 1.0. At a physical midpoint the engine values were 40.11333%, while
  both cockpit LVars were 0.53125. All 25 queried values and identity were received
  without errors. This confirms a wrong target reference, not evidence of another
  event-source reset. Why the generic engine correction differed from previous
  sessions was not determined. The 0.4.0 UI change did not alter learning logic.
- The agreed rule is to use actual cockpit control positions for learning,
  alignment markers and pickup. `control_targets.h` selects the verified 58P
  `bksq-aircraft-baronpropress` path and normalizes its two cockpit-mixture LVars;
  other aircraft keep standard lever references. No LVar/engine-state writes are
  added. New learning waits for identity; invalid targets prevent pickup. Saved
  format and the ±10% tolerance are unchanged. Add more aircraft mappings only
  after checking their cockpit references.
- Native status pushes at 10 Hz rather than 2 Hz. The EFB heartbeat remains
  500 ms. Unchanged text/selector writes are skipped in the UI. This addresses
  the reported sluggish display without changing graphics settings or command
  confirmation semantics.
- Native regressions cover captured mixture values, actual packet/identity
  selection, both mixtures learning/alignment and invalid-reference rejection.
  Browser wheel/touch and scroll-retention tests pass. WASM Release builds with
  zero warnings/errors. Existing controller, registration, persistence, package
  and cockpit interactions also pass. **Pending:** restart and check Setup
  scrolling, smoother markers and actual mixture pickup in MSFS.

**The original Windows/Baron suppression milestone passed in version 0.1.3.**
The user confirmed all acceptance checks on 2026-11-14. Version 0.2.0 added aligned
takeover; the user reported that alignment/attachment worked but learning was
flaky: roll could lose its learned status and both mixtures could stay unlearned.
Version 0.2.1 fixes learning persistence and recovery. Its build and local
regressions pass, and the user has now confirmed the corrected behavior works in
the simulator. Learning is session-only in that passed baseline: aircraft/module
reload and MSFS restart clear it. Version 0.3.0 adds per-aircraft persistent
mappings with fresh hardware readings; general device bindings remain future work.

Read [the prototype README](FsCopilot.Efb/README.md) for build and installation
instructions. This handoff records the broader requirements, technical context,
and priorities for the Codex session continuing the work.

## Repository and starting point

| Item | Value |
| --- | --- |
| User fork | <https://github.com/joje1234/FsCopilotJoje> |
| Working branch | `efb-control-test` |
| Prototype implementation commit | `9b99696` — Add local EFB control masking prototype |
| Original project | <https://github.com/yury-sch/FsCopilot> |
| Mac remote names | `origin` is the original project; `fork` is the user fork |
| Windows clone | Cloning the user fork normally calls that remote `origin` |

Fetch/pull the latest branch before starting; the handoff documentation is newer
than the prototype implementation commit. Preserve any subsequent Windows edits.
Push this work to the user fork, using the remote names actually configured in
that checkout. A pull request to the original author is a separate later action.

The Windows toolchain is now installed and verified (see below). The user
confirmed that the yoke, pedals, and quadrant are bound directly in MSFS rather
than through an external input utility. The hardware make/model is unknown. The
design should support different standard controllers rather than depend on one
brand.

## Agreed product requirements

FS Copilot already allows participants to share an aircraft across their local
MSFS instances. The desired improvements are an EFB app, explicit ownership of
flight controls, a way to detach conflicting hardware, controlled handovers, and
better recovery from instrument differences.

The user simplified the initial ownership design to **two groups**:

| Group | Controls transferred as one unit |
| --- | --- |
| Steering | Pitch, roll, rudder/nosewheel steering, left/right wheel brakes, and trim |
| Engine quadrant | Both throttles, both propeller controls, and both mixture controls in the Baron |

Each group belongs to the left pilot or right copilot. Individual engine levers
remain independently operable, but their ownership transfers together. Separate
owners for throttle, propeller, mixture, or individual engines would undo the
user's simplification. The implementation may track individual axes internally.

Hardware attachment must eventually be distinct from ownership. A pilot can own
the quadrant, detach their hardware, move it freely, then reconnect it after
alignment. The original issue was throttle hardware repeatedly reporting values
even when stationary and apparently fighting the other participant's input.
That cause is plausible, but has not been reproduced or confirmed in the Baron.

The EFB should show a cockpit from above, the two seats, and the main controls.
It should make ownership and hardware attachment obvious and provide control
management, key/button bindings, and a manual resync action. The first prototype
deliberately implements only the local mock-copilot experiment.

## Original immediate milestone — passed in 0.1.3

The user's latest implementation priority, paraphrased:

> Get the EFB working first. Assign steering or the quadrant to a mock copilot,
> then verify that my hardware no longer controls the airplane. If that works,
> we can move forward.

The Windows build, installation, and local aircraft experiment are complete.
The current extension is aligned takeover, as requested by the user. Do not
replace the experiment with a browser simulation or report input suppression
based only on a selected button or an event counter.

The initial test is solo, with the original FS Copilot desktop app closed. It
does not require the user's friend, aircraft YAML profiles, or network setup.

## What the prototype implements

All new implementation lives in [FsCopilot.Efb](FsCopilot.Efb/). The existing
desktop application, networking, aircraft definitions, and original WASM bridge
were left unchanged by the prototype commit.

| Component | Current behavior |
| --- | --- |
| EFB app | Cockpit overview; seat/graphic assignment, Setup, per-group right-click menu; connection/error state |
| Native module | Dedicated SimConnect connection; standard control event observation and masking |
| Diagnostics | Events seen/blocked and last event/value; per-axis observed input, cockpit target, calibration/freshness and alignment |
| Group changes | Remain pending until a matching simulator configuration-barrier reply arrives |
| Me | Arms native aligned pickup while keeping masks active; attaches automatically after the hold and confirmation |
| Force takeover | Right-click recovery override; returns only that group to Me without alignment |
| EFB closure | Does not release masks; the native module retains ownership |
| Aircraft loading | Requests a reset of both groups to `Me` and clears activity counters |
| Native failure | Closes its connection to release notification groups and reports an error |
| Browser preview | Same layout, explicitly disconnected; no fake simulator or real input suppression |

The event catalogue currently generates **173 bindings**, covering steering and
general/per-engine quadrant commands for engines 1–4. These names were checked
against the official MSFS event index. Their existence in documentation is not
proof that a particular aircraft uses them.

The original catalogue had 176 entries. The Windows runtime rejected the aliases
`AILERON_CENTER`, `AXIS_LEFT_BRAKE_LINEAR_SET`, and `AXIS_RIGHT_BRAKE_LINEAR_SET`;
version 0.1.2 removes them while retaining their already-listed canonical events.

The UI polls every 500 ms and treats the bridge as unavailable after three
seconds without an accepted status. Native configuration confirmation has a
five-second timeout; the UI stops waiting for a mutation after six seconds. A UI
timeout reports uncertainty and does not automatically resend the mutation.

### Important limitation of event masking

The module masks simulator **events**, not identified physical devices. Keyboard
and cockpit actions that emit the same events can also be blocked. Direct writes
to SimVars/LVars, aircraft-specific InputEvents, vendor utilities, or earlier
aircraft handlers may bypass it. This distinction is central to the experiment.

The configuration barrier is a `RequestSystemState("Sim")` reply on the module's
connection. It is used to defer confirmation until after configuration requests;
it is not a read-back proving that hardware can no longer change aircraft state.
The `Blocked` counter records events observed while a group has a confirmed mask;
it does not independently observe the aircraft's final response.

In the original 0.1.3 baseline, returning ownership to `Me` reconnected controls
immediately. Normal Me now waits for alignment; right-click **Force takeover**
deliberately bypasses it for that group. The mock copilot does not fly the aircraft, hold
a target position, or freeze the simulation.

## Files to read or edit

| File | Purpose |
| --- | --- |
| [FsCopilot.Efb/README.md](FsCopilot.Efb/README.md) | Build, install, test procedure, and prototype limits |
| [build.ps1](FsCopilot.Efb/build.ps1) | Windows MSBuild discovery, generation, native build, and ZIP packaging |
| [FsCopilotControls.vcxproj](FsCopilot.Efb/Wasm/FsCopilotControls.vcxproj) | C++ project requiring the `MSFS2024` platform toolset |
| [control_test.cpp](FsCopilot.Efb/Wasm/control_test.cpp) | SimConnect setup, event masks, CommBus, diagnostics, failure handling |
| [control_gate.h](FsCopilot.Efb/Wasm/control_gate.h) | Ownership changes and configuration confirmation state machine |
| [control_protocol.h](FsCopilot.Efb/Wasm/control_protocol.h) | Bounded command parser |
| [control_pickup.h](FsCopilot.Efb/Wasm/control_pickup.h) | Axis learning, current input readings and group alignment |
| [control_profile.h](FsCopilot.Efb/Wasm/control_profile.h) | Per-aircraft mapping storage, checksums and recovery |
| [events.json](FsCopilot.Efb/events.json) | Source event catalogue; edit this to change event coverage |
| [src/controller.js](FsCopilot.Efb/src/controller.js) | Commands, status validation, requests, revisions, liveness |
| [src/view.js](FsCopilot.Efb/src/view.js) | Cockpit drawing, group buttons, counters, and errors |
| [src/app.js](FsCopilot.Efb/src/app.js) | EFB runtime registration and lifecycle adapter |
| [src/controls.css](FsCopilot.Efb/src/controls.css) | UI styling |
| [scripts/package.mjs](FsCopilot.Efb/scripts/package.mjs) | Community package, WASM export checks, manifest and layout |
| [tests](FsCopilot.Efb/tests/) | Native contract tests, JS/controller tests, registration, DOM, and packaging tests |
| [FsCopilot.Ownership](FsCopilot.Ownership/) | Host-authoritative two-group protocol and bounded wire messages |
| [Ownership contract runner](FsCopilot.Ownership.Tests/Program.cs) | Serialized two-participant scenarios, with no simulator side effects |
| [Relay origin envelope](FsCopilot.Transport/RelayOriginFrame.cs) | Bounded server-stamped sender metadata shared by relay and client |
| [Ownership network host](FsCopilot/Network/OwnershipNetworkHost.cs) | Experimental source-aware transport adapter, with no simulator writes |
| [Network integration runner](FsCopilot.Network.Tests/Program.cs) | Actual relay/client loopback tests and legacy compatibility |

Two files are generated and checked into Git: `Wasm/control_events.h` and
`PackageSources/FsCopilotControls/FsCopilotControls.js`. Change their source files
and rerun the generators rather than editing only the generated output.

The plain JavaScript EFB adapter implements the runtime App contract using
`window.EFB_API` and `window.msfssdk`. It avoids an npm build dependency on the
SDK's EFB framework. This adapter also needs validation in the actual simulator;
the stub registration test cannot establish runtime compatibility. Its MIT
attribution is in `THIRD_PARTY_NOTICES.txt` and embedded in the generated bundle.

### Local prototype communication

The EFB sends CommBus event `FSC_CONTROLS_TEST_COMMAND`; the native module publishes
`FSC_CONTROLS_TEST_STATUS`. These are separate from the original bridge protocol.
Commands use the following bounded text format:

```text
2|client-id|request-id|read
2|client-id|request-id|set|steering|mock
2|client-id|request-id|set|quadrant|me
2|client-id|request-id|tolerance|quadrant|3
2|client-id|request-id|learn|quadrant|reset
2|client-id|request-id|restore
```

Status replies are JSON with protocol version, session, revision, client/request
IDs, readiness, pending state, the two confirmed mask states, errors, and activity
counters, plus `pickup` state for each group with waiting, tolerance, automation,
and axis markers. Version 2 rejects old bridges lacking alignment support. A Me
request is acknowledged when armed, with the confirmed mask still on; final
attachment remains conditional on alignment and the configuration barrier. The
UI ignores older revisions and retired bridge sessions. A periodic
status or reply for another client/request cannot acknowledge its pending change.
This is a local prototype protocol, not the future multiplayer ownership protocol.

## Windows build and installation

If the repository is not cloned yet:

```powershell
git clone --branch efb-control-test https://github.com/joje1234/FsCopilotJoje.git
cd FsCopilotJoje
code .
```

For an existing clean checkout of that branch, use `git pull --ff-only` to obtain
the handoff and latest changes. Inspect local work before switching branches.

Keep VS Code as the editor. The native build additionally needs Microsoft C++
build tools/MSBuild, a Windows SDK, and the SDK's **MSFS2024** platform toolset.
Install the build tools before the MSFS SDK. If the SDK was installed first,
rerun its installer after installing the development tools. Verify that it has
installed integration for the chosen Visual Studio/Build Tools installation;
MSBuild alone does not supply the MSFS toolset.

Download the SDK from **MSFS 2024 → Developer Mode → Help → SDK Installer**.
Enable Developer Mode in the game's settings first. The EFB package scripts need
Node.js 16 or later and have no npm dependencies. The original desktop app needs
.NET 9, but building that app is unnecessary for this standalone experiment.

From the repository root:

```powershell
node --version
git status --short --branch
.\FsCopilot.Efb\build.ps1
```

If automatic MSBuild discovery selects the wrong installation, pass its full path
using the script's `-MSBuildPath` argument. Resolve SDK/toolset errors against the
actual Windows installation; do not change `PlatformToolset` to a normal Windows
MSVC toolset just to make the project build. The required output is a WASM module.

A successful script run produces:

```text
artifacts/efb-control-test/fscopilot-controls-test/
artifacts/efb-control-test/fscopilot-controls-test.zip
```

Copy the package directory into the active MSFS 2024 Community folder. Its
`manifest.json` and `layout.json` must be directly inside that directory. Restart
the simulator and find **FS Copilot Controls** in the EFB. Alternatively, after
building, use the provided `EfbControlTest.xml` in DevMode. Avoid loading both a
mounted DevMode copy and a Community copy of the same test module.

## Baron acceptance test

Run the initial test on the ground with autopilot off and normal hardware
bindings active. Compare the same control under `Me`, `Mock copilot`, and `Me`
again, so a parking brake, control lock, or another condition cannot be mistaken
for successful suppression.

| Test | Required observation |
| --- | --- |
| EFB registration | App appears and opens in the actual MSFS EFB |
| Native connection | Status reaches `Simulator connected`; confirmed owners start as `Me` |
| Baseline input | Hardware normally operates the corresponding cockpit controls |
| Quadrant assigned to mock | Both throttles, propellers and mixtures stop responding to local hardware; steering remains available |
| Quadrant restored | The same hardware operates the quadrant again |
| Steering assigned to mock | Pitch, roll, rudder/nosewheel, brakes and trim stop responding; quadrant remains available |
| Both groups assigned to mock | Both groups are suppressed independently of the UI remaining visible |
| EFB reopened | Current ownership is recovered from the native module |
| Restore both | Both groups become operable again after confirmation |
| Aircraft reload | The module resets the newly loaded aircraft's groups to `Me` |

Check actual cockpit lever/control positions and, where necessary, the aircraft's
underlying variables. A selected `Mock copilot` button or increasing counter
alone is insufficient. Test all six Baron quadrant controls, not just one throttle.

Record the simulator and Baron versions, controller models if known, configured
MSFS bindings, input utilities, affected axes, last observed events, and counters.
Capture errors from the WASM/DevMode console and the build output when applicable.
Update this handoff with the real results before treating the milestone as passed.

### Diagnosing failures

- **App absent:** check the installed package root/layout, generated JS, and the
  matching `FsCopilotControls` internal name, folder, and filename. Check EFB
  registration errors; loading may be delayed.
- **App opens but stays disconnected:** verify the WASM module built with the
  correct SDK, is present under `modules`, loaded successfully, and exchanges
  CommBus messages. Browser preview mode is intentionally disconnected.
- **Control moves with no observed event:** investigate an uncovered standard
  event, aircraft-specific InputEvent, direct variable write, or earlier handler.
- **Blocked counter increases but control still moves:** inspect duplicate input
  paths, vendor software, handler priority, and subsequent variable writes. The
  counter measures our handler, not the final effect in the aircraft.
- **Configuration error:** preserve the exception/send/index details. The module
  closes its connection on failure. Do not make the UI claim success by removing
  its readiness checks.

Preserve the SDK's WASM dispatch convention: `SimConnect_CallDispatch` is registered
once in `module_init`. Do not apply the native desktop pumping pattern to WASM
without checking the SDK documentation.

If event masking cannot isolate the Baron's hardware path, investigate input
mapping/masking before the aircraft handler, or app-managed raw device bindings.
A dedicated MSFS controller profile with duplicate native bindings removed is a
possible fallback. Continue to observe detached physical axes for later alignment.
Filtering outgoing network packets or constantly overwriting aircraft state
would not establish that unwanted hardware input has been suppressed.

## Existing shared cockpit synchronization

These findings describe the original application, which the local EFB experiment
does not replace:

| Data path | Existing behavior |
| --- | --- |
| Physics and surfaces | Grouped packets from the pilot with global control, sampled at up to 20 Hz; unreliable sequenced delivery |
| YAML `master` entries | Individual variable updates from the global master, sampled at up to about 33 Hz per variable; unreliable sequenced delivery |
| YAML `shared` entries | Individual changes/events in both directions; reliable ordered transport |
| Instrument UI interactions | Instrument/element/event/value messages replayed on the receiving instrument |

The usual packet contains the source variable name and typed value. The receiving
side uses its own aircraft profile's `set` mapping; the setter expression is not
sent with every update. Some HTML instrument element IDs are generated from DOM
structure, so matching instrument versions/layouts matter.

SimConnect reads generally request `CHANGED` data. The original bridge's variable
watcher emits initial and changed values; repeating a watch registration does
not force it to resend unchanged values. Local Rx `Replay(1)` caches do not
automatically send a snapshot when a peer joins.

No general join/reconnect cockpit snapshot, periodic comparison/repair, or
acknowledgement that a simulator setter took effect was found. Ordinary variable
and UI packets do not carry timestamps/revisions for resolving cross-peer
conflicts. Physics/surface packets have local elapsed timestamps and receiver
offset estimation for interpolation. Echo suppression skips expected callbacks;
its expiry warning does not repair aircraft state.

Relevant original code: [Coordinator.cs](FsCopilot/Simulation/Coordinator.cs),
[MasterSwitch.cs](FsCopilot/Simulation/MasterSwitch.cs),
[Definitions.cs](FsCopilot/Simulation/Definitions.cs),
[SimClient.cs](FsCopilot/Connection/SimClient.cs), and
[the network layer](FsCopilot/Network/).

## Later architecture and milestones

The following work is planned after successful local input suppression. Defaults
such as comparison frequency and alignment tolerance are proposals to validate,
not already implemented behavior.

### Ownership and aircraft physics

Separate three responsibilities: who owns a control group, whether their hardware
is attached, and which simulator runs the authoritative aircraft physics. Both
pilots' accepted inputs must reach the physics host. For example, the left pilot
flies the yoke while the right pilot operates the entire quadrant, and one
simulator still produces the shared aircraft motion.

Do not use the current global `MasterSwitch.Join()` to give away only the
quadrant: that switches the whole simulator into its slave/freeze behavior.
Likewise, a broad local event mask cannot simply be dropped into multiplayer;
accepted remote commands must reach the simulator without being blocked or
recaptured into an echo loop. Source-aware input routing needs its own design.

A session coordinator should establish ownership and an agreed order of changes.
Ownership transitions need acknowledgement, participant identity, and a generation
number. Packets from a previous owner/generation must be rejected after transfer.
Duplicate paths through direct and relay transports must not apply an action
twice. A reconnect must negotiate current ownership and state rather than revive
old ownership claims or silently attach hardware.

### Ownership protocol foundation implemented

`FsCopilot.Ownership` is a dependency-free .NET 9 library. It models exactly two
groups and two explicitly negotiated participants. The physics host is fixed for
the lifetime of a session; a group transfer never calls `MasterSwitch.Join()`.
Every new session has a fresh GUID. Each group has an independent generation,
owner and attachment state. Full snapshots carry a monotonically increasing
session revision; replicas reject old revisions, changed sessions and ownership
changes that reuse a generation.

The transfer sequence is implemented as:

1. A known participant requests a group change against its current generation,
   with a monotonically increasing request number. Duplicate or obsolete requests
   cannot become valid again after a later transition.
2. The host stages a preparation and rejects input for that group. Both
   participants acknowledge that their local group input gate is suppressed.
   The incoming owner must also confirm alignment. Duplicate acknowledgement
   through another transport does not count as a second participant.
3. The host commits the new owner/generation after both acknowledgements. Hardware
   remains detached. The incoming adapter must recheck current alignment and
   acknowledge actual simulator attachment before input frames are accepted.
4. Frames require the transport-supplied owner identity, current session/group
   generation and a newer sequence. Axis IDs, engine count, finite normalized
   positions and group boundaries are validated before advancing the sequence.

These are absolute position samples, not reliable button actions. A future input
adapter must send complete current samples for its configured group rather than
rely on delivery of individual axis changes. Lower-sequence samples are dropped
as a whole. Relative actions require a separate reliable action protocol.

Cancellation or a five-second preparation timeout keeps the preceding owner but
invalidates the generation and leaves hardware detached. A subsequent attachment
needs explicit confirmation. Detaching an already attached owner also invalidates
in-flight input. Copilot loss revokes its groups to no owner; local host recovery
is explicit and still does not attach hardware automatically. Reconnection needs
a new negotiated session, not an old ownership snapshot. Host migration is not
implemented.

`OwnershipHostEndpoint` decodes bounded versioned JSON, applies policy and returns
either a changed snapshot or one accepted input frame. It performs no SimConnect
writes. All actual sender identities must come from established transport links,
not a sender claim inside the message. Production integration must serialize gate
updates, ownership changes and simulator application together, including checking
the generation again when queued simulator work executes.

Run the contract runner with a .NET 9 SDK:

```powershell
.\FsCopilot.Ownership.Tests\run.ps1
```

On this PC a SHA512-verified portable Microsoft SDK **9.0.321** is installed in
the workspace. Use:

```powershell
.\FsCopilot.Ownership.Tests\run.ps1 -DotNetPath .\artifacts\toolchain\dotnet-9.0.321\dotnet.exe
```

Results are saved to `artifacts/ownership-protocol/tests.log`. Release compilation
passes with zero warnings/errors. The 13 scenarios include 100 repeated
independent handovers and concurrent duplicate acknowledgements. Participants are
simulated through the real serializer/endpoint, not two live simulator clients.
No claim of a completed multiplayer flight milestone is made.

### Sender-aware transport integration implemented

`INetwork.StreamWithSource<T>()` now reports the established sender and Direct/
Relay route. The existing `Stream<T>()` API remains available. Direct delivery
uses the connection's peer tag; `HybridNetwork` merges the two attributable
streams, leaving session/generation/sequence deduplication to the protocol.
`PacketInbox` publishes both APIs without reflection-based observer invocation.

The relay/client share `FsCopilot.Transport.RelayOriginFrame`: a bounded versioned
envelope stamped by the server using its connection record. Clients continue to
send ordinary encoded payloads on data channel 1. Only the server emits origin
envelopes on channel 2; client attempts to supply that channel are discarded.
The client accepts envelopes only from its relay connection and for a linked
participant. Source fields inside an application packet are never trusted.

New clients advertise `origin=1`; the updated relay returns capability control
message 12 and envelopes data for those recipients. Old recipients still get
the original data format. A new client on an old relay can continue legacy sync,
but those packets are published only to the legacy stream, with no guessed
sender and no sender-aware ownership acceptance. The public relay has not been
deployed/changed; relayed ownership needs the matching updated server.

`OwnershipPacket` carries bounded validated protocol bytes through the existing
codecs. `OwnershipNetworkHost.RegisterProtocol` must run before peer connections
so schemas agree. The host adapter serializes incoming packets/local operations
on one event loop, publishes authoritative snapshots, emits accepted normalized
control frames once, handles preparation deadlines and revokes remote authority
when the known copilot link is lost. It makes no simulator writes. It is compiled
into the app but not instantiated by app startup; session negotiation and native
gate acknowledgements still need integration.

The network runner compiles the actual application network and server sources,
then exercises the real LiteNetLib relay/clients on loopback, including a third
participant, duplicate commands, fake origin injection, old-client/new-server and
new-client/old-server paths. Nine checks pass. Both actual app and relay server
build successfully: server zero warnings/errors; desktop four warnings in
untouched SetupViewModel, UiSounds and UpdateAvailableWindow code. The full NAT
hole-punch path and real two-computer flight remain untested.

```powershell
.\FsCopilot.Network.Tests\run.ps1 -DotNetPath .\artifacts\toolchain\dotnet-9.0.321\dotnet.exe
```

Results are under `artifacts/ownership-protocol/`. The runner contacts no public
server and changes no simulator controls.

### Next integration work

Session negotiation and independent-pair authority are implemented and tested;
next implement the simulator adapter and EFB/backend bridge.
Preparation acknowledgements
must wait for confirmed local masking; attachment acknowledgements must wait for
fresh alignment plus confirmed unmasking. Accepted remote inputs need a verified
path past local hardware masks without recapturing or rebroadcasting them as
hardware. Existing YAML/global-master synchronization must not overwrite the
group's accepted commands. Preserve one physics host independently of the two
group owners, with optional independent engine-pair ownership. Keep this mode
inactive until these paths are locally tested, then
perform a matching-version two-player Baron test.

### Autopilot and GTN discussion in progress

The user uses a **GTN750** and proposes keeping each participant's GTN independent
instead of relying on screen-press replay, with one participant able to claim
shared autopilot control. Manufacturer/version (PMS50 versus TDS GTNXi) and the
navigation-source policy are still being clarified. A read-only check of the
active Community2024 folder identified `pms50-instrument-gtn750`, title GTN750
Avionics, version **2.2.64**. The user wants universal ownership behavior and is
interested in flight-plan-only synchronization, with NAV/APR guidance claimable
independently of the physics host. Existing profiles/hooks have not been changed
for this proposal.

The bridge currently replays instrument interactions by instrument/element/event
IDs. The PMS50 module maps radio state and bezel H-events; some COM/CDI entries
are commented out pending echo handling. This does not provide a durable complete
GTN flight-plan/page snapshot. Do not promise periodic full-state repair without
verifying the specific product's available interfaces.

The proposed design separates two hardware ownership groups from an additional
AP command authority. One AP runs authoritatively on the physics host; agreed AP
modes, targets and annunciations need state read-back and explicit setters where
available. AP command ownership is separate from whose GTN supplies NAV/APR
guidance. The options posed to the user are (1) host GTN supplies guidance while
AP controls may transfer, or (2) claiming AP control also transfers guidance from
that pilot's own GTN, which requires additional navigation integration. Keeping
AP disconnect available to the steering pilot was proposed, not yet implemented.

The refined proposal is a claimable flight-guidance authority (AP plus NAV/APR),
defaulting to the physics host but transferable without moving physics authority.
Screens/pages may remain independent. Prefer synchronizing a verified navigation
snapshot (route/procedures, active leg/Direct-To, approach activation and selected
GPS/VLOC source) over screen-touch replay or repeatedly reloading the route.
The ownership rule is generic; reading/applying navigation state needs a verified
adapter. PMS50's integrator guide documents flight-plan/LNAV/VNAV exchange with
other WT instruments, which is a lead to investigate, not proof of a complete
cross-computer read/write API. This design is still under discussion.

### Barometer investigation and instrument ownership proposal (2026-11-15)

The user and a friend experienced runaway/oscillating altimeter barometric
settings with vanilla FS Copilot. They request investigation and a proposal for
exclusive instrument controls, especially baro following the AP/guidance owner.
This extends the guidance discussion; Steering/Quadrant remain the only two
hardware axis groups. No barometer runtime/profile changes were made for this
investigation, and EFB 0.4.2 is not a barometer/multiplayer fix.

Concrete evidence from the user's vanilla installation under
`C:\Users\jorge\Downloads\FsCopilot_v1.2.1`:

- `Definitions/bksq-aircraft-baronpropress.yaml:141-144` places both
  `L:INSTRUMENT_Altimeter_1/2` in `shared`. Their setters choose KOHLSMAN_INC when
  the incoming value is greater than current; every other value, including an
  equal target, emits KOHLSMAN_DEC. A large missed difference produces the same
  single adjustment as a small change. These are non-idempotent setters for a
  state replication path.
- That aircraft includes `modules/PMS50_GTN_650-750.yaml`, whose shared baro
  definition also writes an absolute setting from `A:KOHLSMAN SETTING MB:0`.
  Distinct routes can therefore write barometric settings. Actual indices and
  interactions with the Baron's cockpit implementation must be captured live.
- The installed vanilla `log` contains **25** expired Skip-counter warnings for
  these two altimeter LVars. These establish actual echo-suppression mismatches;
  they do not alone prove the complete causal sequence of the reported runaway.
- The current legacy `Coordinator` still sends shared state in both directions,
  uses count-based `Skip` suppression, and separately replays HTML interactions.
  `HtmlEvents` turns a received mouseup into mousedown/mouseup/click, with a
  `selfEmit` flag for direct DOM echoes. This is not rotary state reconciliation
  and should not coexist with authoritative baro sync for the same controls.
- Definition includes are concatenated, with no duplicate get-key validation.
  The existing `ignore` mechanism only filters outgoing HTML instrument events;
  it does not enforce per-player write ownership on all paths.
- An additional legacy issue to audit before expanding instrument control:
  `SimClient.Set(string,...)` can use the typed A/L/K setter and then fall through
  into calculator execution for the same write. This is separate from the
  Baron's shared expression branch, which calls Execute directly; do not claim
  it caused this incident without a trace.

`artifacts/instrument-sync/investigate-baro.cjs` evaluates the exact installed
profile expressions offline and records a SHA256, outcomes and warning examples
in `baro-findings.json`. It confirms equal targets decrement and small/large
increases emit the same one-step command. It never connects to or writes MSFS.

Recommended design, still a proposal:

1. User clarification accepted: the Baron's left altimeter belongs to the pilot
   and right altimeter to the copilot by default. Each instrument has its own
   pressure state and input owner, replicated to the matching instrument on both
   computers. Separate ownership does not mean unsynchronized copies of the same
   instrument. AP/guidance ownership stays separate; changing it does not silently
   seize the other pilot's baro knob or equalize their settings. Add optional
   explicit follow-guidance/linked ownership later through the generic authority
   policy. Control-group handovers do not change seat roles. This works with AP
   off and without transferring physics authority.
2. Only the authorized owner's commands reach the host. The physics host orders
   commands, applies a verified absolute pressure setting once and publishes
   confirmed state with session/authority generation/sequence. Replicas apply
   that state without turning read-back into new commands. Reject old-owner,
   duplicated, delayed and unauthorized packets. Use tolerance at simulator
   precision, not the axes' 10% pickup tolerance, and avoid repeated equal writes.
3. Keep one pressure representation, plus instrument index and STD state where
   supported. mbar/inHg presentation may remain local. Track left/right/standby
   altimeters separately; each has its own owner and setting. Initial aircraft support must verify actual
   cockpit pressure references and explicit indexed setters/read-back, not
   blindly adopt the existing index-0 alias.
4. Remove the old relative LVar setters and any overlapping click/H-event/state
   writers only when the replacement owns those controls. Stop retransmitting
   rotary clicks; use an absolute state snapshot for late join/reconnect or
   bounded repair. Route per-knob intents locally to the authorized command path.
5. Enforce read-only controls for the non-owner before local application, as
   well as in network validation. Cover mouse, wheel, keyboard/hardware and
   relevant custom InputEvent/H/L routes. Masking standard K-events alone does
   not guarantee a custom cockpit knob is locked. Both players retain live
   instrument indications. Do not mark a control locked until its local gate is
   confirmed; otherwise report that blocking is unsupported for that mapping.
   Retain the steering pilot's emergency AP disconnect policy from above.
6. Prototype with baro as the first instrument, then extend to AP targets and
   modes. Keep GTN page/touch state local as proposed; navigation guidance and
   flight-plan synchronization remain a separate adapter task.

SDK support: [indexed absolute KOHLSMAN_SET and barometric events](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-instrumentation-events/)
and [indexed pressure/STD read-back](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-system-variables/).
KOHLSMAN_SET documents pressure in millibars times 16 and an altimeter-index
parameter. The standard SimVar index starts at 1; aircraft mapping still needs
verification. These APIs establish feasibility, not tested Baron compatibility.

Acceptance before calling this fixed: log a full two-player baro trace, test
slow and fast rotations both directions, stop and observe no continued movement,
equal/duplicate snapshots produce no adjustment, owner handover rejects late
commands, non-owner input is blocked through each supported path, independent
indices stay independent, pressure agrees despite display units, reconnect
repairs state without replaying turns, and AP/altitude behavior stays correct.
No live two-player reproduction has been performed for this investigation.

Further index inspection after the user's clarification: the two aircraft
definitions use distinct LVar names and different numerical arguments to
KOHLSMAN_INC/DEC, but these arguments are not proof of separate instrument
targeting. The local SDK 1.8.2 template
`ModelBehaviorDefs/Asobo/Common/Inputs/Instrument_Inputs.xml:67-80` consumes the
INC/DEC argument as a step multiplier; its explicit Set path uses BARO_ID for
indexed KOHLSMAN_SET. The Baron may override this template, which has not been
verified. The generic index-0 baro writer is an additional possible coupling
path. Do not describe the present mapping as proven independent or directly
linked. The replacement must verify left/right identification, use explicit
per-index absolute setters, and prove that changing one leaves the other alone.
Input gates also need per-instrument discrimination; globally masking the common
baro events is not sufficient when each participant owns a different knob.

### Hardware alignment and pickup

The user clarified the preferred UI flow: **press Me once to request takeover,
then automatically finish when the hardware is aligned**, with a yellow waiting
state. A second takeover press is not the normal flow.

The intended handover is:

```text
Current owner remains active
    -> Incoming pilot presses Me (or the equivalent binding) for the group
    -> Me turns yellow: waiting for hardware alignment
    -> Incoming hardware remains detached while the controls are aligned
    -> All required bound axes stay within their configured tolerances
    -> Transfer is requested automatically, with no second button press
    -> Ownership transfer is acknowledged and committed as one group
    -> Me turns green: incoming hardware is active; outgoing inputs are rejected
```

Show a red indicator for each bound axis outside tolerance and green when it is
aligned. Include text and movement direction so color is not the only feedback.
The group stays yellow during alignment and backend confirmation; it turns green
only after attachment/ownership is confirmed. For the quadrant, all six bound
throttle/propeller/mixture axes must be ready together; ownership remains one group.
Provide cancellation of a pending request. Cancelling or losing fresh hardware
or target readings must not attach the hardware or claim successful takeover.

Keep monitoring physical hardware while it is detached. Readiness needs valid,
calibrated physical-axis readings and current cockpit targets; a stopped event
stream or cockpit position alone is not proof of alignment. If a target moves
or an axis leaves tolerance before commit, keep waiting and recheck. The current
0.1.3 prototype restored immediately. Version 0.2.0 implements this local flow
with MSFS event observations and cockpit read-back; raw device identity and the
general binding layer remain future work. The user explicitly reaffirmed that
Steering and Engine quadrant remain the only ownership groups.

Show physical position, current cockpit control position, and which direction to
move each bound lever. Compare against the corresponding lever/control position,
not engine N1, thrust, or another delayed engine response. If the outgoing pilot
moves a target during alignment, refresh readiness before committing the transfer.

Use configurable calibration, deadbands, inversion, detents, and a brief stable
alignment period. Small numeric tolerances were discussed, but no particular
value is an accepted requirement. Unbound axes retain their current settings and
remain operable through the cockpit. Combined-axis hardware must not silently
equalize different engine settings to make pickup possible.

Provide local detach/reposition/attach independently of ownership transfer. Keep
a deliberate immediate-takeover option distinct from normal aligned pickup.
Spring-centered steering, absolute quadrant levers, trim, and aircraft automation
need appropriate handling; do not use autopilot-driven surface deflection as a
universal hardware alignment target.

### General hardware bindings

Keep device configuration separate from aircraft command mappings. The hardware
layer discovers devices, captures axes/buttons, and calibrates them. The aircraft
adapter translates accepted normalized controls into the aircraft's correct
events or variables. Persistent device identity and reconnect behavior need
verification; do not depend only on changing joystick index numbers.

The future editor should support axis assignment by moving a control, keyboard
and hardware-button bindings for group requests/detach/takeover/resync, and clear
feedback about missing devices or conflicting mappings. Binding capture must not
operate the aircraft. Button actions must use the same ownership and alignment
rules as the EFB controls. Device reconnection must not silently attach an axis at
an unrelated physical position.

### State snapshots and resynchronization

Add an initial snapshot on join/reconnect and periodic comparison of durable
settings, with manual `Resync` using the same process. An initial comparison
interval around two seconds was proposed; tune it against simulator cost and
observed behavior. Use appropriate numeric tolerances and actual read-back.

SimConnect can provide grouped/periodic reads including unchanged values by
using suitable request flags and periods. The app still needs aircraft mappings
and a policy identifying whose value is authoritative. Readability does not imply
that a SimVar is directly writable or that writing it is the correct control path.

Classify profile entries as persistent state or one-off actions. Never bulk replay
all existing `set` expressions: clicks, toggles and increment/decrement commands
can change state again. Define safe correction methods per state, account for
aircraft automation, and report unresolved differences instead of repeatedly
fighting the aircraft.

Every durable value needs a revision, with ownership/session generation where
applicable. A snapshot must carry the revision of the state it represents; a
periodic scan must not assign a newer revision to an old reading. Ignore stale
snapshots, protect pending local writes, and account for simulator processing
delays and callback echoes.

For example, heading `090` at revision 41 must not overwrite heading `100` at
revision 42 just because the older snapshot arrived later. Agreed revision order
is sufficient for consistency without perfectly synchronized wall clocks. The
rule for nearly simultaneous changes on different computers must be explicit;
arrival order is not proof of the physically latest action.

A resync should show values checked, corrected and unresolved. It should preserve
ownership and hardware attachment and should not teleport the aircraft or replay
unrelated actions as a side effect of repairing a cockpit setting.

### Full EFB integration

Retain the overhead cockpit with the two seats and the two ownership groups.
Show confirmed ownership, attachment, alignment, pending transfers, connection
health and resync results. The desktop/native control service must continue
enforcement while the EFB is closed or suspended. On reopen, fetch current state;
disable commands when the backend is unavailable.

Future multiplayer transfer and failure policies need explicit treatment. On
remote loss, stop accepting stale remote commands and expose local takeover.
Reconnecting an old peer must not silently recover its previous authority. Keep
autopilot/autothrottle interaction separate from which human owns manual inputs.

### Delivery sequence

| Milestone | Completion criterion |
| --- | --- |
| Local EFB experiment | Actual Windows SDK build loads in MSFS; Baron hardware suppression and restoration verified for both groups |
| General input handling | Verified capture/suppression across the relevant device layouts and aircraft input paths |
| Group ownership protocol | Two real participants can split steering and quadrant without fighting inputs; transfers reject stale/duplicate commands |
| Alignment and bindings | Whole-quadrant pickup works without an unintended position jump; common actions can be bound |
| Snapshot and repair | Join/reconnect snapshots and manual/periodic repair correct differences while preserving newer changes |
| Integrated release | EFB lifecycle, reconnects, device changes, automation, direct/relay transport, and matching package versions pass joint tests |

## Verification already completed

The prototype implementation was checked on macOS with:

- Fifteen JavaScript controller/registration tests.
- A delayed EFB registration test covering late globals and a transient bus failure.
- DOM integration checks of buttons, pending/confirmed ownership, counters, restore,
  offline behavior and cleanup, using a temporary `linkedom` installation.
- Native module tests against SimConnect contract stubs, compiled with C++14 to
  LLVM bitcode and executed with LLVM's interpreter.
- Packaging tests that reject invalid modules and verify paths, required callback
  exports, licenses, layout entries and file sizes in a temporary directory.
- Generated-file consistency, project XML parsing, and a headless Chrome visual
  check of the disconnected UI preview.

Use the test commands in [the README](FsCopilot.Efb/README.md) when changing the
prototype. At the time of the macOS handoff there was no Windows SDK compilation,
installed Community package test, real EFB registration test, Baron input test,
or two-person flight test. The Windows update below supersedes the build status.

### Windows verification update — 2026-11-14

- Branch documentation is at `4e859df`; Windows source/build fixes remain local
  and uncommitted. No further product features have been added.
- MSFS 2024 SDK **1.8.2** is installed at `C:\MSFS 2024 SDK`.
- Visual Studio Build Tools **2022** was installed at `C:\BuildTools2022`
  alongside the existing 2026 installation. SDK integration required explicitly
  supplying its 2022 install directory and repairing the SDK's `VS2022` feature.
- Portable Node.js **26.13.1** is in
  `artifacts/toolchain/node-v26.13.1-win-x64/`; its archive SHA256 was checked
  against the official download checksums.
- The real SDK defines `HANDLE` as an integer. Replaced `nullptr` initialization
  and resets with zero initialization, and corrected the contract test stubs.
- `build.ps1` now selects an MSBuild installation with the MSFS2024 platform,
  refreshes a missing SDK environment variable, and accepts `-NodePath`.
  CSS line endings are normalized when bundling on Windows.
- Release WASM compilation and linking pass with **zero warnings and errors**.
  Controller, registration, package, DOM, and native contract tests pass on
  Windows. Native tests used MSVC C++14 with `/W3 /WX`.
- Required WASM exports, actual package layout sizes, ZIP root, and copied file
  SHA256 hashes were checked. The installed package is:
  `C:\Users\jorge\AppData\Roaming\Microsoft Flight Simulator 2024\Packages\Community2024\fscopilot-controls-test`.
- Build artifacts and the ZIP are under `artifacts/efb-control-test/`; a machine
  verification receipt is saved there as `verification.json`.
- To rebuild on this PC from the repository root:
  `.\FsCopilot.Efb\build.ps1 -NodePath .\artifacts\toolchain\node-v26.13.1-win-x64\node.exe`.
- **Runtime result:** the EFB app appears and opens. It receives the native error
  `Simulator did not confirm the change. Test released; reload to retry.` Both
  groups remain unconfirmed and all buttons stay disabled. No masking success
  is claimed. A read-only external SimConnect probe received the same `Sim`
  system-state reply successfully; this does not validate the WASM callback path.
- Version **0.1.1** defers status polling until the EFB view opens, waits for
  `SIMCONNECT_RECV_ID_OPEN` before observation setup, and starts the configuration
  watchdog only then. Startup/loading is a suspected cause, not yet proven.
  Timeout errors now include callback diagnostics; lifecycle messages also go to
  the DevMode console and `\\work\\controls-test.log`. Dispatch remains registered
  once as required by the WASM contract. Regression tests cover delayed startup,
  either EFB/OPEN ordering, late replies, timeout release, and EFB preloading.
- Version **0.1.1 runtime result:** the native log confirms `OPEN` and EFB status
  polling, then configuration failed with `SimConnect error 7 (send 6, index 2)`.
  The exact error was reproduced by an external, non-masking client: send 6 maps
  `AILERON_CENTER`, an unrecognized name. Checking the full catalogue found two
  more rejected aliases: `AXIS_LEFT_BRAKE_LINEAR_SET` and
  `AXIS_RIGHT_BRAKE_LINEAR_SET`. Their canonical names are already covered.
- Version **0.1.2** removes those three entries and tracks mapping packet IDs so
  future asynchronous errors include the event name. `tests/simulator_events.cpp`
  validates the actual catalogue against a running simulator without masking or
  transmitting controls. All **173** names, non-masking notification additions,
  group priorities, aircraft subscription, and confirmation passed that external
  check with zero failures. Native exception-correlation tests also pass.
- Version **0.1.2 runtime result:** the EFB reached `Simulator connected`.
  Selecting steering for the mock copilot then produced
  `SimConnect error 3 (send 354, index 2)` and closed the masking connection.
  Hardware still moved the simulator yoke after release, so suppression has not
  been established. The log confirms initial barrier 1 succeeded and barrier 2
  failed. Packet 354 corresponds to the first event addition after both clears.
- Version **0.1.3** maps each event again after clearing its notification group
  and before re-adding it. A bounded packet ledger identifies mapping, addition,
  clear, priority, and confirmation errors. Contract stubs now discard event IDs
  on clear and reject additions without valid mappings; repeated changes, restore,
  reset, and failure release tests pass. The external simulator check now tests
  four non-masking setup/rebuild cycles. MSFS closed before that live check could
  be run, so the revision needs runtime validation.
- **Version 0.1.3 external check completed:** once MSFS was running again,
  `simulator-events.exe` checked all 173 names across **4/4** initial/rebuilt
  configurations with **zero failures**. Masking remained disabled throughout;
  the probe used its own connection and did not alter EFB ownership. This
  validates repeated setup calls, independently of the user-reported yoke test.
- **Version 0.1.3 runtime result:** after restarting and retrying the steering
  handover, the user reported that it works. Record steering/yoke blocking in the
  actual Baron as confirmed by the user.
- **Version 0.1.3 follow-up passed:** the user confirmed the requested checks:
  steering back to **Me** restores the yoke; **Engine quadrant → Mock copilot**
  blocks both throttles, both propellers, and both mixtures while steering stays
  available; returning the quadrant to **Me** restores all six levers. Core group
  suppression and restoration in the Baron are therefore user-confirmed.
- **Original milestone complete:** the user subsequently reported "all checks
  good," confirming the remaining acceptance checks. Record version 0.1.3 as the
  passed baseline. Its installed package is backed up before the alignment build.
- **Version 0.2.0 alignment:** native requests keep the group masked while Me is
  yellow. Every required axis must match current cockpit read-back for 300 ms;
  only then is unmasking requested, and the existing barrier confirms attachment.
  Per-axis red/green markers and movement directions show readiness; ownership
  stays in two groups. Tolerance is selectable per group (1/2/3/5/10%, default 3%).
  Mock cancels a waiting request without attaching. The old restore operation is
  deliberately labelled **Immediate takeover both**, an explicit alignment bypass.
- Absolute axis events are observed while masked. While Me is active, stationary
  samples are checked against cockpit read-back to resolve conventional input
  scales/signs; ambiguous or inconsistent mappings remain unavailable. Both yoke
  axes and all levers for active engines are required; additional observed
  steering/brake/trim axes join the group. This first pickup build targets the
  fully bound Baron quadrant, not arbitrary unbound-device configurations.
  Inputs must have been observed since detaching and within 30 seconds; cockpit
  data must be under one second old. This is not raw device/presence detection.
  Calibration/requests reset on aircraft load. Steering pickup waits with AP on.
- New tests cover data-packet parsing, full native-to-JS status, calibration,
  group hold, target movement, stale data, cancellation and confirmation ordering.
  `tests/simulator_targets.cpp` checks the new position definitions read-only.
  The live read-only target probe subsequently received all 23 values with zero
  failures (two engines, AP off). A non-masking event capture observed
  AXIS_MIXTURE_SET raw 2432 with both cockpit mixtures at 57.03125%, consistent
  with signed 16k within the learner's tolerance. It did not capture full sweeps.
- **Version 0.2.1 learning fix:** the previous learner eliminated provisional
  scales permanently on mismatched read-back and could call multiple scales
  learned where their predictions happened to agree. This caused poisoning and
  position-dependent learned status. The fix recovers from bad provisional
  samples, requires one scale validated at separated positions (10% travel,
  three consistent readings), and freezes validated mappings through handovers.
  Only explicit Relearn or aircraft/module reload clears them. Small input
  chatter and endpoint overshoots are tolerated. Learned-axis transitions are
  logged. Native, native-to-JS status, controller, DOM, package and Windows WASM
  checks pass. **Version 0.2.1 runtime result:** after the requested restart and
  hardware retest, the user reported that it works and authorized continuing.
  Record this as the passed local learning/alignment baseline. This does not
  establish cross-session saved calibration or real multiplayer ownership.
- **Version 0.3.0 saved learning:** validated event scales/directions are stored
  per normalized AircraftLoaded path and engine count, using two alternating
  checksummed generations under the module's persistent work directory. Ownership,
  pending requests, raw values and freshness are never saved. Restored mappings
  need three fresh, unambiguous settled read-backs at a single position while Me
  is active; unchanged bindings no longer need a full learning sweep. Ambiguous
  centre/endpoints keep verification waiting. Changed events reset the affected
  mapping; changed scale/direction discards the hint and resumes normal learning.
  Explicit Relearn clears durable mappings for that group. Storage/identity
  failures leave session learning and ownership handling operational. Profiles
  are not USB-device-aware; variants/liveries may have different aircraft paths.
  Native restart/reload, corrupt files, changed bindings, fresh validation, durable
  reset and file-failure isolation tests pass. The live read-only probe returned
  `SimObjects\\Airplanes\\bksq-aircraft-baronpropress\\aircraft.CFG` and all 23
  position values with zero failures. **Pending:** learn once in 0.3.0, restart
  MSFS, verify restored mappings and repeat aligned handovers with the hardware.
  The 0.3.0 Release WASM build has zero warnings/errors. The Community package
  is installed and all seven payload file hashes match; layout and ZIP root were
  verified. The passed 0.2.1 package is backed up at
  `artifacts/efb-control-test/installed-backup-0.2.1-passed`. Restart MSFS to load
  the new module; the simulator was left running and no control inputs were sent
  by the external probe.
- **Version 0.3.0 user result:** storing mappings between sessions works. The
  throttles remained at "Learning: pause at two positions"; the user requested
  10% alignment tolerance. Runtime logs show roll, pitch, props and mixtures
  learning/revalidating, but no throttle learned transitions.
- **Version 0.3.1:** both groups now start at ±10% tolerance (the dropdown can
  still select a smaller band). Fixed a reproducible source-switch reset: AXIS
  streams take precedence over ordinary SET events addressing the same axis.
  SET feedback cannot replace a primary AXIS raw value, clear its learned/saved
  mapping, or refresh its observation timestamp. AXIS_SET_EX1 streams receive
  the same precedence. SET-only bindings remain supported; changing from an AXIS
  binding to SET-only requires explicit Relearn. The saved format is unchanged.
  Tests cover paired signed-axis/unsigned-SET streams, saved mapping verification
  amid SET feedback, stale input, SET-only learning and the 10% alignment band.
  All native/controller/registration/DOM/package checks and the Windows WASM
  build pass (zero warnings/errors). Read-only capture saw THROTTLE1_SET and
  THROTTLE2_SET raw 10624 with cockpit positions 64.8438%, but did not capture
  the requested multi-position sweep; it does not prove the user's root cause.
  **Pending:** restart, learn/verify the throttles while Me, then repeat quadrant
  Mock/Me pickup at 10%. Existing saved mappings are preserved.
- **Follow-up capture after the user's delayed diagnostic reply:** confirmed the
  exact alternating stream on the running old module: THROTTLE_AXIS_SET_EX1 raw
  9856/9984 is followed by THROTTLE1_SET and THROTTLE2_SET raw 13120/13184, with
  cockpit positions 80.4688/80.0781%. The old learner's settle timer repeatedly
  reset and its evidence stayed empty. A regression using those captured EX1/
  SET pairs now retains learning evidence through the chatter and learns both
  throttles after a second separated position. The installed 0.3.1 production
  code already contains this fix; no additional package change was needed.
  Restart MSFS to load it; actual aligned handover with it is still pending.
- **Version 0.3.1 user result:** the user subsequently reported "everything
  works," confirming the corrected throttle behavior and local alignment setup.
  Cross-session persistence was already confirmed in 0.3.0. Treat 0.3.1 as the
  passed local baseline; real multiplayer ownership remains future work.

All original acceptance checks and the 0.2.1 learning/alignment retest have passed.
For future regression checks, test by first moving
every bound axis while Me is active, pausing at low/mid/high positions and checking
that Input and Cockpit agree. Assign a group to Mock, move hardware away, press
Me, and verify yellow waiting with inputs still blocked. Align all required axes
and verify automatic attachment after the hold and confirmation. Repeat several
handovers without relearning; learned indicators must stay learned. Check cancel,
stale input/target handling, tolerance changes, EFB closure, and aircraft reset.
Keep the flight on the ground with AP off and the desktop app closed. Preserve
exactly two ownership groups; individual readings are alignment feedback only.

## First instructions for the Windows Codex session

Read this file and `FsCopilot.Efb/README.md`, inspect the current branch and local
changes, then check the installed build tools and MSFS SDK integration. Build the
existing standalone prototype, resolve build errors, install it, and validate
actual EFB loading and Baron input behavior with the user. Record concrete
results and remaining bypasses here. Continue to the next milestone after the
local hardware-suppression test succeeds and the user chooses to move forward.

## Reference documentation

- [MSFS 2024 SDK download menu](https://docs.flightsimulator.com/msfs2024/retail/devmode/menus/help/)
- [SDK setup and Visual Studio integration](https://docs.flightsimulator.com/msfs2024/flighting/introduction/sdk-overview/)
- [EFB API](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/efb/electronic-flight-bag-api/)
- [Event masking](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/events-and-data/simconnect_addclienteventtonotificationgroup/)
- [Input event APIs](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/inputevents/input-events/)
- [Grouped data requests](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/events-and-data/simconnect_requestdataonsimobject/)
- [Data definitions](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/events-and-data/simconnect_addtodatadefinition/)
- [CommBus](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/javascript/communication-api/)
