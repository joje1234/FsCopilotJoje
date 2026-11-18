# Native EFB bridge

`FsCopilot.Native` defines a fixed 1,024-byte little-endian packet and a fresh
instance/challenge monitor. `FsCopilot.Native.Windows` implements the actual
SimConnect transport. `FsCopilot.Native.Probe` is a diagnostic console client.
The desktop app references the library. Its explicit `--efb-backend` mode now
starts the consent-bound session service; ordinary desktop startup retains the
vanilla flow. The installed solo EFB package still cannot enable shared ownership.

The dedicated `FsCopilot.Efb.Backend` project now provides `FsCopilot.Efb.exe`,
which starts directly from EFB-saved connection preferences without constructing
the desktop application. The discovery packet's optional connection API 1 uses
bytes 808–999 for those preferences and typed backend UI status. Kind 4 reports
only helper status; instance/configuration/sequence fences and a three-second
expiry apply. It cannot affect controls, consent, native leases or proof acks.
Zero-extension packets remain compatible with installed solo 0.5.3. Session
commands continue to use the separate consent-bound channel. See the
[helper README](../FsCopilot.Efb.Backend/README.md) and
`artifacts/efb-connection/verification.json`; actual new-WASM/two-PC validation is pending.

The EFB WASM module creates three named ClientData areas, ending in
`NATIVE_STATUS_V1`, `NATIVE_COMMAND_V1`, `NATIVE_REPLY_V1`. The native version is
independent of EFB UI protocol v5 and .NET authority protocol v3. The channel
accepts Hello/discovery and UI-only helper notices. Ownership/input commands use
the separate consent-bound channel below. The Windows client's default mode only observes.
A Hello starts the same non-masking
observation used by opening the EFB, preserving existing ownership and requests.

Status includes native identity, normalized aircraft path, adapter, engine count,
four mask/tolerance values (preset percentages 10/20/30), global quadrant Auto mask (0 or 14), required/learned/
fresh/aligned axis masks and all 21 input/cockpit positions. Unknown positions
are -1. Capabilities currently equal **11** (observation, confirmed masks,
independent engine pairs). **Isolated remote input (4) is deliberately absent**;
this cannot yet satisfy the multiplayer negotiation's required capabilities.

The native module registers one WASM dispatch callback. Optional IPC calls have
their own error tracking; ordered SDK checkpoints bound the pending call table.
An IPC error or stalled checkpoint disables the link while retaining current
control masks. Unknown control SDK errors keep the existing release behavior.
ClientData areas persist after their creator closes; a cached packet alone is
insufficient. Each Windows connection uses fresh challenges. Native identity,
reply and observation freshness, monotonic serials, retired instances and
conflicting responders are checked before reporting a live link.

Build/run the managed diagnostic, with MSFS and the matching EFB package loaded:

```powershell
$dotnet = '.\artifacts\toolchain\dotnet-9.0.321\dotnet.exe'
& $dotnet build FsCopilot.Native.Probe -c Release -p:NuGetAudit=false
& $dotnet FsCopilot.Native.Probe/bin/Release/net9.0/FsCopilot.Native.Probe.dll --seconds=30
```

