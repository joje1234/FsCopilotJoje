# FS Copilot EFB control test

**0.9.10 and its matching helper are installed, with saved learning/checks kept.**
0.9.9 failed before native initialization. This update removes large startup
temporaries, reducing the compiled initialization frame from 78,160 to 34,528
bytes. The build now checks actual WASM frames against a conservative 64 KiB
budget. Actual MSFS startup confirmation is pending; no reset/Relearn is needed.
The prior user-tested baseline is 0.9.8, with all four wizard checks passed. 0.9.10
contains isolated Force-button assignment errors and Setup's read-only
**Instrument sync monitor > Open monitor**. Recent value changes and searchable
definitions show local coverage/direction; both PCs are required to prove delivery.
Keep learning/output checks; no Reset/Relearn for this update. Actual 0.9.10
hardware assignment and simulator monitor behavior still need validation.
The user requested no timing changes; control/EFB rates remain ~30Hz/10Hz.

0.9.8 is installed with matching helper and the user confirms its wizard works.
All four local output checks passed. Historical candidate notes follow.

**0.9.8 historical candidate:** Actual 0.9.7 logs
show Nosewheel requested at 51.0000% but read back at 48.9990%, with original
positions restored. The shared flight-control definition now reverses its
axis-event sign. The new independent SDK-sign regression reproduces the old
failure and passes with the correction. This update keeps learned controls;
run the wizard's missing groups after installation, without Reset/Relearn.
Actual 0.9.8 simulator confirmation is pending. The same 0.9.7 logs confirm
that Reset setup itself works without restarting MSFS.

**0.9.7 is installed with its matching helper.** Settings were preserved during
installation, then backed up and cleared at the user's request for a fresh wizard run.
A verified 0.9.6 rollback is available. Setup adds **Reset setup > Clear setup**, clearing only
this aircraft's learned controls and output checks while MSFS remains running.
Connection settings, tolerance, Force binding, ownership and aircraft positions
are kept. Use it while parked, brake ON, AP OFF, all groups on Me and outside a
shared session/wizard. Then run Setup wizard with engines OFF. Disabled buttons
state their reason. Reset does not repair a failed simulator output adapter.

All three trims now synchronize as ordinary shared instruments. Steering
contains only roll/pitch, rudder, tiller/nosewheel and both brakes. Trim input
is never blocked by Steering and is absent from its learning/alignment/checks.
The tiller uses a standard event setter in definitions. The real 0.9.6 error-20
Steering batch did not record its exact rejected call; new diagnostics do.
Actual MSFS validation of this version is still pending. The Windows build,
UI/native contracts and local relay checks pass; they are not two-PC simulator
validation. Both PCs need the complete matching 0.9.7 kit for shared testing.

The installed 0.9.6 and older development records follow.

**0.9.6 is installed with its matching helper.** Saved learning/output checks
were preserved; the verified 0.9.4 rollback remains available. Standard
AP modes, FD, heading, selected altitude and VS now use the existing shared
YAML/core path. Either pilot may operate them, without an AP owner. The source
includes the prepared 0.9.5 Steering fixes. Actual simulator/two-PC AP behavior
still needs validation after installation.

The supported GTN setup requires **PMS50 GTN750 Premium active on both PCs**.
The user confirms both are activated and the inspector now shows points
matching the GTN plan while the simulator EFB is empty. Reading does not copy
plans or transfer active-leg/direct-to guidance. GTN screens stay independent.
Guidance currently comes from the flight host; selecting the other pilot's
GTN remains pending.

The runtime is largely reusable, but **only the Baron currently has the role
map and cockpit policy needed for the EFB shared-flight catalog**. Other
vanilla YAML aircraft definitions are not automatically supported by this
path. They need those profiles and simulator validation; engine-group and
avionics differences should remain in definitions. See the
[portability and reuse audit](VANILLA_REUSE_AUDIT.md).

The following records describe earlier installed/candidate versions.

Current flight readiness: **installed 0.9.4 is a manual shared-flight test
prototype, not complete cockpit synchronization.** Steering and the engine
pairs share with per-group ownership. Ordinary scalar cockpit sharing includes
barometers, gear/flaps and electrical/light mappings; actual 0.9.4 two-PC
switch/recovery behavior still needs validation. AP modes, heading bug and
selected altitude/VS are not enabled in production sharing. GTN pages stay
local; GTN750 Lite route copying has not been established.

The user's current AP design allows **both pilots** to operate all AP buttons
and selectors, with shared states and no AP owner. Only the GTN navigation
guidance source is selected explicitly. Both separate barometers remain
normally shared, using the corrected indexed absolute setters. This is the
target design, not a feature already installed.

The **0.9.5 candidate is built and locally checked**. It fixes Steering membership before
any hardware is touched and removes pedal/brake/tiller/trim discovery from
the wizard. Only yoke roll/pitch and engine levers need manual learning.
Output checks still run automatically, and any selected alternate brake
response is saved rather than changed by later hardware events. Existing
settings and unchanged engine checks are kept. It has not been installed.
Matching full two-player kit:
`artifacts/fixed-steering/test-kit/fscopilot-0.9.5-two-player-test.zip`.
Local definition/native/relay/browser checks pass; actual simulator and two-PC
validation remain pending. See the current evidence in [HANDOFF.md](../HANDOFF.md).

The notes below are historical; the first installed-status record takes
precedence over older installation-waiting and baseline statements.

**0.9.4 is now installed on the user's PC.** The matching helper is running;
file/process checks pass. At the user's explicit request, all six saved setup
files were backed up and cleared for a fresh wizard. Learning, output checks
and connection preferences now start empty; MSFS hardware bindings and logs
are intact. Restart the parked Baron with engines/AP OFF and parking brake ON,
then open **FS Copilot → Setup → Setup wizard**. Enter connection codes again
afterward. Do not reset again without a new request. Friend needs the matching
0.9.4 kit before joining. See the installation/reset receipts and backup location
at the top of [HANDOFF.md](../HANDOFF.md). Actual simulator validation is pending.

The sections below record the candidate's development and earlier versions.

**0.9.4 is ready; installation is waiting until the user finishes the previous
version's test.** Short simulator/heartbeat/world gaps now warn and retain the
existing session for bounded recovery, with the same ownership/masks/frozen
guest. Fresh same-identity data resumes without another Join within eight
seconds. Actual writes still need fresh data; cached input/world frames are
discarded. Persistent loss, restarted identities and failed critical proofs
still end the session. Logs show INF/WRN/ERR and the EFB shows an update-delay
notice. New join consent remains strict. Local native/relay/UI/build tests pass;
actual two-PC pause recovery still needs testing.

This includes the pending battery/light, brake-axis and yoke-only pickup changes.
Use the complete matching kit on BOTH PCs:
`artifacts/shared-recovery/test-kit/fscopilot-0.9.4-two-player-test.zip`.
Keep learning/checks/connection codes. **Still installed: 0.9.1.** Nothing has
been replaced while the user is testing. AP selectors remain pending, GTN pages
stay local and separate barometers remain normally shared. See [HANDOFF.md](../HANDOFF.md).

**0.9.3 is ready, awaiting the end of the user's previous-version test.** The
EFB ordinary catalog accidentally excluded the Baron's 11 existing B-variable
battery/alternator/avionics/light mappings. Generic B reads now restore those
unchanged vanilla definitions. Both PCs need the complete matching helper and
package; keep saved learning and connection codes. The complete kit is
`artifacts/cockpit-input-events/test-kit/fscopilot-0.9.3-two-player-test.zip`.
This includes the pending 0.9.2 changes. Local tests/builds pass; actual switch
sync and brake-axis wizard still need simulator validation after updating.
**Still installed: 0.9.1.** No installation while MSFS is running.

