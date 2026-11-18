using System.Text;
using FsCopilot.Ownership;

internal static class SeatAuthorityTests
{
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Seat authority assertion failed."); }
    public static int Run()
    {
        var count = 0;
        void Test(string name, Action run) { run(); ++count; Console.WriteLine("PASS " + name); }
        Test("seat swap requires the other participant and preserves queued controls", () => {
            var host = new ControlAuthoritySession("host", "peer", 2, () => TimeSpan.Zero);
            var endpoint = new ControlAuthorityEndpoint(host);
            var replica = new ControlAuthorityReplica("host", host.Snapshot);
            bool Send(string who, ulong sequence, SeatChangeAction action, ulong ticket = 0, ulong? revision = null) {
                var bytes = AuthorityWire.Encode(new AuthoritySeatChange(AtomicProtocol.Version,
                    new(host.Session, sequence, revision ?? host.Snapshot.SeatRevision, action, ticket)));
                var applied = endpoint.Receive(who, bytes, out var changed, out _);
                if (changed is not null) Check(replica.Receive("host", changed));
                return applied;
            }
            var steering = host.Snapshot.Groups[0];
            Check(host.AcknowledgeAttachment("host", new(host.Session, ControlSelection.Steering, [new(steering.Group, steering.Generation)], true)));
            var frame = new AtomicControlFrame(host.Session, AtomicControlGroup.Steering, 1, 1,
                [new(ControlAxis.Roll, .25), new(ControlAxis.Pitch, .75)]);
            Check(host.AcceptInput("host", frame));
            var groups = host.Snapshot.Groups.ToArray();
            Check(Send("peer", 1, SeatChangeAction.Request));
            var pending = host.Snapshot.SeatChange!;
            Check(host.Snapshot.Seats.PilotPeer == "host" && pending.RequestedBy == "peer");
            Check(!Send("peer", 2, SeatChangeAction.Accept, pending.Ticket));
            Check(Send("host", 1, SeatChangeAction.Accept, pending.Ticket));
            Check(host.Snapshot.Seats == new CockpitSeats("peer", "host") && host.Snapshot.SeatChange is null);
            Check(host.Snapshot.Groups.SequenceEqual(groups) && host.Snapshot.PhysicsHost == "host");
            var writes = 0;
            Check(host.TryApplyAcceptedInput("host", frame, _ => ++writes) && writes == 1);
            Check(replica.Snapshot.Seats == host.Snapshot.Seats && AtomicProtocol.ValidSnapshot(host.Snapshot));
        });
        Test("seat requests reject strangers, stale revisions, stale tickets and repeated sequences", () => {
            var host = new ControlAuthoritySession("host", "peer", 2, () => TimeSpan.Zero);
            SeatChange Request(ulong sequence = 1) => new(host.Session, sequence, host.Snapshot.SeatRevision, SeatChangeAction.Request, 0);
            Check(!host.ChangeSeat("stranger", Request()));
            Check(!host.ChangeSeat("peer", Request() with { Session = Guid.NewGuid() }));
            Check(host.ChangeSeat("peer", Request()));
            var pending = host.Snapshot.SeatChange!;
            Check(!host.ChangeSeat("host", new(host.Session, 1, 1, SeatChangeAction.Accept, pending.Ticket)));
            Check(!host.ChangeSeat("host", new(host.Session, 2, 2, SeatChangeAction.Accept, pending.Ticket + 1)));
            Check(!host.ChangeSeat("peer", Request(1)));
            Check(!host.ChangeSeat("host", new(host.Session, 3, 2, SeatChangeAction.Cancel, pending.Ticket)));
            Check(host.ChangeSeat("host", new(host.Session, 4, 2, SeatChangeAction.Decline, pending.Ticket)));
            Check(host.Snapshot.Seats.PilotPeer == "host" && host.Snapshot.SeatChange is null);
            Check(!host.ChangeSeat("host", new(host.Session, 5, 2, SeatChangeAction.Accept, pending.Ticket)));
        });
        Test("seat timeout, requester cancellation and disconnect retain the seats", () => {
            var now = TimeSpan.Zero;
            var host = new ControlAuthoritySession("host", "peer", 2, () => now, hostSeat: CockpitSeat.CopilotRight);
            Check(host.Snapshot.Seats == new CockpitSeats("peer", "host"));
            Check(host.ChangeSeat("host", new(host.Session, 1, 1, SeatChangeAction.Request, 0)));
            var first = host.Snapshot.SeatChange!;
            now = TimeSpan.FromSeconds(15);
            Check(host.ExpireTransfers() && host.Snapshot.SeatChange is null && host.Snapshot.SeatRevision == 3);
            Check(!host.ChangeSeat("peer", new(host.Session, 1, 2, SeatChangeAction.Accept, first.Ticket)));
            Check(host.ChangeSeat("host", new(host.Session, 2, 3, SeatChangeAction.Request, 0)));
            var second = host.Snapshot.SeatChange!;
            Check(second.Ticket != first.Ticket && host.ChangeSeat("host", new(host.Session, 3, 4, SeatChangeAction.Cancel, second.Ticket)));
            Check(host.ChangeSeat("peer", new(host.Session, 2, 5, SeatChangeAction.Request, 0)));
            host.PeerDisconnected();
            Check(host.Snapshot.SeatChange is null && host.Snapshot.Seats == new CockpitSeats("peer", "host"));
            Check(!host.ChangeSeat("host", new(host.Session, 4, host.Snapshot.SeatRevision, SeatChangeAction.Request, 0)));
            Check(AtomicProtocol.ValidSnapshot(host.Snapshot));
        });
        Test("seat replicas reject rollback and conflicting assignments", () => {
            var host = new ControlAuthoritySession("host", "peer", 2, () => TimeSpan.Zero);
            var initial = host.Snapshot;
            var replica = new ControlAuthorityReplica("host", initial);
            Check(!AtomicProtocol.ValidSnapshot(initial with { Seats = new("host", "host") }));
            Check(!replica.Receive("host", initial with { Revision = 2, Seats = new("peer", "host") }));
            Check(host.ChangeSeat("host", new(host.Session, 1, 1, SeatChangeAction.Request, 0)));
            Check(replica.Receive("host", host.Snapshot));
            Check(!replica.Receive("host", initial with { Revision = 20 }));
            Check(!AuthorityWire.TryDecode(Encoding.UTF8.GetBytes("{\"kind\":\"seat\",\"Version\":3}"), out _));
        });
        return count;
    }
}
