using System.Text.Json;
using FsCopilot.Native;

if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("The SimConnect probe requires Windows."); return 2; }
var prefix = args.FirstOrDefault(arg => arg.StartsWith("--prefix=", StringComparison.Ordinal))?[9..] ?? "";
var durationArg = args.FirstOrDefault(arg => arg.StartsWith("--seconds=", StringComparison.Ordinal))?[10..];
var seconds = durationArg is null ? 20 : int.Parse(durationArg);
var expectExpiry = args.Contains("--expect-expiry");
var sessionTest = args.Contains("--session");
var observeSession = args.Contains("--observe-session");
if (sessionTest && prefix.Length == 0) throw new ArgumentException("Session command fixture checks require an isolated test prefix.");
if (expectExpiry && prefix.Length == 0) throw new ArgumentException("Expiry checks require an isolated test prefix.");
if (seconds is < 1 or > 300) throw new ArgumentException("Probe timeout must be 1..300 seconds.");
var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var serials = new HashSet<ulong>(); var lastError = "No native reply received.";
var sessionSent = false; var sessionStateSent = false; var sessionInputSent = false; var sessionAccepted = false;
using var client = new NativeControlClient(prefix, enableSessions: sessionTest || observeSession);
client.StateChanged += state => {
    if (!OperatingSystem.IsWindows()) return;
    if (state.Error.Length != 0) lastError = state.Error;
    if (sessionTest && state.Live && state.Session is { } shared) {
        if (!sessionSent && shared.Phase == NativeSessionPhase.Armed) {
            var command = new NativeSessionPacket {
                Kind = NativeSessionKind.Bind, Native = shared.Native, Backend = state.Backend, Consent = shared.Consent,
                Session = new string('b', 32), Epoch = 7, Command = 1, Revision = 1, Engines = 2, Axes = 0x7e03,
                Generation = [1, 1, 1, 1], Owner = [NativeOwner.Local, NativeOwner.Local, NativeOwner.Local, NativeOwner.Local], Flags = NativeSessionFlags.PhysicsHost
            };
            sessionSent = OperatingSystem.IsWindows() && client.TrySendSession(command);
        }
        if (sessionSent && !sessionStateSent && shared.Phase == NativeSessionPhase.Active && shared.Ack == 1 && shared.Result == NativeSessionResult.Succeeded) {
            if (shared.Backend != state.Backend || shared.Session != new string('b', 32) || shared.Epoch != 7 || shared.Blocked != 15 || shared.Barrier != 1)
                done.TrySetException(new InvalidOperationException("Session fixture acknowledgement changed across SDK transport."));
            else {
                var command = new NativeSessionPacket {
                    Kind = NativeSessionKind.State, Native = shared.Native, Backend = state.Backend, Consent = shared.Consent, Session = shared.Session,
                    Epoch = 7, Command = 2, Revision = 2, Engines = 2, Axes = 0x7e03, Generation = [1, 2, 1, 1],
                    Owner = [NativeOwner.Local, NativeOwner.Remote, NativeOwner.Local, NativeOwner.Local], Attached = 2,
                    CommittedTransfer = ["", new string('4', 32), "", ""]
                };
                sessionStateSent = OperatingSystem.IsWindows() && client.TrySendSession(command);
            }
        }
        if (sessionStateSent && !sessionInputSent && shared.Ack == 2 && shared.Result == NativeSessionResult.Succeeded) {
            if (shared.CommittedTransfer[1] != new string('4', 32) || shared.Generation[1] != 2 || shared.Owner[1] != NativeOwner.Remote)
                done.TrySetException(new InvalidOperationException("Session commit identity changed across SDK transport."));
            else {
                var positions = Enumerable.Repeat(-1d, 21).ToArray(); positions[9] = 0.25; positions[12] = 0.75;
                var command = new NativeSessionPacket {
                    Kind = NativeSessionKind.Input, Native = shared.Native, Backend = state.Backend, Consent = shared.Consent, Session = shared.Session,
                    Epoch = 7, Command = 3, Revision = 2, Generation = [1, 2, 1, 1], Selection = 2, Sequence = 1,
                    Axes = (1u << 9) | (1u << 12), Positions = positions
                };
                sessionInputSent = OperatingSystem.IsWindows() && client.TrySendSession(command);
            }
        }
        if (sessionInputSent && shared.Command == 3 && shared.Ack == 2) sessionAccepted = true;
        if (sessionAccepted && serials.Count >= 3 && !expectExpiry) done.TrySetResult();
    }
    if (expectExpiry && serials.Count >= 3 && (!sessionTest || sessionAccepted) && !state.Live && state.TransportConnected && state.Error.Contains("timed out", StringComparison.Ordinal)) { done.TrySetResult(); return; }
    if (!state.Live || state.Snapshot is not { } packet || !serials.Add(packet.Serial)) return;
    if (!packet.Flags.HasFlag(NativeStatusFlags.Ready)) { serials.Remove(packet.Serial); lastError = "Native link is live; waiting for ready aircraft/control observations."; return; }
    if (prefix.Length != 0 && (packet.Native != "abc123def456" || packet.Blocked != 14 || packet.Required != 0x7e03 ||
        packet.Capabilities != 11 || packet.Input[9] != 0.23 || packet.Input[12] != 0.77 || packet.Targets[11] != 0.61 ||
        !packet.Tolerance.SequenceEqual(new uint[] { 10, 20, 30, 10 }))) {
        done.TrySetException(new InvalidOperationException("Live native transport fixture values changed across the SDK boundary.")); return;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { packet.Native, packet.Serial, packet.Request, packet.Aircraft, packet.Adapter,
        packet.Flags, packet.Blocked, packet.Automatic, packet.Engines, packet.Required, packet.Capabilities,
        packet.Learned, packet.Fresh, packet.ConnectionApi, packet.Connection, packet.BackendPhase, packet.BackendCode,
        throttle1 = packet.Input[9], throttle2 = packet.Input[12], mixture1 = packet.Targets[11], mixture2 = packet.Targets[14],
        rudderInput = packet.Input[2], rudderTarget = packet.Targets[2], brakeLeftInput = packet.Input[4], brakeRightInput = packet.Input[5],
        brakeLeftTarget = packet.Targets[4], brakeRightTarget = packet.Targets[5],
        hardwareMixture1 = packet.Input[11], hardwareMixture2 = packet.Input[14],
        sharedPhase = state.Session?.Phase, sharedBlocked = state.Session?.Blocked,
        sharedContext = state.Session is { } summary ? new { summary.Session, summary.Epoch, summary.Revision, summary.Owner,
            summary.Attached, summary.Flags, summary.Command, summary.Ack, summary.Result, summary.Code, summary.WorldSerial } : null,
        worldValuesValid = state.Session is { } worldState && NativeWorldFrame.ValidWorld(worldState.World),
        unknownRequiredTargets = Enumerable.Range(0, 21).Where(axis => (packet.Required & (1u << axis)) != 0 && packet.Targets[axis] < 0).ToArray(),
        guidanceLive = client.GuidanceState.Live, guidanceFlags = client.GuidanceState.Snapshot?.Flags,
        cockpitLive = client.CockpitState.Live,
        cockpit = observeSession && client.CockpitState.Packet is { } cockpit ? new {
            cockpit.Native, cockpit.Serial, cockpit.Profile, cockpit.Count, cockpit.Active, cockpit.Linked,
            known = Enumerable.Range(0, (int)cockpit.Count).Count(cockpit.Known),
            values = cockpit.Values.Take((int)cockpit.Count).ToArray(), cockpit.Valid
        } : null }));
    var observationsReady = !observeSession || state.Session is not null &&
        ((packet.Capabilities & 16) == 0 || client.GuidanceState.Live) &&
        ((packet.Capabilities & 32) == 0 || client.CockpitState.Live);
    if (serials.Count >= 3 && !expectExpiry && (!sessionTest || sessionAccepted) && observationsReady) done.TrySetResult();
};
if (await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(seconds))) != done.Task) {
    Console.Error.WriteLine("Native discovery probe timed out: " + lastError); return 1;
}
await done.Task;
if (sessionTest) Console.WriteLine("PASS actual native session bind/state/commit-ticket/input SDK transport in isolated fixture areas. No hardware masks or aircraft writes.");
if (observeSession) Console.WriteLine("PASS current native session and advertised AP status observed without sending session or AP commands.");
Console.WriteLine(expectExpiry ? "PASS actual C# transport expires after the native server closes despite retained ClientData. No aircraft/control writes." :
    "PASS actual C# SimConnect transport: fresh native discovery and three current observation packets. No aircraft/control writes.");
return 0;
