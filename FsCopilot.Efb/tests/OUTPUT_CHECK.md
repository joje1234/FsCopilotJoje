# Bounded Baron output diagnostic

This is a development diagnostic, separate from the EFB helper and the shared
session controller. It validates the existing output adapter against the
accepted **solo 0.5.3** module or installed **unbound 0.6.9** module. It does not enable shared output, create a network
connection, bind a native session, change physics/freeze state, or write profiles.

Build and exercise the actual diagnostic with local SDK implementations:

```powershell
./FsCopilot.Efb/tests/run-output-check.ps1
```

The live executable is staged as
`artifacts/native-output-check/live/control_write_probe.exe`, beside its SDK DLL.
The script only builds it. It does not run a live control test.

Live prerequisites are a fresh, stationary Black Square Baron 58P with parking
brake on, both EFB groups on Me, Auto off, and saved mappings. The probe obtains
a fresh native challenge reply, reads the actual cockpit positions, and loads
the matching checksummed aircraft profile. It refuses mismatched, missing,
unlearned, stale, non-solo or moving contexts before adding any mask. Keep the
selected hardware pair stationary during the test.

For the API-1 package, it also reads the native session status and requires a
fresh matching-native Idle observation with empty binding/consent, no owners,
no pending/attached controls and no held world freeze. It does not send a session
command. Armed, active, stale or missing status refuses or ends the check. Remote
physical-output capability must still be off; Me ownership alone is insufficient.

The diagnostic now distinguishes captured hardware noise from interference.
Only events from the saved hardware source can qualify, and their calibrated
position must remain within 0.45% of the initial hardware position. That is below
the 0.49% minimum displacement needed to prove the 1% output movement. Events on
any output route, unknown routes, larger/cumulative motion and unexplained native
input changes still fail. If hardware and output share the same event name, its
events cannot qualify as noise. Masks still prevent allowed noise from reaching
the aircraft during the check; output must have its own matching readbacks.

For the current user's actual WASM work directory, the first command is:

```powershell
$work = 'C:\Users\jorge\AppData\Roaming\Microsoft Flight Simulator 2024\WASM\MSFS2020\fscopilot-controls-test\work'
./artifacts/native-output-check/live/control_write_probe.exe --same-state-ground-test --group=throttle "--profile-root=$work"
```

This temporarily masks only the throttle pair in its own SDK connection and
writes its current measured positions using `group_output` and
`control_sim_output`. It waits for an SDK checkpoint plus three fresh read-backs
and checks that output did not reappear as hardware. It clears its own group,
confirms that cleanup, then closes its connection. It never changes the EFB's
existing masks or ownership. The mask phase is bounded to four seconds; cleanup
has a further two-second confirmation window, with connection close on every
exit path.

A same-value write cannot prove that movement works. The explicit
`--nudge-and-restore` option performs a small real lever movement instead. It first runs
the same-state check, requests a 1% movement of each selected lever toward an
in-range value, requires a distinct matching read-back, then sends the original
positions and requires their read-back again. Raw event quantization is allowed
up to 0.51%; unchanged read-back cannot pass the movement check. The modes are
mutually exclusive. `--group=rpm` and `--group=mixture` select the other complete
engine pairs. Each run touches only one pair, and only the aircraft declared in
the saved profile.

On a write/SDK failure it stops issuing new test movement, attempts restoration
while the same aircraft/adapter remains available, and releases its own mask.
If an SDK write itself failed, original positions may not be confirmed; the
result explicitly fails and local hardware is reconnected by mask removal/close.
No failure result may be counted as shared-flight verification. Even a passed
direct SDK test does not establish the new embedded WASM session path or actual
two-PC synchronization; those remain separate tests.

The SDK documents the parking-brake Boolean and the priority flag used to send
the output below the temporary mask. See [brake variables](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simvars/aircraft-simvars/aircraft-brake-and-landing-gear-variables/)
and [EX1 event transmission](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/events-and-data/simconnect_transmitclientevent_ex1/).

Original diagnostic evidence: `artifacts/native-output-check/verification.json`.
Current live evidence: `artifacts/baron-ap-input/verification.json`. On the parked
Baron, throttle and RPM same-state/movement/restoration/mask-cleanup checks pass.
Mixture refused before masking/writing against 0.5.3 because its cockpit reference
was unavailable. Version 0.6.8 then exposed a scale regression after restart;
0.6.9 fixes this with native Vars API reads in explicit Percent units. Actual
hardware/cockpit mixture reads now agree at 48.81% without Relearn.

The 0.6.9 AXIS output check still fails distinct movement: a 1% request leaves
the cockpit mixture levers at their original positions. The diagnostic-only
`--mixture-set-route` (requires `--group=mixture`) uses absolute
`MIXTURE1_SET/2_SET` values from 0 to 16384 and also fails that live check.
Both runs confirm the original readout and remove their own masks. An unshipped
priority experiment instead hit its own mask during the same-state step and
aborted; it was removed. See `artifacts/mixture-typed-reader/verification.json`
and that directory's `tests/live-0.6.9-mixture-*.log`. No failed result enables
shared output or verifies two-PC flight.

`--standard-priority` selects the SDK's named STANDARD priority instead of the
production DEFAULT. Both lie below the hardware masks; arbitrary adjacent
priority values are rejected because the SDK relocates occupied group slots.
The live private-event check confirms STANDARD isolation, but a subsequent
Baron `--mixture-set-route --standard-priority` check still fails distinct
movement. Original-position readback and mask cleanup pass, with no SDK errors.
Receipt: `artifacts/mixture-output-route/verification.json`. The installed 0.6.9
package and its production DEFAULT output route are unchanged.

## Read-only altimeter and camera identification

The same build script also stages `live/cockpit_state_probe.exe`. The latest
12-variable build is also at `artifacts/altimeter-sdk/live/cockpit_state_probe.exe`.
It only opens the SDK, requests the aircraft identity and reads twelve variables. It sends no
control events, changes no camera settings and writes no ClientData.

```powershell
./artifacts/altimeter-sdk/live/cockpit_state_probe.exe --seconds=30 > artifacts/altimeter-sdk/tests/live-cockpit.csv
```

The CSV contains indexed displayed and EX1 tuned barometric pressures and standard-pressure flags,
the two `INSTRUMENT_Altimeter` LVars, camera state, view type, view option and
the reported maximum pilot-view option. The left/right column labels are
candidates: correlate them with deliberate changes to one cockpit knob at a
time before assigning instrument ownership. A successful read alone cannot
establish which knob controls which instrument.

The EX1 pressure is needed to retain tuned QNH while STD is selected. Ordinary
`KOHLSMAN SETTING MB` instead reports standard pressure in that mode. The capture
keeps both variants so a future write test cannot silently lose the tuned value.

The SDK documents an explicit altimeter index for `KOHLSMAN_SET`, while its
`KOHLSMAN_INC` and `KOHLSMAN_DEC` entries list no parameters. Do not assume a
relative event's first value selects the left or right instrument. See
[instrument events](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-instrumentation-events/)
and [indexed instrument variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-system-variables/).

The default camera table uses pilot-view options 2 for Pilot and 5 for Copilot;
it also contains unused indices. The Baron's actual views still need read-back
verification. `COCKPIT CAMERA SIDE` is documented as unused, so it cannot be the
basis of seat selection. See [camera variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/camera-variables/).
