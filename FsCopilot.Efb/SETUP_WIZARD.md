# Shared output setup wizard

**0.9.8 candidate:** the Nosewheel setter reverses its event sign to match
actual MSFS readback (0.9.7 requested 51% and observed 48.999%). Learning is
kept; rerun only the missing wizard groups after updating. No reset/Relearn
is needed for this correction. The native output check still requires actual
movement and restoration; its tolerances/coverage have not been reduced.

**0.9.7 policy:** Steering contains yoke roll/pitch, rudder, tiller/nosewheel
and both brakes. All three trims are ordinary bidirectional shared instruments;
Steering never blocks them and the wizard never learns or tests them. Only
yoke roll/pitch must align for Steering pickup. Rudder/tiller/brakes retain
fixed state mappings and require no manual learning or alignment.

**Reset setup** appears beside Setup wizard, with inline Clear setup/Cancel.
It clears only the current aircraft's pickup learning and all output-check
proofs, including the saved alternate brake response. Both pickup generations
are replaced with empty records and the output store is revoked before the
in-memory learner resets. Connection preferences, tolerance, Force binding,
ownership and aircraft positions are unchanged. Duplicate requests cannot
erase newly learned input a second time. Storage failure reports failure and
allows an explicit retry. The native operation sends no aircraft-control write.

Reset requires fresh local parked state, parking brake ON, AP OFF, all controls
on Me, no applying/pending handover, no shared session/recovery hold and no
active wizard/output test. It does not require engines OFF; the subsequent
wizard does. Reset does not repair a disconnected/failed simulator writer.
After confirmed reset, press Setup wizard. No simulator restart is needed to
reset/relearn once this package is installed. Disabled buttons state why.

The following versioned notes are historical.

Implementation guide for **Setup → Setup wizard**. The wizard replaces the
repeated Me/Mock assignments and four separate actions-menu checks. Once the
aircraft is ready, the user should only need to move, align or hold their
hardware when prompted. Configuration and progression are automatic.

**0.9.5 candidate policy (not installed):** Steering takeover waits for yoke
roll and pitch only. Rudder, nosewheel, both brakes and all three trims belong
to the supported aircraft's fixed definition list before any hardware input.
The wizard asks for roll/pitch and engine levers only. Pedals, brake buttons or
axes, tiller and trim need no discovery, two-position learning or matching.
They share actual cockpit demand through vanilla getters/setters. Keep the
parking brake on. The automatic output diagnostic still checks every fixed
Steering route, independently of hardware alignment. If a declared alternate
brake response matches actual readback, the check selects it once and verifies
movement and restoration before saving it. Later hardware events cannot change
that output route or invalidate the saved check. Existing learning and valid
engine checks are retained; the expanded Steering route needs one new check.

**Status:** implemented and installed in 0.7.4. Local tests and the Windows WASM
build pass, and the user confirms the wizard works in the actual Baron after
being asked to complete all four checks and return tested controls to Me. The
update includes the definition-driven right-brake curve correction. Restart
reuse in 0.7.4 and the matching two-PC test remain unconfirmed; cancellation and
failure scenarios have automated coverage with simulated SDK calls.

## Entry and progress

Place **Setup wizard** beside the shared-output status near the top of Setup.
Show **Steering, Throttle, RPM, Mixture**, each marked **Saved**, **Still needed**,
**In progress**, or **Failed**. Keep the explicit missing-group list; a fraction
alone is insufficient.

Use one focused panel inside Setup with the current group, one instruction,
the relevant hardware/cockpit alignment display, and **Cancel**. Identify each
axis so the user can distinguish Throttle 2 or Right brake. Advance when the
requested movement/alignment completes, without repeated Next clicks or menus.

Opening text: **“This checks Steering, Throttle, RPM and Mixture. Move your
controls when asked, then hold them still. The wizard temporarily holds each
group, checks its cockpit movement, and returns it to Me.”**

## Aircraft readiness

