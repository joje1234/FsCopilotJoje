# EFB helper

**0.9.10 is installed with saved settings preserved.** It removes oversized
native initialization temporaries; actual MSFS startup confirmation is pending.
The working 0.9.8 rollback remains available; 0.9.9 failed before native startup.
The prior user-tested baseline is 0.9.8. The new
Force-input failure isolation and local read-only monitor are in WASM/UI.
Use the full matching helper/package/definitions kit for file/profile checks.
Keep learning and saved output proofs. Control update rates are unchanged.
Actual 0.9.10 simulator/hardware tests are pending.

**0.9.8 candidate is ready; installed remains 0.9.7.** It includes the corrected
definition-driven Nosewheel event sign and matching compiler/catalog. Keep
learning and update both complete kits before shared testing. Actual simulator
verification of the sign correction is pending.

The installed helper/package are **0.9.7**, with a verified 0.9.6 rollback.
Settings were subsequently backed up and cleared at the user's request for
a fresh wizard run. Trims now use ordinary bidirectional instrument sync,
independent of Steering ownership. Ordinary cockpit protocol v2 tracks which
peer adjustment a host receipt confirms; both helpers must match. Setup has a
confirmed local reset of learning/output checks without restarting MSFS.
The revised Steering output route and reset button still need MSFS validation.
Standard
AP modes/FD and heading/altitude/VS selectors now use the ordinary shared YAML
path, with either pilot allowed to adjust them. The older exclusive AP-owner
adapter below remains experimental/test-only. The supported GTN setup requires
Premium on both PCs. Screens stay local; route copying and selectable remote
GTN guidance remain pending. See the current root HANDOFF and EFB README.

The following notes describe older versions.

Current installed helper is **0.9.1**. For the next two-PC test, use the complete
`artifacts/two-player-ready/fscopilot-0.9.1-two-player-test.zip` on both PCs. Its
`Check setup.cmd` verifies the installed package and the files beside the actual
running helper, including the complete definition tree. A matching existing
0.9.1 helper can stay running. The kit's `Collect test logs.cmd` adds preflight,
kit identity and the filled test card to the existing helper/native log collector.
Source tools are in `FsCopilot.Efb/TestKit`. These scripts make no simulator or
network requests and do not upload anything. Actual two-PC tests are pending;
see the current status in [HANDOFF.md](../HANDOFF.md).

Source **0.8.0** adds a separate ordinary-instrument channel driven by the
complete vanilla definition tree and `Definitions/cockpit` selection policies.
Both indexed barometers remain normally shared. All matching definitions must
be kept beside the new helper. **0.8.0 is now installed locally** with a closed-state
0.7.4 rollback and unchanged learning/settings; **0.7.4 is still the last user-tested
baseline**. This source does not enable AP claims or GTN
flight-plan copying. Local SDK/loopback tests are not two-PC simulator proof.

`FsCopilot.Efb.exe` is a separate Windows background helper. Launching it without
arguments reads connection settings from the EFB. It contains the network and
native bridge sources, and does not include the Avalonia desktop application,
vanilla Coordinator, MasterSwitch, rotary synchronization or GTN click broadcaster.

**Previous tested deployment: 0.7.4 module and 0.7.4 helper.** Both PCs need the
complete matching Community/Helper/Definitions bundle, including the candidate
right-brake curve correction. The owned local helper runs from
`artifacts/setup-wizard/bundle/Helper` (PID recorded in `live-helper/helper-process.json`).
The update adds the native/EFB Setup wizard; helper networking remains unchanged.
The host's Auto policy is still applied after confirmed binding. The user
confirms the wizard works in the actual Baron. Restart reuse in 0.7.4 and the
new two-PC test remain unconfirmed, as does the friend's update. See
`artifacts/setup-wizard/installation.json` and `HANDOFF.md`.

The previous **0.7.2 Community-only update** used the old 0.7.0 helper. Its receipt
is retained at `artifacts/output-validation-cache/installation.json`; that pairing
has been superseded by the complete 0.7.3 update.

The **0.7.3 helper** writes per-run logs to `%LOCALAPPDATA%\FsCopilot\Efb\Logs`, with a
`latest-log.txt` pointer, version/PID, launch context, native masks/mapping state,
session identifiers/reasons and periodic world-transport counters. Logging adds
no simulator commands. Its actual published dry run from an unrelated working
directory verifies the fixed log location and clean exit without simulator or
network clients.

