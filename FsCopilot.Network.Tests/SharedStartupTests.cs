using FsCopilot.Native;
using FsCopilot.Network;
using FsCopilot.Ownership;

internal static class SharedStartupTests
{
    private sealed class RetainedNative(INativeSessionClient inner) : INativeSessionClient
    {
        private NativeBridgeState _retained = inner.State.Copy();
        public NativeBridgeState State => _retained.Copy();
        public event Action<NativeBridgeState>? StateChanged;
        public event Action<ulong>? SessionCommandDropped { add => inner.SessionCommandDropped += value; remove => inner.SessionCommandDropped -= value; }
        public bool TrySendSession(NativeSessionPacket command) => inner.TrySendSession(command);
        public void Replay() => StateChanged?.Invoke(State);
        public void Reconnect() {
            _retained = _retained with { Backend = Guid.NewGuid().ToString("N"),
                Snapshot = _retained.Snapshot! with { Serial = _retained.Snapshot!.Serial + 1 },
                Session = _retained.Session! with { Serial = _retained.Session!.Serial + 1 } };
            Replay();
        }
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static async Task Wait(Func<bool> condition, string reason, Func<string>? details = null) {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
        Check(condition(), "Shared startup timeout: " + reason + "; " + details?.Invoke());
    }
    public static void Options()
    {
        Check(SharedBackendOptions.Parse([]) is null, "Ordinary app launch opted into shared backend");
        string[] valid = ["--efb-backend", "--relay-host=127.0.0.1", "--relay-port=5010", "--local=pilot001", "--peer=copil001", "--role=host", "--seat=copilot", "--efb-dry-run"];
        var parsed = SharedBackendOptions.Parse(valid)!;
        Check(parsed.Session.Role == SessionRole.PhysicsHost && parsed.Session.Seat == CockpitSeat.CopilotRight && parsed.DryRun, "Physics role became coupled to pilot seat");
        foreach (var malformed in new[] {
            valid.Where(arg => !arg.StartsWith("--relay-host=", StringComparison.Ordinal)).ToArray(),
            valid.Concat(new[] { "--relay-port=5000" }).ToArray(),
            valid.Select(arg => arg == "--relay-port=5010" ? "--relay-port=0" : arg).ToArray(),
            valid.Select(arg => arg == "--peer=copil001" ? "--peer=pilot001" : arg).ToArray(),
            valid.Select(arg => arg == "--role=host" ? "--role=pilot" : arg).ToArray(),
            valid.Concat(new[] { "--unexpected=yes" }).ToArray() }) {
            try { SharedBackendOptions.Parse(malformed); throw new InvalidOperationException("Malformed backend options accepted"); }
            catch (ArgumentException) { }
        }
        Console.WriteLine("PASS actual backend option parser requires explicit relay/identities and keeps physics role independent of seat");
        var frame = new NativeWorldFrame(Guid.NewGuid(), 1, 1, [1, 1, 1, 1], 0x7e07,
            [0.8, 0.15, 4321, 0.1, -0.1, 2.5, 5, 3, 160, 0.1, 0.2, 0.3], Enumerable.Repeat(0.5, 21).ToArray());
        var codecs = new Codecs().Add<SharedWorldPacket, SharedWorldPacket.Codec>();
        var bytes = codecs.Encode(new SharedWorldPacket(frame));
        Check(bytes.Length == 334 && codecs.Decode(bytes, 0, bytes.Length) is SharedWorldPacket decoded && decoded.Frame.World[2] == 4321,
            "World codec did not preserve its bounded binary payload");
        Check(codecs.Decode(bytes, 0, bytes.Length - 1) is null && codecs.Decode(bytes.Concat(new byte[] { 0 }).ToArray(), 0, bytes.Length + 1) is null,
            "World decoder accepted truncated/trailing bytes");
        var badWorld = bytes.ToArray(); badWorld[1] = 2; Check(codecs.Decode(badWorld, 0, badWorld.Length) is null, "Unknown world version accepted");
        badWorld = bytes.ToArray(); BitConverter.GetBytes(double.NaN).CopyTo(badWorld, 70);
        Check(codecs.Decode(badWorld, 0, badWorld.Length) is null, "Nonfinite world position accepted");
        Console.WriteLine("PASS bounded world codec preserves physical units and rejects malformed/truncated/trailing packets");
    }
    public static async Task Run(int port)
    {
        using var hostNative = new CppNativeSessionClient(sharedCapabilities: true, armed: false);
        using var peerNative = new CppNativeSessionClient(sharedCapabilities: true, armed: false);
        using var a = new RelayNetwork("127.0.0.1", "pilot001", "Startup host", false, port);
        using var b = new RelayNetwork("127.0.0.1", "copil001", "Startup peer", false, port);
        await using var host = new NativeSharedSessionService(a, hostNative, new("pilot001", "copil001", SessionRole.PhysicsHost, CockpitSeat.CopilotRight));
        await using var peer = new NativeSharedSessionService(b, peerNative, new("copil001", "pilot001", SessionRole.Copilot, CockpitSeat.PilotLeft));
        SharedServiceProgress? hp = null, pp = null; NegotiatedControlSession? hs = null, ps = null;
        using var subscriptions = new CompositeDisposable();
        subscriptions.Add(host.Progress.Subscribe(value => hp = value)); subscriptions.Add(peer.Progress.Subscribe(value => pp = value));
        subscriptions.Add(host.Sessions.Subscribe(value => hs = value)); subscriptions.Add(peer.Sessions.Subscribe(value => ps = value));
        string Details() => $"host={hp}; peer={pp}; host native={hostNative.State.Session?.Phase}/{hostNative.State.Session?.Code}; peer native={peerNative.State.Session?.Phase}/{peerNative.State.Session?.Code}";
        await Task.Delay(200);
        Check(hp?.Phase == SharedServicePhase.WaitingForJoin && pp?.Phase == SharedServicePhase.WaitingForJoin &&
            hostNative.State.Session?.Blocked == 0 && peerNative.State.Session?.Blocked == 0, "Startup changed controls without local EFB consent");
        hostNative.Join(); peerNative.Join();
        await Wait(() => hp?.Phase == SharedServicePhase.Active && pp?.Phase == SharedServicePhase.Active && hs?.Authority is not null && ps?.AuthorityReplica is not null,
            "automatic connection, negotiation and native binding", Details);
        var authority = hs!.Authority!;
        await Wait(() => authority.Snapshot.Groups.All(group => group.Owner == "pilot001" && group.Attached), "initial host pickup", Details);
        Check(hs.Seats.PilotPeer == "copil001" && hs.Seats.CopilotPeer == "pilot001", "Startup replaced selected seats with physics roles");
        var first = authority.Snapshot.Session;
        Console.WriteLine("PASS fresh EFB joins start actual relay negotiation and both native coordinators without manual bridge construction");

        peerNative.Efb(ControlSelection.Throttle, NativeSessionIntent.Me);
        await Wait(() => authority.Snapshot.Groups.Single(group => group.Group == AtomicControlGroup.Throttle) is { Owner: "copil001", Attached: true }, "EFB request after service startup", Details);
        peerNative.Hardware((1u << 9) | (1u << 12), 0.7);
        await Wait(() => Math.Abs(hostNative.State.Snapshot!.Targets[9] - 0.7) < 0.006, "remote input after real startup", Details);
        peerNative.Leave();
        await Wait(() => hp?.Phase == SharedServicePhase.Ended && pp?.Phase == SharedServicePhase.Ended, "explicit EFB leave", Details);
        Check((hostNative.State.Session!.Blocked & 2) != 0, "Leave automatically restored host throttle hardware");
        await Task.Delay(250);
        Check(hp?.Phase == SharedServicePhase.Ended && pp?.Phase == SharedServicePhase.Ended && hs is null && ps is null,
            "Retained consent or relay reconnection restarted shared controls");
        Console.WriteLine("PASS EFB Leave ends both backends, retains hardware suppression and cannot reuse consumed consent");

        await Wait(() => hostNative.State.Session?.Phase == NativeSessionPhase.Ended && peerNative.State.Session?.Phase == NativeSessionPhase.Ended &&
            !hostNative.State.Snapshot!.Flags.HasFlag(NativeStatusFlags.Applying) && !peerNative.State.Snapshot!.Flags.HasFlag(NativeStatusFlags.Applying),
            "native teardown before fresh local joins", Details);
        Check(peerNative.State.Session!.Flags.HasFlag(NativeSessionFlags.WorldFrozen), "Guest silently resumed its own physics after Leave");
        peerNative.Recover(2);
        await Wait(() => peerNative.State.Session?.Phase == NativeSessionPhase.Idle &&
            !peerNative.State.Session.Flags.HasFlag(NativeSessionFlags.WorldFrozen), "scoped explicit guest physics recovery", Details);
        Check((peerNative.State.Session!.Blocked & 13) == 13 && (peerNative.State.Session.Blocked & 2) == 0,
            "Guest recovery released a different hardware group");
        Console.WriteLine("PASS scoped guest Force restores its own physics and only the selected hardware group");
        hostNative.Hardware((1u << 9) | (1u << 12), 0.7);
        hostNative.Join(); peerNative.Join();
        await Wait(() => hp?.Phase == SharedServicePhase.Active && pp?.Phase == SharedServicePhase.Active && hs?.Authority is not null && hs.Authority.Snapshot.Session != first,
            "fresh consent and new negotiated session", Details);
        authority = hs!.Authority!;
        await Wait(() => authority.Snapshot.Groups.All(group => group.Owner == "pilot001" && group.Attached), "fresh session attachment", Details);
        b.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(new AuthorityInput(AtomicProtocol.Version,
            new(first, AtomicControlGroup.Throttle, 1, 999, [new(ControlAxis.Throttle1, 0.95), new(ControlAxis.Throttle2, 0.95)])))));
        await Task.Delay(200);
        Check(Math.Abs(hostNative.State.Snapshot!.Targets[9] - 0.7) < 0.006, "Previous session's input reached the restarted native writer");
        Console.WriteLine("PASS explicit rejoin creates a new session and rejects prior-session input over the real relay");

