# Control value isolation (0.9.11 candidate)

The 2026-11-20 host/guest helper logs share a final aircraft frame sequence of
76807. The host reports unavailable RPM readbacks (slots 13 then 10), while the
guest subsequently reports delayed host updates and an ended native session.
The exact original raw RPM value was not captured. A local reproduction with
the actual production validator confirmed that one required target marked -1
discarded an otherwise valid complete physics frame. The local relay regression
also reproduced stream loss at the Windows send gate before its correction.

This is a generic correction to EFB shared control transport. Vanilla's complete
Baron definition/include tree and typed absolute setters were retained. No
aircraft exception, guessed feather range, control setter, ownership rule, timing
constant or stored calibration/output-check format is changed. Vanilla has no
combined EFB ownership/physics stream to reuse for this behavior.

| Path | Handling |
| --- | --- |
| Roll, pitch, rudder, tiller and both brakes | Invalid normalized readbacks become unknown. Steering output waits as one complete group; physics and engine groups continue. Alignment still requires only roll/pitch. |
| Throttle, RPM and mixture, engines 1–4 | Each engine has its own target. An unknown member pauses only that complete engine pair/type. Independent valid left/right positions still use separate existing setters. |
| Hardware input | Invalid samples do not erase a saved scale. Cockpit SET echoes or another event cannot replace a validated binding during a session. A deliberately changed binding requires explicit Relearn. |
| Local acquisition | Non-finite or significantly out-of-range readings become -1. Existing small endpoint rounding allowance is retained; large outliers are not clamped to a guessed extreme. Learning cannot consume unknown targets. |
| Aircraft layout / AP observation | A malformed engine count cannot replace the last verified layout. Startup waits for a valid first count. Unknown AP state cannot grant manual steering. A genuine new aircraft/layout still invalidates the session. |
| Native IPC and network packets | Length, schema, identity, generation, sequence and ownership checks remain strict. NaN/infinity/arbitrary out-of-range positions on the wire are refused; only the explicit unknown marker is allowed. |
| Owned control frames | Still require a complete valid group. Unknown hardware/cockpit demand cannot be written, satisfy aligned pickup or be replaced by a remembered value. |
| World producer and Windows send fence | Permit unknown control slots while preserving the full fixed schema. Fresh, valid position/physics continues independently of control availability. |
| Native guest world executor | Preflights each complete group separately, skips unavailable groups and applies fresh physics plus valid groups. A held group's old cockpit position cannot produce a current aligned-pickup proof in native status, EFB or helper. New complete readings resume without reconnecting. Replayed/wrong-owner/stale-generation frames remain refused. |
| Ordinary instrument catalog, including trims, gear/flaps, lights and barometers | Reads are finite and bounded per instrument, with independent known bits. Invalid values cannot stop neighboring entries. A reappearing guest read establishes a baseline instead of manufacturing a knob request. Existing definition setters and per-instrument failure handling remain in use. |
| AP / navigation channels | Existing typed range and finite checks, independent channel failure handling and ownership fences remain. No change to the GTN feature scope. |
| Diagnostics | Native logs record invalid/recovered control names, raw readout and units on state transitions. Helper warnings identify unavailable groups; recovery is logged. |

Both PCs need the complete matching 0.9.11 kit. Join behavior revision 3 prevents
mixed old/new helpers from establishing a shared session. Binary packet layouts
and saved setup signatures are unchanged. The native package must also match;
the kit preflight checks both the helper and all seven installed package files.

## Verification

- Native normalization matrix: all 21 slots, 1–4 engines, NaN, both infinities,
  negative/high outliers and recovery; saved scale/event retention.
- Native world execution: unknown in each slot, complete-group isolation,
  sustained all-unknown frames beyond both recovery deadlines, current-value
  resumption, invalid packets, masks, freezes and replay/authority fences.
- Real C++ module + C# backend through the local relay: all 21 fault slots,
  nine-second sustained loss, fresh physics, held masks and recovery without
  Relearn/reconnect. Simulator I/O is captured by SDK fixtures.
- Ordinary catalog: all 512 replication slots isolate a missing neighbor;
  native NaN/infinity/huge reads and guest telemetry recovery are covered.
- Existing native contracts, definitions/Jint, ownership, UI and compiled-WASM
  stack checks remain required before freezing the candidate.

These are local checks, not an actual two-PC simulator validation of the fix.
The engine-start/mouse/feather sequence must be repeated in MSFS on both PCs.
A legitimate position outside the currently verified definition range will be
reported unavailable, not guessed or forced into another position. Capture its
new raw-value warning before extending any aircraft definition range.

Actual loss of simulator/peer/physics freshness, changed identity, or a rejected
critical SDK write still ends/holds the appropriate session. This change does
not send cached physics or suppress those failures.
