using FsCopilot.Network;
using FsCopilot.Ownership;

internal static class AltimeterAuthorityNetworkTests
{
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static async Task Wait(Func<bool> ready, string reason) {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Check(ready(), "Altimeter relay timeout: " + reason);
    }
    public static async Task Run(int port)
    {
        using var a = new RelayNetwork("127.0.0.1", "baro-host", "Baro host", false, port);
        using var b = new RelayNetwork("127.0.0.1", "baro-peer", "Baro peer", false, port);
        using var subscriptions = new CompositeDisposable(); var registrations = 0;
        foreach (var network in new[] { a, b }) {
            OwnershipNetworkNegotiation.RegisterProtocol(network);
            subscriptions.Add(network.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref registrations)));
        }
        await Wait(() => registrations == 2, "attributed clients registered");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Check(await a.Connect("baro-peer", deadline.Token) == ConnectionResult.Success, "Barometer clients did not link");
        // The transport is real. Gate and SDK-completion proofs below are explicit
        // test data, using a logical clock; this does not establish actual input blocking.
        var authority = new ControlAuthoritySession("baro-host", "baro-peer", 2, () => TimeSpan.Zero);
        var main = new ControlAuthorityReplica("baro-host", authority.Snapshot);
        var instruments = new AltimeterAuthorityReplica(authority.Snapshot);
        var invalid = 0;
        subscriptions.Add(b.StreamWithSource<ControlAuthorityPacket>().Subscribe(packet => {
            if (!AuthorityWire.TryDecode(packet.Packet.Payload, out var message)) { Interlocked.Increment(ref invalid); return; }
            if (message is AuthorityState state && state.State.Revision > main.Snapshot.Revision && !main.Receive(packet.Sender, state.State)) Interlocked.Increment(ref invalid);
            if (message is AuthorityAltimeterState baro && baro.State.Revision > instruments.Snapshot.Revision && !instruments.Receive(packet.Sender, baro.State, main.Snapshot)) Interlocked.Increment(ref invalid);
        }));
        using var host = new ControlAuthorityNetworkHost(a, authority);
        var seed = new AltimeterSetting[] { new(15986, true), new(16200, false) };
        Check(await host.StartAltimeters(seed, 10), "Host did not seed both altimeters");
        await Wait(() => instruments.Snapshot.Phase == AltimeterPhase.Synchronizing, "separate pressure seed reaches replica");
        void Peer(AuthorityMessage message) => b.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(message)));
        ulong hostRead = 10, peerRead = 0, hostProof = 0, peerProof = 0;
        AltimeterGateAck Gate(string who) {
            var state = authority.Altimeters;
            return new(authority.Session, state.Epoch, state.Preparation, who == "baro-host" ? ++hostProof : ++peerProof, true, "test-verified-altimeters-v1",
                state.Phase is AltimeterPhase.Synchronizing or AltimeterPhase.Quiescing ? 3u : AltimeterProtocol.Blocked(state.Seats, who),
                who == "baro-host" ? ++hostRead : ++peerRead,
                state.Instruments.Select(value => value.Confirmed!.Value).ToArray());
        }
        Check(await host.ApplyLocal(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-host"))), "Host gate proof rejected");
        Peer(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-peer")));
        await Wait(() => instruments.Snapshot.Phase == AltimeterPhase.Attaching, "both masked baseline proofs precede knob release");
        Check(await host.ApplyLocal(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-host"))), "Host attachment proof rejected");
        Peer(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-peer")));
        await Wait(() => instruments.Snapshot.Phase == AltimeterPhase.Active, "both gate proofs enable instrument authority");
        var controlRevision = authority.Snapshot.Revision; var groups = authority.Snapshot.Groups.ToArray();
        var change = new AltimeterChange(authority.Session, 1, authority.Altimeters.Preparation, 1, 2, new(16160, false));
        Peer(new AuthorityAltimeterChange(AtomicProtocol.Version, change with { Instrument = 1 }));
        Peer(new AuthorityAltimeterChange(AtomicProtocol.Version, change));
        await Wait(() => authority.Altimeters.Instruments[1].Pending == change, "right-seat owner request reaches host");
        Check(authority.Altimeters.Instruments[0].Pending is null, "A peer changed the other seat's instrument");
        var writes = 0;
        Check(await host.ApplyAltimeter(change, hostRead, _ => { ++writes; return true; }), "Pending pressure was not dispatched");
        Check(authority.Altimeters.Instruments[1].Confirmed == seed[1], "A queued write was published as confirmed");
        Check(await host.ApplyLocal(new AuthorityAltimeterConfirmation(AtomicProtocol.Version, new(change, ++hostRead, true, change.Setting))), "Host SDK confirmation rejected");
        await Wait(() => instruments.Snapshot.Instruments[1].Confirmed == change.Setting, "confirmed pressure reaches peer without rotary replay");
        Check(authority.Snapshot.Revision == controlRevision && writes == 1, "Pressure tuning changed control authority or duplicated output");
        Peer(new AuthoritySeatChange(AtomicProtocol.Version, new(authority.Session, 1, authority.Snapshot.SeatRevision, SeatChangeAction.Request, 0)));
        await Wait(() => authority.Snapshot.SeatChange is not null, "seat request");
        var seat = authority.Snapshot.SeatChange!;
        Check(await host.ApplyLocal(new AuthoritySeatChange(AtomicProtocol.Version, new(authority.Session, 1, authority.Snapshot.SeatRevision, SeatChangeAction.Accept, seat.Ticket))), "Seat acceptance rejected");
        await Wait(() => instruments.Snapshot.Phase == AltimeterPhase.Quiescing, "accepted seat swap waits for both input gates");
        Check(main.Snapshot.Seats.PilotPeer == "baro-host" && main.Snapshot.SeatChange is { Accepted: true }, "Seats changed before gate confirmation");
        Peer(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-peer")));
        Check(await host.ApplyLocal(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-host"))), "Host stopped-input proof rejected");
        await Wait(() => main.Snapshot.Seats.PilotPeer == "baro-peer" && instruments.Snapshot.Epoch == 2, "confirmed seat and instrument ownership swap");
        Check(instruments.Snapshot.Instruments[0].Confirmed == seed[0] && instruments.Snapshot.Instruments[1].Confirmed == change.Setting, "Seat swap changed pressure or STD");
        Peer(new AuthorityAltimeterChange(AtomicProtocol.Version, change with { Sequence = 2 }));
        Check(await host.ApplyLocal(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-host"))), "New host seat gate rejected");
        Peer(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-peer")));
        await Wait(() => instruments.Snapshot.Phase == AltimeterPhase.Attaching, "new native baselines remain masked before attachment");
        Check(await host.ApplyLocal(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-host"))), "New host seat attachment rejected");
        Peer(new AuthorityAltimeterGate(AtomicProtocol.Version, Gate("baro-peer")));
        await Wait(() => instruments.Snapshot.Phase == AltimeterPhase.Active, "new owners resume after native baseline confirmation");
        var left = new AltimeterChange(authority.Session, 2, authority.Altimeters.Preparation, 2, 1, new(16000, true));
        Peer(new AuthorityAltimeterChange(AtomicProtocol.Version, left));
        await Wait(() => authority.Altimeters.Instruments[0].Pending == left, "new pilot controls left instrument");
        Check(authority.Altimeters.Instruments[1].Pending is null, "Retired owner command survived seat swap");
        Check(await host.ApplyAltimeter(left, hostRead, _ => { ++writes; return true; }), "New pilot pressure not dispatched");
        Check(await host.ApplyLocal(new AuthorityAltimeterConfirmation(AtomicProtocol.Version, new(left, ++hostRead, true, left.Setting))), "New pilot confirmation rejected");
        await Wait(() => instruments.Snapshot.Instruments[0].Confirmed == left.Setting, "new pilot pressure replicated");
        Check(authority.Snapshot.PhysicsHost == "baro-host" && authority.Snapshot.Groups.SequenceEqual(groups) && writes == 2 && invalid == 0,
            "Instrument transfer changed physics/control groups or invalidated the peer replica");
        Check(await host.EndSession(), "Shared session did not end");
        await Wait(() => instruments.Snapshot.Phase == AltimeterPhase.Ended, "instrument lifecycle follows explicit session end");
        Console.WriteLine("PASS real relay seat-owned absolute altimeter requests, host confirmation, preserved QNH/STD during a gated seat swap, rejected retired owner and independent control revisions. Native gate/output proofs are test data; no simulator access.");
    }
}
