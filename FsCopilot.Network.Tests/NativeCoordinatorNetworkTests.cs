using FsCopilot.Native;
using FsCopilot.Network;
using FsCopilot.Ownership;

internal static class NativeCoordinatorNetworkTests
{
    private sealed class HeartbeatFilter(INetwork inner) : INetwork {
        public bool Muted;
        public bool WorldMuted;
        public int Dropped;
        public int AlivePackets;
        public IObservable<ICollection<Peer>> Peers => inner.Peers;
        public Task<ConnectionResult> Connect(string target, CancellationToken ct) => inner.Connect(target, ct);
        public void Disconnect() => inner.Disconnect();
        public void RegisterPacket<TPacket, TCodec>() where TPacket : notnull where TCodec : IPacketCodec<TPacket>, new() => inner.RegisterPacket<TPacket, TCodec>();
        public IObservable<TPacket> Stream<TPacket>() => inner.Stream<TPacket>();
        public IObservable<ReceivedPacket<TPacket>> StreamWithSource<TPacket>() => inner.StreamWithSource<TPacket>();
        public void SendAll<TPacket>(TPacket packet, bool unreliable = false) where TPacket : notnull {
            if (packet is ControlAuthorityPacket alivePacket && AuthorityWire.TryDecode(alivePacket.Payload, out var alive) && alive is AuthorityAlive)
                Interlocked.Increment(ref AlivePackets);
            if (Volatile.Read(ref WorldMuted) && packet is SharedWorldPacket) { Interlocked.Increment(ref Dropped); return; }
            if (Volatile.Read(ref Muted) && packet is ControlAuthorityPacket control && AuthorityWire.TryDecode(control.Payload, out var message) && message is AuthorityAlive) {
                Interlocked.Increment(ref Dropped); return;
            }
            inner.SendAll(packet, unreliable);
        }
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static async Task Wait(Func<bool> condition, string reason, Func<string>? details = null) {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(20);
        Check(condition(), "Native coordinator timeout: " + reason + (details is null ? "" : "; " + details()));
    }
    public static async Task Run(int port) {
        using var hostNative = new CppNativeSessionClient(); using var peerNative = new CppNativeSessionClient();
        using var a = new RelayNetwork("127.0.0.1", "native-host", "Native host", false, port);
        using var b = new RelayNetwork("127.0.0.1", "native-peer", "Native peer", false, port);
        foreach (var network in new[] { a, b }) OwnershipNetworkNegotiation.RegisterProtocol(network);
        var ready = 0; using var registrations = new CompositeDisposable();
        foreach (var network in new[] { a, b }) registrations.Add(network.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref ready)));
        await Wait(() => ready == 2, "fixture relay registration");
        // Fixture metadata negotiates the tested native implementation. This
        // does not advertise remote input in the installed real-MSFS package.
        var configuration = new SessionConfiguration("bksq-baron58p", 2, "bksq-baron58p-cockpit-v1", 2, SessionNegotiation.AdvancedCapabilities, 7);
        using var hostJoin = new OwnershipNetworkNegotiation(a, "native-host", "native-peer", SessionRole.PhysicsHost, configuration);
        using var peerJoin = new OwnershipNetworkNegotiation(b, "native-peer", "native-host", SessionRole.Copilot, configuration);
        NegotiatedControlSession? hostSession = null, peerSession = null;
        using var hs = hostJoin.Sessions.Subscribe(session => hostSession = session);
        using var ps = peerJoin.Sessions.Subscribe(session => peerSession = session);
        using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Check(await a.Connect("native-peer", connect.Token) == ConnectionResult.Success, "Native test peers did not link");
        await Wait(() => hostSession?.Authority is not null && peerSession?.AuthorityReplica is not null, "real serialized negotiation");
        var authority = hostSession!.Authority!;
        await using var peerBridge = new NativeControlAuthorityBridge(b, peerNative, "native-peer", peerSession!);
        await using var hostBridge = new NativeControlAuthorityBridge(a, hostNative, "native-host", hostSession);
        NativeAuthorityProgress? hostProgress = null, peerProgress = null;
        using var hp = hostBridge.Progress.Subscribe(value => hostProgress = value);
        using var pp = peerBridge.Progress.Subscribe(value => peerProgress = value);
        string Detail() => $"host={hostProgress}; peer={peerProgress}; host native={hostNative.State.Session?.Code}/{hostNative.State.Session?.Ack}; peer native={peerNative.State.Session?.Code}/{peerNative.State.Session?.Ack}";
        AtomicGroupState Group(AtomicControlGroup group) => authority.Snapshot.Groups.Single(value => value.Group == group);
        bool Owns(string owner, AtomicControlGroup group) => Group(group).Owner == owner && Group(group).Attached;
        double Target(int axis) => hostNative.State.Snapshot!.Targets[axis];
        bool Near(int axis, double expected) => Math.Abs(Target(axis) - expected) < 0.006;
        // World and cockpit read-backs now cross the real backend/native path.
        {
            await Wait(() => authority.Snapshot.Groups.All(group => group.Owner == "native-host" && group.Attached), "initial native suppression/aligned host attachment", Detail);
            Check(hostNative.State.Session!.Blocked == 0 && peerNative.State.Session!.Blocked == 15, "Initial host/peer masks are wrong");
            await Wait(() => hostNative.State.Session!.SeatRevision == 1 && peerNative.State.Session!.SeatRevision == 1, "initial native seat feedback", Detail);
            Check(hostNative.State.Session!.Seat == 1 && peerNative.State.Session!.Seat == 2, "Initial seats do not match negotiation");
            var beforeSeats = authority.Snapshot.Groups.ToArray();
            peerNative.EfbSeat("request");
            await Wait(() => hostNative.State.Session!.SeatPending == 2 && peerNative.State.Session!.SeatPending == 1, "EFB seat request across native FIFO and real relay", Detail);
            var ticket = hostNative.State.Session!.SeatRequest;
            // Requesters cannot accept their own request, and an old UI cannot
            // accept a ticket using a previous revision.
            peerNative.EfbSeat("accept"); hostNative.EfbSeat("accept", revision: 1, ticket: ticket);
            await Task.Delay(100);
            Check(authority.Snapshot.Seats.PilotPeer == "native-host" && authority.Snapshot.SeatChange is not null, "Unconfirmed seat request changed seats");
            hostNative.EfbSeat("accept");
            await Wait(() => hostNative.State.Session!.Seat == 2 && peerNative.State.Session!.Seat == 1 &&
                hostNative.State.Session.SeatPending == 0 && peerNative.State.Session.SeatPending == 0, "confirmed seat swap read-back on both natives", Detail);
            Check(authority.Snapshot.PhysicsHost == "native-host" && authority.Snapshot.Groups.SequenceEqual(beforeSeats) &&
                hostNative.State.Session!.Blocked == 0 && peerNative.State.Session!.Blocked == 15, "Seat swap changed physics or control ownership");
            hostNative.EfbSeat("request");
            await Wait(() => peerNative.State.Session!.SeatPending == 2, "reverse seat request", Detail);
            peerNative.EfbSeat("decline");
            await Wait(() => hostNative.State.Session!.SeatPending == 0 && peerNative.State.Session!.SeatPending == 0, "declined seat swap", Detail);
            Check(authority.Snapshot.Seats.PilotPeer == "native-peer", "Declining changed the seats");
            hostNative.EfbSeat("request");
            await Wait(() => peerNative.State.Session!.SeatPending == 2, "second reverse seat request", Detail);
            peerNative.EfbSeat("accept");
            await Wait(() => hostNative.State.Session!.Seat == 1 && peerNative.State.Session!.Seat == 2 &&
                hostNative.State.Session.SeatPending == 0 && peerNative.State.Session.SeatPending == 0, "confirmed return to original seats", Detail);
            Check(authority.Snapshot.Groups.SequenceEqual(beforeSeats), "Reverse seat swap changed control generations");
            Console.WriteLine("PASS actual EFB seat request/accept/decline through C++ native FIFO, host authority and real relay; masks and physics host preserved");
            var beforeBaro = hostNative.Transmissions;
            hostNative.Altimeters(999.125, true, 1012.5, false);
            var baro = hostNative.State.Snapshot!.Altimeters;
            Check(baro.Fresh && baro.Serial > 0 && baro.Pressure1 == 15986 && baro.Pressure2 == 16200 && baro.StandardMask == 1,
                "Actual C++ tuned pressure/STD read-back did not cross the native observation wire");
            Check(hostNative.Transmissions == beforeBaro && authority.Snapshot.Groups.SequenceEqual(beforeSeats), "Reading barometers changed simulator output or group ownership");
            Console.WriteLine("PASS distinct indexed tuned QNH and STD read-backs cross the actual C++/C# observation boundary without pressure writes");
            Console.WriteLine("PASS real negotiation, coordinator and C++ native bind/initial aligned attachment");
            Check(!hostNative.State.Session!.Flags.HasFlag(NativeSessionFlags.WorldFrozen) &&
                peerNative.State.Session!.Flags.HasFlag(NativeSessionFlags.WorldFrozen), "Physics freeze was not restricted to the guest");
            hostNative.World(4321);
            await Wait(() => Math.Abs(peerNative.State.Session!.World[2] - 4321) < 0.001, "native guest follows host world data through the actual relay", Detail);
            peerNative.World(9000);
            await Wait(() => Math.Abs(peerNative.State.Session!.World[2] - 4321) < 0.001, "host world replaces local guest drift", Detail);
            Check(Math.Abs(hostNative.State.Session!.World[2] - 4321) < 0.001, "Guest world changed the fixed physics host");
            Console.WriteLine("PASS fixed-host world replication writes guest position through the actual native SDK adapter");

            const uint throttle = (1u << 9) | (1u << 12), rpm = (1u << 10) | (1u << 13), mixture = (1u << 11) | (1u << 14);
            peerNative.Efb(ControlSelection.Quadrant, NativeSessionIntent.AutoOn);
            await Wait(() => authority.Snapshot.Automatic && hostNative.State.Session!.Automatic && peerNative.State.Session!.Automatic,
                "single quadrant Auto policy confirmed on both native instances", Detail);
            await Task.Delay(150); // First hardware observation establishes the anchor.
            hostNative.Hardware(throttle, 0.65);
            await Wait(() => Math.Abs(peerNative.State.Snapshot!.Targets[9] - 0.65) < 0.006, "cockpit-only change reaches peer", Detail);
            await Task.Delay(150);
            Check(Owns("native-host", AtomicControlGroup.Throttle) && peerNative.State.Session!.AutomaticWaiting == 0, "Cockpit feedback armed Auto");
            hostNative.Hardware(throttle, 0.5);
            peerNative.Hardware(throttle, 0.52); await Task.Delay(200);
            Check(Owns("native-host", AtomicControlGroup.Throttle) && peerNative.State.Session!.AutomaticWaiting == 0, "Exactly two percent armed Auto");
            peerNative.Hardware(throttle, 0.51); await Task.Delay(100);
            peerNative.Hardware(throttle, 0.1);
            await Wait(() => peerNative.State.Session!.AutomaticWaiting == 2, "automatic throttle waits visibly for alignment", Detail);
            Check(Owns("native-host", AtomicControlGroup.Throttle) && authority.Snapshot.Transfers.Length == 0, "Unaligned Auto revoked the current owner");
            peerNative.Hardware(7, 0.7); await Task.Delay(150);
            Check(Owns("native-host", AtomicControlGroup.Steering), "Steering acquired an automatic mode");
            peerNative.Hardware(7, 0.5);
            peerNative.Hardware(throttle, 0.5);
            await Wait(() => Owns("native-peer", AtomicControlGroup.Throttle), "automatic aligned peer throttle pickup", Detail);
            Check(Owns("native-host", AtomicControlGroup.Propeller) && Owns("native-host", AtomicControlGroup.Mixture), "Throttle Auto changed another pair");
            foreach (var pair in new[] { (AtomicControlGroup.Propeller, rpm), (AtomicControlGroup.Mixture, mixture) }) {
                peerNative.Hardware(pair.Item2, 0.54);
                await Wait(() => Owns("native-peer", pair.Item1), "same global Auto policy applies to " + pair.Item1, Detail);
                await Task.Delay(150);
                hostNative.Hardware(pair.Item2, 0.55);
                await Wait(() => Owns("native-host", pair.Item1), "independent automatic return of " + pair.Item1, Detail);
            }
            await Task.Delay(150);
            Check(Owns("native-peer", AtomicControlGroup.Throttle), "A stationary previous owner reclaimed controls");
            hostNative.Hardware(throttle, 0.52); await Task.Delay(150);
            Check(Owns("native-peer", AtomicControlGroup.Throttle), "Two-percent host noise reclaimed throttle");
            hostNative.Hardware(throttle, 0.54);
            await Wait(() => Owns("native-host", AtomicControlGroup.Throttle), "automatic throttle return after cumulative anchored movement", Detail);
            await Task.Delay(150);
            peerNative.Hardware(throttle, 0.1);
            await Wait(() => peerNative.State.Session!.AutomaticWaiting == 2, "second automatic throttle wait", Detail);
            peerNative.Efb(ControlSelection.Throttle, NativeSessionIntent.Cancel);
            await Wait(() => peerNative.State.Session!.AutomaticWaiting == 0 && peerNative.State.Session.IntentId == 0,
                "scoped automatic cancellation is acknowledged", Detail);
            await Task.Delay(200);
            Check(peerNative.State.Session!.AutomaticWaiting == 0 && Owns("native-host", AtomicControlGroup.Throttle), "Stationary hardware rearmed a cancelled Auto pickup");
            peerNative.Hardware(throttle, 0.14);
            await Wait(() => peerNative.State.Session!.AutomaticWaiting == 2, "new hardware movement rearms Auto after cancellation", Detail);
            peerNative.Hardware(mixture, 0.1); peerNative.Efb(ControlSelection.Mixture, NativeSessionIntent.Me);
            await Wait(() => (peerNative.State.Session!.IntentWaiting & 8) != 0, "manual mixture wait alongside Auto", Detail);
            hostNative.Efb(ControlSelection.Quadrant, NativeSessionIntent.AutoOff);
            await Wait(() => !authority.Snapshot.Automatic && !peerNative.State.Session!.Automatic && peerNative.State.Session.AutomaticWaiting == 0,
                "Auto off clears automatic waiting on both simulators", Detail);
            Check((peerNative.State.Session!.IntentWaiting & 8) != 0 && Owns("native-host", AtomicControlGroup.Throttle), "Auto off cancelled manual pickup or changed ownership");
            peerNative.Hardware(throttle, 0.54); await Task.Delay(400);
            Check(Owns("native-host", AtomicControlGroup.Throttle), "Auto off still took throttle");
            peerNative.Hardware(mixture, 0.5);
            await Wait(() => Owns("native-peer", AtomicControlGroup.Mixture), "manual mixture survives Auto off", Detail);
            hostNative.Efb(ControlSelection.Mixture, NativeSessionIntent.Me);
            await Wait(() => Owns("native-host", AtomicControlGroup.Mixture), "restore initial mixture owner", Detail);
            hostNative.Hardware(throttle, 0.5); peerNative.Hardware(throttle, 0.5);
            Console.WriteLine("PASS one shared Auto switch, hardware-only movement above 2%, independent aligned pickup both ways, manual steering and selective Auto-off cancellation");

            peerNative.Hardware(rpm, 0.1); hostNative.Efb(ControlSelection.Propeller, NativeSessionIntent.Other);
            await Wait(() => Group(AtomicControlGroup.Propeller).Pending is not null, "unaligned peer RPM request", Detail);
            peerNative.Efb(ControlSelection.Throttle, NativeSessionIntent.Me);
            await Wait(() => Owns("native-peer", AtomicControlGroup.Throttle), "aligned throttle bypasses unaligned pending RPM", Detail);
            Check(Group(AtomicControlGroup.Propeller).Pending is not null && Owns("native-host", AtomicControlGroup.Mixture), "Independent pickup changed another pair");
            peerNative.Hardware(throttle, 0.75);
            await Wait(() => Near(9, 0.75) && Near(12, 0.75), "qualified peer throttle reaches actual C++ SDK output", Detail);
            hostNative.Hardware(throttle, 0.2); await Task.Delay(150);
            Check(Near(9, 0.75) && Near(12, 0.75), "Blocked host hardware overwrote peer throttle");
            hostNative.Efb(ControlSelection.Propeller, NativeSessionIntent.Cancel);
            await Wait(() => Group(AtomicControlGroup.Propeller).Pending is null, "cancel pending RPM", Detail);
            Check(!Group(AtomicControlGroup.Propeller).Attached, "Cancellation silently reattached old hardware");
            hostNative.Efb(ControlSelection.Propeller, NativeSessionIntent.Me);
            await Wait(() => Owns("native-host", AtomicControlGroup.Propeller), "explicit aligned recovery after cancellation", Detail);
            Console.WriteLine("PASS independent aligned throttle while RPM waits; native event output, masked hardware and explicit cancellation recovery");

            peerNative.Efb(ControlSelection.Mixture, NativeSessionIntent.Me);
            await Wait(() => Owns("native-peer", AtomicControlGroup.Mixture), "independent mixture grant", Detail);
            peerNative.Hardware(mixture, 0.9);
            await Wait(() => Near(11, 0.9) && Near(14, 0.9), "peer mixture cockpit read-back", Detail);
            hostNative.Efb(ControlSelection.Throttle, NativeSessionIntent.Me); await Task.Delay(150);
            Check(Owns("native-peer", AtomicControlGroup.Throttle), "Misaligned host reclaimed throttle");
            hostNative.Hardware(throttle, 0.75);
            await Wait(() => Owns("native-host", AtomicControlGroup.Throttle), "return throttle to host", Detail);
            Check(Owns("native-peer", AtomicControlGroup.Mixture) && Owns("native-host", AtomicControlGroup.Propeller), "Return handover changed another owner");
            hostNative.Hardware(throttle, 0.6);
            await Wait(() => Near(9, 0.6) && Near(12, 0.6), "new host throttle", Detail);
            peerNative.Hardware(throttle, 0.3); await Task.Delay(150);
            Check(Near(9, 0.6) && Near(12, 0.6), "Retired peer throttle values executed after return handover");
            Console.WriteLine("PASS bidirectional throttle ownership, retained independent mixture owner and rejected old-owner input");

            peerNative.Hardware(throttle, 0.6);
            peerNative.Efb(ControlSelection.Throttle, NativeSessionIntent.Me);
            await Wait(() => Group(AtomicControlGroup.Throttle).Owner == "native-peer" && !Group(AtomicControlGroup.Throttle).Attached,
                "committed incoming transfer before native attachment", Detail);
            peerNative.Efb(ControlSelection.Throttle, NativeSessionIntent.Cancel);
            await Wait(() => peerNative.State.Session?.IntentId == 0, "native consumes cancellation", Detail);
            await Task.Delay(450);
            Check(!Group(AtomicControlGroup.Throttle).Attached && (peerNative.State.Session!.Blocked & 2) != 0,
                "Cancel during committed pickup automatically reattached hardware");
            peerNative.Efb(ControlSelection.Throttle, NativeSessionIntent.Me);
            await Wait(() => Owns("native-peer", AtomicControlGroup.Throttle), "explicit resume after committed pickup cancellation", Detail);
            hostNative.Efb(ControlSelection.Throttle, NativeSessionIntent.Me);
            await Wait(() => Owns("native-host", AtomicControlGroup.Throttle), "restore owner for mixed quadrant scenario", Detail);
            Console.WriteLine("PASS EFB cancellation after commit retains suppression until a new explicit pickup");

            peerNative.Efb(ControlSelection.Propeller, NativeSessionIntent.Force);
            await Wait(() => Owns("native-peer", AtomicControlGroup.Propeller), "scoped force through native suppression/epoch proofs", Detail);
            await Wait(() => Near(10, 0.1) && Near(13, 0.1), "forced unaligned RPM input", Detail);
            Check(Owns("native-host", AtomicControlGroup.Throttle) && Owns("native-peer", AtomicControlGroup.Mixture), "Force affected another group");
            peerNative.Hardware(throttle, 0.6);
            peerNative.Efb(ControlSelection.Quadrant, NativeSessionIntent.Me);
            await Wait(() => AtomicProtocol.Members(ControlSelection.Quadrant).All(group => Owns("native-peer", group)), "whole quadrant from mixed owners", Detail);
            Check(Owns("native-host", AtomicControlGroup.Steering), "Quadrant transfer changed steering");
            Console.WriteLine("PASS selected-pair Force and atomic whole-quadrant handover through relay and both native controllers");

            hostNative.Autopilot(true);
            peerNative.Efb(ControlSelection.Steering, NativeSessionIntent.Force);
            await Wait(() => Owns("native-peer", AtomicControlGroup.Steering), "steering authority independent of host autopilot", Detail);
            peerNative.Hardware(7, 0.7); await Task.Delay(150);
            Check(Near(0, 0.5) && Near(1, 0.5) && Near(2, 0.5), "Peer steering input fought the host autopilot");
            hostNative.Autopilot(false);
            await Wait(() => Near(0, 0.7) && Near(1, 0.7) && Near(2, 0.7), "remote roll/pitch/rudder after AP disengagement", Detail);
            Console.WriteLine("PASS remote roll/pitch/rudder and physics-host autopilot input interlock");

            var held = Target(9); peerNative.Crash();
            await Wait(() => hostProgress?.Phase == NativeAuthorityPhase.Ended, "native loss revokes remote authority", Detail);
            Check((hostNative.State.Session!.Blocked & 14) == 14, "Link loss released engine hardware masks");
            hostNative.Hardware(throttle | mixture, 0.95); await Task.Delay(150);
            Check(Math.Abs(Target(9) - held) < 0.006, "Host hardware resumed without explicit recovery");
            hostNative.Recover(2);
            await Wait(() => Near(9, 0.95), "explicit local throttle recovery", Detail);
            Check((hostNative.State.Session!.Blocked & 12) == 12, "Scoped recovery released RPM or mixture");
            Check(hostNative.Transmissions > 0, "No actual C++ simulator output adapter was exercised");
            Console.WriteLine("PASS native loss, stopped remote authority, retained hardware masks and scoped EFB Force recovery");
        }
    }
    public static async Task HeartbeatExpiry(int port, bool worldOnly = false) {
        using var hostNative = new CppNativeSessionClient(); using var peerNative = new CppNativeSessionClient();
        using var a = new RelayNetwork("127.0.0.1", "lease-host", "Lease host", false, port);
        using var b = new RelayNetwork("127.0.0.1", "lease-peer", "Lease peer", false, port);
        var filtered = new HeartbeatFilter(worldOnly ? a : b); var ready = 0;
        INetwork hostNetwork = worldOnly ? filtered : a, peerNetwork = worldOnly ? b : filtered;
        foreach (var network in new[] { hostNetwork, peerNetwork }) OwnershipNetworkNegotiation.RegisterProtocol(network);
        using var registrations = new CompositeDisposable();
        foreach (var network in new[] { a, b }) registrations.Add(network.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref ready)));
        await Wait(() => ready == 2, "heartbeat fixture relay registration");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Check(await a.Connect("lease-peer", deadline.Token) == ConnectionResult.Success, "Heartbeat fixture link failed");
        var clock = Stopwatch.StartNew(); var authority = new ControlAuthoritySession("lease-host", "lease-peer", 2, () => clock.Elapsed, 7);
        var replica = new ControlAuthorityReplica("lease-host", authority.Snapshot); var seats = new CockpitSeats("lease-host", "lease-peer");
        await using var peerBridge = new NativeControlAuthorityBridge(peerNetwork, peerNative, "lease-peer", new(SessionRole.Copilot, seats, null, null, AuthorityReplica: replica));
        await using var hostBridge = new NativeControlAuthorityBridge(hostNetwork, hostNative, "lease-host", new(SessionRole.PhysicsHost, seats, null, null, Authority: authority));
        NativeAuthorityProgress? state = null, peerState = null;
        using var progress = hostBridge.Progress.Subscribe(value => state = value);
        using var peerProgress = peerBridge.Progress.Subscribe(value => peerState = value);
        string Detail() => $"host={state}; peer={peerState}; groups={string.Join(',', authority.Snapshot.Groups.Select(group => $"{group.Group}:{group.Attached}/{group.Generation}/{group.Pending}"))}; " +
            $"host native={hostNative.State.Session?.Phase}/{hostNative.State.Session?.Code}/{hostNative.State.Session?.Ack}; peer native={peerNative.State.Session?.Phase}/{peerNative.State.Session?.Code}/{peerNative.State.Session?.Ack}";
        await Wait(() => authority.Snapshot.Groups.All(group => group.Attached), "heartbeat fixture initial attachment", Detail);
        var aliveBefore = filtered.AlivePackets;
        if (worldOnly) Volatile.Write(ref filtered.WorldMuted, true); else Volatile.Write(ref filtered.Muted, true);
        await Wait(() => (worldOnly ? peerState : state)?.Recovering == true, "soft stream loss enters recovery before ending", Detail);
        await Task.Delay(250);
        Check(state?.Phase==NativeAuthorityPhase.Active && peerState?.Phase==NativeAuthorityPhase.Active,
            "Short stream loss ended the shared session");
        if (worldOnly) Volatile.Write(ref filtered.WorldMuted, false); else Volatile.Write(ref filtered.Muted, false);
        await Wait(() => state is {Recovering:false,Phase:NativeAuthorityPhase.Active} && peerState is {Recovering:false,Phase:NativeAuthorityPhase.Active},
            "short stream loss recovers automatically", Detail);
        Check(peerNative.State.Session!.Blocked==15, "Stream recovery released guest input masks");
        Console.WriteLine("PASS temporary "+(worldOnly?"world":"heartbeat")+" gap: warning, preserved session and automatic fresh-data recovery");
        if (worldOnly) Volatile.Write(ref filtered.WorldMuted, true); else Volatile.Write(ref filtered.Muted, true);
        await Wait(() => state?.Phase == NativeAuthorityPhase.Ended, "application heartbeat expiry without socket failure");
        Check(filtered.Dropped > 0 && (worldOnly ? filtered.AlivePackets > aliveBefore : state!.Error.Contains("heartbeat", StringComparison.Ordinal)), "Session ended for a different reason");
        Check(peerNative.State.Session!.Blocked == 15, "Heartbeat expiry released the peer hardware masks");
        if (worldOnly) Check(peerNative.State.Session.Flags.HasFlag(NativeSessionFlags.WorldFrozen), "World-stream loss released guest physics");
        Console.WriteLine(worldOnly ? "PASS world-stream loss ends control despite live application heartbeats and retains guest freezes/masks" :
            "PASS session heartbeat expires independently of a live relay socket and retains native hardware suppression");
    }
}
