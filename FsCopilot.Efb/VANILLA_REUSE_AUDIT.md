# Baron code and vanilla-definition audit

## 0.9.10 startup allocation regression

0.9.9's installed hashes matched but its native module never logged startup.
Its new diagnostic objects raised the compiled module_init frame from 48,272
to 78,160 bytes. In-place reconstruction in existing static storage now reduces
it to 34,528 bytes, with no behavior/definition changes. This generic fix adds
no aircraft-specific code. A build check inspects actual WASM frames and rejects
the frozen 0.9.9 regression. The
[MSFS debugger guidance](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/wasm/debugging-webassembly-modules/)
identifies large stack frames/stack probes as a cause of access violations.
No actual MSFS callstack was captured, so startup still needs simulator
confirmation. A successful Node import-shim run is not MSFS validation.

## 0.9.9 optional input isolation and read-only coverage monitor

The observed 0.9.8 Force-assignment failure produced a cleanup-error feedback
loop and then failed general observation. Optional input calls now retain exact
attribution, cleanup is idempotent, rejected unused button candidates do not
cancel valid candidates and local client event IDs are registered once. No
aircraft branch or control-learning change is needed. This repairs the existing
generic Force-binding adapter. The
[SDK input mapping](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/inputevents/simconnect_mapinputeventtoclientevent_ex1/)
supports device/button events; controller button counts vary and the enumeration
metadata does not supply that count. Old logging did not identify the initial
rejected call; actual assignment still needs simulator validation.

The local monitor reuses the exact selected ordinary-sync catalog, existing
readbacks and policy labels. It introduces bounded diagnostic state and paged
UI metadata because vanilla has no EFB coverage display. It never evaluates a
setter, changes ownership, sends network values or changes update rates. It
labels local configured coverage and direction explicitly, without claiming
end-to-end delivery or support for GTN touches/event-only controls. Existing
direction/exclusion policy remains authoritative; no extra aircraft mappings
were added. Local tests and browser layouts are not two-PC simulator proof.

## 0.9.8 Nosewheel output direction

Actual 0.9.7 MSFS logs repeatedly show Steering's Nosewheel probe requesting
51.0000% and observing 48.9990%, with original positions restored. Setup reset
also completed successfully in those logs. The event route no longer causes
the earlier SDK error; the remaining defect is the setter's direction.

The complete Baron include tree has one tiller definition in the shared
flight_controls module and no competing included setter. Vanilla's
apply_surfaces already reverses the rudder axis event relative to its readback.
The tiller definition now also uses a negative signed-16k scale. The generic
compiler accepts negation plus constant scaling and retains its sampled
absolute-affine validation. No aircraft-specific runtime branch, wider output
tolerance, skipped check or hardware relearning is introduced. The
[SDK event reference](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/key-events/aircraft-miscellaneous-events/)
documents AXIS_STEERING_SET's signed range; the direction correction is based
on the actual simulator readback above.

The native wizard test now models the observed SDK sign independently of the
definition, rather than using that definition to derive both output and fake
readback. This reproduces the 0.9.7 failure before the change. Definition and
SDK-output tests check both sides, center, endpoints and the exact 51% probe.
The relay harness also models the observed direction independently. These
are local regressions; actual 0.9.8 MSFS confirmation is still required.

## 0.9.7 reset, ordinary shared trims and Steering error 20

The user's latest trim policy supersedes the fixed-nine-axis proposal: elevator,
aileron and rudder trims are ordinary shared instruments, not Steering members.
Their existing YAML getters move from master to shared; both pilots may adjust
them. Their role bindings and all trim events are removed from the physical
interceptor. No runtime aircraft-name branch is added. Wire slots 6..8 remain
reserved for compatibility but cannot become required owned controls. The
Baron's physical role map has ten roles; with roll/pitch fallback the active
two-engine required mask is 0x7e3f. Steering is six axes, independent of trim.

Vanilla's complete Baron tree/include and setter engine were checked first.
Elevator retains its legacy signed getter/domain and ELEVATOR_TRIM_SET route.
Aileron and rudder use the documented high-precision trim SET_EX1 events, with
unit conversion/rounding in YAML. Per-entry readback precision follows actual
event quantization, preventing tiny rounding differences from becoming rotary
input. See the [standard control events](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-flight-control-events/)
and [control variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-control-variables/).

