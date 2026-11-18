using FsCopilot.Native;
using FsCopilot.Network;
using FsCopilot.Ownership;

internal static class NativeAltimeterNetworkTests
{
    private sealed class DelayedInstrumentState(INetwork inner) : INetwork {
        private int _hold;
        private ControlAuthorityPacket? _latest;
        public void Hold() => Volatile.Write(ref _hold, 1);
        public void Release() { Volatile.Write(ref _hold, 0); if (Interlocked.Exchange(ref _latest, null) is { } latest) inner.SendAll(latest); }
        public IObservable<ICollection<Peer>> Peers => inner.Peers;
        public Task<ConnectionResult> Connect(string target, CancellationToken ct) => inner.Connect(target, ct);
        public void Disconnect() => inner.Disconnect();
        public void RegisterPacket<TPacket, TCodec>() where TPacket : notnull where TCodec : IPacketCodec<TPacket>, new() => inner.RegisterPacket<TPacket, TCodec>();
        public IObservable<TPacket> Stream<TPacket>() => inner.Stream<TPacket>();
        public IObservable<ReceivedPacket<TPacket>> StreamWithSource<TPacket>() => inner.StreamWithSource<TPacket>();
        public void SendAll<TPacket>(TPacket packet, bool unreliable = false) where TPacket : notnull {
            if (Volatile.Read(ref _hold) != 0 && packet is ControlAuthorityPacket control && AuthorityWire.TryDecode(control.Payload, out var message) &&
                message is AuthorityAltimeterState { State.Epoch: 2, State.Phase: AltimeterPhase.Active }) {
                Volatile.Write(ref _latest, control); return;
            }
            inner.SendAll(packet, unreliable);
        }
    }
    private sealed class InitialEmptyPeers(INetwork inner) : INetwork {
        public IObservable<ICollection<Peer>> Peers => Observable.Return<ICollection<Peer>>(Array.Empty<Peer>()).Concat(inner.Peers);
        public Task<ConnectionResult> Connect(string target, CancellationToken ct) => inner.Connect(target, ct);
        public void Disconnect() => inner.Disconnect();
        public void RegisterPacket<TPacket, TCodec>() where TPacket : notnull where TCodec : IPacketCodec<TPacket>, new() => inner.RegisterPacket<TPacket, TCodec>();
        public IObservable<TPacket> Stream<TPacket>() => inner.Stream<TPacket>();
        public IObservable<ReceivedPacket<TPacket>> StreamWithSource<TPacket>() => inner.StreamWithSource<TPacket>();
        public void SendAll<TPacket>(TPacket packet, bool unreliable = false) where TPacket : notnull => inner.SendAll(packet, unreliable);
    }
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static async Task Wait(Func<bool> ready, string reason, Func<string> details) {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(20);
        Check(ready(), "Native instrument timeout: " + reason + "; " + details());
    }
    public static async Task Run(int port, bool peerSupported = true, bool hostIsCopilot = false)
    {
        // Only these test processes install a simulated, verified input provider.
        // Production supplies no provider and remains unable to start syncing.
        using var hostNative = new CppNativeSessionClient(sharedCapabilities: true, verifiedAltimeters: true);
        using var peerNative = new CppNativeSessionClient(sharedCapabilities: true, verifiedAltimeters: peerSupported);
        var hostId = hostIsCopilot ? "baro-right-host" : peerSupported ? "instrument-host" : "baro-none-host";
        var peerId = hostIsCopilot ? "baro-right-peer" : peerSupported ? "instrument-peer" : "baro-none-peer";
        using var a = new RelayNetwork("127.0.0.1", hostId, "Instrument host", false, port);
        using var b = new RelayNetwork("127.0.0.1", peerId, "Instrument peer", false, port);
        var delayed = new DelayedInstrumentState(a);
        using var subscriptions = new CompositeDisposable(); var registrations = 0;
        foreach (var network in new[] { a, b }) {
            OwnershipNetworkNegotiation.RegisterProtocol(network);
            subscriptions.Add(network.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref registrations)));
        }
        var clock = Stopwatch.StartNew();
        var authority = new ControlAuthoritySession(hostId, peerId, 2, () => clock.Elapsed, steeringAxes: 7,
            hostSeat: hostIsCopilot ? CockpitSeat.CopilotRight : CockpitSeat.PilotLeft);
        var initial = authority.Snapshot;
        var hostSession = new NegotiatedControlSession(SessionRole.PhysicsHost, initial.Seats, null, null, authority);
        var peerSession = new NegotiatedControlSession(SessionRole.Copilot, initial.Seats, null, null, AuthorityReplica: new(initial.PhysicsHost, initial));
        string RegistrationDetail() => "registrations=" + registrations;
        await Wait(() => registrations == 2, "clients registered", RegistrationDetail);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Check(await a.Connect(peerId, timeout.Token) == ConnectionResult.Success, "Instrument clients did not connect");
        await using var peer = new NativeControlAuthorityBridge(new InitialEmptyPeers(b), peerNative, initial.Peer, peerSession);
        await using var host = new NativeControlAuthorityBridge(delayed, hostNative, initial.PhysicsHost, hostSession);
        NativeAuthorityProgress? hostProgress = null, peerProgress = null;
        using var hp = host.Progress.Subscribe(value => hostProgress = value);
        using var pp = peer.Progress.Subscribe(value => peerProgress = value);
        string Detail() => $"model={authority.Altimeters.Phase}/{authority.Altimeters.Revision} " +
            $"host={host.Instruments.Phase}/{host.InstrumentError}/{hostProgress} peer={peer.Instruments.Phase}/{peer.InstrumentError}/{peerProgress} " +
            $"hNative={hostNative.State.Session?.Phase}/{hostNative.State.Session?.Revision}/{hostNative.State.Session?.SeatRevision}/" +
            $"{hostNative.State.Session?.BarometerGateAck}/{hostNative.State.Session?.BarometerValueAck}/{hostNative.State.Snapshot?.Altimeters} " +
            $"pNative={peerNative.State.Session?.Phase}/{peerNative.State.Session?.Revision}/{peerNative.State.Session?.SeatRevision}/" +
            $"{peerNative.State.Session?.BarometerGateAck}/{peerNative.State.Session?.BarometerValueAck}/{peerNative.State.Snapshot?.Altimeters}";
        await Wait(() => authority.Snapshot.Groups.All(value => value.Attached) &&
            hostNative.State.Session?.SeatRevision == 1 && peerNative.State.Session?.SeatRevision == 1, "physical session active", Detail);
        if (!peerSupported) {
            await Task.Delay(1200);
            Check(authority.Altimeters.Phase == AltimeterPhase.Disabled && hostNative.State.Snapshot!.Altimeters.Blocked == 0 &&
                hostNative.State.Session!.BarometerGateAck == 0 && peerNative.State.Session!.BarometerGateAck == 0,
                "A missing peer adapter caused instrument masking");
            Console.WriteLine("PASS real native backend refuses instrument startup before masking when either peer lacks a verified adapter"); return;
        }
        await Wait(() => authority.Altimeters.Phase == AltimeterPhase.Active && peer.Instruments.Phase == AltimeterPhase.Active,
            "automatic masked seed and paired gate proofs", Detail);
        if (hostIsCopilot) {
            Check(hostNative.State.Snapshot!.Altimeters.Blocked == 1 && peerNative.State.Snapshot!.Altimeters.Blocked == 2,
                "Physics host in the right seat received pilot input masks");
            var writes = hostNative.Transmissions;
            hostNative.Altimeters(999.125, true, 1014.25, true);
            await Wait(() => authority.Altimeters.Instruments[1].Confirmed == new AltimeterSetting(16228, true) &&
                peerNative.State.Snapshot!.Altimeters.Pressure2 == 16228, "right-seat physics host owns and observes only the right knob", Detail);
            Check(hostNative.Transmissions == writes, "Right-seat host's own input was written back");
            peerNative.Altimeters(1000.5, false, 1014.25, true);
            await Wait(() => authority.Altimeters.Instruments[0].Confirmed == new AltimeterSetting(16008, false) &&
                hostNative.State.Snapshot!.Altimeters.Pressure1 == 16008, "left-seat peer owns the left knob independently of physics hosting", Detail);
            Console.WriteLine("PASS native instrument authority follows initial pilot/copilot seats when the physics host occupies the right seat"); return;
        }
        Check(hostNative.State.Snapshot!.Altimeters.Blocked == 2 && peerNative.State.Snapshot!.Altimeters.Blocked == 1,
            "Wrong per-seat instrument masks");
        var controls = authority.Snapshot.Groups.ToArray(); var physicsRevision = authority.Snapshot.Revision;
        var hostWrites = hostNative.Transmissions;
        hostNative.Altimeters(1001.5, false, 1012.5, false);
        await Wait(() => authority.Altimeters.Instruments[0].Confirmed == new AltimeterSetting(16024, false) &&
            peerNative.State.Snapshot!.Altimeters.Pressure1 == 16024 && peerNative.State.Snapshot.Altimeters.StandardMask == 0,
            "host owner's local knob observed and mirrored on peer", Detail);
        Check(hostNative.Transmissions == hostWrites, "Host owner's knob was written back instead of observed");
        hostNative.DropNextInstrumentValue();
        hostNative.Altimeters(1002, false, 1012.5, false);
        await Wait(() => authority.Altimeters.Instruments[0].Confirmed == new AltimeterSetting(16032, false) &&
            peerNative.State.Snapshot!.Altimeters.Pressure1 == 16032, "dropped host observation retried inside the final model fence", Detail);
        Check(hostNative.DroppedInstrumentValues == 1 && hostNative.Transmissions == hostWrites, "Queue retry echoed the host's knob or failed to exercise a drop");
        hostNative.Altimeters(1001.5, false, 1012.5, false);
        await Wait(() => authority.Altimeters.Instruments[0].Confirmed == new AltimeterSetting(16024, false) &&
            peerNative.State.Snapshot!.Altimeters.Pressure1 == 16024, "return to prior pressure after retry", Detail);
        peerNative.Altimeters(1001.5, false, 1002.25, true);
        await Wait(() => authority.Altimeters.Instruments[1].Confirmed == new AltimeterSetting(16036, true) &&
            hostNative.State.Snapshot!.Altimeters.Pressure2 == 16036 && hostNative.State.Snapshot.Altimeters.StandardMask == 2,
            "copilot owner's indexed absolute request confirmed by host", Detail);
        Check(authority.Snapshot.Revision == physicsRevision && authority.Snapshot.Groups.SequenceEqual(controls), "Pressure tuning changed physical authority");
        peerNative.Altimeters(1001.5, false, 1011, false);
        peerNative.Altimeters(1001.5, false, 1005.25, true);
        await Wait(() => authority.Altimeters.Instruments[1].Confirmed == new AltimeterSetting(16084, true) &&
            hostNative.State.Snapshot!.Altimeters.Pressure2 == 16084 && peerNative.State.Snapshot!.Altimeters.Pressure2 == 16084,
            "rapid same-instrument inputs coalesce without rewinding its owner", Detail);
        delayed.Hold(); hostNative.EfbSeat("request");
        await Wait(() => peerNative.State.Session?.SeatPending == 2, "seat swap request", Detail);
        peerNative.EfbSeat("accept");
        await Wait(() => authority.Altimeters.Phase == AltimeterPhase.Active && authority.Altimeters.Epoch == 2 &&
            hostNative.State.Session?.Seat == 2 && peerNative.State.Session?.Seat == 1 &&
            hostNative.State.Snapshot!.Altimeters.Blocked == 1 && peerNative.State.Snapshot!.Altimeters.Blocked == 2,
            "accepted seat swap drains pressures and changes native knob masks", Detail);
        Check(authority.Altimeters.Instruments[0].Confirmed == new AltimeterSetting(16024, false) &&
            authority.Altimeters.Instruments[1].Confirmed == new AltimeterSetting(16084, true), "Seat swap changed QNH/STD");
        peerNative.Altimeters(1003.5, false, 1005.25, true);
        await Wait(() => authority.Altimeters.Instruments[0].Confirmed == new AltimeterSetting(16056, false) &&
            hostNative.State.Snapshot!.Altimeters.Pressure1 == 16056, "new pilot owns left instrument after swap", Detail);
        Check(peer.Instruments.Phase == AltimeterPhase.Attaching, "Test did not delay the new owner's final Active snapshot");
        delayed.Release();
        await Wait(() => peer.Instruments.Phase == AltimeterPhase.Active, "delayed final state reaches the new owner without resetting its knob", Detail);
        Check(authority.Snapshot.PhysicsHost == initial.PhysicsHost && authority.Snapshot.Groups.SequenceEqual(controls), "Instrument swap changed physics or controls");
        Check(!hostNative.DuplicateCommands && !peerNative.DuplicateCommands, "Instrument/physical operations reused a native command ID");
        hostNative.InstrumentAdapter(false);
        await Wait(() => authority.Altimeters.Phase == AltimeterPhase.Holding &&
            hostNative.State.Snapshot!.Altimeters.Blocked == 3 && peerNative.State.Snapshot!.Altimeters.Blocked == 3,
            "adapter loss holds instruments on both peers", Detail);
        Check(hostNative.State.Session!.Phase == NativeSessionPhase.Active && peerNative.State.Session!.Phase == NativeSessionPhase.Active &&
            authority.Snapshot.PeerConnected && !authority.Snapshot.Ended && authority.Snapshot.Groups.SequenceEqual(controls),
            "Instrument failure ended physical controls or reassigned a group");
        hostNative.UseLocalInstruments();
        await Wait(() => authority.Altimeters.Phase == AltimeterPhase.Local && hostNative.State.Snapshot!.Altimeters is
            { LocalOnly: true, Held: false, Recovering: false, InputReady: true, Blocked: 0 },
            "EFB instrument-only recovery releases native inputs despite loss of sync profile", Detail);
        Check(!hostNative.State.Snapshot!.Altimeters.Profile && hostNative.State.Session!.Phase == NativeSessionPhase.Active &&
            authority.Snapshot.Groups.SequenceEqual(controls), "Instrument-only recovery changed flight controls or required a valid tuning profile");
        peerNative.UseLocalInstruments();
        await Wait(() => peerNative.State.Snapshot!.Altimeters is { LocalOnly: true, Held: false, Recovering: false, InputReady: true, Blocked: 0 },
            "other pilot releases only their own instrument input masks", Detail);
        var peerPressure = peerNative.State.Snapshot!.Altimeters;
        hostNative.Altimeters(1007.5, false, 1008.25, false); await Task.Delay(250);
        Check(peerNative.State.Snapshot!.Altimeters.Pressure1 == peerPressure.Pressure1 &&
            peerNative.State.Snapshot.Altimeters.Pressure2 == peerPressure.Pressure2 &&
            hostNative.State.Snapshot!.Altimeters.Pressure1 == 16120, "Retired shared instrument authority rewrote a local knob");
        hostNative.EfbSeat("request");
        await Wait(() => peerNative.State.Session?.SeatPending == 2, "seat swap remains available with local instruments", Detail);
        peerNative.EfbSeat("accept");
        await Wait(() => hostNative.State.Session?.Seat == 1 && peerNative.State.Session?.Seat == 2 &&
            authority.Altimeters.Phase == AltimeterPhase.Local, "local instrument policy permits confirmed seat changes without instrument handover", Detail);
        hostNative.InstrumentAdapter(true); hostNative.Leave();
        await Wait(() => hostNative.State.Session?.Phase == NativeSessionPhase.Ended && peerNative.State.Session?.Phase == NativeSessionPhase.Ended,
            "explicit leave ends shared ownership", Detail);
        peerNative.Recover(2); // One local action must wait for both instrument and physics read-back, then release only throttle hardware.
        await Wait(() => peerNative.State.Snapshot!.Altimeters.Blocked == 0 && peerNative.State.Session?.Phase == NativeSessionPhase.Idle &&
            peerNative.State.Snapshot.Blocked == 13 && !peerNative.State.Session.Flags.HasFlag(NativeSessionFlags.WorldFrozen),
            "single scoped local recovery restores instrument input and prior physics before throttle attachment", Detail);
        hostNative.Recover(2);
        await Wait(() => hostNative.State.Snapshot!.Altimeters.Blocked == 0 && hostNative.State.Session?.Phase == NativeSessionPhase.Idle,
            "host local input recovery after adapter restoration", Detail);
        Console.WriteLine("PASS real relay + both production C++ sessions/SDK adapters: masked startup, indexed ownership, no host echo, queue-drop retry, rapid input coalescing, delayed seat activation, adapter-loss isolation and one-click scoped recovery. QNH/STD survive swaps. Input routes/SDK are simulated; no MSFS access.");
    }
}
