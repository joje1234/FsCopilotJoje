using FsCopilot.Network;
using FsCopilot.Ownership;

internal static class AdvancedAuthorityNetworkTests
{
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static async Task Wait(Func<bool> ready, string reason)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Check(ready(), "Advanced authority timeout: " + reason);
    }
    public static async Task Run(int port)
    {
        using var a = new RelayNetwork("127.0.0.1", "atomic-A", "A", false, port);
        using var b = new RelayNetwork("127.0.0.1", "atomic-B", "B", false, port);
        using var c = new RelayNetwork("127.0.0.1", "atomic-C", "C", false, port);
        var ready = 0;
        using var subscriptions = new CompositeDisposable();
        foreach (var network in new[] { a, b, c }) {
            OwnershipNetworkNegotiation.RegisterProtocol(network);
            subscriptions.Add(network.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref ready)));
        }
        await Wait(() => Volatile.Read(ref ready) == 3, "relay capability");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Check(await a.Connect("atomic-B", deadline.Token) == ConnectionResult.Success, "A/B link failed");
        Check(await c.Connect("atomic-A", deadline.Token) == ConnectionResult.Success, "C/A link failed");
        var config = new SessionConfiguration("bksq-aircraft-baronpropress", 2, "baron58p-v2", 2, SessionNegotiation.AdvancedCapabilities, 0x37);
        using var hostNegotiation = new OwnershipNetworkNegotiation(a, "atomic-A", "atomic-B", SessionRole.PhysicsHost, config);
        using var peerNegotiation = new OwnershipNetworkNegotiation(b, "atomic-B", "atomic-A", SessionRole.Copilot, config);
        var hostBindings = new ConcurrentQueue<NegotiatedControlSession?>(); var peerBindings = new ConcurrentQueue<NegotiatedControlSession?>();
        subscriptions.Add(hostNegotiation.Sessions.Subscribe(hostBindings.Enqueue)); subscriptions.Add(peerNegotiation.Sessions.Subscribe(peerBindings.Enqueue));
        await Wait(() => hostBindings.LastOrDefault() is not null && peerBindings.LastOrDefault() is not null, "four-group negotiation");
        var session = hostBindings.Last()!.Authority!; var replica = peerBindings.Last()!.AuthorityReplica!;
        Check(session.Snapshot.Groups.Length == 4 && replica.Snapshot.SteeringAxes == 0x37, "Wrong negotiated control layout");
        using var host = new ControlAuthorityNetworkHost(a, session);
        var accepted = new ConcurrentQueue<AttributedControlFrame>();
        subscriptions.Add(host.AcceptedInputs.Subscribe(accepted.Enqueue));
        subscriptions.Add(b.StreamWithSource<ControlAuthorityPacket>().Subscribe(packet => {
            if (packet.Sender == "atomic-A" && AuthorityWire.TryDecode(packet.Packet.Payload, out var message) && message is AuthorityState state)
                replica.Receive(packet.Sender, state.State);
        }));
        GroupEpoch[] Epochs(ControlSelection selection) => session.Snapshot.Groups.Where(group => (selection & AtomicProtocol.Select(group.Group)) != 0)
            .Select(group => new GroupEpoch(group.Group, group.Generation)).ToArray();
        void Remote(AuthorityMessage message) => b.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(message)));
        Check(await host.ApplyLocal(new AuthorityAttachment(AtomicProtocol.Version, new(session.Session, ControlSelection.All, Epochs(ControlSelection.All), true))), "Initial host gate attach");
        Remote(new AuthorityRequest(AtomicProtocol.Version, new(session.Session, 1, ControlSelection.Throttle, Epochs(ControlSelection.Throttle), "atomic-B")));
        await Wait(() => session.Snapshot.Transfers.Length == 1, "throttle transfer request");
        var transfer = session.Snapshot.Transfers.Single();
        Check(await host.ApplyLocal(new AuthorityPreparation(AtomicProtocol.Version, new(session.Session, transfer.Transfer, true, false))), "Host throttle gate close");
        Remote(new AuthorityPreparation(AtomicProtocol.Version, new(session.Session, transfer.Transfer, true, true)));
        await Wait(() => session.Snapshot.Groups.Single(group => group.Group == AtomicControlGroup.Throttle).Owner == "atomic-B", "throttle commit");
        Remote(new AuthorityAttachment(AtomicProtocol.Version, new(session.Session, ControlSelection.Throttle, Epochs(ControlSelection.Throttle), true)));
        await Wait(() => session.Snapshot.Groups.Single(group => group.Group == AtomicControlGroup.Throttle).Attached, "throttle attachment");
        var frame = new AtomicControlFrame(session.Session, AtomicControlGroup.Throttle, Epochs(ControlSelection.Throttle)[0].Generation, 1,
            [new(ControlAxis.Throttle1, 0.3), new(ControlAxis.Throttle2, 0.7)]);
        Remote(new AuthorityInput(AtomicProtocol.Version, frame)); Remote(new AuthorityInput(AtomicProtocol.Version, frame));
        await Wait(() => accepted.Count == 1, "deduplicated complete throttle pair");
        Check(accepted.Single().Sender == "atomic-B" && accepted.Single().Frame.Axes[0].Position != accepted.Single().Frame.Axes[1].Position, "Input lost transport identity or engine independence");
        var queued = accepted.Single(); var writes = 0;
        queued.Frame.Axes[0] = new(ControlAxis.Throttle1, 0.9);
        Check(!session.TryApplyAcceptedInput(queued.Sender, queued.Frame, _ => ++writes), "Queued values changed after host acceptance");
        queued.Frame.Axes[0] = new(ControlAxis.Throttle1, 0.3);
        Check(session.TryApplyAcceptedInput(queued.Sender, queued.Frame, sample => {
            Check(sample.Axes[0].Position == 0.3 && sample.Axes[1].Position == 0.7, "Writer did not receive the accepted engine values");
            ++writes;
        }) && writes == 1, "Accepted relay sample did not execute once at writer boundary");
        Check(!session.TryApplyAcceptedInput(queued.Sender, queued.Frame, _ => ++writes), "Writer executed a duplicate relay sample");
        c.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(new AuthorityInput(AtomicProtocol.Version, frame with { Sequence = 2 }))));
        c.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(new AuthorityRequest(AtomicProtocol.Version, new(session.Session, 2, ControlSelection.Quadrant, Epochs(ControlSelection.Quadrant), "atomic-C")))));
        await Task.Delay(100);
        Check(accepted.Count == 1 && session.Snapshot.Transfers.Length == 0, "Third participant influenced control authority");
        Console.WriteLine("PASS negotiated four-group authority carries independent throttle values once and rejects a third participant");

        var steering = session.Snapshot.Groups.Single(group => group.Group == AtomicControlGroup.Steering);
        Remote(new AuthorityRequest(AtomicProtocol.Version, new(session.Session, 2, ControlSelection.Quadrant, Epochs(ControlSelection.Quadrant), "atomic-B")));
        await Wait(() => session.Snapshot.Transfers.Length == 1, "mixed whole-quadrant request");
        transfer = session.Snapshot.Transfers.Single();
        Check(transfer.Next.Length == 3, "Whole quadrant was split into separate transactions");
        Check(await host.ApplyLocal(new AuthorityPreparation(AtomicProtocol.Version, new(session.Session, transfer.Transfer, true, false))), "Whole-quadrant host preparation");
        Remote(new AuthorityPreparation(AtomicProtocol.Version, new(session.Session, transfer.Transfer, true, true)));
        await Wait(() => session.Snapshot.Transfers.Length == 0, "whole-quadrant commit");
        Check(session.Snapshot.Groups.Where(group => group.Group != AtomicControlGroup.Steering).All(group => group.Owner == "atomic-B" && !group.Attached), "Whole-quadrant commit was partial or auto-attached");
        Remote(new AuthorityAttachment(AtomicProtocol.Version, new(session.Session, ControlSelection.Quadrant, Epochs(ControlSelection.Quadrant), true)));
        await Wait(() => session.Snapshot.Groups.Where(group => group.Group != AtomicControlGroup.Steering).All(group => group.Attached), "whole-quadrant confirmed attachment");
        await Wait(() => replica.Snapshot.Revision == session.Snapshot.Revision, "replica state convergence");
        Check(session.Snapshot.Groups.Single(group => group.Group == AtomicControlGroup.Steering) == steering, "Quadrant transfer changed steering");
        Remote(new AuthorityInput(AtomicProtocol.Version, frame with { Sequence = 2 })); await Task.Delay(100);
        Check(accepted.Count == 1, "Old throttle generation survived whole-quadrant transfer");
        // Negotiation can mutate the same authority before the network host's
        // peer callback runs. That external revision must still reach replicas.
        session.PeerDisconnected();
        await Wait(() => !replica.Snapshot.PeerConnected, "external negotiation revocation reaches replica");
        b.Disconnect();
        await Wait(() => !session.Snapshot.PeerConnected, "advanced peer disconnect");
        Check(session.Snapshot.Groups.Where(group => group.Group != AtomicControlGroup.Steering).All(group => group.Owner is null && !group.Attached), "Remote engine groups survived disconnect");
        Check(session.Snapshot.Groups.Single(group => group.Group == AtomicControlGroup.Steering) == steering, "Peer loss changed host steering");
        Console.WriteLine("PASS mixed whole-quadrant handover is atomic across real relay, preserves steering and revokes old epochs on disconnect");
    }
}