Actual 0.9.6 native evidence: Steering wizard reached output phase 4, then
logged SDK error 20 and disabled output. This was not alignment failure. The
old writer tracked send IDs but did not report which call failed, so do not
claim the exact axis is proven. The tiller definition now uses standard
AXIS_STEERING_SET instead of a direct readback-SimVar write. With trims removed,
the physical Steering batch contains only standard events. Generic compiler
support accepts constant affine multiplication and rounds the final SDK value
after conversion, rather than rounding a -1..1 getter into three detents.
New tracked SDK-call metadata survives checkpoint compaction and logs target,
axis, operation, send ID and index. The actual route still needs MSFS testing.

Reset setup is generic native orchestration missing from vanilla: it revokes
saved output proofs, replaces both pickup generations with empty mappings,
then resets the in-memory learner. It keeps connection/tolerance/Force binding
and never unmasks hardware, moves controls or changes aircraft state. It needs
fresh local parked/Me state and refuses active shared/wizard/output operations.
Duplicate requests are idempotent; storage errors are reported and retryable.
It does not reinitialize a failed SDK writer or pretend stale input is fresh.

A relay regression exposed another older-receipt race during rapid peer rotary
changes. Ordinary packet v2 now attributes each host receipt to its accepted
peer proposal. An older receipt cannot undo a newer local adjustment; genuine
host input remains authoritative. Codec/profile identities require matching
helpers and native definitions. This is generic synchronization logic, not a
Baron mapping exception or a barometer ownership restriction.

## 0.9.6 shared AP and aircraft portability

The current user policy is normal shared AP operation. A generic opt-in
`sharedAutopilot` cockpit policy is enabled in the Baron JSON. Both pilots can
edit shared AP values; production does not start the older experimental
exclusive AP-owner adapter. GTN/GNS modules, screen events, navigation writers
and physical control outputs remain excluded from ordinary synchronization.

The full Baron tree and AS_G1000_NXi module were checked first. Existing HDG,
ALT, VS and FLC absolute on/off patterns are reused; other standard modes use
their documented absolute equivalents in YAML. FD retains its normal toggle
with equality/current-readback guards and no blind retries. Barometers are
unchanged. No AP event table was added to the native runtime.

Corrected definition defects: conflicting duplicate heading getters; obsolete
altitude/VS event names; and `AUTOPILOT VERTICAL SPEED VAR` with Feet units.
Heading now uses indexed `AP_HEADING_BUG_SET_EX1`; altitude and VS use indexed
`AP_ALT_VAR_SET_ENGLISH` / `AP_VS_VAR_SET_ENGLISH` in feet / ft per minute.
See the official [event reference](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-autopilot-and-flight-assistance-events/)
and [variable reference](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-autopilot-and-assistant-variables/).

The heading event quantizes to 360/16384 degrees. Generic per-definition
readback precision is carried in the catalog, helper and native confirmation;
only heading opts into 0.022 degrees. Other entries retain 0.000001. This is
separate from hardware alignment tolerance. Current-readback fences and
same-revision replay checks remain strict. An input anchor accumulates small
rotary changes without resending an unchanged proposal every observation.

The ordinary relay suite was previously run only with `--cockpit-only`; it is
now also in the full run. It exposed a stale-snapshot echo: old host state
could undo a copilot proposal before its receipt. The generic model omits
pending setters from snapshots and ignores already initialized duplicate host
revisions. Explicit resync still uses Retry. Deterministic checks and real
relay tests cover this without changing any switch setter or physical lease.

The new runtime is reusable, but only `bksq-aircraft-baronpropress` currently
has profiles in the EFB catalog. Mixture LVars, brake response, indexed
instrument corrections and exclusions live in YAML/role/cockpit data. The
generated header contains that one profile by design. An arbitrary vanilla
YAML alone does not enable EFB shared flight: another aircraft needs its full
include tree, role map, cockpit policy, matching catalog and simulator tests.
Review engine groups and the twin-engine UI for fixed-pitch aircraft, jets
and custom avionics. Synthetic alternative identities are not compatibility
validation. Vanilla desktop aircraft loading remains separate.

