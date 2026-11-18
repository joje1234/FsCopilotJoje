using FsCopilot.Network;
using FsCopilot.Ownership;

internal static class NegotiationNetworkTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> ready, string reason)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Check(ready(), "Negotiation timed out: " + reason);
    }
    public static async Task Run(int port)
    {
        using var a = new RelayNetwork("127.0.0.1", "neg-A", "A", false, port);
        using var b = new RelayNetwork("127.0.0.1", "neg-B", "B", false, port);
        using var c = new RelayNetwork("127.0.0.1", "neg-C", "C", false, port);
        var capable = 0;
        using var subscriptions = new CompositeDisposable();
        foreach (var network in new[] { a, b, c }) {
            OwnershipNetworkNegotiation.RegisterProtocol(network);
            subscriptions.Add(network.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref capable)));
        }
        await Wait(() => Volatile.Read(ref capable) == 3, "origin-capable relay registration");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Check(await a.Connect("neg-B", timeout.Token) == ConnectionResult.Success, "A/B link failed");
        Check(await c.Connect("neg-B", timeout.Token) == ConnectionResult.Success, "C/B link failed");
        var configuration = new SessionConfiguration("bksq-aircraft-baronpropress", 2, "baron58p-v1", 1, SessionNegotiation.RequiredCapabilities);
        var offers = new ConcurrentQueue<SessionOffer>();
        subscriptions.Add(b.StreamWithSource<SessionHandshakePacket>().Subscribe(packet => {
            if (packet.Sender == "neg-A" && SessionWire.TryDecode(packet.Packet.Payload, out var message) && message is SessionOffer offer) offers.Enqueue(offer);
        }));
        using var host = new OwnershipNetworkNegotiation(a, "neg-A", "neg-B", SessionRole.PhysicsHost, configuration, seat: CockpitSeat.CopilotRight);
        using var guest = new OwnershipNetworkNegotiation(b, "neg-B", "neg-A", SessionRole.Copilot, configuration, seat: CockpitSeat.PilotLeft);
        var hostBindings = new ConcurrentQueue<NegotiatedControlSession?>();
        var guestBindings = new ConcurrentQueue<NegotiatedControlSession?>();
        subscriptions.Add(host.Sessions.Subscribe(hostBindings.Enqueue));
        subscriptions.Add(guest.Sessions.Subscribe(guestBindings.Enqueue));
        await Wait(() => hostBindings.LastOrDefault() is not null && guestBindings.LastOrDefault() is not null, "two-sided negotiated session");
        var hostBinding = hostBindings.Last()!; var guestBinding = guestBindings.Last()!;
        var session = hostBinding.Host!;
        Check(session.Snapshot == guestBinding.Replica!.Snapshot, "Negotiated snapshots differ");
        Check(hostBinding.Seats == new CockpitSeats("neg-B", "neg-A") && guestBinding.Seats == hostBinding.Seats, "Seat selection did not agree independently of physics host");
        Check(SessionNegotiation.InitialSnapshot(session.Snapshot), "Negotiation attached controls");
        Console.WriteLine("PASS real relay negotiation agrees aircraft, bridge capabilities, independent seat roles and a fresh detached session");

        await Wait(() => !offers.IsEmpty, "captured authoritative offer");
        var originalOffer = offers.First();
        c.SendAll(new SessionHandshakePacket(SessionWire.Encode(originalOffer)));
        c.SendAll(new SessionHandshakePacket(SessionWire.Encode(new SessionEnd(1, originalOffer.HostAttempt, originalOffer.ClientAttempt, originalOffer.Initial.Session))));
        await Task.Delay(100);
        Check(ReferenceEquals(guestBindings.LastOrDefault(), guestBinding), "Third participant replaced or ended negotiated authority");
        Console.WriteLine("PASS a linked third participant cannot replay a host offer or terminate another participant's session");

        using var endpoint = new OwnershipNetworkHost(a, session);
        var accepted = new ConcurrentQueue<ControlFrame>();
        subscriptions.Add(endpoint.AcceptedInputs.Subscribe(accepted.Enqueue));
        Check(await endpoint.ApplyLocal(new AttachmentMessage(new(session.Session, ControlGroup.Steering, 1, true))), "Local steering attach failed");
        b.SendAll(new OwnershipPacket(OwnershipWire.Encode(new RequestMessage(new(session.Session, 1, ControlGroup.Quadrant, 1, "neg-B")))));
        await Wait(() => session.Snapshot.Quadrant.Pending is not null, "negotiated quadrant request");
        var transfer = session.Snapshot.Quadrant.Pending!;
        Check(await endpoint.ApplyLocal(new PreparationMessage(new(session.Session, ControlGroup.Quadrant, transfer.Transfer, true, true))), "Host mask acknowledgement failed");
        b.SendAll(new OwnershipPacket(OwnershipWire.Encode(new PreparationMessage(new(session.Session, ControlGroup.Quadrant, transfer.Transfer, true, true)))));
        await Wait(() => session.Snapshot.Quadrant.Owner == "neg-B", "negotiated transfer commit");
        b.SendAll(new OwnershipPacket(OwnershipWire.Encode(new AttachmentMessage(new(session.Session, ControlGroup.Quadrant, 2, true)))));
        await Wait(() => session.Snapshot.Quadrant.Attached, "negotiated attachment");
        var input = new ControlFrame(session.Session, ControlGroup.Quadrant, 2, 1, [new(ControlAxis.Throttle1, 0.6)]);
        b.SendAll(new OwnershipPacket(OwnershipWire.Encode(new InputMessage(input))));
        await Wait(() => accepted.Count == 1, "accepted input from negotiated peer");
        guest.Dispose();
        await Wait(() => hostBindings.LastOrDefault() is null && !session.Snapshot.CopilotConnected, "session termination revocation");
        Check(session.Snapshot.Quadrant.Owner is null && session.Snapshot.Steering.Owner == "neg-A", "Termination changed wrong group authority");
        b.SendAll(new OwnershipPacket(OwnershipWire.Encode(new InputMessage(input with { Sequence = 2 }))));
        await Task.Delay(100);
        Check(accepted.Count == 1, "Ended-session input was accepted");
        host.Dispose();
        var lateBinding = false;
        using var late = host.Sessions.Subscribe(binding => lateBinding |= binding is not null);
        Check(!lateBinding, "Disposed session replayed a live binding to a new subscriber");
        Console.WriteLine("PASS negotiated authority feeds the actual host endpoint; explicit end revokes remote input and clears cached bindings");

        using var freshHost = new OwnershipNetworkNegotiation(a, "neg-A", "neg-B", SessionRole.PhysicsHost, configuration);
        using var freshGuest = new OwnershipNetworkNegotiation(b, "neg-B", "neg-A", SessionRole.Copilot, configuration);
        var freshHostBindings = new ConcurrentQueue<NegotiatedControlSession?>();
        var freshGuestBindings = new ConcurrentQueue<NegotiatedControlSession?>();
        subscriptions.Add(freshHost.Sessions.Subscribe(freshHostBindings.Enqueue));
        subscriptions.Add(freshGuest.Sessions.Subscribe(freshGuestBindings.Enqueue));
        a.SendAll(new SessionHandshakePacket(SessionWire.Encode(originalOffer)));
        await Wait(() => freshHostBindings.LastOrDefault() is not null && freshGuestBindings.LastOrDefault() is not null, "fresh session after end");
        var freshSession = freshHostBindings.Last()!.Host!;
        Check(freshSession.Session != session.Session && SessionNegotiation.InitialSnapshot(freshSession.Snapshot), "Fresh negotiation revived old ownership");
        Check(freshHostBindings.Last()!.Seats == new CockpitSeats("neg-A", "neg-B"), "Fresh explicit seat choice was not applied");
        b.Disconnect();
        await Wait(() => !freshSession.Snapshot.CopilotConnected, "disconnect revokes the remote participant");
        Check(freshHostBindings.LastOrDefault()?.Host == freshSession, "Peer loss discarded the host recovery policy");
        Console.WriteLine("PASS fresh join rejects old offers, supports changed seats and preserves host recovery policy on peer loss");
    }
}
