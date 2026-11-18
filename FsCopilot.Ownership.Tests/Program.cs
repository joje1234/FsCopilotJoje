using System.Text;
using FsCopilot.Ownership;

internal static class Program
{
    private sealed class Room
    {
        public TimeSpan Now;
        public OwnershipSession Host { get; }
        public OwnershipHostEndpoint Endpoint { get; }
        public OwnershipReplica Guest { get; }
        public Room()
        {
            Host = new("host", "copilot", 2, () => Now);
            Endpoint = new(Host);
            Guest = new("host", Host.Snapshot);
        }
        public GroupState State(ControlGroup group) => group == ControlGroup.Steering ? Host.Snapshot.Steering : Host.Snapshot.Quadrant;
        public bool Send(string sender, OwnershipMessage message)
        {
            // Both participants cross the real serialized protocol boundary;
            // connection identity is supplied independently of message contents.
            var result = Endpoint.Receive(sender, OwnershipWire.Encode(message), out var update, out _);
            if (update is not null)
            {
                Require(OwnershipWire.TryDecode(OwnershipWire.Encode(new SnapshotMessage(update)), out var decoded));
                Require(Guest.Receive("host", ((SnapshotMessage)decoded!).State));
            }
            return result;
        }
        public void Attach(string owner, ControlGroup group) => Require(Send(owner,
            new AttachmentMessage(new(Host.Session, group, State(group).Generation, true))));
        public TransferPreparation Request(ControlGroup group, string sender = "copilot", ulong request = 1, string newOwner = "copilot")
        {
            Require(Send(sender, new RequestMessage(new(Host.Session, request, group, State(group).Generation, newOwner))));
            return State(group).Pending!;
        }
        public PreparationMessage Ack(ControlGroup group, TransferPreparation transfer, bool aligned = true, bool suppressed = true) =>
            new(new(Host.Session, group, transfer.Transfer, suppressed, aligned));
        public ControlFrame Frame(ControlGroup group, ulong sequence = 1, ulong? generation = null) =>
            new(Host.Session, group, generation ?? State(group).Generation, sequence,
                [new(group == ControlGroup.Steering ? ControlAxis.Roll : ControlAxis.Throttle1, 0.5)]);
        public void Transfer(ControlGroup group, ulong request = 1, string sender = "copilot", string newOwner = "copilot")
        {
            var transfer = Request(group, sender, request, newOwner);
            Require(Send("host", Ack(group, transfer)));
            Require(Send("copilot", Ack(group, transfer)));
        }
    }

    private static void Require(bool value, string? reason = null)
    { if (!value) throw new InvalidOperationException(reason ?? "Protocol assertion failed."); }

    private static void Test(string name, Action run)
    { run(); Console.WriteLine("PASS " + name); }