Run locally before joining a shared session. Require a fresh native bridge,
supported output definitions, stationary aircraft on the ground, parking brake
on, and AP off. For the initial wizard release, require engines off as well,
because learning prompts can move live throttle and mixture controls. Candidate
0.7.4 adds an explicit engine-state observation separately from the existing
individual-check parked guard.

Show the exact unmet prerequisite, such as **“Set the parking brake”**, rather
than an unexplained disabled button. Aircraft preparation happens before Start.
Once running, Me/Mock, quadrant Auto and output checks need no manual setup.
Recheck readiness throughout the run.

Reject starting while a shared session is armed/active, shared controls need
recovery, another output check is running, or the native bridge is applying an
unconfirmed control change. Do not silently end a shared session.

## Automatic sequence

On Start, obtain the native list of valid saved results and skip groups whose
current output routes already have valid results. If all four are saved, show
**“Setup complete. All four output checks are saved.”** without masking or
moving anything.

For missing groups, remember the quadrant Auto setting and pause Auto for the
entire run. Preserve tolerance and existing calibration. Process **Steering →
Throttle → RPM → Mixture**, skipping saved groups.

| Step | User instruction | Automatic action and completion condition |
| --- | --- | --- |
| Return the current group to Me if needed | “Align these controls to begin.” | Request ordinary aligned takeover and wait for confirmed Me. Skip if already Me. Never Force takeover automatically. |
| Read hardware | “Move roll and pitch, then center the yoke,” or “Move both throttles.” | Require current hardware input only for yoke roll/pitch or the selected engine pair. Reuse saved mappings. Only those alignment axes may receive “Pause at two positions” prompts. |
| Settle controls | “Center the yoke and hold still,” or “Hold both levers still.” | Wait for stable alignment hardware and fresh cockpit readings. Fixed Steering states use actual unmasked cockpit demand; no pedal, brake, tiller or trim movement is requested. |
| Hold the group | “Checking Steering. Keep controls still.” | Assign only this group to Mock and wait for native mask confirmation. Other groups retain their state. |
| Check output | Keep hardware still. | Run the existing same-position, 1% movement and restoration check, including SDK checkpoint and fresh matching observations. |
| Save | “Steering passed. Saving…” | Save through the existing validation store and confirm success before marking Saved. |
| Return control | “Align yoke roll and pitch,” or “Move these levers into the highlighted range.” | Request Me automatically. Only roll/pitch gate Steering takeover. Pedals, brakes and trim transfer without alignment. Wait for confirmed Me before advancing. |

For the supported Baron profile, Steering always includes roll, pitch, rudder,
nosewheel, both brakes and elevator/aileron/rudder trim. Hardware discovery never
adds or removes a member. Combined brake buttons, separate buttons and axes all
drive cockpit demand while Me is active; non-owner events remain masked. Only
yoke roll/pitch need a learned hardware pose for alignment.

The output diagnostic retains its own strict movement thresholds.
**Tight/Medium/Loose** changes hardware takeover tolerance; it must not make an
inaccurate output route pass.

Once every needed group has passed and returned to Me, restore and confirm the
original Auto setting. Leave tested groups on Me; untouched saved groups retain
their initial ownership. Finish with all four named results and saved status.
Say **“Ready for shared flight”** only when the native shared-output gate is
satisfied. Each PC performs its own local setup before joining.

## Cancellation and failure

Cancel is a native operation, not removal of the panel. If diagnostic movement
has begun, stop new test movement and attempt restoration within the existing
bounded check while its aircraft, mask and parked context remain valid. Then
request aligned Me for the current group and show hardware movement needed to
finish cancellation. Never reconnect through an automatic forced takeover.

After clean cancellation with restoration and Me confirmed, restore prior Auto.
On SDK/output failure or unconfirmed restoration, keep Auto off and show the
actual control state and recovery instruction. Preserve masks where the
connection supports them; do not claim a disconnected bridge still blocks
hardware. Do not write old targets into a changed aircraft or automatically
resume after reconnection.

Identify **group, axis, phase and reason**, for example: **“Steering · Right
brake: requested movement was not reached. Original position restored. Auto is
paused.”** Distinguish movement failure, hardware interference, lost parked
state, missing mapping, failed restoration and failed saving. Offer Retry after
safe recovery. Retain scoped Force takeover as an explicit user recovery choice.