The accompanying `Collect logs.cmd` creates a diagnostic ZIP locally, with the
last three fixed-location helper logs, any legacy helper `log`, and this package's
native log. It excludes saved learning/settings. The actual CMD/PowerShell/ZIP
path passes isolated fixtures, including exclusion of planted `.bin` files.
It does not fetch logs remotely or send anything. Distribute all matching helper
files to both PCs together; different assembly versions must not be mixed in one
session.

Historical baseline: the **0.7.0 package** was installed; its preserved helper is in
`artifacts/definition-controls/bundle/Helper`. All seven installed hashes match,
saved learning is unchanged and 0.6.9 has a closed-state rollback copy. All four
parked output checks passed; the live module advertises capability 31 and all
controls are back on Me. A bounded helper launch passed live native discovery and
definition selection, reached WaitingForSetup and exited cleanly without joining.
Actual two-PC shared flight remains unverified. The user-passed 0.5.3 package is
also retained for rollback. See `artifacts/definition-controls/live-validation.json`.

This candidate includes an optional, separate AP client and shared-session
coordinator. AP startup requires a verified aircraft input adapter
on both PCs; the default Baron has no such provider yet. This does not enable
remote physical output, GTN screen synchronization or AP claims in the installed
EFB. See the latest [handoff](../HANDOFF.md) for validation and remaining work.

The intended setup is:

1. Start this helper on each PC, alongside the matching EFB package.
2. In EFB **Setup → Shared cockpit**, enter two different eight-character codes,
   choose Host/Join and your seat, then save. Either player can sit on either side.
3. Use the same server on both PCs. Default port 3600 tries direct transport and
   then relay; custom ports use the specified relay. Shared controls require an
   attributed transport. An older relay cannot silently enable shared ownership.
4. Select **Join shared session** on both EFBs. Saving settings never joins or
   changes hardware masks. Pending/active sessions lock connection edits.

Preferences are saved in the package's WASM work directory as alternating,
checksummed `connection-settings.0.bin` and `.1.bin`. They contain the codes,
server, initial physics role and seat. Learned control mappings remain in their
existing separate aircraft profiles. Ownership, consent and pending requests are
never restored from the settings file.

During a shared session, **Setup → Swap seats** requests a swap. The other pilot
accepts or declines; the requester can cancel. A confirmed swap changes both
seats without changing physics hosting or control ownership. The saved seat is
still the preference for the next join. Camera selection is local to each
simulator and reports confirmation in Setup; the Baron's actual views still
need live validation.

After a lost session, held guest physics must be restored with a scoped local
Me/Force action before rejoining. A helper heartbeat only updates UI feedback;
it cannot keep a native control lease alive or replace a handover proof. A second
normal helper exits, so double-clicking does not start another writer.

Build with .NET 9 and the repository's existing SDK DLLs:

```powershell
dotnet publish FsCopilot.Efb.Backend -c Release -p:NuGetAudit=false -o artifacts/efb-connection/backend
```

Distribute **all three** runtime files and the **Definitions folder** together: `FsCopilot.Efb.exe`,
`Microsoft.FlightSimulator.SimConnect.dll` and `SimConnect.dll`. The managed SDK
DLL must remain external to the single-file helper. Diagnostic symbols are not
needed for launch. `--efb-dry-run` validates startup without simulator/network
clients; `--run-seconds=5` bounds a normal launch. Explicit development connection
options remain available for diagnostics, but are unnecessary for ordinary use.

0.7.0 source selects aircraft control roles through the same
`FsCopilot.Definitions` library as the desktop app and checks the native catalog
fingerprint. Preserve the published `Definitions` folder, including `controls`
JSON files. `--efb-dry-run` validates those actual packaged profiles before
returning without simulator/network clients. Physical setters are guarded by
the native session controller; regular cockpit/altimeter definition streaming
is still pending. The installed 0.7.0 package passed the four local Baron output
checks; actual two-PC flight remains pending.

Current evidence is in `artifacts/efb-connection/verification.json`. Loopback
relay tests use the production C++ module with an SDK fake. They establish
protocol/control behavior, not actual two-PC flight or real Baron remote output.