Contract runner (builds and runs the native packet generators before C#, then
checks C# packets with the native parser; SDK/input providers are simulated):

```powershell
./FsCopilot.Native.Tests/run.ps1 -DotNetPath $dotnet
```

For isolated SDK transport validation, build
`FsCopilot.Efb/tests/bridge_transport_probe.cpp` against the installed SDK using
the same MSVC command as the other native probes. Then run:

```powershell
& .\FsCopilot.Native.Probe\verify-transport.ps1 -DotNetPath $dotnet
```

This uses the production C++ transport and C# client with fixture metadata under
unique `FSC_TEST_...` areas. It asserts exact values, live discovery and expiry
after the server exits despite retained data. No aircraft/control writes occur.
Both processes must exit 0. Results are in `artifacts/native-bridge/`.
This verifies the live SDK transport, not installed WASM callback delivery or
actual hardware data; those remain separate simulator checks after installation.

The separate installed-WASM check passed in the Baron cockpit on 0.5.3: a fresh
challenge/reply and three Ready observations identified the two-engine Baron
adapter and current hardware/mask data. See
`artifacts/native-bridge/installed-wasm-0.5.3-restarted-direct.log`. Running the
probe outside the sandbox was necessary for the local SimConnect COM connection.
The user also confirmed the tolerance presets work. No aircraft/control writes
were performed; real multiplayer control enforcement remains disabled.

The current source adds a separate consent-bound session command channel:
`FSC_CONTROLS_NATIVE_SESSION_STATUS_V1` and `...SESSION_COMMAND_V1`. Native local
CommBus consent creates a fresh token; Bind confirms hardware masks before
starting a session. State/Prepare/Attach use authority generations and exact
pending/committed ticket identities. Preparation drains prior SDK writes, and
incoming pickup/attachment holds alignment for 300 ms. Lease loss preserves
masks; heartbeat never overwrites an outstanding proof. Cancellation cannot
reuse a prepared same-owner transfer. See `control_session_protocol.h` and
`control_session.h` for the fixed 1,024-byte contract and controller.

`NativeControlClient(enableSessions: true)` opts into that separate channel.
It queues copied packets, rechecks their current binding/ownership before SDK
submission and drops expired samples. Default construction and the canonical
diagnostic remain read-only, compatible with installed 0.5.3. Session mode needs
the new candidate; it is not available in installed 0.5.3. No client creates areas.

Actual-module tests capture hardware-mask and output SDK calls in the fake. The
separate candidate builds under `artifacts/native-session/candidate/` and is not
installed. `NativeAuthorityCoordinator` and the actual
`NativeControlAuthorityBridge` now connect source-aware authority, native proofs
and the final accepted-input fence. `INativeSessionClient` is implemented by the
Windows client. EFB join/intent routing and explicit backend startup are now
implemented. Fixed-host world/cockpit replication is now connected in current
source; actual WASM remote-axis and two-PC flight validation remain.
Capability 4 remains unadvertised.

The integration runner uses the real relay/negotiation and two binary
subprocesses containing the actual C++ module with the SDK fake. It checks
independent pickup/owner return, scoped Force, mixed whole-quadrant transfer,
roll/pitch/rudder, AP interlock, native loss and application-heartbeat expiry.
The fixture now captures the production native physics/cockpit SDK writes and
models their readbacks; manual target mirroring is removed. This is not a real
two-PC flight test. Run:

```powershell
& .\FsCopilot.Network.Tests\run.ps1 -DotNetPath $dotnet
```

Latest candidate: `artifacts/native-world/candidate/`. Native transport is
30 Hz in shared mode; the EFB display remains 10 Hz. AP engagement is a status-only
session flag and blocks steering from fighting the host AP. AP setting/guidance
synchronization remains separate unfinished work.

The preceding coordinator candidate's consolidated receipt is
`artifacts/native-coordinator/verification.json`. Its rebuilt isolated SDK
fixture and the installed 0.5.3 read-only Baron discovery both passed; live logs
are in `artifacts/native-coordinator/live/`. All seven installed payload hashes
still match the accepted solo build. These live checks performed no aircraft
control writes and do not establish real multiplayer execution.

The EFB increment adds local Join/Leave actions and a bounded native intention
FIFO. Shared Me/Other/Force/Cancel commands pass through the real CommBus parser;
the coordinator reads each head once and acknowledges it separately from native
handover proofs. Cancel can stop an attachment after authority commit and keeps
hardware suppressed until explicit pickup. Shared graphics use confirmed owners
even during mask changes. The solo Auto writer remains suspended in shared mode.
Receipt: `artifacts/efb-session/verification.json`; full real-relay/native and
browser evidence remains separate from real aircraft/two-PC validation.

The latest shared Auto increment uses one host-confirmed quadrant policy,
changeable from either EFB. Each engine pair independently arms only after its
local hardware moves more than 2% from an anchored position; fresh calibrated
input is required. First samples, exact 2% movement, cockpit/world feedback and
Steering never arm pickup. Alignment still uses each pair's tolerance and the
existing native suppression, 300 ms preparation and attachment proofs. A scoped
cancel resets the hardware anchor. Auto off removes automatic waits/transactions
while retaining manual requests and current owners. A cancelled transaction that
already suppressed hardware retains that suppression until explicit pickup.
Authority protocol v3 distinguishes automatic requests from manual requests.
Native session offsets 968/972 carry the confirmed policy and local waiting
mask; feedback commands cannot move controls, renew a lease or replace a native
proof. `AutoOn`/`AutoOff` EFB intentions require whole-quadrant scope. The UI shows
the shared policy only after the native state reflects host confirmation.
Receipt: `artifacts/shared-auto/verification.json`. Installed solo 0.5.3 and the
remote-input capability gate remain unchanged; actual two-PC validation is pending.

`NativeSharedSessionService` now watches fresh local EFB consent, establishes the
configured relay peer, negotiates seats/physics role and owns coordinator shutdown.
Consumed native consent cannot restart after a changed SDK backend GUID. New
sessions require a new local Join. Actual relay tests exercise service startup,
remote native output under the SDK fake, Leave, retained masks, fresh rejoin and
old-session/retained-native rejection.

The published Windows executable is in `artifacts/shared-startup/backend/`.
Its native `SimConnect.dll` remains an external published file. Runtime trimming
is disabled to preserve reflection-based JSON contracts/views. The published
executable passed actual installed-0.5.3 discovery in observation-only mode;
the separate live SDK command fixture still passes. Receipt:
`artifacts/shared-startup/verification.json`. World/physics synchronization is
now connected in source as described below; this experimental backend is not
installed for flight testing.

Session-enabled clients defer mapping session areas until fresh discovery offers
remote-output capability 4, so installed solo packages remain observable without
missing-area SDK errors. Isolated `FSC_TEST_...` fixtures map their known areas
directly; this does not advertise that capability in a real package.

The fixed-host world stream carries 12 explicit-unit position, attitude and
velocity values plus normalized cockpit targets. It is a single 334-byte encoded
`SharedWorldPacket`, source/session/epoch checked before the native guest writes.
World command kind 10 uses session bytes 864..959 and serial 960..967; size remains
1,024 bytes. Status flags 16/32/64 mean world mode, fresh data and confirmed owned
freeze. World samples do not acknowledge handovers or renew the native lease.

Only the guest freezes; bind waits for confirmed hardware masks, a fresh freeze
checkpoint and readback. Masked cockpit targets remain current during pickup,
while actively local controls are never mirrored. A missing world stream retains
freezes/masks. Scoped local Me/Force restores the original freeze settings before
attaching only the selected hardware group. Source: `control_world.h` and
`control_session.h`. Definitions follow the SDK's
[aircraft variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-miscellaneous-variables/) and
[world velocity variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/miscellaneous-variables/).

The actual SDK reader and a user-authorized stationary-ground same-state write /
freeze / restoration check passed, with zero SDK errors and freeze flags restored
to zero. The ground test changes no hardware masks or axes and refuses airborne,
moving (above 0.1 knots) or already-frozen aircraft. This does not establish actual
WASM remote-axis behavior or real two-PC flight. Logs/receipt:
`artifacts/native-world/verification.json`. The accepted 0.5.3 install is unchanged.

The isolated SDK fixture also verifies actual bind/state/commit-ticket/input
bytes through the queued C# writer and production C++ transport, followed by
expiry. It never configures hardware masks or writes aircraft controls:

```powershell
& .\FsCopilot.Native.Probe\verify-transport.ps1 -DotNetPath $dotnet -Sessions `
    -NativeExePath artifacts/native-session/tests/bridge_transport_probe.exe
```

Receipt/logs: `artifacts/native-session/live/`. Both exits were 0 with no SDK
failures. This proves transport of fixture commands; real installed-WASM remote
execution, native/authority coordination and two-player behavior still need tests.

References: [ClientData creation/persistence](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_CreateClientData.htm),
[WASM communication and dispatch](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/wasm/communication-api/communication-api/).

## Seat confirmation (0.6.2 source; not installed)

Authority protocol v4 carries unique current pilot/copilot seats independently
of physics hosting and physical control ownership. EFB Swap seats submits a
revision-bound request through the native FIFO. Only the other participant can
accept or decline; the requester can cancel. The host commits both seats
together, with a 15-second timeout and cancellation on session loss. It does not
change group generations, attached controls, Auto or pressure values.

The fixed 1024-byte session packet uses the following former reserved bytes:

| Offset | Field |
| --- | --- |
| 976 | Seat revision, uint64 |
| 984 | Local seat, uint32: 0 unknown, 1 pilot/left, 2 copilot/right |
| 988 | Pending request, uint32: 0 none, 1 local, 2 other player |
| 992 | Host-issued seat request ticket, uint64 |
| 1000 | FIFO intent's expected seat revision, uint64 |
| 1008 | FIFO intent's seat ticket, uint64 |
| 1016 | Eight reserved zero bytes |

Intent actions 7–10 are seat request/accept/decline/cancel, with control selection
zero. Status and UI-only Feedback carry current seat fields; only Status may
carry a FIFO seat intent. Seat revisions and tickets are bounded to uint32 for
the EFB representation. Feedback cannot change native masks or renew a lease.
Connection API 1 advertises that session areas exist even with output capability
4 off; opening those areas does not enable shared authority.

`control_camera.h` is a separate local camera adapter. It selects default Pilot
or Copilot views only after saved preference or confirmed-seat changes, defers
outside the cockpit, and requires a checkpoint and three matching read-backs.
Failure affects camera feedback only. The SDK's default view indices still need
verification in the actual Baron; see [camera variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/camera-variables/).
Actual two-PC, embedded remote-output and Baron camera checks remain pending.
Receipt: `artifacts/seat-authority/verification.json`.

## Altimeter observations and SDK adapter (0.6.3 source; not installed)

The last 24 former reserved bytes of the 1024-byte discovery packet contain:

| Offset | Field |
| --- | --- |
| 1000 | Altimeter 1 tuned pressure, uint32, millibars × 16 |
| 1004 | Altimeter 2 tuned pressure, uint32, millibars × 16 |
| 1008 | STD mask, uint32, bits 0/1 for indices 1/2 |
| 1012 | Fresh flag, uint32, 0 or 1 |
| 1016 | Altimeter sample serial, uint64 |

A zero serial requires zero data/flags. With a serial, both pressures must be
8000–19200 (500–1200 mbar). Stale samples retain values with Fresh false; neither
stale data nor a UI report grants write ownership. Hello and backend report
packets cannot contain altimeter observations. Old zero-extended packets remain
readable; a new module needs the matched helper when these fields are nonzero.

`control_altimeters.h` reads `KOHLSMAN SETTING MB EX1` and STD per index, keeping
tuned QNH even while STD is active. Setup displays these as local observations.
Its absolute setter is currently used only by local SDK tests. No production
session/UI command calls it, and barometer synchronization/input blocking is not
enabled. Seat-owned authority and a verified input adapter are the next step.
The real Baron indices and custom knob routes still need live capture.
Confirming the requested index does not establish that the aircraft leaves its
other index unchanged. Verify that independence in the actual aircraft before
using this adapter for per-instrument ownership.

The first native/relay regression attempt timed out during heartbeat-fixture
startup. A rerun with added diagnostics passes; the cause remains unresolved.
Receipt and both logs: `artifacts/altimeter-sdk/verification.json`.

## Seat-owned altimeter authority (0.6.4 core; native writes not connected)

Authority protocol v5 adds independent instrument state and request messages.
The ownership model and the real relay use the current pilot/copilot seats,
not physics hosting or control-group owners. Left/index 1 and right/index 2
remain distinct settings, retaining tuned QNH and STD across a seat swap.
The actual Baron's index/knob mappings are still unverified.

`ControlAuthoritySession.Altimeters` exposes the separate instrument revision.
`Publication` captures it and physical-control state under one lock so a seat
commit cannot mix old control metadata with new instrument owners. Host-only
initialization needs two verified gate/baseline proofs before accepting input.
Requests include session, epoch, preparation token, sequence and index; native
confirmation must be newer than the dispatch baseline and latest known host
sample. Equal state does not dispatch. Superseded work cannot commit, and SDK
failures stop instrument synchronization without reassigning flight controls.

An accepted seat swap first drains pressure work and obtains both-mask proofs
plus a fresh host snapshot. It then commits both seats and rechecks the new
owners' baselines. Cancellation/timeout preserves the settings. Native SeatPending
3/4 means a local/remote request is accepted but waiting for instrument proof;
Cancel/Decline remains scoped to that request. Shared UI JSON includes the
session ID and monotonic native status serial to reject delayed displays.

The SDK adapter supports observe-only confirmation and superseding pending waits.
Do not write an old target onto the host owner's moving knob or overwrite a
guest owner's newer input with an old canonical acknowledgement. Seed behind
both input masks; apply absolute output only to a remote-owned index.

Native command/ack plumbing, verified per-knob gates, recovery feedback and actual
Baron/two-PC checks are still required. Production does not start this instrument
model or expose the SDK setter. The loopback authority test uses simulated gate
and SDK-completion proofs, while the existing physical-control cases exercise
the actual C++ controller under SDK implementations. These are different scopes.
Receipt: `artifacts/altimeter-authority/verification.json`.

## Native instrument command transport (0.6.5 candidate)

The native session now accepts kind 12 instrument commands only inside a current
consent/session binding. A kind-specific 96-byte body occupies the command's
world-data region; status still carries actual world values there. The last
eight status bytes hold independent 32-bit gate and pressure acknowledgements,
each packing a 29-bit native command ID and three-bit result. The flight-control
acknowledgement is unchanged. Instrument commands do not renew its two-second
lease. Gate and pressure operations can finish independently during quiescing.

Discovery's barometer flags now distinguish fresh data (1), transport (2),
verified native aircraft profile (4), current gate proof (8), and blocked input
bits (16/32). Profile requires transport; gate readiness requires profile and
fresh indexed pressures. Hello/backend reports cannot carry this metadata.
The Windows queue rechecks these observations, the current physical revision
and the confirmed seat at execution. Native validation repeats those fences,
requires both initial baselines while both knobs are masked, prevents writing
the local seat's owned knob, and rejects index/epoch/sequence replays.

The SDK adapter waits for a new pressure sample after a queued write checkpoint
before starting a replacement. Returning to the original pressure therefore
cannot become a no-op based on cached pre-write data. Session end retains input
holds; rejoin waits for explicit release and fresh read-back. A provider failure
during release restores the hold. Instrument failures preserve physical-control
ownership and acknowledgements.

Production has no verified Baron knob input provider and advertises transport
only. It rejects setters as unsupported. The backend instrument coordinator and
an instrument-only recovery flow remain unfinished; no production caller starts
the instrument authority model. C#/C++ byte fixtures, integrated native-session
tests and simulated SDK failures exercise this code, not actual Baron behavior.
Receipt: `artifacts/altimeter-native-command/verification.json`.

## Backend instrument coordinator (0.6.6 candidate)

`NativeAltimeterCoordinator` shares the native control actor and command counter.
Availability preflight requires both peers' fresh supported adapter reports.
The host masks both knobs before capturing the initial pressure seed. Instrument
protocol v2 adds Attaching after both masked baseline proofs and before Active,
so movement during a delayed final state update cannot be reset by initialization.

The host observes its own seat's knob without echo and applies the other owner's
absolute indexed settings. The guest mirrors only its non-owned index. A native
value confirmation waits for fresh matching pressure data even if its ack arrived
first. Queue drops re-enter the model's exact pending-frame fence without extending
its deadline; a retired or undispatched value cannot use the retry path. Adapter
loss holds instruments without changing physical ownership. One local recovery
action after session end waits for both instrument-input and physics read-back.

Production still advertises no verified Baron input profile. The end-to-end relay
test opts into a simulated input provider in the native subprocess only. It tests
the production coordinator, session, SDK setter and wire, rather than accepting
synthetic completion DTOs. Actual custom rotary routing, EFB instrument recovery
UI and two-PC simulator validation remain unfinished. Run the integrated cases:

```powershell
./FsCopilot.Network.Tests/run.ps1 -DotNetPath $dotnet -InstrumentsOnly
```

Receipt: `artifacts/altimeter-coordinator/verification.json`.

## Instrument UI and local recovery (0.6.7 candidate)

Instrument protocol v3 adds terminal Local policy and an attributed stop request.
Either session member can retire synchronization while physical ownership and
the shared flight continue. Local policy permits seat confirmation immediately;
old pressure requests and confirmations cannot revive it. Native per-peer input
recovery remains explicit and is not inferred from the policy change.

The local-only action is available only through EFB CommBus and is scoped to the
displayed shared-session GUID. It cancels unstarted output, preserves queued SDK
checkpoints, confirms native mask release plus a later read-back, and retires
backend instrument commands until the next explicit join. Known input masks
can be released even if the aircraft tuning profile is no longer verified.
Unconfirmed recovery stays held and offers retry; it never resets other controls
or restores guest physics as a side effect.

Discovery flags add LocalOnly=64, Recovering=128, Held=256 and InputReady=512.
InputReady describes native mask state, independently of the stronger verified
aircraft profile and bound instrument gate. Kind 13 InstrumentFeedback uses the
96-byte command world-data region for epoch/preparation/revision/seat revision,
phase, pending mask, protocol and issue code; reserved bytes remain zero. The
native session stores it for EFB display only, expires it after one second and
invalidates it on seat changes. It cannot renew control leases or mask inputs.

The EFB combines fresh backend state with actual native input/profile proof.
Backend Active alone cannot produce a Shared label. Held/releasing/local states
stay distinct and labels remain indexed until the aircraft mapping is verified.
Matching helper/WASM binaries are required. Production has no Baron provider;
the new integrated cases use an explicitly simulated native input adapter.
Receipt: `artifacts/instrument-ui/verification.json`.