Keep earlier successful saved results. A failed recheck leaves that group's
previous proof revoked through the existing durable revocation before movement.
Closing/reopening Setup redisplays the native step. Reloading the EFB view must
not duplicate a check, lose the original Auto setting, or abandon cleanup.

## Persistence

Reuse the existing aircraft/engine identity and per-group output-route proofs
in `pickup-<aircraft-hash>-<engines>-output.0.bin` and `.1.bin`, beside saved
learning in the package's writable WASM `work` directory. Do not add a second
“wizard completed” flag that could disagree with those proofs.

Later sessions reuse unchanged saved results without diagnostic movement.
Hardware positions still need fresh observation and normal alignment. A changed
output route invalidates its affected group; unrelated UI changes must not
repeat all four checks. Storage failure cannot be presented as saved success.

## Implementation sequence

1. **Resolve the actual Steering output failure.** The 0.7.3 observer recorded
   left-brake demand 326/32768 (about 1%) and right-brake demand 104/32768
   (about 0.32%) during a requested 1% nudge. Original positions were restored
   and the observer reported zero SDK errors. Investigate the existing YAML
   setter and reference before runtime changes; the cause is not yet established.
   Preserve diagnostic thresholds and working engine routes.
2. **Add native orchestration.** Keep original Auto, group, step, cancellation
   and recovery in WASM. Reuse `control_runtime`, `control_output_check`, the
   parked guard and validation storage. Add restore-on-cancel behavior: current
   `control_output_check::cancel()` only marks the check failed.
3. **Expose commands and structured status.** Add start/cancel/retry plus native
   readiness, group, axes, progress, failure axis/reason and restoration status.
   Make commands idempotent. Reject competing ownership, Relearn, Auto and Join
   commands while running, retaining explicit emergency recovery.
4. **Build the Setup panel.** Render native status through the existing
   controller. Preserve keyboard access, readable labels and Setup scrolling.
   Avoid a JavaScript timer chain controlling simulator operations; reopening
   the view must reconnect to the same native run.
5. **Log and verify.** Record aircraft/adapter, step, axis, expected/observed
   values, SDK errors, save outcome, original/restored Auto and final mask.
   Complete the acceptance checks before packaging a matching update.

Implementation starts in [`control_test.cpp`](Wasm/control_test.cpp),
[`control_runtime.h`](Wasm/control_runtime.h),
[`control_output_check.h`](Wasm/control_output_check.h),
[`control_output_validation_storage.h`](Wasm/control_output_validation_storage.h),
[`controller.js`](src/controller.js) and [`view.js`](src/view.js).
Actual failure evidence is
[`live-steering-output-observer.log`](../artifacts/mixed-brakes/tests/live-steering-output-observer.log).

## Acceptance checks

- One entry handles all needed groups without manual Me/Mock, Auto, Relearn or
  Check shared output menu operations.
- Cover every saved/missing group combination. A fully saved setup performs no
  diagnostic writes; restart reuses valid results.
- Combined/separate brake buttons, absolute pedal axes and relative rudder work
  without calibration, with parking brake on. Test mixed button/axis peers.
- Missing roll/pitch/engine calibration names the axis, learns only while Me
  owns it, and preserves other mappings.
- Command submission alone cannot advance a step: masking, restoration, saving,
  Me attachment and restored Auto require native confirmation.
- Hardware interference fails clearly and attempts safe restoration. Loose
  takeover tolerance cannot hide a bad output route.
- Cancel at each step, view close/reload, duplicate commands, bridge loss,
  aircraft change and failed restoration cannot create a false pass or forced
  takeover. Saved successes survive interruption of a later group.
- Auto initially on/off is restored after normal completion. Failure explains
  any held control or paused Auto setting.
- Check EFB readability/scrolling, then each actual output route and the full
  guided flow in the parked Baron, followed by restart and two-PC tests. SDK
  simulations alone do not establish simulator success.