The supported GTN setup requires active PMS50 GTN750 Premium on both PCs.
The user confirms both are activated; see PMS50's [feature list](https://pms50.com/msfs/)
and [integrator guide](https://pms50.com/fs2020/gtn750/integration.pdf).
The user reports the route inspector matches GTN points while the stock EFB
is empty. That validates the local route-read test, not copying or guidance.
Responses do not identify the instrument. Route copying, active-leg/direct-to
transfer and selectable remote GTN guidance remain unimplemented/unverified.
Screens stay local; current guidance comes from the physics host. Premium's
local EFB integration is not proof of a cross-PC guidance API.

## 0.9.5 fixed Steering definitions (candidate; not installed)

The user rejects discovering extra Steering members during a shared session.
For a supported definition profile, required axes now come from its role map,
not hardware events: roll/pitch, rudder, nosewheel, both brakes and all three
trims exist before any hardware is touched. Unsupported solo mode retains its
existing discovery fallback; it does not acquire shared-output capability.
The new generic ownership layer still blocks every Steering event, but only
yoke roll/pitch require a physical pose for aligned takeover. The wizard asks
for no pedal, brake, tiller or trim learning. Their owner input is actual,
unmasked cockpit demand, matching vanilla's state-based synchronization.

Reviewed the Baron root, all includes, original snapshot and Coordinator/YAML
setter path. Rudder/brake/trim getters and setters are reused unchanged. New
roles for elevator, aileron and rudder trim point at those existing entries.
In particular elevator trim retains vanilla's signed `Percent scaler 16k`
getter and `ELEVATOR_TRIM_SET`, instead of assuming EX1's asymmetric midpoint
means neutral. Aileron/rudder units and setter domains also stay explicit in
definitions. No aircraft-name branch or duplicate trim event table was added.

One standard getter was absent from vanilla's module: `A:STEER INPUT CONTROL`.
It is now a generic `flight_controls.yaml` entry with its normal SimVar setter,
selected by the aircraft's role data. The existing typed definition compiler,
reader and writer support it. Runtime exceptions remain only the ownership,
blocking, pickup and bounded output-proof mechanisms absent from vanilla.

The previous candidate selected the optional left-brake response after seeing
an absolute hardware event. That could change the route/proof during flight.
Now a parked output check may select the declared alternate only when observed
cockpit demand matches its predicted response. It retries baseline, movement
and restoration, retaining the strict accuracy and SDK confirmations. Ignored
output cannot pass. Neither a later brake event nor first trim use changes a
route or required list. This addresses the friend's recorded 1.1895% versus
0.3789% mismatch without treating hardware type as an SDK guarantee.

The selected response is saved with output proofs in dual-slot format v2.
Exact-size, checksum-checked v1 records migrate in memory; unchanged engine
proofs survive. Different response selections across interrupted copies revoke
only their group's proof. No raw hardware pose or ownership is stored. The
expanded Steering routes need one automatic recheck. No settings are reset.
Local SDK/relay checks do not establish actual two-PC simulator behavior.

## Updated AP design agreed with the user

Both pilots must be able to adjust heading, selected altitude/VS and AP buttons,
sharing resulting states as in vanilla. Do not require an AP owner or assign
barometers to an AP owner. Both independent barometers stay normally shared
through the corrected indexed absolute setters. Only GTN navigation guidance
has an explicit selected pilot/source; screens and finger presses stay local.
This supersedes earlier exclusive-AP-control proposals. It is a design update,
not a claim that AP selector production sharing or remote GTN guidance is
enabled in installed 0.9.4 or the Steering candidate. Check vanilla's complete
AP definitions and avoid overlapping the ordinary and guidance writers.

## 0.9.4 bounded shared-session recovery

The observed drops were explicit one-second freshness guards, not uncaught
exceptions. Vanilla synchronization has no consent-bound, per-group native
authority lease to reuse for this purpose. The new recovery policy is generic;
no Baron definition, setter, barometer ownership rule or aircraft branch changes.

Data freshness and session retention are now separate. Simulator/world data
older than one second pauses shared output/transfers; peer heartbeats warn at
three seconds. The existing session can resume under the same verified identity
within eight seconds. Native session/writer/world retention uses that window;
actual writes still require the existing fresh readback, SDK barriers, ownership
generation and 250-ms input/world queue TTL. No cached control pose is replayed
to recover. The frozen guest and last ownership masks remain held during gaps.

Native transport marks retained observations as non-fresh. Optional cockpit/AP
queues reject them. Only lease heartbeat/End packets may use retained primary
status; a heartbeat performs no aircraft write or attachment. New joins retain
their original strict freshness/consent checks. Actual transport loss, identity
changes, unconfirmed ownership/SDK operations and sustained gaps remain fatal.
This does not suppress arbitrary exceptions after an uncertain aircraft write.

Recovery transition warnings include native/session ages; recovery info includes
gap duration. Terminal errors preserve their original reason. Helper files now
include INF/WRN/ERR levels. The EFB displays a retained-session warning via the
existing Active + NativeUnavailable status; no IPC schema/ownership flag is
weakened. Matching helper and module are required for coordinated retention.

Production C++/C# local-relay tests cover short local SDK, heartbeat and world
gaps, recovery without a new session, strict retained-data join refusal, long
loss termination and preserved masks/freezes. These are simulated SDK tests;
actual two-PC MSFS pause/stall recovery still needs validation.

## 0.9.3 missing input-event switch synchronization

The user reports a working previous-version shared connection, but battery and
lights do not mirror either way. The host's real log confirms an Active shared
session and ordinary-channel resync activity (206 old catalog entries), so this
is not evidence that the complete instrument transport is disconnected.

The exact battery and exterior-light mappings already exist in the full Baron
definition tree as `B:` getters and vanilla `>B:` setters. The EFB catalog
explicitly admitted only `A:` and `L:` getters; its native reader had the same
omission. This dropped all 11 electrical/lighting input-event values. These
are not GTN touch events, and should never have been excluded with them.

Vanilla `var_watcher.h` reads the configured expression regardless of A/L/B
type; Coordinator uses each definition's existing expression and suppresses
equal toggle states. Preserve those YAML mappings and Jint setters unchanged.
The generic catalog now admits B getters within the existing AP/navigation
and excluded-module boundaries. Native reads use the SDK's typed B-variable
getter for existing values (`callable=false`), fail unavailable on invalid ID,
API error or nonfinite values, and still verify current state before executing
the existing setter. Equal updates execute nothing. No aircraft-specific
runtime rule, separate switch table or new ownership restriction is added.

The ordinary catalog identity changes, so both PCs need matching full packages
and helpers. Hardware learning, output proofs and connection preferences do
not depend on this ordinary catalog and are not reset. B toggles are not added
to automatic retry-safe setters. Actual two-PC switch verification is pending.

SDK references: [read existing B-variable ID](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/wasm/vars-api/fsvarsgetbvarid/),
[typed B-variable value](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/wasm/vars-api/fsvarsbvarget/).

## 0.9.2 yoke-only Steering pickup and left brake-axis candidate

The user explicitly removed pedal/brake matching from takeover. Native local
pickup and shared helper authority now gate Steering on roll/pitch only. Its
complete schema, masking and normalized output still include rudder, brakes and
detected trim. This generic ownership rule has no aircraft-name branch.

The friend's final three failures requested 1.1895%, 1.4863%, 1.3809% left brake
and observed 0.3789%, 0.4746%, 0.4395%. This is output readback, independent of
pickup alignment. The full Baron YAML/includes, role map and vanilla setters
were checked again. The SDK axis curve explains these ratios; incrementing
BRAKES events and writing EX1 SimVars are unsuitable absolute replacements.

The existing local button-user left output was verified with a linear response,
whereas the friend's absolute-axis case follows the nonlinear curve. The SDK
does not document this difference as a hardware-type rule. Preserve the working
default left setter and right curve; add optional normalized `axisInputResponse`
data to the left role. An observed absolute-axis source selects that response
for normalization and inverse output through the existing vanilla setter.
This is a candidate selection supported by the two captures, still subject to
the parked output check on the actual friend's aircraft. Do not call it an
established SDK distinction or claim both physical PCs are validated.

The compiler validates monotonic response samples and emits them in its generic
catalog. The output proof includes the effective response only when selected,
so the changed Steering route needs checking while existing button and engine
proofs remain valid. Profile identity includes the data: update both helpers
and packages together. No user's calibration/settings are reset. Output accuracy
tolerance is unchanged; no output-check failure is bypassed.

References: [SDK brake axis events and curve](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/key-events/aircraft-general-systems-events/),
[EX1 brake variables](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simvars/aircraft-simvars/aircraft-brake-and-landing-gear-variables/).

## 0.9.0 recovery and hardware binding audit

The complete Baron definition tree and vanilla Coordinator/Definition setters
were checked before this change. Flap detent variation stays in the Baron YAML:
three absolute SDK events replace one relative increment/decrement. Gear keeps
its existing absolute B-event. Both indexed altimeters remain normally shared.
Recovery reuses `CockpitDefinitionProfile.Plan`, vanilla's loader/Jint expressions
and the existing native writer/readback fences. Retry permission and readable
instrument labels live in `Definitions/cockpit/...json`; runtime has no Baron
name, flap detent count or barometer-ownership special case.

Two generic runtime additions are necessary because vanilla lacks them: an
explicit finite recovery of previously failed ordinary-state revisions, and a
raw hardware button that requests the fork's per-group Force ownership action.
The button listener uses SDK EnumerateControllers/MapInputEventToClientEvent_EX1,
separate from suppressed steering events. It saves local device identity, never
edits an MSFS input profile, and delegates to the existing Force command rather
than adding another ownership implementation. Definitions cannot express raw
controller enumeration, button assignment or the fork's ownership requests.
SDK APIs compile in the Windows WASM build; fake/loopback tests do not establish
actual controller delivery or aircraft output behavior.

Primary references: [absolute flap events](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-flight-control-events/),
[controller enumeration](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/inputevents/simconnect_enumeratecontrollers/),
[EX1 input mapping](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/inputevents/simconnect_mapinputeventtoclientevent_ex1/).

## AP, barometer and local GTN policy — investigation 2026-11-17

The user reaffirmed independent GTN screens and wants the pilot supplying AP
navigation to publish flight-plan changes to the other pilot. This section
records the agreed rules and implementation recommendation; it does not claim
that AP ownership or automatic plan copying is enabled.

| Function | Rule |
| --- | --- |
| Flight physics and AP engine | One authoritative engine on the physics host. Changing AP commander does not move physics hosting. |
| AP controls | A separately claimable role for either participant. Confirm handover before accepting the new owner's commands; keep the current steering owner's independent AP-off path. |
| NAV/APR guidance | A distinct source role, initially the host's navigator. Remote selection requires verified route application and execution-state compatibility; AP-command ownership alone does not change it. |
| Altimeters | Both physical instruments remain normally shared, separately indexed. No seat/AP-owner knob restriction. Preserve the aircraft's actual AP altitude-reference instrument. |
| GTN display | Local pages, map zoom, charts, touch/bezel interactions and display preferences. No screen-event replay. |
| Shared flight plan | One publishing navigator; the follower receives route data and confirms it. Local exploration/planning cannot silently replace the publisher's route. |

Black Square's **Baron Professional manual, p. 25**, identifies the pilot-side
encoding altimeter as the KFC 150's altitude reference. Seat/AP-role swaps therefore
must not switch the AP to the right instrument or copy one altimeter setting over
the other. The KAS 297B section (pp. 40–42) also distinguishes selector arming and
VS behavior; the UP/DN rocker requires pitch-reference coverage. Store any future
aircraft-specific source/gauge routes in definitions rather than hard-coding
"AP owner = left/right barometer" in the common ownership controller.
[Official manual](https://downloads.justflight.com/products/JFF004079/BKSQ_BaronProfessionalManual.pdf),
[readable copy of the same authored guide](https://msfs.dev-pset.com/assets/docs/BKSQ_BaronProfessionalManual.pdf).
The official printer-friendly PDF was also saved under
`artifacts/navigation-research/sources` for local review; nothing in aircraft
packages was edited.

The existing guidance protocol already separates commander and navigator and
tests stale-owner rejection, absolute targets, transfer and AP-off priority.
Actual Baron input suppression and guest-side AP-engine isolation remain
unverified. The earlier passive capture contained no button transitions, so it
does not certify a provider. Do not expose an AP claim merely because contract
tests pass. In the next live capture, correlate a short, named action with both
its key/InputEvent and cockpit readback before testing suppression; include KAS
arming and the rocker's ALT/VS/PITCH meanings.

### Preferred flight-plan path

PMS50 advertises **MSFS 2024 EFB sync** and confirms loading the framework for the
detected simulator version. Its installed 2.2.64 package contains the 2024 Working
Title route manager. That is a promising supported interchange path; the mere
presence of SDK code does not establish that this Baron's selected GTN responds.
[PMS50 features](https://pms50.com/msfs/),
[platform compatibility](https://pms50.com/msfs/msfs2024-compatibility/).

Microsoft's **Planned Route API** exchanges airports, procedures and enroute
route data. Its WASM getter reads the EFB route, not proof of the GTN's active
plan. The installed JS manager additionally exposes `requestAvionicsRoute`,
`avionicsRouteRequestResponse`, `getEfbRoute` and `sendRouteToAvionics`. Delivery
completion does not confirm instrument import; responses carry request ID and
route without a provider identifier. A route snapshot also lacks full active-leg,
Direct-To, OBS/suspend and approach-execution state. Keep route-copy support and
NAV/APR-source-transfer support as separate capabilities.
[Microsoft Planned Route API](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/wasm/planned-route-api/planned-route-api/).

Next, request an avionics route without any route writes while the GTN contains a
known distinctive plan and the EFB contains a different one. Confirm which unit
answered, edition/mode support, procedures/waypoints and subsequent GTN edits.
Only after that succeeds, test a bounded route import with AP off and request
the receiver's actual avionics route again. An unchanged EFB echo is insufficient.
Multiple conflicting responders must be reported, not silently selected.

The intended implementation sends a full route revision after an editor settles,
then uses periodic hash comparisons and explicit resync after loss/rejoin. Suppress
echoes and stale revisions. It must preserve local pages/settings and identify
receiver-side local planning rather than silently overwrite a draft. Until active
leg/Direct-To/procedure execution can be preserved and verified at the host, retain
host NAV/APR guidance even when the other pilot owns AP buttons.

The fallback is explicit user-driven PLN/SimBrief import. PMS50's manual documents
those imports but no flight-plan export-to-file function; catalog storage is
internal. Thus copying its `fpl.pln` is not a way to export current in-cockpit edits.
Its external-source integration describes local instrument coupling, not an
established cross-PC resync contract.
[PMS50 manual, pp. 21–26](https://pms50.com/fs2020/gtn750/documentation.pdf?v=2),
[integrator guide](https://pms50.com/fs2020/gtn750/integration.pdf).
Microsoft recommends modern route APIs over legacy FS9GPS; no legacy GPS-variable
writer will be added for this feature.
[Microsoft GPS API guidance](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/gpsvars/gps-variables-legacy/).

When ordinary definition streaming is connected, classify/exclude local GTN UI
events from the existing includes. The present no-baro PMS50 variant still lists
bezel H events. Blindly starting the entire vanilla stream would reintroduce
interaction replay and overlapping navigation/AP writers. Reuse the loader and
working setters with ownership-aware selection; do not reimplement the aircraft.

Reviewed 2026-11-16 against vanilla source before the EFB branch (`7b85a16`),
the current working tree, and the user's downloaded FS Copilot 1.2.1 definition
tree. No installed package, Downloads application or simulator setting changed.

The user has confirmed the installed **0.6.9** mixture calibration, blocking and
aligned takeover. Actual shared-flight output remains disabled/unverified.

**0.7.0 follow-through:** runtime aircraft selection and engine output now use
the catalog generated from the vanilla definitions and explicit role map.
The literal Baron selector and mixture names were removed from runtime code;
they remain only in definition data, generated catalog and test fixtures.
The helper selects/verifies that same profile before shared setup. New parked
checks require real movement and restoration before the test build advertises
shared output. Actual simulator validation is pending; see HANDOFF.md and
`FsCopilot.Definitions/README.md`. The table below records the preceding audit
and its decisions rather than claiming the 0.7.0 candidate is already installed.

## Findings and decisions

| Area | Current branch code | Existing vanilla behavior | Decision |
| --- | --- | --- | --- |
| Aircraft detection / mixture names | `Wasm/control_targets.h` checks a specific Baron directory and names two BKSQ LVars. | The Baron YAML already declares both cockpit mixture LVars in Percent, alongside generic engine mixture variables. | Replace the hardcoded selector/names with definition data and explicit control-role selection. Do not write both the generic engine value and cockpit reference blindly. |
| Typed cockpit reads | `Wasm/control_cockpit_refs.h` resolves existing native LVars and reads Percent; `control_test.cpp` switches these into axes 11/14. | Vanilla's loader understands LVars and units, but its SimConnect LVar registration can change shared units. | Reuse definition names/units; retain a generic explicit-unit reader. The 0.6.9 scale fix is necessary; YAML does not eliminate the native unit-conversion problem. Keep the tested path until its replacement passes. |
| Adapter ID and calibration reset | `control_test.cpp` emits `bksq-baron58p-cockpit-v1` and resets the two mixture samples on aircraft identification. | Vanilla selects a YAML using the aircraft name. | Derive adapter identity and affected-axis invalidation from the selected definition/role-map revision. Preserve saved-calibration compatibility deliberately. |
| Throttle/RPM/mixture output | `control_output.h` inverts learned mappings to engine-specific key events; `control_sim_output.h` transmits below masks. | Baron definitions use direct throttle values, propeller setters, and cockpit mixture LVars. | Use vanilla get/set routes through an ownership-aware executor. The failed generic mixture event tests did not exercise vanilla's configured LVar route. No new Baron-only writer was implemented. |
| Steering and rudder blocking | Common key-event catalogue, `control_runtime.h`, `control_gate.h`, `control_pickup.h`; includes sensor-rudder aliases. | Vanilla's `Surfaces.cs` syncs surface values but does not block the non-owner's local hardware. | Keep common masking/alignment logic. Definitions supply mappings; they cannot replace input interception. The rudder fix is a standard event alias, not a custom Baron physics implementation. |
| Learned raw-axis scales and storage | `control_pickup.h`, `control_profile.h` and saved slots. | YAML describes simulator values and setters, not the player's hardware raw range/direction or alignment state. | Keep generic calibration/persistence. Obtain the cockpit reference from definitions. The extra steering-axis detection policy is prototype behavior, not an aircraft mapping. |
| Altimeter observations/setters | `control_altimeters.h` reads standard indexed MB EX1/STD and has generic absolute SDK setters. | Existing Baron definitions have two relative setters plus a third barometer writer in the GTN include. | Fix the profile first. Standard pressure observations may remain useful; do not build a new Baron knob implementation around the broken expressions. |
| Altimeter seat ownership | Ownership/native barometer channels, `NativeAltimeterCoordinator`, transport/UI and recovery logic. | Vanilla allows ordinary shared instrument operation. | **Removed from the active product plan at the user's request.** Existing experimental code remains in the branch; production has no verified input provider and does not enable it. Do not add a provider or make this a prerequisite for flight controls. Restore normal shared instruments through corrected definitions. |
| KFC 150 / KAS 297B AP | `control_guidance.h` provides standard observations/setters; guidance authority/channel code is generic, with no verified actual Baron input adapter. | Baron YAML already includes AP variables, H events, UP/DN/selector routes and custom LVars. | Reuse those routes before adding any custom ones. Retain separate AP commander and physics/navigation-source policy. Definitions do not prove input blocking or make a toggle an absolute setter. Pitch-reference coverage still needs review. |
| GTN 750 | Current native AP transport does not implement GTN screen sync. Vanilla includes PMS50 definitions and interaction forwarding. | Existing module synchronizes many radio/GTN events and also includes a barometer mapping. | Keep the agreed local-screen policy. Do not automatically replay the whole module when importing definitions. Flight-plan-only exchange remains separate/unimplemented. |
| Pilot/copilot camera | `control_camera.h` uses the SDK's default cockpit Pilot/Copilot options (2/5), checks bounds and readback. | No equivalent seat-selection mapping is present in the reviewed aircraft YAML. | Retain common SDK camera behavior as a new requested feature; allow profile overrides if actual aircraft views differ. It is not Baron-specific camera geometry. |
| Generic ownership / world sync | Native session, authority, per-pair Auto, tolerance, leases and physics-host code. | Vanilla has a global master/shared model and existing physics/transport code. | Keep required group ownership, reuse vanilla transport/definitions where compatible. Running the old unrestricted Coordinator in parallel would bypass ownership. |
| Diagnostics, recovery and fixtures | `same_state_control_check.h`, `recover_mixture_profile.cpp`, actual-module/network fixtures and captures deliberately name the Baron. | These are validation/recovery tools, not aircraft mapping sources. | Retain their narrow aircraft guards and evidence. Do not generalize a recovery tool or recast a Baron test as verification of every aircraft. |

The literal production aircraft selector and BKSQ names are concentrated in
`control_targets.h`, `control_cockpit_refs.h`, and their integration in
`control_test.cpp`. Other Baron references are documentation, test fixtures or
bounded development tools. The pressure/AP/camera mechanics are mostly standard
SDK code; their existence alone does not mean they require an aircraft-specific
replacement.

## Confirmed definition defects and remaining uncertainty

The original Baron profile's two shared altimeter expressions are effectively:

```text
received > current ? INC : DEC
```

Equality therefore sends DEC. Once a target is reached, replaying the same target
issues a reverse command. A small and a large mismatch also produce exactly one
step. These defects are reproduced with the **actual vanilla Definition class
and Jint engine**, not a reimplementation of those expressions.

The included PMS50 module additionally maps `A:KOHLSMAN SETTING MB:0` to an
absolute setter, while the aircraft maps two custom instrument LVars to INC/DEC.
These three independent writers are a plausible source of interaction and
oscillation. The SDK documents an explicit altimeter index for `KOHLSMAN_SET`,
but lists no index parameter for INC/DEC; the constants 1/2 in the old expressions
are therefore not sufficient evidence that they select separate instruments.

This establishes defective configuration, **not a complete live reproduction of
the user's two-PC symptoms**. The meaning of the two BKSQ instrument LVars,
actual physical left/right index routing, STD behavior and concurrent knob use
still require simulator validation. Do not label the entire issue fixed yet.

Other import hazards found: duplicate heading/OBS/transponder getters across
the tree, and AP toggle events such as `AP_MASTER` that are not recognized by
the legacy Coordinator's literal `TOGGLE` name check. These need semantic
review before importing them into the new ownership path. Do not silently
deduplicate or rewrite every existing definition in this audit.

`SimClient.Set` also has independent L/A/K setters followed by a calculator-code
fallback for any prefix, so some master routes can issue two writes per update.
That is existing vanilla code, not a new branch change. The new shared barometer
expressions use the existing shared-expression execution path; this audit does
not change unrelated master setters. The definition adapter must choose a single
execution path and verify its units rather than copying that double-dispatch.

## Prepared changes

- `Definitions/bksq-aircraft-baronpropress.yaml` preserves the downloaded profile
  and authorship, replacing only the barometer mappings and GTN include. It
  reads each indexed tuned QNH in Millibars and emits an absolute
  `KOHLSMAN_SET` with pressure ×16 and the corresponding index. It refuses
  nonfinite/out-of-range pressures and suppresses changes below event precision.
  STD ownership/synchronization was not added.
- `Definitions/modules/PMS50_GTN_650-750_no_baro.yaml` is a scoped variant of
  the existing module with only its barometer mapping removed. The original
  module remains unchanged for other aircraft; all remaining mappings are
  checked against it.
- `Definition.ParseSet` now preserves an empty expression as an empty set of
  arguments, instead of falling back to writing the original variable.
  `Coordinator` returns before adding an echo-suppression count for a no-op.
  These are generic vanilla-core corrections, not Baron special cases.
- `FsCopilot.Definitions` now builds the existing vanilla definition source as
  one shared library. Desktop, EFB helper and the tests reference that same
  implementation. The canonical source remains at its vanilla location for
  review. The helper also packages the YAML files, but runtime selection and
  guarded control execution are not yet wired up.
- `FsCopilot.Definitions.Tests` exercises that actual shared vanilla library,
  reproduces the original defects, validates the candidate/included modules,
  and checks pressure/index ordering, replay stability, quantization, invalid
  values, unchanged mixture mappings and generic master/shared no-ops.
- `AGENTS.md` records vanilla-first reuse and the user's normal shared-altimeter
  policy for later work.

The profile stays in vanilla-compatible YAML syntax. It does **not** make the EFB
helper load definitions yet. The staged desktop candidate includes the generic
no-op fixes; testing only a YAML swap with an old executable would omit those
core fixes.

## Next implementation work

1. **Done:** share the vanilla definition loader/expression machinery with the
   EFB helper and desktop, and make profiles available beside the helper.
2. Add a small declarative map from existing definition entries to semantic
   controls (Steering, Throttle, RPM, Mixture). Legacy `master`/`shared` does not
   describe group ownership, hardware alignment or which of several mixture
   variables is the authoritative cockpit reference.
3. Feed those selected mappings to the generic native reader/executor, retaining
   session, owner, sequence and hardware-mask checks. Remove the literal Baron
   selection only after equivalent local behavior is verified.
4. Validate the corrected vanilla barometers with both players and normal knob
   use. Keep both instruments independent without seat-owner restrictions.
5. Validate actual shared control output and then two-PC handovers. Remote output
   stays disabled until that evidence exists.

Validation: **1,059 checks** through the actual vanilla loader/expression engine,
including preservation of all 448 unaffected definition entries. The desktop
Release build/publish pass with the four existing warnings. Run the checks with:

```powershell
./FsCopilot.Definitions.Tests/run.ps1 -DotNetPath ./artifacts/toolchain/dotnet-9.0.321/dotnet.exe
```

Validation receipts and builds: `artifacts/vanilla-reuse-audit/`. The separate
desktop candidate is for validating the vanilla definition correction; it is not
an installed EFB update. Actual simulator/two-PC verification remains pending.

Primary references:

- [SDK instrumentation events: absolute pressure and altimeter index](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/Key_Events/Aircraft_Instrumentation_Events.htm)
- [SDK indexed tuned pressure and STD variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-system-variables/)
- [Black Square's supported cockpit-mixture interfaces](https://support.justflight.com/support/solutions/articles/17000132376-why-does-the-mixture-behave-strangely-in-the-turbocharged-version-and-i-cannot-bind-it-to-hardware-c)
