# Persistent Windows transport rejoin regression

Run `dotnet run --project FsCopilot.Native.Windows.Tests -c Release` on Windows.

The project links the actual production `NativeCockpitClient.cs` and
`NativeGuidanceClient.cs`. Only the external SimConnect API is replaced with a
capturing boundary. The production worker loops, serialization, primary-state
gates, execution fences, queues, expiration and replay checks run unchanged.
There is no real SDK DLL, simulator connection or aircraft write in this test.

Each channel sends command 2001, changes to a new verified session on the same
SDK connection and sends command 1, then repeats rejoin. The original code
fails at the first new-session command 1. Additional cases refuse duplicate or
older commands within a session, queued writes from retired sessions, stale
failure notifications, expired commands and a declined final model fence.
Detaching/replacing an execution fence does not reset the same-session counter.
Only a successful SDK dispatch advances it. The test asserts that the SDK
connection was never recreated, so restarting the helper cannot mask the bug.

The 0.9.12 correction uses the immutable native/backend/session identity. AP
value/epoch or cockpit execution-fence changes are deliberately not new replay
scopes. All current native-state, scope, freshness and ownership gates remain.
Actual two-PC MSFS switch and engine-start validation is still required.

The project also links the production helper instance guard. It exercises the
real Windows exclusive file lease, duplicate refusal, replacement of a simulated
older lease owner, failed-stop/newer/unknown refusal and restart after disposal.
Process enumeration/termination is injected for these tests; they never stop
an actual helper. `FsCopilot.Efb/tests/startup.test.ps1 -HelperPath <published exe>`
tests the actual startup configuration script against temporary XML fixtures.