Both independent barometer setters already use normal shared definitions.
Heading bug, selected altitude/VS and AP modes are still pending production
AP integration; they are not enabled by this update. GTN pages remain local.
Current evidence and the two accidental native-update timeouts are documented
at the top of [HANDOFF.md](../HANDOFF.md).

**0.9.2 candidate ready; installation pending.** Steering takeover now requires
only yoke roll/pitch alignment. Rudder, brakes and trim transfer/block with
Steering without matching. The wizard drops pedal centering/brake release
prompts. A definition-driven left brake-axis response addresses the friend's
reported output mismatch while retaining the working button-user route. That
output check is separate from alignment; its accuracy is unchanged. Keep saved
learning and valid checks. Update both PCs' full matching helper/definitions/
Community package. The new kit is
`artifacts/yoke-only-takeover/test-kit/fscopilot-0.9.2-two-player-test.zip`.
Local builds/tests pass; actual brake-axis wizard and two-PC validation remain
pending. Installed 0.9.1 stays in place until MSFS is fully closed. Current
evidence and installation status are at the top of [HANDOFF.md](../HANDOFF.md).

**Two-player test kit is ready.** The user confirms the installed 0.9.1 UI works;
the latest native log records all four fresh-wizard output checks saved. Keep
that matching installation. Send the friend the complete
`artifacts/two-player-ready/fscopilot-0.9.1-two-player-test.zip` and follow its
`START HERE.txt`. It includes file/process preflight, numbered parked/taxi/flight/
recovery cases, a test card and a collector for both PCs' helper/native logs.
The complete local relay/SDK-fake and definition suites pass. Actual two-PC
internet/simulator testing is still required; AP assignment remains disabled and
GTN pages stay local. Current evidence is at the top of [HANDOFF.md](../HANDOFF.md).

**Installed 0.9.1 input compatibility candidate:** adds explicit pointer hover and
coordinate picking when the embedded view targets the whole SVG. Hover selectors
are isolated from optional focus-visible rules. The Force menu accepts pointer
secondary buttons and suppresses trailing clicks. **Press and hold a control for
0.65 seconds**, release, then choose Force takeover if right-click is consumed
by the simulator. Opening/releasing the menu does not change ownership. Browser
and emulated-input checks pass; the user now confirms the in-sim UI is good.

The requested fresh-wizard reset backed up and removed all six saved mod binaries
with MSFS closed. The subsequent wizard run saved all four output checks; new
learning/output files now exist as intended. Connection settings still need to
be entered again. Keep this new learning and do not press Relearn for the network
test. Current receipts and matching full bundle are
under `artifacts/efb-input-compat`; reset backup is under `artifacts/cockpit-recovery`.
See the current status at the top of [HANDOFF.md](../HANDOFF.md).

**0.9.0 development candidate:** integrates the approved cockpit UI, absolute
Baron flap targets, **Setup → Cockpit synchronization → Resync cockpit from host**
and **Setup → Steering → Force takeover button**. Both PCs need the complete
matching helper/definitions/Community package. Current installation and test
evidence are recorded at the top of [HANDOFF.md](../HANDOFF.md); local tests do
not establish actual hardware or two-PC behavior. The candidate's explicit guide
is `artifacts/cockpit-recovery/GUIDE.txt`.

The hardware binding is saved per PC in the package's WASM work folder as
`force-steering-button.0.bin` / `.1.bin`. Assign a dedicated button by choosing
its controller and pressing/releasing it. After load/replug, press/release once
to arm; subsequent presses invoke the same Steering-only Force operation as the
UI, even with the EFB closed. Holding it does not repeat. Neither AP nor other
control owners are changed by this action. Actual hardware behavior needs testing.

Quadrant Auto starts on and the normal toggle is hidden. Existing wizard pauses
and failure recovery remain; Setup offers Resume automatic takeover if paused.
Resync uses current host readings and existing definitions, reports named issues,
and bounds retries of explicitly safe absolute setters. Gear retains its existing
absolute mapping; its historical issue remains undiagnosed.

The interactive **[browser preview](preview.html)** uses the actual cockpit SVG,
styles and UI controller with a separate simulated transport. It never connects
to MSFS, CommBus or a helper. Select seats and individual controls; use the
simulated hardware sliders or **Align waiting controls** to try aligned takeover.
Presets provide mixed ownership, a waiting request and all controls on Me.
Click outside the cockpit or the same seat to cancel unfinished assignments;
confirmed ownership remains. The hardware panel represents physical input, so
using it does not dismiss assignment mode.

Open `preview.html` directly, or run
`node FsCopilot.Efb/scripts/preview-server.mjs` and open
`http://127.0.0.1:4173/preview.html`. The local server reloads the preview when its
source files change. No package build or simulator restart is needed. It serves
only the preview's own files on localhost. This is a visual/interaction mock,
not simulator or network validation.

The current review uses pastel blue/orange ownership, a clear instrument panel,
and rudder/brake alignment on the pedal graphics. Owner/status text is removed
from the cockpit. Auto stays on in the mock. With **Me** selected, double-click
a control to force just that group; a stable Force-only right-click fallback
remains. The real Setup wizard keeps its temporary Auto pause for output checks;
the reviewed cockpit is now included in the installed 0.9.1 candidate. Actual
EFB double-click behavior remains to be tested.

