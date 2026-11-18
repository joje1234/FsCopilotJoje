using FsCopilot.Network;
using FsCopilot.Ownership;

internal static class GuidanceAuthorityNetworkTests
{
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static async Task Wait(Func<bool> ready, string reason) {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Check(ready(), "Guidance relay timeout: " + reason);
    }
    public static async Task Run(int port)
    {
        using var a = new RelayNetwork("127.0.0.1", "ap-host", "AP host", false, port);
        using var b = new RelayNetwork("127.0.0.1", "ap-peer", "AP peer", false, port);
        using var subscriptions = new CompositeDisposable(); var registrations = 0;
        foreach (var client in new[] { a, b }) {
            OwnershipNetworkNegotiation.RegisterProtocol(client);
            subscriptions.Add(client.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref registrations)));
        }
        await Wait(() => registrations == 2, "attributed clients registered");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Check(await a.Connect("ap-peer", deadline.Token) == ConnectionResult.Success, "AP clients did not link");
        // Real production relay/clients/actor. Input gates and avionics readback
        // below are explicit fixtures, not evidence of actual Baron AP enforcement.
        var authority = new ControlAuthoritySession("ap-host", "ap-peer", 2, () => TimeSpan.Zero);
        var controls = new ControlAuthorityReplica("ap-host", authority.Snapshot);
        var guidance = new GuidanceAuthorityReplica(authority.Snapshot); var invalid = 0;
        subscriptions.Add(b.StreamWithSource<ControlAuthorityPacket>().Subscribe(packet => {
            if (!AuthorityWire.TryDecode(packet.Packet.Payload, out var message)) { Interlocked.Increment(ref invalid); return; }
            if (message is AuthorityState state && state.State.Revision > controls.Snapshot.Revision && !controls.Receive(packet.Sender, state.State)) Interlocked.Increment(ref invalid);
            if (message is AuthorityGuidanceState ap && ap.State.Revision > guidance.Snapshot.Revision && !guidance.Receive(packet.Sender, ap.State, controls.Snapshot)) Interlocked.Increment(ref invalid);
        }));
        using var host = new ControlAuthorityNetworkHost(a, authority);
        var actual = new GuidanceState(false, GuidanceModes.None, 90, 5000, 500, true, 1, true, new string('a', 64));
        Check(await host.StartGuidance(actual, 1), "Host guidance did not start");
        await Wait(() => guidance.Snapshot.Phase == GuidancePhase.Preparing, "host-only default reaches peer");
        ulong hostRead = 1, peerRead = 1, hostProof = 0, peerProof = 0;
        GuidanceGateProof Proof(string who) {
            var state = authority.Guidance;
            return new(authority.Session, state.Preparation, state.CommandEpoch, state.NavigationEpoch,
                who == "ap-host" ? ++hostProof : ++peerProof, "test-absolute-ap-v1", GuidanceCapabilities.Commands, true,
                state.Phase is GuidancePhase.Attaching or GuidancePhase.Active && state.Commander == who, true,
                who == "ap-host" ? ++hostRead : ++peerRead, actual, false, GuidanceProtocol.AllSettings);
        }
        void Peer(AuthorityMessage message) => b.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(message)));
        async Task GatePair(GuidancePhase expected) {
            Check(await host.ApplyLocal(new AuthorityGuidanceGate(AtomicProtocol.Version, Proof("ap-host"))), "Host gate rejected");
            Peer(new AuthorityGuidanceGate(AtomicProtocol.Version, Proof("ap-peer")));
            await Wait(() => guidance.Snapshot.Phase == expected, "gated " + expected);
        }
        await GatePair(GuidancePhase.Attaching); await GatePair(GuidancePhase.Active);
        var mainRevision = authority.Snapshot.Revision; var baroRevision = authority.Altimeters.Revision;
        var original = authority.Guidance;
        Peer(new AuthorityGuidanceClaim(AtomicProtocol.Version, new(authority.Session, 1, GuidanceSelection.Commands, "ap-peer", 1, 1)));
        await Wait(() => guidance.Snapshot.Phase == GuidancePhase.Quiescing, "peer claim gates old commander");
        await GatePair(GuidancePhase.Attaching); await GatePair(GuidancePhase.Active);
        Check(guidance.Snapshot.Commander == "ap-peer" && guidance.Snapshot.Navigator == "ap-host", "AP claim moved navigation source");
        GuidanceCommand Command(ulong sequence, GuidanceSetting setting, double value) => new(authority.Session, authority.Guidance.Preparation,
            authority.Guidance.CommandEpoch, authority.Guidance.NavigationEpoch, sequence, setting, value);
        var command = Command(1, GuidanceSetting.Heading, 130);
        Check(!await host.ApplyLocal(new AuthorityGuidanceCommand(AtomicProtocol.Version, command)), "Old commander retained AP input");
        Peer(new AuthorityGuidanceCommand(AtomicProtocol.Version, command));
        Peer(new AuthorityGuidanceCommand(AtomicProtocol.Version, command));
        await Wait(() => authority.Guidance.Pending?.Command == command, "new commander target accepted");
        var writes = 0;
        Check(await host.ApplyGuidance(command, hostRead, _ => { ++writes; return true; }), "Target was not dispatched");
        Check(!await host.ApplyGuidance(command, hostRead, _ => { ++writes; return true; }), "Duplicate target dispatched");
        actual = actual with { Heading = 130 };
        Check(await host.ObserveGuidance(++hostRead, actual), "Actual host readback rejected");
        Check(authority.Guidance.Pending is not null, "Raw observation replaced SDK completion proof");
        Check(await host.ConfirmGuidance(command, hostRead, actual), "SDK completion proof rejected");
        await Wait(() => guidance.Snapshot.Pending is null && guidance.Snapshot.Confirmed!.Heading == 130, "readback is replicated");
        Peer(new AuthorityGuidanceClaim(AtomicProtocol.Version, new(authority.Session, 2, GuidanceSelection.Navigation, "ap-peer", 2, 1, new string('b', 64))));
        // A forged host publication and a retired command cannot alter host authority.
        Peer(new AuthorityGuidanceState(AtomicProtocol.Version, original with { Revision = 999 }));
        Peer(new AuthorityGuidanceCommand(AtomicProtocol.Version, command with { Sequence = 2, CommandEpoch = 1, Value = 140 }));
        var barrier = Command(3, GuidanceSetting.Altitude, 6000);
        Peer(new AuthorityGuidanceCommand(AtomicProtocol.Version, barrier));
        await Wait(() => authority.Guidance.Pending?.Command == barrier, "ordered negative-message barrier");
        Check(authority.Guidance.Navigator == "ap-host" && authority.Guidance.Phase == GuidancePhase.Active, "Unverified navigator was enabled");
        Check(authority.Snapshot.Revision == mainRevision && authority.Altimeters.Revision == baroRevision && writes == 1 && invalid == 0,
            "AP authority affected another owner, duplicated writes or invalidated replica");
        Peer(new AuthorityGuidanceStop(AtomicProtocol.Version, authority.Session, 1));
        await Wait(() => guidance.Snapshot.Phase == GuidancePhase.Local, "scoped stop reaches both peers");
        Check(!await host.ApplyGuidance(barrier, hostRead, _ => { ++writes; return true; }), "Stopped target reached output");
        Check(await host.EndSession(), "Session did not end");
        await Wait(() => guidance.Snapshot.Phase == GuidancePhase.Ended, "AP lifecycle ends with shared session");
        Console.WriteLine("PASS real relay AP commander handover independent of navigation/physics/controls/seats/altimeters; attributed absolute targets, readback, replay rejection and scoped stop. Gate/avionics proofs are test fixtures; no simulator access.");
    }
}
