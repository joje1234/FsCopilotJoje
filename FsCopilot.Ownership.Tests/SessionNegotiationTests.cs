using System.Text;
using FsCopilot.Ownership;

internal static class SessionNegotiationTests
{
    private static void Check(bool value, string reason = "Session negotiation assertion failed")
    { if (!value) throw new InvalidOperationException(reason); }
    internal static readonly SessionConfiguration Configuration = new("bksq-aircraft-baronpropress", 2, "baron58p-v1", 1, SessionNegotiation.RequiredCapabilities);
    private sealed class Pair
    {
        public TimeSpan Now;
        public SessionNegotiation Host { get; }
        public SessionNegotiation Guest { get; }
        public Pair(SessionConfiguration? guestConfig = null, CockpitSeat hostSeat = CockpitSeat.PilotLeft, CockpitSeat guestSeat = CockpitSeat.CopilotRight)
        {
            Host = new("host", "guest", SessionRole.PhysicsHost, Configuration, () => Now, seat: hostSeat);
            Guest = new("guest", "host", SessionRole.Copilot, guestConfig ?? Configuration, () => Now, seat: guestSeat);
            Host.Start();
        }
        public static SessionHandshake Wire(SessionHandshake message)
        {
            Check(SessionWire.TryDecode(SessionWire.Encode(message), out var decoded)); return decoded!;
        }
        public SessionOffer Offer() => (SessionOffer)Host.Receive("guest", Wire(Guest.Start()!))!;
        public SessionCommit Establish()
        {
            var offer = Offer(); Check(Host.HostSession is null && Guest.Replica is null);
            var accepted = Guest.Receive("host", Wire(offer))!;
            Check(Guest.State.Phase == NegotiationPhase.WaitingForCommit && Guest.Replica is null);
            var commit = (SessionCommit)Host.Receive("guest", Wire(accepted))!;
            Guest.Receive("host", Wire(commit)); return commit;
        }
    }
    public static int Run()
    {
        var count = 0;
        void Test(string name, Action test) { test(); ++count; Console.WriteLine("PASS " + name); }
        Test("negotiation pins two identities/configuration; all groups remain detached", () => {
            var p = new Pair(); p.Establish();
            var state = p.Host.HostSession!.Snapshot;
            Check(p.Guest.Replica!.Snapshot == state && state.PhysicsHost == "host");
            Check(SessionNegotiation.InitialSnapshot(state));
            Check(p.Host.Seats == new CockpitSeats("host", "guest") && p.Guest.Seats == p.Host.Seats);
            Check(!p.Host.HostSession.AcceptInput("host", new(state.Session, ControlGroup.Steering, 1, 1, [new(ControlAxis.Roll, 0.5)])));
        });
        Test("physics host may sit right while the joining participant chooses pilot/left", () => {
            var p = new Pair(hostSeat: CockpitSeat.CopilotRight, guestSeat: CockpitSeat.PilotLeft); p.Establish();
            Check(p.Host.HostSession!.Snapshot.PhysicsHost == "host" && p.Host.Seats == new CockpitSeats("guest", "host"));
        });
        Test("duplicate seat choice is rejected without transferring or attaching controls", () => {
            var p = new Pair(guestSeat: CockpitSeat.PilotLeft);
            var reject = p.Host.Receive("guest", Pair.Wire(p.Guest.Start()!))!;
            p.Guest.Receive("host", Pair.Wire(reject));
            Check(p.Host.State.Phase == NegotiationPhase.Rejected && p.Guest.State.Phase == NegotiationPhase.Rejected);
            Check(p.Host.HostSession is null && p.Guest.Replica is null);
        });
        Test("aircraft/engine/adapter/native protocol mismatches fail explicitly", () => {
            foreach (var config in new[] { Configuration with { Aircraft = "different-plane" }, Configuration with { Engines = 1 },
                Configuration with { Adapter = "different-v1" }, Configuration with { NativeProtocol = 2 } }) {
                var p = new Pair(config); var reject = p.Host.Receive("guest", Pair.Wire(p.Guest.Start()!))!;
                p.Guest.Receive("host", Pair.Wire(reject));
                Check(p.Host.State.Phase == NegotiationPhase.Rejected && p.Guest.State.Phase == NegotiationPhase.Rejected);
            }
        });
        Test("unknown senders, wrong members, fabricated offers and early commits cannot establish", () => {
            var p = new Pair(); var hello = p.Guest.Start()!;
            Check(p.Host.Receive("third", Pair.Wire(hello)) is null && p.Host.State.Phase == NegotiationPhase.WaitingForPeer);
            var offer = (SessionOffer)p.Host.Receive("guest", Pair.Wire(hello))!;
            Check(p.Guest.Receive("third", Pair.Wire(offer)) is null);
            Check(p.Guest.Receive("host", Pair.Wire(offer with { ClientAttempt = Guid.NewGuid() })) is null);
            Check(p.Guest.Receive("host", new SessionCommit(1, offer.HostAttempt, offer.ClientAttempt, offer.Initial.Session)) is null);
            Check(p.Guest.Replica is null);
            var accepted = (SessionAcceptance)p.Guest.Receive("host", Pair.Wire(offer))!;
            Check(p.Host.Receive("guest", accepted with { HostAttempt = Guid.NewGuid() }) is null);
            Check(p.Host.Receive("third", accepted) is null && p.Host.HostSession is null);
        });
        Test("duplicate paths and lost commit retry preserve one session and current replica state", () => {
            var p = new Pair(); var offer = p.Offer();
            Check(p.Host.Receive("guest", Pair.Wire(p.Guest.Tick()!)) == offer);
            var accept = p.Guest.Receive("host", Pair.Wire(offer))!;
            var commit = p.Host.Receive("guest", Pair.Wire(accept))!; // Drop first commit.
            Check(p.Guest.Replica is null);
            Check(p.Host.Receive("guest", Pair.Wire(p.Guest.Tick()!)) == commit);
            p.Guest.Receive("host", Pair.Wire(commit));
            var replica = p.Guest.Replica!;
            p.Host.HostSession!.AcknowledgeAttachment("host", new(offer.Initial.Session, ControlGroup.Steering, 1, true));
            Check(replica.Receive("host", p.Host.HostSession.Snapshot));
            p.Guest.Receive("host", Pair.Wire(offer)); p.Guest.Receive("host", Pair.Wire(commit));
            Check(ReferenceEquals(replica, p.Guest.Replica) && replica.Snapshot.Steering.Attached);
        });
        Test("deadline is not extended by duplicate packets; timed-out session cannot revive", () => {
            var p = new Pair(); var offer = p.Offer(); var accept = p.Guest.Receive("host", Pair.Wire(offer))!;
            p.Now = TimeSpan.FromSeconds(9); p.Host.Receive("guest", Pair.Wire(p.Guest.Tick()!));
            // Host established, but guest never received commit. Guest timeout ends host agreement.
            p.Now = TimeSpan.FromSeconds(10); var end = p.Guest.Tick()!;
            Check(p.Guest.State.Phase == NegotiationPhase.TimedOut);
            p.Host.Receive("guest", Pair.Wire(end));
            Check(p.Host.State.Phase == NegotiationPhase.Closed && p.Host.HostSession is null);
            Check(p.Guest.Receive("host", new SessionCommit(1, offer.HostAttempt, offer.ClientAttempt, offer.Initial.Session)) is null);
        });
        Test("fresh join rejects old offers/commits/end messages and creates new authority", () => {
            var old = new Pair(); var commit = old.Establish(); var end = old.Host.End()!;
            var fresh = new Pair(); var offer = fresh.Offer();
            Check(fresh.Guest.Receive("host", commit) is null && fresh.Guest.Replica is null);
            Check(fresh.Guest.Receive("host", end) is null && fresh.Guest.State.Phase == NegotiationPhase.WaitingForOffer);
            var accept = fresh.Guest.Receive("host", Pair.Wire(offer))!;
            Check(fresh.Host.Receive("guest", new SessionAcceptance(1, commit.HostAttempt, commit.ClientAttempt, commit.Session)) is null);
            var freshCommit = fresh.Host.Receive("guest", Pair.Wire(accept))!;
            fresh.Guest.Receive("host", Pair.Wire(freshCommit));
            Check(fresh.Guest.Replica!.Snapshot.Session != commit.Session);
        });
        Test("wire rejects malformed, oversized, unknown version, missing capabilities and attached initial snapshots", () => {
            var p = new Pair(); var offer = p.Offer();
            Check(!SessionWire.Valid(offer with { Initial = offer.Initial with { Steering = offer.Initial.Steering with { Attached = true } } }));
            Check(!SessionWire.Valid(offer with { Seats = new("host", "host") }));
            Check(!SessionWire.Valid(new SessionHello(1, Guid.NewGuid(), Configuration with { Capabilities = ControlCapabilities.ConfirmedMasks }, CockpitSeat.CopilotRight)));
            foreach (var bad in new[] { "{}", "null", "[]", "{\"kind\":\"unknown\"}", "{\"kind\":\"hello\",\"version\":99}" })
                Check(!SessionWire.TryDecode(Encoding.UTF8.GetBytes(bad), out _));
            Check(!SessionWire.TryDecode(new byte[SessionWire.MaxBytes + 1], out _));
            var json = Encoding.UTF8.GetString(SessionWire.Encode(offer));
            Check(!SessionWire.TryDecode(Encoding.UTF8.GetBytes(json[..^1] + ",\"unexpected\":1}"), out _));
        });
        Test("advanced handshake establishes four groups and configured pedal axes with independent seats", () => {
            var config = Configuration with { NativeProtocol = 2, Capabilities = SessionNegotiation.AdvancedCapabilities, SteeringAxes = 0x37 };
            var host = new SessionNegotiation("host", "guest", SessionRole.PhysicsHost, config, () => TimeSpan.Zero, seat: CockpitSeat.CopilotRight);
            var guest = new SessionNegotiation("guest", "host", SessionRole.Copilot, config, () => TimeSpan.Zero, seat: CockpitSeat.PilotLeft);
            host.Start(); var hello = guest.Start()!;
            var offer = (AtomicSessionOffer)host.Receive("guest", Pair.Wire(hello))!;
            Check(!offer.Initial.Automatic);
            Check(!SessionWire.Valid(offer with { Initial = offer.Initial with { Automatic = true } }));
            Check(host.AuthoritySession is null && host.HostSession is null && guest.AuthorityReplica is null);
            var accepted = guest.Receive("host", Pair.Wire(offer))!;
            var commit = host.Receive("guest", Pair.Wire(accepted))!;
            guest.Receive("host", Pair.Wire(commit));
            Check(host.AuthoritySession!.Snapshot.Groups.Length == 4 && guest.AuthorityReplica!.Snapshot.SteeringAxes == 0x37);
            Check(host.HostSession is null && guest.Replica is null && host.Seats == new CockpitSeats("guest", "host"));
            var replica = guest.AuthorityReplica;
            guest.Receive("host", Pair.Wire(offer)); guest.Receive("host", Pair.Wire(commit));
            Check(ReferenceEquals(replica, guest.AuthorityReplica));
            var end = guest.End()!; host.Receive("guest", Pair.Wire(end));
            Check(host.AuthoritySession is null && guest.AuthorityReplica is null);
        });
        Test("advanced capability mismatch and disguised legacy offers cannot silently downgrade", () => {
            var config = Configuration with { NativeProtocol = 2, Capabilities = SessionNegotiation.AdvancedCapabilities };
            var host = new SessionNegotiation("host", "guest", SessionRole.PhysicsHost, Configuration, () => TimeSpan.Zero);
            var guest = new SessionNegotiation("guest", "host", SessionRole.Copilot, config, () => TimeSpan.Zero);
            host.Start(); var rejected = host.Receive("guest", Pair.Wire(guest.Start()!))!;
            guest.Receive("host", Pair.Wire(rejected));
            Check(host.State.Phase == NegotiationPhase.Rejected && guest.State.Phase == NegotiationPhase.Rejected);
            var p = new Pair(); var legacy = p.Offer();
            Check(!SessionWire.Valid(legacy with { Configuration = config }));
            var initial = new ControlAuthoritySession("host", "guest", 2, () => TimeSpan.Zero).Snapshot;
            Check(!SessionWire.Valid(new AtomicSessionOffer(1, Guid.NewGuid(), Guid.NewGuid(), Configuration, initial, new("host", "guest"))));
        });
        Test("peer loss preserves host input authority without reviving a disconnected peer", () => {
            var p = new Pair(); p.Establish(); var session = p.Host.HostSession!;
            Check(session.AcknowledgeAttachment("host", new(session.Session, ControlGroup.Steering, 1, true)));
            p.Host.PeerDisconnected(); p.Guest.PeerDisconnected();
            Check(p.Host.State.Phase == NegotiationPhase.PeerLost && p.Host.HostSession == session && p.Guest.Replica is null);
            Check(session.Snapshot.Steering.Attached && !session.Snapshot.CopilotConnected);
            Check(session.AcceptInput("host", new(session.Session, ControlGroup.Steering, 1, 1, [new(ControlAxis.Roll, 0.5)])));
            Check(p.Host.Receive("guest", new SessionHello(1, Guid.NewGuid(), Configuration, CockpitSeat.CopilotRight)) is null);
            p.Host.End(); Check(p.Host.HostSession is null && !session.Snapshot.Steering.Attached);
        });
        return count;
    }
}
