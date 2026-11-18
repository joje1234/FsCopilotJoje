using FsCopilot.Native;
using FsCopilot.Network;

internal static class EfbConnectionTests
{
    private sealed class InterruptedNative(INativeBackendClient inner) : INativeBackendClient, IDisposable
    {
        private volatile bool _muted;
        private NativeBridgeState? _delayed;
        private bool _subscribed;
        public NativeBridgeState State => _muted ? new(false, false, null, "Simulated SDK connection loss.") : Volatile.Read(ref _delayed) ?? inner.State;
        public event Action<NativeBridgeState>? StateChanged;
        public event Action<ulong>? SessionCommandDropped { add => inner.SessionCommandDropped += value; remove => inner.SessionCommandDropped -= value; }
        public bool TrySendSession(NativeSessionPacket command) => inner.TrySendSession(command);
        public bool Report(NativeBackendNotice notice) => !_muted && inner.Report(notice);
        private void Observe(NativeBridgeState _) { if (!_muted) StateChanged?.Invoke(State); }
        public void ConnectEvents() { inner.StateChanged += Observe; _subscribed = true; }
        public void Mute(bool value) { _muted = value; StateChanged?.Invoke(State); }
        public void Delay(bool value) { Volatile.Write(ref _delayed, value ? inner.State with {Fresh=false} : null); StateChanged?.Invoke(State); }
        public void Dispose() { if (_subscribed) inner.StateChanged -= Observe; }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> condition, string reason, Func<string>? detail = null) {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
        Check(condition(), "EFB connection timeout: " + reason + "; " + detail?.Invoke());
    }
    public static async Task Run(int port)
    {
        Check(EfbCompanionOptions.Parse([]) is null, "Ordinary desktop launch selected the companion");
        var options = EfbCompanionOptions.Parse(["--efb-backend", "--name=Tablet pilot", "--run-seconds=2", "--efb-dry-run"]);
        Check(options is { Name: "Tablet pilot", RunSeconds: 2, DryRun: true }, "EFB helper required command-line connection details");
        foreach (var args in new[] { new[] { "--efb-backend", "--unknown=x" }, new[] { "--efb-backend", "--run-seconds=0" },
            new[] { "--efb-backend", "--name=x", "--name=y" }, new[] { "--efb-backend", "--efb-backend" } }) {
            try { EfbCompanionOptions.Parse(args); throw new InvalidOperationException("Invalid helper options accepted"); }
            catch (ArgumentException) { }
        }
        Check(EfbCompanionOptions.Parse(["--efb-backend", "--relay-host=127.0.0.1"]) is null, "Explicit diagnostic options were reinterpreted");
        Console.WriteLine("PASS EFB helper accepts tablet configuration without command-line identities and rejects malformed options");

        using var hostNative = new CppNativeSessionClient(sharedCapabilities: true, armed: false, definitionOutput: true, canonicalControls: true, relativeControls: true);
        using var peerNative = new CppNativeSessionClient(sharedCapabilities: true, armed: false, definitionOutput: true, canonicalControls: true);
        using var interruptedHost = new InterruptedNative(hostNative); interruptedHost.ConnectEvents();
        var hostCreated = 0; var peerCreated = 0; var profileAccepted = 0;
        await using var host = new EfbConnectionBackend(interruptedHost, value => {
            Interlocked.Increment(ref hostCreated); return new RelayNetwork(value.Relay, value.Local, "Tablet host", false, (int)value.Port);
        }, _ => Volatile.Read(ref profileAccepted) == 0 ? "Aircraft definition mismatch (test fixture)." : null);
        await using var peer = new EfbConnectionBackend(peerNative, value => {
            Interlocked.Increment(ref peerCreated); return new RelayNetwork(value.Relay, value.Local, "Tablet peer", false, (int)value.Port);
        });
        EfbBackendProgress? hp = null, pp = null;
        using var hs = host.Progress.Subscribe(value => hp = value);
        using var ps = peer.Progress.Subscribe(value => pp = value);
        string Detail() => $"host={hp}; peer={pp}; native={hostNative.State.Session?.Code}/{peerNative.State.Session?.Code}";
        await Wait(() => hp?.Notice.Code == NativeBackendCode.UnsupportedPackage, "mismatched definitions block network creation", Detail);
        Check(hostCreated == 0, "Mismatched definitions opened a network.");
        Interlocked.Exchange(ref profileAccepted, 1);
        await Wait(() => hostNative.State.Snapshot!.BackendPhase == NativeBackendPhase.WaitingForSetup &&
            peerNative.State.Snapshot!.BackendPhase == NativeBackendPhase.WaitingForSetup, "helper presence reaches the actual native interface", Detail);
        Check(hostCreated == 0 && peerCreated == 0, "Helper opened a network before configuration");
        var hostSettings = new NativeConnectionSettings(1, "127.0.0.1", (uint)port, "tableth1", "tabletp1", 1, 2);
        var peerSettings = new NativeConnectionSettings(1, "127.0.0.1", (uint)port, "tabletp1", "tableth1", 2, 1);
        hostNative.Configure(hostSettings); peerNative.Configure(peerSettings);
        await Wait(() => hp?.Notice.Phase == NativeBackendPhase.WaitingForJoin && pp?.Notice.Phase == NativeBackendPhase.WaitingForJoin &&
            hostNative.State.Snapshot!.BackendPhase == NativeBackendPhase.WaitingForJoin && peerNative.State.Snapshot!.BackendPhase == NativeBackendPhase.WaitingForJoin,
            "saved native preferences start both configured helpers", Detail);
        Check(hostCreated == 1 && peerCreated == 1 && hostNative.State.Session!.Blocked == 0 && peerNative.State.Session!.Blocked == 0 &&
            hp!.Settings?.Seat == 2 && pp!.Settings?.Seat == 1, "Saving connection granted controls or coupled the physics host to the left seat");
        Check(hostNative.State.Session!.Phase != NativeSessionPhase.Armed && peerNative.State.Session!.Phase != NativeSessionPhase.Armed, "Saving armed retained consent");
        Console.WriteLine("PASS actual EFB command configures both native/helper/relay paths while preserving masks and separate physics/seat choices");
        hostNative.Join(); peerNative.Join();
        await Wait(() => hp?.Notice.Phase == NativeBackendPhase.Active && pp?.Notice.Phase == NativeBackendPhase.Active &&
            hostNative.State.Session!.Attached == 15 && hostNative.State.Session.Blocked == 0 && peerNative.State.Session!.Attached == 15,
            "fresh EFB Join establishes the configured shared session", Detail);
        var retainedSession = hostNative.State.Session!.Session;
        hostNative.ResetSetup();peerNative.ResetSetup();await Task.Delay(250);
        Check(hostNative.State.Session!.Session==retainedSession && peerNative.State.Session!.Session==retainedSession &&
            hostNative.State.Session.Phase==NativeSessionPhase.Active && peerNative.State.Session.Phase==NativeSessionPhase.Active &&
            hostNative.State.Snapshot!.Learned==hostNative.State.Snapshot.Required && peerNative.State.Snapshot!.Learned==peerNative.State.Snapshot.Required,
            "Setup reset during active sharing changed calibration or the shared session");
        Console.WriteLine("PASS native setup-reset requests are refused during active sharing on both PCs without changing learning, masks or session identity");
        interruptedHost.Delay(true);
        await Wait(() => hp?.Notice is {Phase:NativeBackendPhase.Active,Code:NativeBackendCode.NativeUnavailable}, "short native stall reports a retained session", Detail);
        await Task.Delay(1200);
        Check(hostNative.State.Session!.Phase==NativeSessionPhase.Active && peerNative.State.Session!.Phase==NativeSessionPhase.Active &&
            peerNative.State.Session.Blocked==15 && peerNative.State.Session.Flags.HasFlag(NativeSessionFlags.WorldFrozen), "Short native stall ended the session or released guest controls/physics");
        interruptedHost.Delay(false);
        await Wait(() => hp?.Notice is {Phase:NativeBackendPhase.Active,Code:NativeBackendCode.None} && pp?.Notice.Phase==NativeBackendPhase.Active,
            "fresh native data recovers the same session automatically", Detail);
        Check(hostNative.State.Session!.Session==retainedSession && peerNative.State.Session!.Session==retainedSession,
            "A temporary stall started a replacement session");
        Console.WriteLine("PASS retained local SDK stall: warning without disconnect, unchanged masks/frozen guest, same-identity automatic recovery before new control data");
        Check(hostNative.State.Snapshot!.Required==0x7e3f && peerNative.State.Snapshot!.Required==0x7e3f,
            "Steering schema must contain fixed yoke/pedals/tiller/brakes and exclude ordinary shared trims");
        Console.WriteLine("PASS fixed Steering membership excludes trims; trims use the separately tested ordinary definition channel");
        const uint throttle = (1u << 9) | (1u << 12);
        await Wait(() => hostNative.State.Session!.Automatic && peerNative.State.Session!.Automatic, "host default Auto is applied after native binding", Detail);
        peerNative.Efb(FsCopilot.Ownership.ControlSelection.Quadrant, NativeSessionIntent.AutoOn);
        await Wait(() => hostNative.State.Session!.Automatic && peerNative.State.Session!.Automatic, "configured Auto policy", Detail);
        await Task.Delay(150); peerNative.Hardware(throttle, 0.55);
        await Wait(() => peerNative.State.Session!.Owner[1] == NativeOwner.Local && (peerNative.State.Session.Attached & 2) != 0,
            "configured peer automatic throttle pickup", Detail);
        peerNative.Hardware(throttle, 0.7);
        await Wait(() => Math.Abs(hostNative.State.Snapshot!.Targets[9] - 0.7) < 0.006, "configured peer input reaches actual host output", Detail);
        Check(hostNative.State.Session!.Owner[2] == NativeOwner.Local && hostNative.State.Session.Owner[3] == NativeOwner.Local, "Configured throttle pickup changed another pair");
        foreach (var pair in new[] {(Index: 2, First: 10, Second: 13), (Index: 3, First: 11, Second: 14)}) {
            var axes = (1u << pair.First) | (1u << pair.Second);
            peerNative.Hardware(axes, .55);
            await Wait(() => peerNative.State.Session!.Owner[pair.Index] == NativeOwner.Local &&
                (peerNative.State.Session.Attached & (1u << pair.Index)) != 0, "definition-driven pair pickup", Detail);
            peerNative.Hardware(axes, .7);
            await Wait(() => Math.Abs(hostNative.State.Snapshot!.Targets[pair.First] - .7) < .006 &&
                Math.Abs(hostNative.State.Snapshot.Targets[pair.Second] - .7) < .006, "definition setter updates both host cockpit levers", Detail);
            hostNative.Hardware(axes, .65);
            await Wait(() => hostNative.State.Session!.Owner[pair.Index] == NativeOwner.Local &&
                (hostNative.State.Session.Attached & (1u << pair.Index)) != 0, "definition pair returns to host through alignment", Detail);
        }
        Console.WriteLine("PASS shared physical groups use vanilla definition setters: SimVar throttle, absolute-key RPM and typed-LVar mixture; reciprocal ownership fences remain enforced (simulated SDK)");
        Check(hostNative.State.Session!.Automatic && peerNative.State.Session!.Automatic, "Default Auto policy was not negotiated");
        const uint pedals = 0x34, brakes = 0x30, rudder = 0x4;
        Check(hostNative.State.Snapshot!.Required == 0x7e3f && peerNative.State.Snapshot!.Required == 0x7e3f, "Mixed hardware changed the fixed steering schema");
        peerNative.Hardware(pedals, .9); await Task.Delay(180);
        Check(Math.Abs(hostNative.State.Snapshot.Targets[4]) < .006 && Math.Abs(hostNative.State.Snapshot.Targets[2] - .5) < .006, "Non-owner pedal axes reached the host");
        hostNative.Hardware(brakes, .65);
        await Wait(() => Math.Abs(peerNative.State.Snapshot!.Targets[4] - .65) < .006 && Math.Abs(peerNative.State.Snapshot.Targets[5] - .65) < .006,
            "one button's combined brake demand reaches both guest wheels", Detail);
        hostNative.Hardware(1u << 4, .2);
        await Wait(() => Math.Abs(peerNative.State.Snapshot!.Targets[4] - .2) < .006 && Math.Abs(peerNative.State.Snapshot.Targets[5] - .65) < .006,
            "independent button brake demand preserves the other wheel", Detail);
        hostNative.Hardware(brakes, 0); hostNative.Hardware(rudder, .5);
        // The axis user's pedals stay at .9: only the yoke must match.
        peerNative.Efb(FsCopilot.Ownership.ControlSelection.Steering, NativeSessionIntent.Me);
        await Wait(() => peerNative.State.Session!.Owner[0] == NativeOwner.Local && (peerNative.State.Session.Attached & 1) != 0,
            "axis user takes Steering with matching yoke and mismatched pedals, without learning", Detail);
        await Wait(() => Math.Abs(peerNative.State.Snapshot!.Targets[4] - .9) < .006 && Math.Abs(peerNative.State.Snapshot.Targets[2] - .9) < .006,
            "new owner supplies its pedal demand after yoke-only attachment", Detail);
        hostNative.Hardware(brakes, 1); hostNative.Hardware(rudder, .1); await Task.Delay(180);
        Check(Math.Abs(peerNative.State.Snapshot!.Targets[4] - .9) < .006 && Math.Abs(peerNative.State.Snapshot.Targets[2] - .9) < .006, "Non-owner relative/button input reached the axis owner");
        peerNative.Hardware(brakes, .8); peerNative.Hardware(rudder, .25);
        await Wait(() => Math.Abs(hostNative.State.Snapshot!.Targets[4] - .8) < .006 && Math.Abs(hostNative.State.Snapshot.Targets[2] - .25) < .006,
            "absolute pedals update the relative/button user's cockpit through definitions", Detail);
        hostNative.Hardware(brakes, 0); hostNative.Hardware(rudder, .5);
        hostNative.Hardware(3, .9);
        hostNative.Efb(FsCopilot.Ownership.ControlSelection.Steering, NativeSessionIntent.Me); await Task.Delay(500);
        Check(peerNative.State.Session!.Owner[0] == NativeOwner.Local, "Steering takeover bypassed mismatched yoke alignment");
        hostNative.Hardware(3, .5); // Pedals remain mismatched on both PCs.
        await Wait(() => hostNative.State.Session!.Owner[0] == NativeOwner.Local && (hostNative.State.Session.Attached & 1) != 0,
            "relative/button user regains Steering when only the yoke matches, without learning", Detail);
        Check(hostNative.State.Snapshot.Required == 0x7e3f && peerNative.State.Snapshot.Required == 0x7e3f, "Takeover changed the shared control layout");
        Console.WriteLine("PASS mixed relative/button vs curved-axis rudder/brakes across actual relay and both production C++ sessions: combined/individual brake demand, suppression both ways, fixed schema, yoke mismatch waits and yoke-only pickup with mismatched pedals. SDK simulated.");
        hostNative.Configure(hostSettings with { Peer = "other003" }); await Task.Delay(150);
        Check(hostCreated == 1 && hostNative.State.Snapshot!.Connection == hostSettings, "An active connection edit replaced its writer");
        Console.WriteLine("PASS tablet-selected session performs native Auto handover/output and rejects connection edits during shared control");
        interruptedHost.Mute(true);
        await Wait(() => hp?.Notice.Code == NativeBackendCode.NativeUnavailable && peerNative.State.Session!.Phase == NativeSessionPhase.Ended,
            "SDK loss ends shared controls without replacing saved connection", Detail);
        await Task.Delay(200); interruptedHost.Mute(false);
        await Wait(() => hp?.Notice.Phase == NativeBackendPhase.Ended && hostNative.State.Snapshot!.BackendPhase == NativeBackendPhase.Ended,
            "fresh native observations restore the ended-session UI instead of leaving stale connection errors", Detail);
        Check(hostCreated == 1 && peerCreated == 1 && hostNative.State.Session!.Phase == NativeSessionPhase.Ended, "SDK reconnection silently constructed or joined a new session");
        Console.WriteLine("PASS SDK reconnection restores current connection feedback while retaining ended authority and requiring fresh local join");
        peerNative.Leave();
        await Wait(() => hp?.Notice.Phase == NativeBackendPhase.Ended && pp?.Notice.Phase == NativeBackendPhase.Ended, "configured EFB Leave", Detail);
        peerNative.Configure(peerSettings with { Peer = "other003" }); await Task.Delay(150);
        Check(peerCreated == 1 && peerNative.State.Snapshot!.Connection == peerSettings && peerNative.State.Session!.Flags.HasFlag(NativeSessionFlags.WorldFrozen),
            "Connection editing bypassed held guest physics or hardware recovery");
        hostNative.Recover(1); peerNative.Recover(1);
        await Wait(() => !peerNative.State.Session!.Flags.HasFlag(NativeSessionFlags.WorldFrozen), "configured guest local recovery", Detail);
        hostNative.Configure(hostSettings with { Peer = "other003" });
        await Wait(() => hostCreated == 2 && hp?.Settings?.Revision == 2 && hp.Settings.Peer == "other003", "explicit settings revision replaces the stopped service", Detail);
        Check(hostNative.State.Session!.Phase != NativeSessionPhase.Armed, "Reconfiguration silently rejoined");
        Console.WriteLine("PASS Leave preserves guest freeze/masks; explicit local recovery permits a drained connection replacement without automatic rejoin");
    }
}