**Latest GTN result:** the user runs **GTN750 Lite/free**. Its plan differs from
the simulator EFB plan, and the new read-only route request timed out. PMS50
[lists MSFS 2024 EFB sync as Premium-only](https://pms50.com/msfs/). Route copying
is optional; GTN screens stay local and this does not block shared controls/AP
development. Installed UI **0.9.1** improves timeout diagnostics and EFB listener
startup. Current installed Community/helper are 0.9.1; route copying stays off.

The **0.8.0 development candidate** adds ordinary scalar instrument sharing
through the existing vanilla definitions, including both independent barometers.
Either player can adjust either barometer; there are no seat or AP ownership
locks. Local native, definition, loopback-network and UI checks pass. Actual
two-PC instrument behavior is not yet verified. **0.8.0 was installed locally
and is now superseded by 0.9.1; 0.7.4 remains the last user-tested baseline.** All
seven current package hashes match. The user subsequently requested a fresh test:
all six saved binaries were backed up and cleared. Current installation receipts
are under `artifacts/efb-input-compat`; reset records and backup are under
`artifacts/cockpit-recovery`. The earlier 0.8.0 installation remains preserved
under `artifacts/instruments-guidance`.

Setup also adds **Flight plan → Read avionics route**, a read-only diagnostic.
Compare its airports/waypoints with the GTN's actual plan. It does not import or
copy a route, and the simulator response does not identify which instrument
answered. GTN touches and display state remain local. AP assignment remains
unavailable pending a verified input adapter; captured SDK events alone are not
proof of input blocking or host-only AP output. See [HANDOFF.md](../HANDOFF.md).

A standalone MSFS 2024 Community package for the first local test: assign
**Steering** or **Engine quadrant** to **Me** or a **Mock copilot**, and check
whether your physical controls can still operate the aircraft. The initial
aircraft for validation is the Black Square Baron 58P.

Solo control testing runs entirely inside MSFS, without the desktop app, another
player, a supported-aircraft YAML profile, or a network connection. Shared-flight
source additionally uses the dedicated EFB helper described below. Ordinary
desktop application startup retains its existing vanilla flow.

For the agreed requirements, broader implementation plan, and the next Windows
Codex session's priorities, read [the project handoff](../HANDOFF.md).
The [vanilla-reuse audit](VANILLA_REUSE_AUDIT.md) inventories aircraft-specific
code, existing definition coverage and the staged barometer corrections. The
user's current policy keeps both altimeters normally shared; per-seat barometer
ownership is no longer planned.

Installed **0.7.4** adds **Setup → Setup wizard**. With the aircraft parked,
engines/AP off and parking brake on, it pauses Auto, guides hardware movement,
assigns each missing group to Mock, checks and saves output, requests aligned Me,
and restores the original Auto setting. Saved results are skipped. Cancellation
guides recovery; failures identify the group/axis and keep Auto paused. The
[implementation guide](SETUP_WIZARD.md) covers the sequence and recovery rules.
The update also corrects the observed right-brake output curve through the
YAML definition and a bounded generic piecewise compiler. Local tests/build pass,
and the user confirms the wizard works in the actual Baron. **0.7.4 is the tested
local baseline.** Restart reuse in this version and the matching two-PC test
remain unconfirmed.

The locally installed **0.7.3 update** removes two-position learning for standard rudder and
brake events. The user's actual binding emits relative `RUDDER_AXIS_MINUS/PLUS`;
those are supported alongside absolute rudder axes, combined `BRAKES`, individual
`BRAKES_LEFT/RIGHT` buttons and left/right brake axes. While Me owns Steering,
the actual cockpit demand is shared, including mixed button/axis use. While held,
reflected remote output is never recorded as local hardware. Absolute axes align
normally; relative rudder/buttons require cockpit rudder centered and brakes
released. Setup identifies relative inputs explicitly without inventing a
blocked hardware position.

Brake demand uses the existing vanilla EX1 definition, independent of parking
brake pressure. A read-only capture confirms ordinary brake positions at 100%
with parking brake on while both EX1 demands remain zero. The complete Steering
schema is stable before the first rudder/brake event, so first detection cannot
change a running session's layout. Aircraft differences remain in definitions.

Throttle/RPM/Mixture are always separated, with one quadrant Auto switch. Auto
starts on locally and the host's selected policy is applied after shared binding.
An explicit output check pauses Auto itself, and resumes it only after confirmed
restoration; SDK/restoration failures or Force/Leave keep it off. Saved output
validation is retained for unchanged engine routes; the new Steering route needs
its own first check. Both PCs need the complete matching 0.7.3 helper/definitions
and Community package. The user confirms actual rudder/brake input without
learning after restart. Read-only SDK status verifies all required mappings loaded,
Auto on and no unknown cockpit targets. New masked output and actual two-PC
validation remain pending; mixed-input loopback tests use simulated SDK calls.

The **0.7.2 update** introduced saved shared-output validation between simulator
sessions. Setup names every missing group, for example **Still needed: Steering,
RPM, Mixture**, and shows when all four results are saved. Startup loads the saved
results without moving cockpit controls. Move the hardware once to supply current
positions; saved validation does not store positions or bypass aligned takeover.
The checks are a prototype output-route diagnostic, not an MSFS requirement and
not hardware learning. Hardware movement alone cannot prove masked remote output.

Results are stored beside saved learning in the package's writable WASM `work`
directory as `pickup-<aircraft-hash>-<engines>-output.0.bin` and `.1.bin`. The record
matches the full aircraft identity, engine count and each group's actual output
routes/reference units. UI/helper changes and unrelated definitions preserve it.
Changed routes invalidate only their group; learned calibration affects validation
only where the fallback output writer actually uses it. Corrupt/incomplete copies
cannot activate validation. Storage errors are visible in Setup. Existing learning
files keep their format and are not rewritten by validation storage.

The old 0.7.0/0.7.1 module never saved its four results. If that module has already
closed, its checks cannot be recovered from disk: the first successful checks in
0.7.2 establish the durable record. Later restarts reuse it. No automatic nudge or
control movement is performed at startup. Local SDK-fake restart/storage tests,
actual Chrome layout tests, network regression and the Windows WASM build pass;
actual simulator persistence verification is still pending.

Development candidate **0.7.0** now selects control roles from the vanilla YAML,
generates its native catalog and verifies the helper/module definition identity.
The literal Baron selector/LVar names have been removed from runtime code.
Me/Mock and the saved calibration format are preserved. See the
[definition integration](../FsCopilot.Definitions/README.md) for supported routes
and the current build-time catalog limitation.

The **0.7.1 UI update** replaces unresponsive Flight/Your seat dropdowns with
explicit Host/Join and Pilot/Copilot buttons. Selection stays a draft until Save
connection. The small update uses the byte-identical 0.7.0 WASM and existing
helper/definitions; saved learning is unchanged. DOM and actual Chrome click/
layout tests pass. It is installed with all seven hashes verified and all three
learning/settings files unchanged. The user confirms the buttons work in the EFB;
saved Host/left settings are also confirmed through the read-only native status.
Receipt and rollback: `artifacts/connection-buttons`. The original 0.7.0 ZIP
is preserved.

The candidate adds **Check shared output** to a masked group's actions menu.
While parked with brake set and AP/Auto off, it performs a bounded 1% movement,
checks three fresh matching observations after an SDK checkpoint, and restores
the original positions. It never changes masks. Steering and all three engine
pairs must pass before this test build advertises shared output. Results reset
on reload; learned hardware mappings do not. These paths pass local/native/relay
tests with simulated SDK calls, but actual 0.7.0 Baron output is still pending.
Version **0.7.1 is now installed**, with all seven payload hashes verified and
saved learning unchanged. The previous 0.6.9 package and closed-state learning
have a rollback copy. The live post-restart reader now passes: all required axes
are learned and hardware/cockpit mixtures agree at 82.8125%, without Relearn.
The user confirmed all four embedded parked output checks pass (**4/4**), with
fresh SDK confirmation of shared-output capability enabled and all controls back
on Me. The matching helper also passes live startup and definition selection.
Actual two-PC shared flight and optional rudder output/alignment remain unverified; see
`artifacts/definition-controls/live-validation.json` and `installation.json`.

The next shared-cockpit ownership protocol is being developed separately in
`FsCopilot.Ownership`, with its contract runner in `FsCopilot.Ownership.Tests`.
An experimental sender-aware network adapter and native coordinator now pass
actual local relay/client tests with the production C++ controller under an SDK
fake. Current source routes EFB shared-session requests through that coordinator
and provides an explicit Windows backend startup mode with fixed-host world and
cockpit synchronization. The current source adds EFB connection setup and a
standalone Windows helper; actual two-PC flight validation remains pending. The installed package continues to use
the local Mock copilot.
Version **0.4.1** passed the user's control, Setup scrolling and cockpit-mixture
test. Version **0.4.2** fixes saved-mapping startup behavior, but the user reported
its right-click menu did not open inside MSFS. Version **0.4.3** adds mouse-button
handling and visible per-group action menus; the user confirmed it works.
The ownership/session contract runner now passes 46 scenarios and actual
loopback relay tests, but native/backend multiplayer integration is still in
progress and is not enabled in the installed solo prototype. Version **0.5.0**
passed the user's Advanced control test. Version **0.5.1** below replaces the
per-pair Auto switches with the requested single quadrant setting.

The previous live-tested build is **0.6.9**. Its post-restart Baron check confirms both
hardware and cockpit mixtures at **48.81%**, with saved mixture mappings loaded
without Relearn. The user also confirmed mixture blocking and aligned takeover
both work without Relearn (Advanced controls, Auto off).
It replaces the ambiguous SimConnect LVar readings behind 0.6.8's 100-times
mixture error with native Vars API reads in explicit Percent units. Tests/builds
pass, and all seven installed hashes match. A validated profile recovery restored
only missing mixture calibration from the pre-Relearn backup, preserving other
newly learned axes. See `artifacts/mixture-typed-reader/verification.json`.
Separate SDK output tests did not produce the requested 1% mixture movement;
shared mixture output remains unverified and remote physical output stays off.
The downloaded vanilla Baron YAML already maps both cockpit mixture LVars in
Percent units. The EFB helper does not yet consume those aircraft definitions;
the next shared-output step is a reusable definition adapter with per-group
ownership checks. See the handoff's output-plan correction. The failed generic
event tests do not establish a vanilla synchronization bug.
The user-tested 0.5.3 package and saved learning retain rollback copies. Earlier
standalone SDK throttle/RPM movement, restoration and mask-cleanup checks pass.
The AP transport described below is included but has no verified Baron input
provider. Shared Auto
now uses the same single quadrant switch with host-confirmed policy and
independent hardware-triggered pickup for all three pairs. Actual loopback relay
tests with both production C++ controllers pass handover in both directions,
the greater-than-2% hardware threshold, alignment, scoped cancellation and Auto
off preserving manual pickup. Native and DOM checks pass; actual shared Baron
and two-PC flight validation remain pending. See the latest handoff section and
`artifacts/shared-auto/verification.json`.

Additional development source now tests separate AP commander and navigation
source authority. The ownership/session runner now passes 89 scenarios. The native
module publishes read-only AP observations and has an independent AP command/status
channel with scope, input-gate, readback, lease and recovery checks. Development
source now connects that channel to the Windows helper and shared-session
coordinator. Actual local relay tests use both production C++ controllers with
simulated SDK/input providers to verify AP claims, absolute targets, input
references and retirement of delayed AP-on commands. The default Baron still has
no verified AP input provider.
AP adapter proofs remain test fixtures; AP claims and GTN
flight-plan exchange are not exposed in the EFB or enabled in production. See
the [latest handoff](../HANDOFF.md) for the policy, PMS50 findings and pending
actual Baron checks. The earlier 0.6.7 ZIP is preserved unchanged.

The latest candidate adds **Setup → Shared cockpit** for codes, Host/Join, seat
and server settings. Saving preferences does not join or change controls. The
standalone [EFB helper](../FsCopilot.Efb.Backend/README.md) starts directly and reads
those preferences, with status shown in the EFB. Native remote output remains
gated pending real Baron validation. Local relay/native tests, actual Chrome input
and narrow Setup scrolling pass; the preceding live SDK check could not connect
to the simulator, so live checks await a restart.
Receipt: `artifacts/efb-connection/verification.json`.

The 0.6.2 candidate adds **Setup → Swap seats** during shared flight. The other
player accepts or declines; a request can be cancelled and expires after 15
seconds. A confirmed swap changes both seats together while preserving physics
hosting and all control assignments. The diagram follows the confirmed seats.
The saved preference remains the initial seat for the next join.

Local camera selection follows a saved seat preference or confirmed swap, waits
until a cockpit view is active, and reports read-back in Setup. It does not keep
recentering the camera after selection. The default Baron views still require a
live simulator check. Local authority/native/relay and actual browser tests pass;
the installed package is unchanged. Receipt: `artifacts/seat-authority/verification.json`.

The 0.6.3 development candidate adds **Setup → Altimeters · local** with separate
indexed tuned pressures and STD flags. These are observations; barometer
synchronization and per-knob blocking remain unavailable. The native SDK adapter
has tested absolute setters, but production commands do not expose them yet.
The new module and helper must be kept together. The initial native/relay run
hit a startup timeout; the diagnostic rerun passes, with the first failure kept
as an unresolved concern. See `artifacts/altimeter-sdk/verification.json`.

The 0.6.4 development candidate adds the seat-owned altimeter authority model and
attributed transport. Its tests preserve separate pressures and STD during a
confirmed seat swap, reject retired owners and suppress equal/obsolete changes.
Native instrument commands and verified per-knob input gates are not connected
yet, so production altimeters remain local. The EFB now waits for simulator
confirmation during a gated swap and rejects old seat displays after rejoining.
Receipt: `artifacts/altimeter-authority/verification.json`.

The 0.6.5 candidate connects typed instrument commands to the native session,
with separate input-mask and pressure confirmations. Commands require the bound
session, current seat and verified native input adapter; they cannot keep the
flight-control lease alive. The production Baron adapter is still unverified,
so it reports instrument transport only and refuses instrument writes. Tests
exercise the integrated path with simulated gates and SDK calls. Backend
instrument scheduling and actual Baron/two-PC validation remain pending.
Receipt: `artifacts/altimeter-native-command/verification.json`.

The 0.6.6 candidate connects the backend instrument coordinator to both native
sessions and the attributed relay. Instrument protocol v2 confirms both masked
baselines before releasing either owner's knob, then waits for native input
attachment. New owner input during the final state exchange is preserved.
The physics host observes its own knob and applies only the other seat's indexed
absolute changes; the peer mirrors only its non-owned instrument. Queue-drop
retries recheck the current frame and retain its original deadline. Adapter loss
holds instruments independently of physical controls. After leaving, one scoped
Me/Force action waits for instrument/physics recovery and releases its selected
hardware group. Native/relay tests cover initial host seating on either side.
Production still lacks a verified Baron knob adapter and leaves altimeters local;
instrument status/recovery UI and actual simulator/two-PC validation remain.
Receipt: `artifacts/altimeter-coordinator/verification.json`.

The 0.6.7 candidate adds instrument status and **Use local altimeters** in Setup.
It shows seat ownership only with matching native proof and waits for actual
input release before reporting Local. Instrument-only recovery preserves shared
flight controls, retires delayed instrument writes and permits later seat swaps.
Each pilot releases their own inputs. Recovery also works for known masks after
the tuning profile becomes unsupported. Actual browser and integrated native/
relay checks pass with simulated SDK/input providers. Production's Baron knob
profile is still unverified, so synchronization remains unavailable.
Receipt: `artifacts/instrument-ui/verification.json`.

## Tolerance presets (0.5.3, hardware passed)

Restart MSFS to load 0.5.3. The **Tolerance** button beside Setup cycles
**Tight → Medium → Loose → Tight**, using **±10% → ±20% → ±30%** in the native
backend. It applies to Steering and all engine pairs in one command. The label
updates after the native reply; changing presets preserves current ownership
and the 300 ms alignment hold. The cockpit alignment bands use the selected
tolerance. The same names are available in Setup for individual group settings.

The single quadrant Auto switch is retained. Saved learning is preserved, and
the accepted 0.5.1 package/mappings are backed up. The unavailable-axis discovery
encoding fix is included; it maps the internal unknown value -10 to wire -1.
The user confirmed the preset cycle and changing alignment bands work after
restart. The read-only backend probe also passed fresh discovery and three Ready
observations from the actual installed WASM in the Baron cockpit.
The intermediate 0.5.2 build was not installed.

## Single quadrant Auto switch (0.5.1, hardware passed)

0.5.1 was the previous installed build. The
accepted 0.5.0 package and saved mappings are backed up, and saved learning is
preserved. After restarting into 0.5.1 the user confirmed the new Auto behavior
seems to work; this is the accepted local baseline.

Open the quadrant's initial **⋯ menu → Auto takeover: on**. This is the only
Auto setting; it enables throttle, RPM and mixture together without requiring
Advanced. Each pair still transfers independently after movement greater than
2%, alignment within its tolerance (default ±10%), the 300 ms hold and native
confirmation. Turning Auto off cancels automatic pending requests while
preserving current owners and manual Me requests. Steering remains manual.

Pair owner colors/labels remain visible in Simple when Auto is on or owners
differ. Advanced is a view option: switching views preserves owners, pending
pickup, Auto and individual tolerances. Simple shows **Per pair** if tolerances
differ; each graphic uses its pair's actual band. Clicking the whole quadrant
still explicitly claims it together. Per-pair Force actions remain in Advanced.

In a real shared session each pair must have exactly one confirmed owner. A
pilot/copilot can reclaim throttle by moving and aligning their own hardware while
RPM and mixture stay with their existing owner. The authority tests exercise
handover in both directions and reject old-owner/queued input. Real two-player
automatic takeover still awaits the native writer/backend integration; the
installed EFB remains a local Mock test.

0.5.1 also adds the native discovery/observation channel described in
[the native bridge README](../FsCopilot.Native/README.md). Actual SDK C++↔C#
transport and expiry passed with isolated fixture data and zero SDK failures.
It has no aircraft/control writer and deliberately lacks the remote-input
capability required to enable multiplayer. Actual WASM discovery found an
unavailable-axis encoding mismatch; the fix is included in 0.5.3. The 0.5.1 Auto
behavior is user-confirmed.

## Advanced engine pairs (0.5.0, passed; Auto UI superseded above)

The default cockpit still shows Steering and Engine quadrant. Open the
quadrant's **⋯ → Advanced engine controls** to claim **Throttle**, **RPM** and
**Mixture** separately. Each pair includes all active engines. Select a seat,
then tap the corresponding lever tracks. Confirmed pair owners appear in the
seat colors, with yellow waiting outlines during aligned pickup. Setup shows
separate pair tolerances and diagnostics and remains scrollable.

Open the quadrant's ⋯ menu again, then choose a pair's actions for **Auto
takeover** or **Force takeover**; right-clicking the pair also opens those actions.
Auto is optional and starts off. With a pair on Mock, physical movement
**greater than 2%** of full travel arms Me pickup. Small movements accumulate
against an anchored position; exactly 2% is insufficient. The first available
hardware reading establishes a baseline, and cockpit updates alone never arm
Auto. Every lever in that pair must then match current cockpit position within
its tolerance (default ±10%) for 300 ms, followed by native mask confirmation.
Turning Auto off cancels its automatic pickup, while a manual Me request remains.
Steering has no Auto mode.

Force takeover affects only the chosen pair; the quadrant's Force action still
affects all engine controls. Returning to Simple requires the engine pairs to
have matching owners and tolerances and no pending pickup. This keeps mixed
ownership visible. Advanced/Auto are session options; saved axis mappings keep
their existing file format and survive the update. Neither mode connects a real
copilot yet.

The native adapter now uses four event groups and protocol v3. Its parser still
accepts legacy two-group commands. Actual native-module tests exercise separate
mask rebuilds, mixed ownership, Auto alignment/confirmation and scoped relearning.
Six C++ contract runners, 21 controller/registration checks, DOM/package checks,
maximum native-to-JavaScript status and real Chrome layout/input checks pass.
The live read-only SDK checker accepted all 177 mappings across four groups and
four rebuilds with no failures; it never masked or transmitted hardware events.
Actual Advanced/Auto hardware behavior still requires the simulator retest.

## Rudder coverage follow-up (0.4.4)

The user reported rudder movement still responding with Steering on Mock in
0.4.3. After restarting into 0.4.4, they confirmed the actual tail rudder is now
blocked. This establishes the passed local Steering baseline for their setup.

0.4.4 adds four previously omitted, documented rudder events to Steering:
`AXIS_SENSOR_RUDDER_SET`, `RUDDER_AXIS_MINUS`, `RUDDER_AXIS_PLUS`, `RUDDER_CENTER`.
The sensor axis can also learn and participate in alignment. Relative rudder
buttons are blocked but have no absolute physical pickup position. Native event
observation now handles `SIMCONNECT_RECV_ID_EVENT_EX1` as well as ordinary EVENT,
with size checks. Extended delivery was verified using a private custom event;
handling that callback fixes observation, not a separate masking mechanism.

All **177** resulting names passed the live non-masking SDK checker through
four setup/rebuild cycles, with zero failures. Two documented linear-brake
aliases were tried only in that isolated checker, rejected by this simulator,
and excluded from the package. Existing pedal, toe-brake, calibration, saved-file,
native lifecycle and output-plan regressions pass. A read-only hardware capture
saw no pedal events/movement during its windows, so the user's actual bypass
path was not identified by those captures; the subsequent user hardware retest
confirmed suppression.

Saved mapping files and the working per-group actions menu are preserved. Keep
0.4.4 as the passed baseline while the next Advanced/Auto increment is tested.

## Simulator menu input correction (0.4.3)

The 0.4.2 menu depended on the browser `contextmenu` event. Coherent's
[event support table](https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/jsevents/)
does not list support for that event, while mouse down/up and click are supported.
This is consistent with the user's EFB report, although the exact event stream
in this MSFS build was not captured. Passing Chrome tests did not establish
embedded-runtime compatibility.

The menu now also handles the secondary mouse button directly on down/up, with
legacy `which` support and mouse-based outside dismissal. A normal left-click on
the **three-dot action icon** beside Steering or below the quadrant opens the
same menu, providing access if MSFS consumes right clicks entirely. Selecting
**Force takeover** returns only that group to Me; opening the menu changes no
ownership. The global override button remains removed. Offline/busy controls
cannot issue a command, and simulator confirmation still determines ownership.

Browser regressions suppress `contextmenu` entirely and use real right mouse
events. They also click both visible action icons, check the resulting scoped
commands and verify icons do not cover group labels at all tested viewports.
DOM keyboard, legacy mouse and offline checks pass. The Windows package builds
with zero warnings/errors; actual EFB menu behavior still needs retesting.
Restart MSFS after installation, then try either a right-click or the control's
three-dot icon and choose Force takeover. Saved mappings are preserved.
The final 0.4.3 package is installed in Community2024 with all seven payload
hashes verified. The 0.4.2 package is backed up, and both live saved-profile files
are unchanged.

## Saved mapping startup and scoped override (0.4.2)

The user's actual persistent cache was present and readable: generation 11
contained all eight mappings (roll, pitch and six engine levers). The old startup
verification could wait indefinitely at ambiguous centres/endpoints, or discard
a saved scale against delayed cockpit read-back. This made successful storage
look like lost learning.

Saved mappings now load as learned. Each requires a current event from its saved
source before a hardware position is displayed or used for alignment, including
while Mock owns the group. There is no second learning sweep or scale verification
against cockpit position. Old raw positions, ownership and waiting requests are
never loaded. AXIS/SET feedback isolation, current cockpit targets, tolerance and
the alignment hold remain enforced. A changed event source or invalid raw range
requires learning; after changing devices, MSFS profiles or axis inversion, use
**Relearn inputs** with the affected group on Me.

The verified folder on this machine is:

```text
C:\Users\jorge\AppData\Roaming\Microsoft Flight Simulator 2024\WASM\MSFS2020\fscopilot-controls-test\work
```

Files `pickup-885d39af-2.0.bin` and `pickup-885d39af-2.1.bin` are the alternating
checksummed generations for the Baron/2-engine identity. `MSFS2020` is the actual
WASM storage subdirectory even in this MSFS 2024 installation. The format and
existing files are preserved.

**Immediate takeover both** is removed from the UI. Right-click either yoke,
pedals or the quadrant, then choose **Force takeover**. It returns only that
whole group to Me without alignment; clicking one lever still means the entire
quadrant. The other group's ownership and pending pickup remain unchanged.
Ownership colors update only after simulator confirmation. The menu also opens
with Shift+F10 or the context-menu key on a focused control; Escape/outside click
close it. The legacy global protocol command remains for compatibility, with no
UI action exposing it.

The 0.4.2 package is installed in this machine's existing Community2024 folder;
all seven payload hashes match the build. The previous 0.4.1 package and both
saved slots are backed up under `artifacts/efb-control-test/`. The live saved
files were left unchanged. Startup SET feedback arriving before aircraft identity
cannot displace a saved AXIS mapping or count as a hardware reading.

Windows WASM compilation, native restart/centre/endpoint/feedback regressions,
both directions of scoped force takeover, controller/DOM and actual Chrome
right-click/keyboard/layout checks pass. Restart MSFS, open the same Baron and
move saved controls once: no two-position learning should be needed. Test each
context-menu override while the other group is on Mock or waiting; it must stay
unchanged. Actual EFB right-click handling and a full simulator restart still
need the user's confirmation.

## Setup, mixture reference and refresh correction (0.4.1)

Setup now has an explicitly bounded scrolling body, separate from the fixed
Close header. Mouse-wheel and touch dragging are handled within the panel;
updates preserve its scroll position. The main cockpit stays on one screen.
Native position status is pushed at **10 Hz**, up from 2 Hz. The EFB's 500 ms
read poll remains a heartbeat. This is an application update-rate change, not a
simulator graphics-setting requirement.

The reference policy is **always the cockpit control position** for learning,
target markers and pickup. Standard lever SimVars are used only as the standard
aircraft reference; custom aircraft need their actual control-position mapping.
Live capture in the Black Square Baron Professional 58P found the generic engine
mixture SimVars at **75.51088%** with cockpit levers at **100%**, and **40.11333%**
with cockpit levers at **53.125%**. They are unsuitable pickup targets for that
aircraft under these conditions. The previous learning algorithm was unchanged
by the 0.4.0 UI update; the old reference could appear to work when those values
happened to match closely.

For the verified `bksq-aircraft-baronpropress` aircraft path, read-only
`L:BKSQ_MixtureLeverPosition_1/2` (normalized 0..1) now supply both mixture targets.
All control writes/masks still use the original event path. The two references
are sampled alongside the other positions in a 25-double packet. No engine
mixture state is written. Setup reports **Mixture alignment reference: cockpit
levers** in this Baron. Invalid references prevent alignment; new learning waits
for aircraft identification. Other aircraft retain the standard lever mapping.

After restarting MSFS, check Setup scrolls to the quadrant. With the quadrant on
Me, move the mixtures to two separated intermediate positions and pause briefly.
Check the white target marks match the visible cockpit levers, then test Mock/Me
pickup. Saved mappings and the ±10% default are preserved. Browser wheel/touch,
native learning/pickup, identity/data packet, persistence and 10 Hz checks pass;
actual WASM/EFB behavior still needs that retest.

## Compact cockpit interface (0.4.0)

The main view contains the cockpit and a compact footer, with no introductory
heading or scrolling control cards. Select the **blue Me seat** or **red Mock
copilot seat**, then tap a yoke/pedal or the engine quadrant to assign that whole
group. Me is selected initially. Selecting a seat alone changes no ownership.
Controls adopt the matching pastel color only after simulator confirmation.
Ownership remains exactly two groups; clicking one lever assigns the entire
engine quadrant.

When requesting Me from Mock, the group stays red with a **yellow dashed border**
until aligned takeover is confirmed. White marks indicate cockpit positions;
colored dots indicate observed hardware positions. Green checks show aligned
axes and arrows show the direction to move. The six Baron lever tracks are drawn
inside the quadrant. Roll/pitch tracks and a dashed cockpit yoke versus a solid
hardware yoke show steering alignment. Additional detected steering/brake/trim
axes appear across the instrument panel. A group-level count includes every
required axis. **Cancel takeover** appears while waiting.

**Setup** opens the tolerance selectors (default ±10%), Relearn buttons, explicit
owner buttons, storage status and detailed diagnostics. These controls retain the
existing protocol and native confirmation checks. Since 0.4.2, a control's
right-click **Force takeover** menu supplies its alignment bypass. Errors remain
visible, and loss of bridge confirmation disables the drawn control actions.

The Windows build, protocol/controller tests, SVG interaction DOM tests and
headless Chrome checks pass. The browser checks cover 24 viewport/state
combinations plus four layouts reserving space for EFB clock/navigation chrome,
without main-view scrolling, confirmed pastel colors, actual yoke/
lever hit coordinates and Setup dismissal. Browser fixtures are explicitly
labelled previews; they do not connect to MSFS. Restart MSFS after installation,
then verify actual EFB sizing, seat/graphic assignment and normal aligned pickup.

## Build on Windows

Prerequisites:

- Visual Studio C++ build tools and the **MSFS2024** platform toolset installed
  by the Microsoft Flight Simulator 2024 SDK.
- Node.js 16 or later. The EFB scripts use no npm packages.

SDK 1.8.2 supplies Visual Studio integration through **2022**. Build Tools 2026
alone does not supply its MSFS platform. Install the 2022 C++ tools alongside it,
then install/repair the SDK's Visual Studio 2022 feature. The script prefers an
MSBuild installation that has the MSFS2024 platform, even when newer tools exist.

From the repository root, run in PowerShell:

```powershell
.\FsCopilot.Efb\build.ps1
```

If MSBuild is not discovered automatically:

```powershell
.\FsCopilot.Efb\build.ps1 -MSBuildPath "C:\path\to\MSBuild.exe"
```

For a portable Node.js installation, pass `-NodePath "C:\path\to\node.exe"`.
The script also refreshes `MSFS2024_SDK` from the installed environment when the
current terminal predates SDK installation.

The script generates the EFB JavaScript, rebuilds the native WASM module, checks
its exports, and creates:

```text
artifacts/efb-control-test/fscopilot-controls-test/
artifacts/efb-control-test/fscopilot-controls-test.zip
```

Copy the **fscopilot-controls-test** folder into your MSFS 2024 **Community2024** folder,
then restart the simulator. Its `manifest.json` and `layout.json` must be directly
inside that folder. Open the EFB and select **FS Copilot Controls**.
Use DevMode's **Tools > Virtual File System > Open Community 2024 Folder** to
locate it; avoid installing a second copy in the legacy Community folder.

Alternatively, after running the build script, open `EfbControlTest.xml` through
MSFS Developer Mode and use **Build All In Project** before loading a flight.
Use either the mounted DevMode package or the Community copy, so the test module
is loaded once.

## Verified suppression and alignment test

Windows verification on **2026-11-14**: SDK **1.8.2**, Visual Studio Build Tools
**2022**, and portable Node.js **26.13.1** produced a Release WASM build with zero
warnings/errors. Controller, registration, package, native contract, and DOM
tests passed. The compiled module's exports, package layout, ZIP root, and
installed file hashes were checked. The SDK's integer `HANDLE` type is now used
by the test stubs; zero initialization fixes the real WASM compilation failure.
The first in-simulator run confirmed that the EFB opens and receives native
status, but initialization failed with **Simulator did not confirm the change**,
leaving all buttons disabled. Version **0.1.1** defers configuration until the
view opens and SimConnect reports its connection open, and adds lifecycle logs
under the package's WASM work directory as `controls-test.log`. It retains the
confirmation barrier and releases masks on timeout. The revised build and its
startup regression tests pass. Later versions below supersede that initial
runtime failure.

The next runtime attempt reached SimConnect setup and reported **error 7
(send 6, index 2)**. A non-masking client reproduced this as an unrecognized
`AILERON_CENTER` mapping and also found that `AXIS_LEFT_BRAKE_LINEAR_SET` and
`AXIS_RIGHT_BRAKE_LINEAR_SET` were rejected. Version **0.1.2** removes those
three aliases; their canonical events were already included. All **173** remaining
names, notification-group additions with masking disabled, priorities, aircraft
subscription, and the confirmation reply passed an external simulator check.
Exceptions now identify the mapped event by its packet ID. **This is setup
validation, not proof of WASM initialization or hardware suppression.**

Version **0.1.2** connected successfully in the EFB. Selecting steering for the
mock copilot then caused **error 3 (send 354, index 2)** and released the masks;
continued hardware response in that state is not a completed suppression test.
Version **0.1.3** re-establishes event mappings after clearing notification
groups, before re-adding their events. Diagnostics now cover the whole setup
sequence, including group additions. Contract tests simulate discarded mappings
and cover repeated changes, restore, and partial-configuration failures.

**Version 0.1.3 simulator result:** the user confirmed that the steering/yoke
blocking test now works in the Baron. This establishes the first actual hardware
suppression result. The external non-masking check also completed **4/4** setup
and rebuild cycles for all 173 events with **zero failures**.
The user also confirmed steering restoration and blocking/restoration of all
six quadrant levers while steering remained available. **Core group suppression
and restoration are now confirmed in the Baron.** The user subsequently confirmed
all remaining acceptance checks, including EFB reopen/persistence, restore-both,
and aircraft-load reset. **The original local suppression milestone passed.**

Version **0.2.0** adds the requested aligned takeover. Ownership remains exactly
two groups: **Steering** and **Engine quadrant**. Individual indicators never
transfer ownership separately. The user confirmed aligned takeover can work,
but reported that roll could switch back to learning and both mixtures could
remain unlearned. Version **0.2.1** fixes this learner: ambiguous scales no longer
count as learned, bad initial read-back cannot permanently eliminate all scales,
and a validated scale is retained through handovers. Small input chatter and
endpoint rounding are tolerated. Native/UI regressions and the Windows WASM build
pass. **Version 0.2.1 simulator result:** the user confirmed that the corrected
learning and aligned takeover work in the Baron on 2026-11-14. This is the passed
local alignment baseline. Learned mappings currently last for the aircraft
session; aircraft/module reload and simulator restart require learning again.

Version **0.3.0** adds saved learning. It stores validated event scales/directions
in the module's persistent `work` directory, separately for the simulator-reported
aircraft path and engine count. Two checksummed file generations protect against
an interrupted write. Only mappings are saved: every flight still starts on Me,
with no pending takeover or old input readings. The Windows build and local
persistence/lifecycle tests pass. The user has confirmed that storing mappings
between simulator sessions works. Throttle learning remained stuck during that
test, so persistence success does not establish that every axis learned.

Version **0.3.1** sets the requested default alignment tolerance to **±10%** for
both groups. It also fixes a source-switch reset: once an absolute AXIS event
stream is observed, ordinary SET events for the same lever no longer erase its
learning, replace its raw value or refresh its input timestamp. This includes
the `_AXIS_SET_EX1` family. SET-only bindings can still learn when no AXIS source
has been selected. If changing a binding from AXIS to SET-only, use **Relearn
inputs** with Me active. The saved-file format is unchanged. Native/UI/build
checks pass. **Version 0.3.1 simulator result:** the user confirmed that everything
works after the correction, including the throttle learning and 10% alignment
setup. Record 0.3.1 as the current passed local baseline.

### Saved-learning test (0.4.2)

1. Restart MSFS after installing 0.4.2. Previously saved mappings are preserved.
   Only new controls need the low/middle/high learning pauses while on **Me**.
   Allow the saving message to finish.
2. Restart MSFS completely and load the same Baron variant. Open the EFB. Saved
   axes should say **Saved: move control to read position**; both groups start on Me.
3. Move each control once. Centre and endpoints work too; no settled read-back
   or two-position sweep is needed for a saved mapping. Input and Cockpit markers
   should agree while Me is active. Fresh readings can also be taken while Mock
   owns the group, without returning to Me first.
4. Repeat **Mock copilot → Me → align** for both groups. Verify normal yellow
   waiting and automatic confirmed takeover. Closing the EFB does not discard
   mappings or pending pickup. An aircraft reload restores saved mappings but
   requires fresh hardware readings again.
5. **Relearn inputs** while Me is active clears that group's saved mappings too.
   A changed event binding or incompatible scale/direction needs learning again.
   If saving is unavailable, the EFB reports it and session learning still works.

The cache is scoped to the loaded aircraft path, which may differ by variant or
livery. It does not identify individual USB devices or the selected MSFS control
profile. After changing devices, binding profiles or inversion, use Relearn;
saved mappings never authorize a takeover without fresh, aligned hardware.

### Suppression and alignment regression

1. Load the Baron on the ground, solo, with autopilot off. Leave the FS Copilot
   desktop app closed for this initial test.
2. Open **FS Copilot Controls**. Wait for **Simulator connected**. Both groups
   initially belong to **Me**.
3. With **Me** active, move each bound absolute axis through low, middle and high
   positions, pausing briefly (about half a second) at each. Confirm that the
   **Input** and **Cockpit** markers agree. This learns the observed event's scale
   and direction from live cockpit read-back; uncertain mappings stay unavailable.
   Learning needs two settled positions separated by at least 10% of full travel
   and three consistent read-back samples selecting one scale. Once learned,
   mappings stay learned through repeated Mock/Me handovers in this aircraft
   session. Use **Relearn inputs** while Me is active to clear a group's readings.
   If already stuck on Mock with an unlearned axis, right-click that cockpit
   control and choose **Force takeover**, then learn with Me active.
4. Set **Engine quadrant → Mock copilot**. Wait for confirmation. Move both
   throttle, propeller and mixture levers, including any hardware buttons that
   command them. They should no longer operate the corresponding aircraft
   controls; steering should still work. Check the **Blocked** counter.
5. Move the hardware away from the cockpit positions, then press **Engine
   quadrant → Me** once. Me turns **yellow** while input stays blocked. Move all
   six levers toward the white cockpit markers. Red dots become green when each
   is in tolerance. Once every required axis remains aligned for **300 ms**, the
   native module requests attachment; **Me turns green only after confirmation**.
   No second press is required. Default tolerance is **±10%** of full travel,
   selectable per group from 1%, 2%, 3%, 5% or 10%.
6. Repeat with **Steering → Mock copilot**. Check pitch, roll, rudder/nosewheel,
   left/right brakes, and trim, while the engine quadrant remains enabled.
7. Test **Cancel takeover** during yellow: the group must stay blocked. Repeat
   the Me request, close/reopen the EFB, and confirm native waiting/ownership
   persists. Moving a target out of tolerance must reset the alignment hold.
   Right-click **Force takeover** is the deliberate recovery override: it bypasses
   alignment for only the selected group at its hardware's current position.
8. Loading a different aircraft resets ownership to **Me**. Unloading the module
   removes its input masks.

For any failure, record the aircraft/version, which control moved, its MSFS
binding name, and the EFB's last event and counters. If the aircraft moves but
**Events seen** does not change, that input probably bypasses these event paths.

## Scope of the prototype

The native module subscribes to standard simulator control events and masks them
before the simulator's default-priority handler when **Mock copilot** is selected.
It does not repeatedly write or freeze aircraft state. All throttle, propeller
and mixture commands form one quadrant group; steering, brakes and trim form
the other. General and per-engine events for engines 1–4 are included.

This is **event masking**, not source-aware hardware filtering. Keyboard and
cockpit actions that send the same events are also masked. Direct writes to
SimVars/LVars, aircraft-specific InputEvents, vendor utilities, and higher-priority
aircraft handlers may bypass the mask. Compatibility with the Black Square Baron
58P must be established by the in-simulator test above; the event list alone is
not proof of successful suppression. Parking brake, flaps, gear and unrelated
switches are outside these two groups in this prototype.

The mock copilot does not fly or hold the aircraft. Alignment uses **observed
absolute MSFS axis events**, not USB/device polling or source-aware identification.
Exercise every bound axis while Me is active before detaching. The Baron test
requires both yoke axes and all six engine levers; additional absolute steering,
brake and trim axes join when observed. Buttons/relative encoders have no physical
pickup position. Combined quadrant axes must match each affected engine's actual
lever position; the module never equalizes engine settings to make them fit.
Unbound quadrant levers and other layouts need the later binding configuration;
an unknown required axis keeps the group waiting rather than silently reconnecting.

New mappings are validated against separated positions while Me is active.
Version 0.3.0 saves these mappings; 0.4.2 restores them without learning again,
requiring only a current hardware event. Newly learned input scales that remain
ambiguous stay unknown. Validated mappings are frozen through handovers; a delayed
cockpit frame after takeover cannot erase them. Changing the observed event
binding clears the affected mapping. An input must be
observed again after detaching and within the last **30 seconds**; a cockpit
sample must be less than **one second** old. Repeated/stationary events are not
USB device-presence detection. If a stale axis turns red, nudge it again.
Steering pickup waits while autopilot is on. The enforcement and 300 ms hold run
in WASM even when the EFB is closed; the simulator confirmation barrier is retained.

Multiplayer handover, raw device bindings, resync, and general aircraft adapters
remain later work. Aircraft reload clears active input readings and pending
takeover and starts with Me, then loads saved mappings awaiting fresh input.

The EFB shows pending changes until the simulator replies to a configuration
barrier. Missing/old bridge replies disable commands. A configuration exception
or confirmation timeout closes the module's dedicated SimConnect connection,
which releases its masks, and reports an error. Reload the package to retry.
Closing the EFB alone does not release masks.

## Local verification and preview

Build the JavaScript and run the dependency-free protocol/controller tests:

```text
node FsCopilot.Efb/scripts/generate-events.mjs
node FsCopilot.Efb/scripts/build-ui.mjs
node FsCopilot.Efb/tests/controller.test.cjs
node FsCopilot.Efb/tests/registration.test.cjs
node FsCopilot.Efb/tests/package.test.mjs
```

`preview.html` displays the same UI in a browser. It deliberately has no fake
simulator: controls stay disabled, and a banner labels it as a browser preview.

The native tests compile the actual module against small simulator contract
stubs. With a normal C++14 compiler:

```text
c++ -std=c++14 -Wall -Wextra -Werror -I FsCopilot.Efb/tests/stubs FsCopilot.Efb/tests/native_test.cpp -o native-test
./native-test
```

The alignment-specific native tests are in `tests/pickup_test.cpp`; compile them
with the same C++14 compiler. They exercise group-wide readiness, calibration,
moving targets, combined controls, stale data, the hold period and automation.
Version 0.2.1 regressions also cover ambiguous centre positions, calibration
retention across repeated handovers, recovery after bad read-back, the captured
mixture event value, input chatter, and simulator endpoint rounding.
`tests/targets_test.cpp` exercises the Baron cockpit-reference adapter using the
captured mixture values, unchanged standard-aircraft behavior and invalid data.
`tests/profile_test.cpp` covers persistence, identity/engine isolation, changed
bindings, fresh verification and damaged-file fallback. Run it from the repository
root after creating `artifacts/efb-control-test/tests`, using the same compiler.
Actual-module tests cover module restart, aircraft reload, explicit durable
relearning, stale aircraft-identity replies and storage-failure isolation.
The actual-module tests also exercise the position-packet path and defer native
unmasking until aligned, then require the configuration reply before confirming Me.

The optional DOM test uses the test-only `linkedom` package. Set `FSC_DOM_MODULE`
to its installed path and run `node FsCopilot.Efb/tests/dom.test.cjs`.
The optional real-browser test is `node FsCopilot.Efb/tests/browser.test.cjs`.
It needs Node with a built-in WebSocket client and Chrome (or set `FSC_BROWSER`
to Edge/Chrome's executable). It saves labelled UI fixture screenshots and
layout measurements under `artifacts/efb-control-test/ui-preview/`.

These tests cover command validation, independent groups, confirmation ordering,
late messages, restore, aircraft changes, failures, and the rendered controls.
They cannot validate the simulator's masking implementation or aircraft behavior.

With MSFS running, `tests/simulator_events.cpp` checks event registration against
the actual simulator. It observes events with masking disabled and never sends
control inputs. From a Visual Studio x64 Native Tools prompt at the repository
root (with `MSFS2024_SDK` set), run:

```powershell
New-Item -ItemType Directory -Force artifacts/efb-control-test/tests | Out-Null
cl /nologo /std:c++14 /EHsc /W3 /WX /I "$env:MSFS2024_SDK\SimConnect SDK\include" FsCopilot.Efb/tests/simulator_events.cpp /Foartifacts/efb-control-test/tests/simulator-events.obj /Feartifacts/efb-control-test/tests/simulator-events.exe /link "$env:MSFS2024_SDK\SimConnect SDK\lib\SimConnect.lib"
Copy-Item "$env:MSFS2024_SDK\SimConnect SDK\lib\SimConnect.dll" artifacts/efb-control-test/tests/ -Force
./artifacts/efb-control-test/tests/simulator-events.exe
```

It must report zero failures and **4/4** confirmed configurations, including
repeated clear/remap/re-add cycles. This external client
check complements the WASM test; it does not establish input suppression.

`tests/simulator_targets.cpp` is a separate read-only check of the cockpit SimVar
definitions and aircraft identity used by alignment/saved learning. Compile it
like `simulator_events.cpp` (changing
the input/output names). It reports the position values and any rejected names or
units without changing controls. This check requires a loaded aircraft.

## SDK references

- [EFB apps and packaging](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/efb/electronic-flight-bag-api/)
- [Notification group masking](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/events-and-data/simconnect_addclienteventtonotificationgroup/)
- [SimConnect priorities](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_API_Reference.htm)
- [CommBus](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/javascript/communication-api/)
- [Standard control event names](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/key-events-index/)

The small EFB registration adapter uses the runtime globals and interface shown
in Rafael Coelho's MIT-licensed minimal EFB example; see `THIRD_PARTY_NOTICES.txt`.