        using var unsupportedNative = new CppNativeSessionClient();
        using var unsupportedNetwork = new RelayNetwork("127.0.0.1", "nooutput", "Observe only", false, port);
        await using var unsupported = new NativeSharedSessionService(unsupportedNetwork, unsupportedNative,
            new("nooutput", "nonepeer", SessionRole.PhysicsHost, CockpitSeat.PilotLeft));
        SharedServiceProgress? missing = null; using var missingSubscription = unsupported.Progress.Subscribe(value => missing = value);
        await Wait(() => missing?.Phase == SharedServicePhase.WaitingForNative, "installed-style observation-only capability gate");
        Check(unsupportedNative.State.Session?.Phase == NativeSessionPhase.Armed && unsupportedNative.State.Session.Blocked == 0,
            "Observation-only package was bound or hardware masked");
        Console.WriteLine("PASS observation-only native capabilities cannot start shared ownership or change hardware masks");

        using var retainedNative = new CppNativeSessionClient(sharedCapabilities: true);
        var retained = new RetainedNative(retainedNative);
        using var waitingNetwork = new RelayNetwork("127.0.0.1", "waitjoin", "Waiting", false, port);
        await using var waiting = new NativeSharedSessionService(waitingNetwork, retained,
            new("waitjoin", "nopilot1", SessionRole.PhysicsHost, CockpitSeat.PilotLeft));
        SharedServiceProgress? pending = null; using var pendingSubscription = waiting.Progress.Subscribe(value => pending = value);
        await Wait(() => pending?.Phase == SharedServicePhase.Negotiating, "fresh armed waiting session");
        for (var index = 0; index < 6; ++index) { retained.Replay(); await Task.Delay(250); }
        Check(pending?.Phase == SharedServicePhase.Ended && retainedNative.State.Session?.Blocked == 0,
            "Repeated copies of retained native packets kept a negotiation fresh or changed hardware masks");
        Console.WriteLine("PASS repeated retained native snapshots cannot keep a pending join alive or silently restart it");
        retained.Reconnect(); await Task.Delay(200);
        Check(pending?.Phase == SharedServicePhase.Ended && retainedNative.State.Session?.Blocked == 0,
            "A new backend GUID reused the previous attempt's local consent");
        Console.WriteLine("PASS SDK reconnect with a new backend GUID cannot reuse consumed local EFB consent");
    }
}