    private static int Main()
    {
        try
        {
            Test("join starts with host ownership, detached hardware and no takeover", () =>
            {
                var r = new Room();
                Require(r.Host.Snapshot.PhysicsHost == "host" && r.Host.Snapshot.Copilot == "copilot");
                foreach (var group in new[] { ControlGroup.Steering, ControlGroup.Quadrant })
                {
                    Require(r.State(group).Owner == "host" && !r.State(group).Attached && r.State(group).Pending is null);
                    Require(!r.Send("host", new InputMessage(r.Frame(group))));
                }
            });
            Test("quadrant transfer preserves physics host and independently active steering", () =>
            {
                var r = new Room(); r.Attach("host", ControlGroup.Steering); r.Attach("host", ControlGroup.Quadrant);
                var steering = r.State(ControlGroup.Steering);
                var pending = r.Request(ControlGroup.Quadrant);
                Require(r.State(ControlGroup.Quadrant).Owner == "host" && !r.State(ControlGroup.Quadrant).Attached);
                Require(!r.Send("host", new InputMessage(r.Frame(ControlGroup.Quadrant))));
                Require(r.Send("host", new InputMessage(r.Frame(ControlGroup.Steering))));
                Require(!r.Send("copilot", r.Ack(ControlGroup.Quadrant, pending, suppressed: false)));
                Require(!r.Send("copilot", r.Ack(ControlGroup.Quadrant, pending, aligned: false)));
                Require(r.Send("host", r.Ack(ControlGroup.Quadrant, pending)));
                Require(!r.Send("host", r.Ack(ControlGroup.Quadrant, pending))); // Direct/relay duplicate.
                Require(r.State(ControlGroup.Quadrant).Pending is not null);
                Require(r.Send("copilot", r.Ack(ControlGroup.Quadrant, pending)));
                Require(r.State(ControlGroup.Quadrant).Owner == "copilot" && !r.State(ControlGroup.Quadrant).Attached);
                Require(!r.Send("copilot", new InputMessage(r.Frame(ControlGroup.Quadrant))));
                r.Attach("copilot", ControlGroup.Quadrant);
                Require(r.Send("copilot", new InputMessage(r.Frame(ControlGroup.Quadrant))));
                Require(!r.Send("host", new InputMessage(r.Frame(ControlGroup.Quadrant, 2))));
                Require(r.Host.Snapshot.PhysicsHost == "host" && r.State(ControlGroup.Steering) == steering);
            });
            Test("old owners, duplicate transports and reordered frames cannot apply commands", () =>
            {
                var r = new Room(); r.Attach("host", ControlGroup.Quadrant);
                var old = r.Frame(ControlGroup.Quadrant, 999);
                r.Transfer(ControlGroup.Quadrant); r.Attach("copilot", ControlGroup.Quadrant);
                var input = r.Frame(ControlGroup.Quadrant, 10);
                Require(r.Send("copilot", new InputMessage(input)));
                Require(!r.Send("copilot", new InputMessage(input)));
                Require(!r.Send("copilot", new InputMessage(input with { Sequence = 9 })));
                Require(!r.Send("host", new InputMessage(old)));
                Require(!r.Send("stranger", new InputMessage(input with { Sequence = 11 })));
                Require(!r.Send("host", new SnapshotMessage(r.Host.Snapshot)));
            });
            Test("wire endpoint exposes an accepted control frame exactly once", () =>
            {
                var r = new Room(); r.Attach("host", ControlGroup.Quadrant);
                var frame = r.Frame(ControlGroup.Quadrant);
                var bytes = OwnershipWire.Encode(new InputMessage(frame));
                Require(r.Endpoint.Receive("host", bytes, out var update, out var accepted));
                Require(update is null && accepted is not null && accepted.Axes[0] == frame.Axes[0]);
                Require(!r.Endpoint.Receive("host", bytes, out update, out accepted));
                Require(update is null && accepted is null);
            });
            Test("cancel and timeout invalidate generations and require confirmed reattachment", () =>
            {
                foreach (var timeout in new[] { false, true })
                {
                    var r = new Room(); r.Attach("host", ControlGroup.Steering);
                    var old = r.Frame(ControlGroup.Steering);
                    var pending = r.Request(ControlGroup.Steering);
                    Require(r.Send("host", r.Ack(ControlGroup.Steering, pending)));
                    if (timeout) { r.Now = TimeSpan.FromSeconds(6); Require(r.Host.ExpireTransfers()); }
                    else Require(r.Send("copilot", new CancellationMessage(new(r.Host.Session, ControlGroup.Steering, pending.Transfer))));
                    Require(r.State(ControlGroup.Steering).Owner == "host" && !r.State(ControlGroup.Steering).Attached);
                    Require(r.State(ControlGroup.Steering).Generation == 2);
                    Require(!r.Send("copilot", r.Ack(ControlGroup.Steering, pending)));
                    Require(!r.Send("host", new InputMessage(old)));
                    r.Attach("host", ControlGroup.Steering);
                    Require(r.Send("host", new InputMessage(r.Frame(ControlGroup.Steering))));
                }
            });
            Test("detaching hardware invalidates in-flight frames from the same owner", () =>
            {
                var r = new Room(); r.Attach("host", ControlGroup.Steering);
                var queued = r.Frame(ControlGroup.Steering, 200);
                Require(r.Send("host", new AttachmentMessage(new(r.Host.Session, ControlGroup.Steering, 1, false))));
                Require(r.State(ControlGroup.Steering).Generation == 2);
                r.Attach("host", ControlGroup.Steering);
                Require(!r.Send("host", new InputMessage(queued)));
                Require(r.Send("host", new InputMessage(r.Frame(ControlGroup.Steering))));
            });
            Test("disconnect revokes remote authority without silently attaching local hardware", () =>
            {
                var r = new Room(); r.Attach("host", ControlGroup.Steering);
                r.Transfer(ControlGroup.Quadrant); r.Attach("copilot", ControlGroup.Quadrant);
                var stale = r.Frame(ControlGroup.Quadrant);
                r.Host.CopilotDisconnected();
                Require(r.State(ControlGroup.Quadrant).Owner is null && !r.State(ControlGroup.Quadrant).Attached);
                Require(r.State(ControlGroup.Steering).Owner == "host" && r.State(ControlGroup.Steering).Attached);
                Require(!r.Send("copilot", new InputMessage(stale)));
                Require(r.Host.TakeLocalControlAfterDisconnect(ControlGroup.Quadrant));
                Require(!r.State(ControlGroup.Quadrant).Attached);
                r.Attach("host", ControlGroup.Quadrant);
                Require(r.Send("host", new InputMessage(r.Frame(ControlGroup.Quadrant))));
                var fresh = new Room();
                Require(fresh.Host.Session != r.Host.Session && !fresh.State(ControlGroup.Quadrant).Attached);
                Require(!fresh.Send("copilot", new InputMessage(stale)));
                Require(!r.Guest.Receive("host", fresh.Host.Snapshot)); // Explicit new negotiation required.
            });
            Test("replicas reject stale snapshots, peer claims and silent session changes", () =>
            {
                var r = new Room(); var initial = r.Host.Snapshot;
                r.Transfer(ControlGroup.Steering);
                Require(!r.Guest.Receive("host", initial));
                Require(!r.Guest.Receive("host", r.Host.Snapshot)); // Duplicate.
                Require(!r.Guest.Receive("copilot", r.Host.Snapshot with { Revision = 999 }));
                Require(!r.Guest.Receive("host", r.Host.Snapshot with { Revision = 999, Session = Guid.NewGuid() }));
                Require(!r.Guest.Receive("host", r.Host.Snapshot with { Revision = 999, Quadrant = initial.Quadrant with { Owner = "copilot" } }));
                Require(r.Guest.Snapshot.Steering.Owner == "copilot");
            });
            Test("request IDs cannot be replayed after cancellation or on the other group", () =>
            {
                var r = new Room(); var pending = r.Request(ControlGroup.Quadrant, request: 5);
                Require(r.Send("host", new CancellationMessage(new(r.Host.Session, ControlGroup.Quadrant, pending.Transfer))));
                Require(!r.Send("copilot", new RequestMessage(new(r.Host.Session, 5, ControlGroup.Steering, 1, "copilot"))));
                Require(!r.Send("copilot", new RequestMessage(new(r.Host.Session, 6, ControlGroup.Quadrant, 1, "copilot"))));
                Require(!r.Send("copilot", new RequestMessage(new(r.Host.Session, 6, ControlGroup.Quadrant, 2, "copilot"))));
                Require(r.Send("copilot", new RequestMessage(new(r.Host.Session, 7, ControlGroup.Quadrant, 2, "copilot"))));
            });
            Test("group, axis, engine, position and sequence validation cannot be bypassed", () =>
            {
                var r = new Room(); r.Attach("host", ControlGroup.Quadrant);
                var frame = r.Frame(ControlGroup.Quadrant);
                foreach (var axes in new[] {
                    Array.Empty<AxisPosition>(), new[] { new AxisPosition(ControlAxis.Roll, 0.5) },
                    new[] { new AxisPosition(ControlAxis.Throttle3, 0.5) },
                    new[] { new AxisPosition(ControlAxis.Throttle1, double.NaN) },
                    new[] { new AxisPosition(ControlAxis.Throttle1, 1.01) },
                    new[] { new AxisPosition(ControlAxis.Throttle1, 0.5), new AxisPosition(ControlAxis.Throttle1, 0.6) }
                }) Require(!r.Host.AcceptInput("host", frame with { Axes = axes, Sequence = 999 }));
                Require(r.Send("host", new InputMessage(frame))); // Invalid frames did not consume sequence.
                Require(!r.Host.AcceptInput("host", frame with { Group = (ControlGroup)99, Sequence = 2 }));
            });
            Test("wire rejects invalid, oversized, unknown and incomplete messages", () =>
            {
                foreach (var json in new[] { "", "{}", "null", "{\"kind\":\"unknown\"}", "{\"kind\":\"request\",\"request\":{}}", "{\"kind\":\"snapshot\",\"state\":null}" })
                    Require(!OwnershipWire.TryDecode(Encoding.UTF8.GetBytes(json), out _));
                Require(!OwnershipWire.TryDecode(new byte[OwnershipWire.MaxBytes + 1], out _));
                var r = new Room();
                var bytes = OwnershipWire.Encode(new SnapshotMessage(r.Host.Snapshot));
                Require(OwnershipWire.TryDecode(bytes, out _));
                Require(!OwnershipWire.TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _));
                var withClaimedSender = Encoding.UTF8.GetString(bytes)[..^1] + ",\"sender\":\"host\"}";
                Require(!OwnershipWire.TryDecode(Encoding.UTF8.GetBytes(withClaimedSender), out _));
            });
            Test("simultaneous duplicated acknowledgements commit only once", () =>
            {
                var r = new Room(); var pending = r.Request(ControlGroup.Quadrant);
                Require(r.Send("host", r.Ack(ControlGroup.Quadrant, pending)));
                var accepted = 0;
                Parallel.For(0, 100, _ => { if (r.Host.AcknowledgePreparation("copilot", r.Ack(ControlGroup.Quadrant, pending).Ack)) Interlocked.Increment(ref accepted); });
                Require(accepted == 1 && r.State(ControlGroup.Quadrant).Generation == 2);
                Require(r.State(ControlGroup.Quadrant).Owner == "copilot" && !r.State(ControlGroup.Quadrant).Attached);
            });
            Test("repeated independent handovers retain one physics host and reject every retired frame", () =>
            {
                var r = new Room(); var retired = new List<(string Owner, ControlFrame Frame)>();
                for (ulong request = 1; request <= 100; ++request)
                {
                    var group = request % 2 == 0 ? ControlGroup.Steering : ControlGroup.Quadrant;
                    var oldOwner = r.State(group).Owner!;
                    var newOwner = oldOwner == "host" ? "copilot" : "host";
                    retired.Add((oldOwner, r.Frame(group, 1000)));
                    r.Transfer(group, request, newOwner, newOwner);
                    r.Attach(newOwner, group);
                    Require(r.Send(newOwner, new InputMessage(r.Frame(group))));
                    foreach (var old in retired) Require(!r.Host.AcceptInput(old.Owner, old.Frame));
                    Require(OwnershipProtocol.ValidSnapshot(r.Host.Snapshot) && r.Host.Snapshot.PhysicsHost == "host");
                }
            });
            Test("queued simulator work rechecks the latest accepted frame and applies it only once", () => {
                var r = new Room(); r.Attach("host", ControlGroup.Steering);
                var older = r.Frame(ControlGroup.Steering, 1); var newer = r.Frame(ControlGroup.Steering, 2);
                Require(r.Host.AcceptInput("host", older) && r.Host.AcceptInput("host", newer));
                var writes = 0;
                Require(!r.Host.TryApplyAcceptedInput("host", older, () => ++writes));
                Require(r.Host.TryApplyAcceptedInput("host", newer, () => ++writes));
                Require(!r.Host.TryApplyAcceptedInput("host", newer, () => ++writes) && writes == 1);
                var queued = r.Frame(ControlGroup.Steering, 3); Require(r.Host.AcceptInput("host", queued));
                r.Request(ControlGroup.Steering);
                Require(!r.Host.TryApplyAcceptedInput("host", queued, () => ++writes) && writes == 1);
            });
            Test("explicit session end invalidates queued local frames and cannot be locally revived", () => {
                var r = new Room(); r.Attach("host", ControlGroup.Steering);
                var queued = r.Frame(ControlGroup.Steering); Require(r.Host.AcceptInput("host", queued));
                r.Host.End();
                Require(!r.Host.TryApplyAcceptedInput("host", queued, () => throw new Exception("Stale write")));
                Require(!r.Host.AcceptInput("host", queued with { Sequence = 2 }));
                Require(!r.Host.AcknowledgeAttachment("host", new(r.Host.Session, ControlGroup.Steering, r.State(ControlGroup.Steering).Generation, true)));
                Require(!r.Host.TakeLocalControlAfterDisconnect(ControlGroup.Quadrant));
                Require(!r.State(ControlGroup.Steering).Attached && OwnershipProtocol.ValidSnapshot(r.Host.Snapshot));
            });
            var sessionScenarios = SessionNegotiationTests.Run();
            var atomicScenarios = AtomicAuthorityTests.Run();
            var seatScenarios = SeatAuthorityTests.Run();
            var altimeterScenarios = AltimeterAuthorityTests.Run();
            var guidanceScenarios = GuidanceAuthorityTests.Run();
            Console.WriteLine($"{15 + sessionScenarios + atomicScenarios + seatScenarios + altimeterScenarios + guidanceScenarios} ownership/session protocol scenarios passed. No simulator controls were changed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
