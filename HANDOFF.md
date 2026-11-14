# FS Copilot EFB plan and Windows handoff

The next task is to build and test the existing EFB prototype on Windows in the
**Black Square Baron 58P**. Assigning a control group to a mock copilot should
stop the local hardware from operating that group. The user wants this local
experiment working before proceeding with multiplayer ownership, alignment,
bindings, and synchronization repair.

**The Windows WASM build and actual aircraft behavior are unverified.** The
prototype has passed local logic, UI, and packaging tests on macOS. Passing those
tests does not establish that the MSFS runtime accepts the package or that the
Baron's input paths are successfully suppressed.

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

The user initially had only VS Code installed on Windows. Installation of the
C++ build tools and MSFS SDK has been discussed, but their successful setup has
not been reported. The hardware make/model is unknown. The design should support
different standard controllers rather than depend on one brand.

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

## Immediate milestone

The user's latest implementation priority, paraphrased:

> Get the EFB working first. Assign steering or the quadrant to a mock copilot,
> then verify that my hardware no longer controls the airplane. If that works,
> we can move forward.

Complete the Windows build, installation, and local aircraft experiment before
expanding the feature set. Fix build/runtime issues within this milestone. Do not
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
| EFB app | Cockpit overview; `Me` and `Mock copilot` buttons for each group; restore button; connection/error state |
| Native module | Dedicated SimConnect connection; standard control event observation and masking |
| Diagnostics | Events seen, events counted as blocked, and last event/value for each group |
| Group changes | Remain pending until a matching simulator configuration-barrier reply arrives |
| Restore | Changes both groups to `Me`; hardware reconnects immediately |
| EFB closure | Does not release masks; the native module retains ownership |
| Aircraft loading | Requests a reset of both groups to `Me` and clears activity counters |
| Native failure | Closes its connection to release notification groups and reports an error |
| Browser preview | Same layout, explicitly disconnected; no fake simulator or real input suppression |

The event catalogue currently generates **176 bindings**, covering steering and
general/per-engine quadrant commands for engines 1–4. These names were checked
against the official MSFS event index. Their existence in documentation is not
proof that a particular aircraft uses them.

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

Returning ownership to `Me` reconnects controls at their current physical
positions. There is no pickup/alignment protection yet. The mock copilot does not
fly the aircraft, hold a target position, or freeze the simulation.

## Files to read or edit

| File | Purpose |
| --- | --- |
| [FsCopilot.Efb/README.md](FsCopilot.Efb/README.md) | Build, install, test procedure, and prototype limits |
| [build.ps1](FsCopilot.Efb/build.ps1) | Windows MSBuild discovery, generation, native build, and ZIP packaging |
| [FsCopilotControls.vcxproj](FsCopilot.Efb/Wasm/FsCopilotControls.vcxproj) | C++ project requiring the `MSFS2024` platform toolset |
| [control_test.cpp](FsCopilot.Efb/Wasm/control_test.cpp) | SimConnect setup, event masks, CommBus, diagnostics, failure handling |
| [control_gate.h](FsCopilot.Efb/Wasm/control_gate.h) | Ownership changes and configuration confirmation state machine |
| [control_protocol.h](FsCopilot.Efb/Wasm/control_protocol.h) | Bounded command parser |
| [events.json](FsCopilot.Efb/events.json) | Source event catalogue; edit this to change event coverage |
| [src/controller.js](FsCopilot.Efb/src/controller.js) | Commands, status validation, requests, revisions, liveness |
| [src/view.js](FsCopilot.Efb/src/view.js) | Cockpit drawing, group buttons, counters, and errors |
| [src/app.js](FsCopilot.Efb/src/app.js) | EFB runtime registration and lifecycle adapter |
| [src/controls.css](FsCopilot.Efb/src/controls.css) | UI styling |
| [scripts/package.mjs](FsCopilot.Efb/scripts/package.mjs) | Community package, WASM export checks, manifest and layout |
| [tests](FsCopilot.Efb/tests/) | Native contract tests, JS/controller tests, registration, DOM, and packaging tests |

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
1|client-id|request-id|read
1|client-id|request-id|set|steering|mock
1|client-id|request-id|set|quadrant|me
1|client-id|request-id|restore
```

Status replies are JSON with protocol version, session, revision, client/request
IDs, readiness, pending state, the two confirmed mask states, errors, and activity
counters. The UI ignores older revisions and retired bridge sessions. A periodic
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

### Quadrant alignment and pickup

The intended handover is:

```text
Current owner remains active
    -> Incoming pilot requests the complete quadrant
    -> Incoming hardware remains detached while levers are aligned
    -> All bound axes are ready within their tolerances
    -> Pilot presses the takeover button or binding
    -> Ownership transfer is acknowledged and committed as one group
    -> Incoming hardware becomes active; outgoing inputs are rejected
```

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
prototype. There was no Windows SDK compilation, installed Community package
test, real EFB registration test, Baron input test, or two-person flight test.

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
