using System.Text;
using FsCopilot.Ownership;

internal static class AtomicAuthorityTests
{
    private static void Check(bool value, string message = "Advanced group assertion failed") { if (!value) throw new InvalidOperationException(message); }
    private sealed class Room
    {
        public TimeSpan Now;
        public readonly ControlAuthoritySession Host;
        public readonly ControlAuthorityEndpoint Endpoint;
        public readonly ControlAuthorityReplica Replica;
        public Room(uint steering = 3) {
            Host = new("host", "peer", 2, () => Now, steering); Endpoint = new(Host); Replica = new("host", Host.Snapshot);
        }
        public AtomicGroupState Group(AtomicControlGroup group) => Host.Snapshot.Groups.Single(value => value.Group == group);
        public GroupEpoch[] Epochs(ControlSelection selection) => Host.Snapshot.Groups.Where(group => (selection & AtomicProtocol.Select(group.Group)) != 0)
            .Select(group => new GroupEpoch(group.Group, group.Generation)).ToArray();
        public bool Send(string sender, AuthorityMessage message) {
            Check(AuthorityWire.TryDecode(AuthorityWire.Encode(message), out _));
            var result = Endpoint.Receive(sender, AuthorityWire.Encode(message), out var changed, out _);
            if (changed is not null) Check(Replica.Receive("host", changed));
            Check(AtomicProtocol.ValidSnapshot(Host.Snapshot)); return result;
        }
        public void Attach(string owner, ControlSelection selection) => Check(Send(owner, new AuthorityAttachment(AtomicProtocol.Version, new(Host.Session, selection, Epochs(selection), true))));
        public Guid Request(ControlSelection selection, ulong request = 1, string owner = "peer", string? sender = null) {
            Check(Send(sender ?? owner, new AuthorityRequest(AtomicProtocol.Version, new(Host.Session, request, selection, Epochs(selection), owner))));
            return Host.Snapshot.Transfers.Single(value => value.Request == request && value.RequestedBy == (sender ?? owner)).Transfer;
        }
        public void Commit(Guid id) {
            Check(Send("host", new AuthorityPreparation(AtomicProtocol.Version, new(Host.Session, id, true, true))));
            Check(Send("peer", new AuthorityPreparation(AtomicProtocol.Version, new(Host.Session, id, true, true))));
        }
        public void Transfer(ControlSelection selection, ulong request = 1, string owner = "peer") { Commit(Request(selection, request, owner)); Attach(owner, selection); }
        public AtomicControlFrame Frame(AtomicControlGroup group, ulong sequence = 1) {
            var required = AtomicProtocol.RequiredAxes(group, 2, Host.Snapshot.SteeringAxes);
            return new(Host.Session, group, Group(group).Generation, sequence,
                Enum.GetValues<ControlAxis>().Where(axis => (required & (1u << (int)axis)) != 0).Select(axis => new AxisPosition(axis, 0.5)).ToArray());
        }
    }
    public static int Run()
    {
        var count = 0;
        void Test(string name, Action run) { run(); ++count; Console.WriteLine("PASS " + name); }
        Test("advanced authority starts with four detached groups and fixed physics host", () => {
            var r = new Room(); Check(r.Host.Snapshot.Groups.Length == 4);
            foreach (var group in r.Host.Snapshot.Groups) Check(group.Owner == "host" && !group.Attached && group.Generation == 1);
            Check(!r.Host.AcceptInput("host", r.Frame(AtomicControlGroup.Throttle)));
        });
        Test("throttle pair transfers independently of RPM, mixtures and steering", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All);
            var unchanged = r.Host.Snapshot.Groups.Where(group => group.Group != AtomicControlGroup.Throttle).ToArray();
            r.Transfer(ControlSelection.Throttle);
            Check(r.Group(AtomicControlGroup.Throttle).Owner == "peer");
            Check(r.Host.Snapshot.Groups.Where(group => group.Group != AtomicControlGroup.Throttle).SequenceEqual(unchanged));
            Check(r.Send("peer", new AuthorityInput(AtomicProtocol.Version, r.Frame(AtomicControlGroup.Throttle))));
            Check(r.Send("host", new AuthorityInput(AtomicProtocol.Version, r.Frame(AtomicControlGroup.Propeller))));
            Check(!r.Send("host", new AuthorityInput(AtomicProtocol.Version, r.Frame(AtomicControlGroup.Throttle, 2))));
        });
        Test("whole-quadrant request from mixed ownership commits all three pairs atomically", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All); r.Transfer(ControlSelection.Throttle);
            var steering = r.Group(AtomicControlGroup.Steering); var id = r.Request(ControlSelection.Quadrant, 2);
            Check(r.Host.Snapshot.Transfers.Length == 1 && r.Host.Snapshot.Transfers[0].Next.Length == 3);
            foreach (var group in new[] { AtomicControlGroup.Throttle, AtomicControlGroup.Propeller, AtomicControlGroup.Mixture }) {
                Check(!r.Group(group).Attached && r.Group(group).Pending == id);
                Check(!r.Host.AcceptInput(r.Group(group).Owner!, r.Frame(group, 4)));
            }
            Check(r.Send("host", new AuthorityPreparation(AtomicProtocol.Version, new(r.Host.Session, id, true, false))));
            Check(!r.Send("host", new AuthorityPreparation(AtomicProtocol.Version, new(r.Host.Session, id, true, true))));
            Check(r.Group(AtomicControlGroup.Propeller).Owner == "host");
            Check(!r.Send("peer", new AuthorityPreparation(AtomicProtocol.Version, new(r.Host.Session, id, true, false))));
            Check(r.Send("peer", new AuthorityPreparation(AtomicProtocol.Version, new(r.Host.Session, id, true, true))));
            foreach (var group in AtomicProtocol.Members(ControlSelection.Quadrant)) Check(r.Group(group).Owner == "peer" && !r.Group(group).Attached);
            r.Attach("peer", ControlSelection.Quadrant);
            Check(r.Group(AtomicControlGroup.Steering) == steering && r.Host.Snapshot.PhysicsHost == "host");
        });
        Test("overlapping requests cannot partially stage or steal a pending engine pair", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All); r.Request(ControlSelection.Mixture);
            var before = r.Host.Snapshot;
            Check(!r.Send("peer", new AuthorityRequest(AtomicProtocol.Version, new(r.Host.Session, 2, ControlSelection.Quadrant, r.Epochs(ControlSelection.Quadrant), "peer"))));
            Check(r.Host.Snapshot.Groups.SequenceEqual(before.Groups) && r.Host.Snapshot.Revision == before.Revision);
            r.Request(ControlSelection.Throttle, 3); Check(r.Host.Snapshot.Transfers.Length == 2);
        });
        Test("stale generation in one pair rejects an entire quadrant transaction", () => {
            var r = new Room(); var epochs = r.Epochs(ControlSelection.Quadrant); r.Transfer(ControlSelection.Propeller);
            var before = r.Host.Snapshot;
            Check(!r.Send("peer", new AuthorityRequest(AtomicProtocol.Version, new(r.Host.Session, 2, ControlSelection.Quadrant, epochs, "peer"))));
            Check(r.Host.Snapshot.Revision == before.Revision && r.Host.Snapshot.Transfers.Length == 0);
        });
        Test("both engines of a pair and all configured pedals are required in each frame", () => {
            var r = new Room(0x37); r.Attach("host", ControlSelection.All);
            foreach (var group in Enum.GetValues<AtomicControlGroup>()) {
                var frame = r.Frame(group);
                Check(!r.Host.AcceptInput("host", frame with { Axes = frame.Axes[..^1] }));
                Check(r.Host.AcceptInput("host", frame));
            }
            Check(r.Frame(AtomicControlGroup.Steering).Axes.Length == 5);
            var throttle = r.Frame(AtomicControlGroup.Throttle, 2);
            Check(!r.Host.AcceptInput("host", throttle with { Axes = [new(ControlAxis.Throttle1, 0.2), new(ControlAxis.Mixture2, 0.8)] }));
            Check(!r.Host.AcceptInput("host", throttle with { Axes = [new(ControlAxis.Throttle1, double.NaN), new(ControlAxis.Throttle2, 0.8)] }));
            Check(r.Host.AcceptInput("host", throttle)); // Invalid data did not consume its sequence.
        });
        Test("pair sequences are independent; old or already-applied queued samples cannot execute", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All);
            var older = r.Frame(AtomicControlGroup.Throttle, 1); var newer = r.Frame(AtomicControlGroup.Throttle, 2); var rpm = r.Frame(AtomicControlGroup.Propeller, 1);
            Check(r.Host.AcceptInput("host", older) && r.Host.AcceptInput("host", newer) && r.Host.AcceptInput("host", rpm));
            var writes = 0;
            Check(!r.Host.TryApplyAcceptedInput("host", older, _ => ++writes));
            Check(r.Host.TryApplyAcceptedInput("host", newer, _ => ++writes));
            Check(!r.Host.TryApplyAcceptedInput("host", newer, _ => ++writes));
            Check(r.Host.TryApplyAcceptedInput("host", rpm, _ => ++writes) && writes == 2);
            var queued = r.Frame(AtomicControlGroup.Mixture); Check(r.Host.AcceptInput("host", queued)); r.Request(ControlSelection.Mixture);
            Check(!r.Host.TryApplyAcceptedInput("host", queued, _ => ++writes));
        });
        Test("simulator writer receives the accepted values despite mutable queue and caller arrays", () => {
            var r = new Room(); r.Attach("host", ControlSelection.Throttle);
            var frame = r.Frame(AtomicControlGroup.Throttle) with { Axes = [new(ControlAxis.Throttle1, 0.2), new(ControlAxis.Throttle2, 0.8)] };
            Check(r.Host.AcceptInput("host", frame));
            frame.Axes[0] = new(ControlAxis.Throttle1, 0.9);
            Check(!r.Host.TryApplyAcceptedInput("host", frame, _ => throw new Exception("Mutated queue reached writer")));
            var faithful = frame with { Axes = [new(ControlAxis.Throttle2, 0.8), new(ControlAxis.Throttle1, 0.2)] };
            Check(r.Host.TryApplyAcceptedInput("host", faithful, sample => {
                Check(sample.Axes.Single(axis => axis.Axis == ControlAxis.Throttle1).Position == 0.2);
                Check(sample.Axes.Single(axis => axis.Axis == ControlAxis.Throttle2).Position == 0.8);
                Check(!ReferenceEquals(sample.Axes, frame.Axes) && !ReferenceEquals(sample.Axes, faithful.Axes));
                sample.Axes[0] = new(ControlAxis.Throttle1, 1); // Callback cannot mutate retained sample.
            }));
            Check(!r.Host.TryApplyAcceptedInput("host", faithful, _ => throw new Exception("Duplicate writer")));
            Check(r.Host.AcceptInput("host", faithful with { Sequence = 2 }));
            Check(r.Host.TryApplyAcceptedInput("host", faithful with { Sequence = 2 }, sample => Check(sample.Axes.Single(axis => axis.Axis == ControlAxis.Throttle1).Position == 0.2)));
        });
        Test("a throwing native writer cannot replay a partially applied control frame", () => {
            var r = new Room(); r.Attach("host", ControlSelection.Throttle); var frame = r.Frame(AtomicControlGroup.Throttle);
            Check(r.Host.AcceptInput("host", frame));
            var writes = 0;
            try {
                r.Host.TryApplyAcceptedInput("host", frame, _ => { ++writes; throw new IOException("Native writer failed after its first event"); });
                throw new Exception("Expected native writer failure");
            }
            catch (IOException) { }
            Check(!r.Host.TryApplyAcceptedInput("host", frame, _ => ++writes) && writes == 1);
        });
        Test("throttle can hand over and back while RPM and mixture retain one unchanged owner", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All);
            var rpm = r.Group(AtomicControlGroup.Propeller); var mixture = r.Group(AtomicControlGroup.Mixture);
            r.Transfer(ControlSelection.Throttle);
            var peerFrame = r.Frame(AtomicControlGroup.Throttle);
            Check(!r.Host.AcceptInput("host", peerFrame) && r.Host.AcceptInput("peer", peerFrame));
            r.Transfer(ControlSelection.Throttle, 1, "host");
            Check(!r.Host.TryApplyAcceptedInput("peer", peerFrame, _ => throw new Exception("Old owner wrote after handover")));
            Check(!r.Host.AcceptInput("peer", peerFrame with { Sequence = 2 }));
            var hostFrame = r.Frame(AtomicControlGroup.Throttle);
            Check(!r.Host.AcceptInput("peer", hostFrame) && r.Host.AcceptInput("host", hostFrame));
            var writes = 0; Check(r.Host.TryApplyAcceptedInput("host", hostFrame, _ => ++writes) && writes == 1);
            Check(r.Group(AtomicControlGroup.Propeller) == rpm && r.Group(AtomicControlGroup.Mixture) == mixture);
        });
        Test("cancel and timeout detach every selected pair without changing their owners", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All); var id = r.Request(ControlSelection.Quadrant);
            Check(r.Send("host", new AuthorityCancellation(AtomicProtocol.Version, new(r.Host.Session, id))));
            foreach (var group in AtomicProtocol.Members(ControlSelection.Quadrant)) Check(r.Group(group).Owner == "host" && !r.Group(group).Attached && r.Group(group).Generation == 2);
            r.Attach("host", ControlSelection.Quadrant); r.Request(ControlSelection.Quadrant, 2);
            r.Now = TimeSpan.FromSeconds(5); Check(r.Host.ExpireTransfers());
            Check(r.Host.Snapshot.Transfers.Length == 0 && r.Group(AtomicControlGroup.Steering).Attached);
        });
        Test("peer loss revokes only affected pairs and explicit recovery stays detached", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All); r.Transfer(ControlSelection.Throttle); r.Transfer(ControlSelection.Mixture, 2);
            var steering = r.Group(AtomicControlGroup.Steering); var rpm = r.Group(AtomicControlGroup.Propeller);
            r.Host.PeerDisconnected();
            Check(r.Group(AtomicControlGroup.Throttle).Owner is null && r.Group(AtomicControlGroup.Mixture).Owner is null);
            Check(r.Group(AtomicControlGroup.Steering) == steering && r.Group(AtomicControlGroup.Propeller) == rpm);
            Check(r.Host.RecoverLocal(ControlSelection.Quadrant));
            foreach (var group in AtomicProtocol.Members(ControlSelection.Quadrant)) Check(r.Group(group).Owner == "host" && !r.Group(group).Attached);
            Check(AtomicProtocol.ValidSnapshot(r.Host.Snapshot));
        });
        Test("session end rejects queued host commands, reattachment and local revival", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All); var frame = r.Frame(AtomicControlGroup.Throttle); Check(r.Host.AcceptInput("host", frame));
            r.Host.End();
            Check(!r.Host.TryApplyAcceptedInput("host", frame, _ => throw new Exception("Stale writer")));
            Check(!r.Host.RecoverLocal(ControlSelection.All));
            Check(!r.Host.AcknowledgeAttachment("host", new(r.Host.Session, ControlSelection.All, r.Epochs(ControlSelection.All), true)));
            Check(AtomicProtocol.ValidSnapshot(r.Host.Snapshot));
        });
        Test("replicas reject changed members, owner changes without epochs and altered snapshots", () => {
            var r = new Room(); var initial = r.Host.Snapshot; r.Transfer(ControlSelection.Throttle);
            Check(!r.Replica.Receive("peer", r.Host.Snapshot with { Revision = 999 }));
            Check(!r.Replica.Receive("host", initial));
            Check(!r.Replica.Receive("host", r.Host.Snapshot with { Revision = 999, Session = Guid.NewGuid() }));
            var stolen = r.Host.Snapshot; stolen.Groups[0] = stolen.Groups[0] with { Owner = "peer" };
            Check(!r.Replica.Receive("host", stolen with { Revision = 999 }));
            var copy = r.Replica.Snapshot; copy.Groups[0] = copy.Groups[0] with { Owner = "peer" };
            Check(r.Replica.Snapshot.Groups[0].Owner == "host");
        });
        Test("advanced wire rejects invalid masks, incomplete epochs, old versions and malformed data", () => {
            var r = new Room();
            Check(!AuthorityWire.Valid(new AuthorityRequest(AtomicProtocol.Version, new(r.Host.Session, 1, ControlSelection.Quadrant, r.Epochs(ControlSelection.Throttle), "peer"))));
            Check(!AuthorityWire.Valid(new AuthorityInput(1, r.Frame(AtomicControlGroup.Throttle))));
            Check(!AuthorityWire.Valid(new AuthorityRequest(AtomicProtocol.Version, new(r.Host.Session, 1, (ControlSelection)32, [], "peer"))));
            foreach (var json in new[] { "{}", "null", "[]", "{\"kind\":\"input\",\"version\":1}", "{\"kind\":\"unknown\",\"version\":2}" })
                Check(!AuthorityWire.TryDecode(Encoding.UTF8.GetBytes(json), out _));
            Check(!AuthorityWire.TryDecode(new byte[AuthorityWire.MaxBytes + 1], out _));
            var state = r.Host.Snapshot; Check(AuthorityWire.TryDecode(AuthorityWire.Encode(new AuthorityState(AtomicProtocol.Version, state)), out _));
        });
        Test("committed transfer identity distinguishes same-owner commit from cancellation", () => {
            var committed = new Room(); var commitId = committed.Request(ControlSelection.Throttle, owner: "host"); committed.Commit(commitId);
            var group = committed.Group(AtomicControlGroup.Throttle);
            Check(group.Owner == "host" && group.Generation == 2 && !group.Attached && group.CommittedTransfer == commitId);
            Check(AuthorityWire.TryDecode(AuthorityWire.Encode(new AuthorityState(AtomicProtocol.Version, committed.Host.Snapshot)), out var decoded));
            Check(((AuthorityState)decoded!).State.Groups.Single(value => value.Group == AtomicControlGroup.Throttle).CommittedTransfer == commitId);
            var canceled = new Room(); var cancelId = canceled.Request(ControlSelection.Throttle, owner: "host");
            Check(canceled.Send("host", new AuthorityCancellation(AtomicProtocol.Version, new(canceled.Host.Session, cancelId))));
            var canceledGroup = canceled.Group(AtomicControlGroup.Throttle);
            Check(canceledGroup.Owner == group.Owner && canceledGroup.Generation == group.Generation && canceledGroup.CommittedTransfer is null);
            var altered = committed.Host.Snapshot;
            altered.Groups[1] = group with { CommittedTransfer = Guid.NewGuid() };
            Check(!committed.Replica.Receive("host", altered with { Revision = altered.Revision + 1 }));
        });
        Test("Force is explicit, self-owned and still needs both suppression and attachment proofs", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All);
            var request = new AtomicTransferRequest(r.Host.Session, 1, ControlSelection.Throttle, r.Epochs(ControlSelection.Throttle), "peer") { Force = true };
            Check(!r.Send("host", new AuthorityRequest(AtomicProtocol.Version, request)));
            Check(r.Send("peer", new AuthorityRequest(AtomicProtocol.Version, request)));
            var transfer = r.Host.Snapshot.Transfers.Single(); Check(transfer.Force && transfer.Selection == ControlSelection.Throttle);
            Check(!r.Send("peer", new AuthorityPreparation(AtomicProtocol.Version, new(r.Host.Session, transfer.Transfer, false, false))));
            Check(r.Send("host", new AuthorityPreparation(AtomicProtocol.Version, new(r.Host.Session, transfer.Transfer, true, false))));
            Check(r.Send("peer", new AuthorityPreparation(AtomicProtocol.Version, new(r.Host.Session, transfer.Transfer, true, false))));
            Check(r.Group(AtomicControlGroup.Throttle).Owner == "peer" && !r.Group(AtomicControlGroup.Throttle).Attached);
            Check(!r.Send("peer", new AuthorityInput(AtomicProtocol.Version, r.Frame(AtomicControlGroup.Throttle))));
            r.Attach("peer", ControlSelection.Throttle);
            Check(r.Send("peer", new AuthorityInput(AtomicProtocol.Version, r.Frame(AtomicControlGroup.Throttle))));
            Check(r.Group(AtomicControlGroup.Propeller).Owner == "host" && r.Group(AtomicControlGroup.Mixture).Owner == "host");
        });
        Test("one shared Auto policy cancels only automatic transactions and rejects stale or foreign toggles", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All);
            Check(!r.Host.SetAutomatic("stranger", r.Host.Session, 1, true));
            Check(!r.Host.SetAutomatic("peer", Guid.NewGuid(), 1, true));
            Check(r.Send("peer", new AuthorityAutomatic(AtomicProtocol.Version, r.Host.Session, 1, true)));
            Check(r.Host.Snapshot.Automatic && r.Replica.Snapshot.Automatic);
            Check(!r.Send("peer", new AuthorityAutomatic(AtomicProtocol.Version, r.Host.Session, 1, false)));
            var auto = new AtomicTransferRequest(r.Host.Session, 1, ControlSelection.Throttle, r.Epochs(ControlSelection.Throttle), "peer") { Automatic = true };
            Check(r.Send("peer", new AuthorityRequest(AtomicProtocol.Version, auto)));
            var automaticTicket = r.Host.Snapshot.Transfers.Single().Transfer;
            var manualTicket = r.Request(ControlSelection.Propeller, 2);
            Check(r.Send("host", new AuthorityAutomatic(AtomicProtocol.Version, r.Host.Session, 1, false)));
            Check(!r.Host.Snapshot.Automatic && !r.Replica.Snapshot.Automatic);
            Check(r.Host.Snapshot.Transfers.Single().Transfer == manualTicket);
            Check(r.Group(AtomicControlGroup.Throttle).Owner == "host" && r.Group(AtomicControlGroup.Mixture).Attached);
            Check(!r.Send("peer", new AuthorityPreparation(AtomicProtocol.Version, new(r.Host.Session, automaticTicket, true, true))));
            Check(!r.Send("peer", new AuthorityRequest(AtomicProtocol.Version, auto with { Request = 3, Expected = r.Epochs(ControlSelection.Throttle) })));
        });
        Test("automatic requests cannot claim steering, force alignment or assign another player", () => {
            var r = new Room(); r.Attach("host", ControlSelection.All);
            Check(r.Host.SetAutomatic("host", r.Host.Session, 1, true));
            var request = new AtomicTransferRequest(r.Host.Session, 1, ControlSelection.Throttle, r.Epochs(ControlSelection.Throttle), "peer") { Automatic = true };
            Check(!r.Host.RequestTransfer("host", request));
            Check(!AuthorityWire.Valid(new AuthorityRequest(AtomicProtocol.Version, request with { Force = true })));
            Check(!AuthorityWire.Valid(new AuthorityRequest(AtomicProtocol.Version, request with { Selection = ControlSelection.Steering, Expected = r.Epochs(ControlSelection.Steering) })));
            Check(!AuthorityWire.Valid(new AuthorityRequest(AtomicProtocol.Version, request with { Selection = ControlSelection.Quadrant, Expected = r.Epochs(ControlSelection.Quadrant) })));
            Check(!AuthorityWire.Valid(new AuthorityAutomatic(2, r.Host.Session, 1, true)));
        });
        return count;
    }
}
