using FsCopilot.Ownership;

internal static class AltimeterAuthorityTests
{
    private static void Check(bool value, string reason = "Altimeter authority assertion failed") { if (!value) throw new InvalidOperationException(reason); }
    private sealed class Room
    {
        public TimeSpan Now;
        public readonly ControlAuthoritySession Host;
        private readonly ControlAuthorityEndpoint _endpoint;
        private readonly ControlAuthorityReplica _main;
        public readonly AltimeterAuthorityReplica Replica;
        private readonly Dictionary<string, ulong> _proofs = new(), _reads = new() { ["host"] = 10, ["peer"] = 0 };
        public int Writes;
        public Room(CockpitSeat seat = CockpitSeat.PilotLeft) {
            Host = new("host", "peer", 2, () => Now, hostSeat: seat); _endpoint = new(Host); _main = new("host", Host.Snapshot); Replica = new(Host.Snapshot);
        }
        public AltimeterAuthoritySnapshot State => Host.Altimeters;
        public ulong Read => _reads["host"];
        public AltimeterSetting[] Settings => State.Instruments.Select(value => value.Confirmed!.Value).ToArray();
        public void Sync() {
            Check(AtomicProtocol.ValidSnapshot(Host.Snapshot)); Check(AltimeterProtocol.Valid(State));
            if (_main.Snapshot.Revision < Host.Snapshot.Revision) Check(_main.Receive("host", Host.Snapshot));
            if (Replica.Snapshot.Revision < State.Revision) Check(Replica.Receive("host", State, _main.Snapshot));
        }
        public bool Send(string who, AuthorityMessage message) {
            var bytes = AuthorityWire.Encode(message); Check(AuthorityWire.TryDecode(bytes, out _));
            var result = _endpoint.Receive(who, bytes, out _, out _); Sync(); return result;
        }
        public void Seed() { Check(Host.StartAltimeters("host", Host.Session, [new(15986, true), new(16200, false)], 10)); Sync(); }
        public AltimeterGateAck Proof(string who, uint? mask = null, ulong? preparation = null, ulong? read = null, AltimeterSetting[]? settings = null) => new(
            Host.Session, State.Epoch, preparation ?? State.Preparation, _proofs[who] = _proofs.GetValueOrDefault(who) + 1,
            true, "test-verified-altimeters-v1", mask ?? (State.Phase is AltimeterPhase.Synchronizing or AltimeterPhase.Quiescing ? 3u : AltimeterProtocol.Blocked(State.Seats, who)),
            read ?? ++_reads[who], settings ?? Settings);
        public bool Gate(string who, uint? mask = null) => Send(who, new AuthorityAltimeterGate(AtomicProtocol.Version, Proof(who, mask)));
        public void Attach() { Check(Gate("host") && Gate("peer") && State.Phase == AltimeterPhase.Active); }
        public void Active() { Seed(); Check(Gate("host") && Gate("peer") && State.Phase == AltimeterPhase.Attaching); Attach(); }
        public AltimeterChange Change(uint instrument, ulong sequence, uint pressure16, bool standard = false) =>
            new(Host.Session, State.Epoch, State.Preparation, sequence, instrument, new(pressure16, standard));
        public bool Request(string who, AltimeterChange change) => Send(who, new AuthorityAltimeterChange(AtomicProtocol.Version, change));
        public bool Apply(AltimeterChange change) {
            var result = Host.TryApplyAltimeter(change, Read, _ => { ++Writes; return true; }); Sync(); return result;
        }
        public bool Confirm(AltimeterChange change) => Send("host", new AuthorityAltimeterConfirmation(AtomicProtocol.Version,
            new(change, ++_reads["host"], true, change.Setting)));
        public void RequestSeat(string who = "peer", ulong sequence = 1) => Check(Send(who,
            new AuthoritySeatChange(AtomicProtocol.Version, new(Host.Session, sequence, Host.Snapshot.SeatRevision, SeatChangeAction.Request, 0))));
        public void Seat(string who, SeatChangeAction action, ulong sequence = 1) => Check(Send(who,
            new AuthoritySeatChange(AtomicProtocol.Version, new(Host.Session, sequence, Host.Snapshot.SeatRevision, action, Host.Snapshot.SeatChange!.Ticket))));
    }
    public static int Run()
    {
        var count = 0;
        void Test(string name, Action test) { test(); ++count; Console.WriteLine("PASS " + name); }
        Test("altimeters activate only after two matching native gate and baseline proofs", () => {
            var room = new Room(); room.Seed(); Check(room.State.Phase == AltimeterPhase.Synchronizing);
            Check(!room.Request("host", room.Change(1, 1, 16000)) && room.Writes == 0);
            Check(room.Gate("host") && room.State.Phase == AltimeterPhase.Synchronizing);
            var unsupported = new AltimeterGateAck(room.Host.Session, room.State.Epoch, room.State.Preparation, 1, false, "", 0, 0, []);
            Check(room.Send("peer", new AuthorityAltimeterGate(AtomicProtocol.Version, unsupported)) && room.State.Phase == AltimeterPhase.Holding);
            var initial = new Room(); initial.Seed();
            Check(initial.Send("peer", new AuthorityAltimeterGate(AtomicProtocol.Version, unsupported with { Session = initial.Host.Session })));
            Check(initial.State.Phase == AltimeterPhase.Unsupported && initial.Writes == 0);
        });
        Test("pilot and copilot instruments keep distinct owners, QNH and STD independent of physics hosting", () => {
            var room = new Room(CockpitSeat.CopilotRight); room.Active(); var control = room.Host.Snapshot;
            var left = room.Change(1, 1, 16016, true); var right = room.Change(2, 1, 16160);
            Check(!room.Request("host", left) && !room.Request("peer", right));
            Check(room.Request("peer", left) && room.Request("host", right));
            Check(room.Apply(left) && !room.Apply(left) && room.Confirm(left));
            Check(room.Apply(right) && room.Confirm(right) && room.Writes == 2);
            Check(room.Settings.SequenceEqual(new AltimeterSetting[] { new(16016, true), new(16160, false) }));
            Check(room.Host.Snapshot.Revision == control.Revision && room.Host.Snapshot.Groups.SequenceEqual(control.Groups));
            Check(room.Host.Snapshot.PhysicsHost == "host" && room.State.Instruments[0].Owner == "peer");
        });
        Test("rotary changes coalesce, equal state does not write and superseded confirmation cannot commit", () => {
            var room = new Room(); room.Active();
            var first = room.Change(2, 1, 16160); var second = room.Change(2, 2, 16161); var latest = room.Change(2, 3, 16162);
            Check(room.Request("peer", first) && room.Request("peer", second)); Check(!room.Apply(first) && room.Apply(second));
            Check(room.Request("peer", latest));
            Check(!room.Send("host", new AuthorityAltimeterConfirmation(AtomicProtocol.Version, new(second, room.Read + 1, true, second.Setting))));
            Check(room.Apply(latest) && room.Confirm(latest));
            var equal = room.Change(2, 4, 16162); Check(room.Request("peer", equal) && !room.Apply(equal));
            Check(!room.Request("peer", equal) && room.Writes == 2 && room.Settings[1] == latest.Setting);
        });
        Test("only fresh host confirmation commits; partial SDK failure holds instruments without ending flight controls", () => {
            var room = new Room(); room.Active(); var before = room.Settings;
            var change = room.Change(1, 1, 16000); Check(room.Request("host", change));
            Check(!room.Host.TryApplyAltimeter(change, 0, _ => throw new Exception("Unexpected stale dispatch")));
            Check(room.Apply(change));
            Check(!room.Send("peer", new AuthorityAltimeterConfirmation(AtomicProtocol.Version, new(change, room.Read + 1, true, change.Setting))));
            Check(!room.Send("host", new AuthorityAltimeterConfirmation(AtomicProtocol.Version, new(change, room.Read, true, change.Setting))));
            Check(room.Send("host", new AuthorityAltimeterConfirmation(AtomicProtocol.Version, new(change, 0, false, null))));
            Check(room.State.Phase == AltimeterPhase.Holding && room.Settings.SequenceEqual(before) && !room.Apply(change));
            Check(room.Host.Snapshot.PeerConnected && !room.Host.Snapshot.Ended);
        });
        Test("seat confirmation drains pressure work and masks both knobs before changing owners", () => {
            var room = new Room(); room.Active(); var groups = room.Host.Snapshot.Groups.ToArray();
            var change = room.Change(2, 1, 16160); Check(room.Request("peer", change));
            room.RequestSeat(); room.Seat("host", SeatChangeAction.Accept);
            Check(room.Host.Snapshot.SeatChange is { Accepted: true } && room.Host.Snapshot.Seats.PilotPeer == "host");
            Check(room.Gate("host") && room.Gate("peer") && room.State.Phase == AltimeterPhase.Quiescing);
            Check(!room.Request("peer", room.Change(2, 2, 16161)));
            Check(room.Apply(change) && room.Confirm(change));
            Check(room.Host.Snapshot.Seats.PilotPeer == "host"); // The old gate read-back predates the confirmed pressure.
            Check(room.Gate("host"));
            Check(room.Host.Snapshot.SeatChange is null && room.Host.Snapshot.Seats == new CockpitSeats("peer", "host"));
            Check(room.State.Epoch == 2 && room.State.Phase == AltimeterPhase.Synchronizing && room.Settings[1] == change.Setting);
            Check(!room.Apply(change) && !room.Request("peer", change with { Sequence = 3 }));
            Check(room.Gate("host") && room.Gate("peer") && room.State.Phase == AltimeterPhase.Attaching); room.Attach();
            Check(room.Host.Snapshot.Groups.SequenceEqual(groups) && room.Host.Snapshot.PhysicsHost == "host");
        });
        Test("cancelled seat preparation cannot revive old pressure frames or gate proofs", () => {
            var room = new Room(); room.Active(); var retired = room.Change(2, 10, 16160);
            room.RequestSeat(); room.Seat("host", SeatChangeAction.Accept);
            Check(room.Gate("host")); var oldProof = room.Proof("peer");
            room.Seat("peer", SeatChangeAction.Cancel, sequence: 2);
            Check(room.State.Phase == AltimeterPhase.Synchronizing && room.State.Epoch == 1);
            Check(!room.Send("peer", new AuthorityAltimeterGate(AtomicProtocol.Version, oldProof)));
            Check(room.Gate("host") && room.Gate("peer") && room.State.Phase == AltimeterPhase.Attaching); room.Attach();
            Check(!room.Request("peer", retired) && room.Host.Snapshot.Seats.PilotPeer == "host");
            Check(room.Request("peer", room.Change(2, 11, 16160)));
        });
        Test("cached gate data cannot renew readiness and accepted seat timeout preserves both pressures", () => {
            var room = new Room(); room.Active(); room.Now = TimeSpan.FromSeconds(.5);
            Check(!room.Send("host", new AuthorityAltimeterGate(AtomicProtocol.Version, room.Proof("host", read: room.Read))));
            room.Now = TimeSpan.FromSeconds(1.01); Check(room.Host.ExpireTransfers()); room.Sync();
            Check(room.State.Phase == AltimeterPhase.Holding && room.State.Gates.All(gate => !gate.Ready));
            var swap = new Room(); swap.Active(); var values = swap.Settings; swap.RequestSeat(); swap.Seat("host", SeatChangeAction.Accept);
            swap.Now = TimeSpan.FromSeconds(6.01); Check(swap.Host.ExpireTransfers()); swap.Sync();
            Check(swap.Host.Snapshot.SeatChange is null && swap.Host.Snapshot.Seats.PilotPeer == "host" && swap.Settings.SequenceEqual(values));
        });
        Test("altimeter wire and replicas reject malformed, foreign, rollback and revived states", () => {
            var room = new Room(); room.Active(); var state = room.State;
            Check(!room.Replica.Receive("peer", state with { Revision = state.Revision + 1 }, room.Host.Snapshot));
            Check(!room.Replica.Receive("host", state with { Revision = state.Revision + 1, Epoch = state.Epoch + 1 }, room.Host.Snapshot));
            Check(!AltimeterProtocol.Valid(state with { Gates = [state.Gates[0] with { Adapter = null! }, state.Gates[1]] }));
            Check(!AuthorityWire.TryDecode("{\"kind\":\"altimeter\",\"Version\":5,\"Change\":null}"u8, out _));
            room.Host.PeerDisconnected(); room.Sync();
            Check(room.State.Phase == AltimeterPhase.Holding && !room.Replica.Receive("host", state with { Revision = room.State.Revision + 1 }, room.Host.Snapshot));
            var copy = room.Replica.Snapshot; copy.Instruments[0] = copy.Instruments[1]; Check(room.Replica.Snapshot.Instruments[0].Index == 1);
        });
        Test("a throwing SDK callback is consumed once and holds only instrument authority", () => {
            var room = new Room(); room.Active(); var values = room.Settings;
            var change = room.Change(1, 1, 16000); Check(room.Request("host", change));
            try { room.Host.TryApplyAltimeter(change, room.Read, _ => { ++room.Writes; throw new IOException("Partial SDK failure"); }); throw new Exception("Missing SDK failure"); }
            catch (IOException) { }
            room.Sync();
            Check(room.State.Phase == AltimeterPhase.Holding && room.Writes == 1 && !room.Apply(change));
            Check(room.Settings.SequenceEqual(values) && room.Host.Snapshot.PeerConnected && !room.Host.Snapshot.Ended);
        });
        Test("masked baselines precede owner release and an attaching owner's new input is preserved", () => {
            var room = new Room(); room.Seed();
            Check(room.Gate("host") && room.Gate("peer") && room.State.Phase == AltimeterPhase.Attaching);
            Check(room.State.Gates.All(value => !value.Ready && value.Blocked == 3));
            Check(room.Gate("host"));
            var change = room.Change(1, 1, 16000); Check(room.Request("host", change) && room.Apply(change));
            Check(room.Gate("peer") && room.State.Phase == AltimeterPhase.Attaching);
            Check(room.Confirm(change) && room.State.Phase == AltimeterPhase.Active && room.Settings[0] == change.Setting);
        });
        Test("pre-execution retry rechecks the exact pending frame and cannot replay a superseded pressure", () => {
            var room = new Room(); room.Active();
            var first = room.Change(1, 1, 16000); Check(room.Request("host", first));
            Check(!room.Host.TryRetryAltimeter(first, room.Read, _ => throw new Exception("Undispatched frame retried")));
            Check(room.Apply(first));
            Check(room.Host.TryRetryAltimeter(first, room.Read, _ => { ++room.Writes; return true; }));
            Check(!room.Host.TryApplyAltimeter(first, room.Read, _ => throw new Exception("Duplicate initial dispatch")));
            var latest = room.Change(1, 2, 16016); Check(room.Request("host", latest));
            Check(!room.Host.TryRetryAltimeter(first, room.Read, _ => throw new Exception("Retired native value retried")));
            Check(!room.Host.TryRetryAltimeter(latest, room.Read, _ => throw new Exception("New frame treated as dispatched")));
            Check(room.Apply(latest) && room.Writes == 3);
        });
        Test("queue retries retain the original pressure deadline while native gate proofs stay fresh", () => {
            var room = new Room(); room.Active(); var change = room.Change(1, 1, 16000);
            Check(room.Request("host", change) && room.Apply(change));
            for (var step = 1; step <= 4; ++step) { room.Now = TimeSpan.FromSeconds(step * .7); Check(room.Gate("host") && room.Gate("peer")); }
            Check(room.Host.TryRetryAltimeter(change, room.Read, _ => true));
            room.Now = TimeSpan.FromSeconds(3.01); room.Host.ExpireTransfers(); room.Sync();
            Check(room.State.Phase == AltimeterPhase.Holding && !room.Host.Snapshot.Ended && room.Host.Snapshot.PeerConnected);
        });
        Test("instrument support preflight is validated without becoming an ownership or mask proof", () => {
            var room = new Room();
            var availability = new AltimeterAvailability(room.Host.Session, 1, true, "test-verified-altimeters-v1");
            var bytes = AuthorityWire.Encode(new AuthorityAltimeterAvailability(AtomicProtocol.Version, availability));
            Check(AuthorityWire.TryDecode(bytes, out var copy) && copy is AuthorityAltimeterAvailability value && value.Availability == availability);
            Check(!AltimeterProtocol.Valid(availability with { Sequence = 0 }) && !AltimeterProtocol.Valid(availability with { Adapter = "" }) &&
                !AltimeterProtocol.Valid(availability with { Protocol = 1 }));
            Check(!room.Send("peer", new AuthorityAltimeterAvailability(AtomicProtocol.Version, availability)) && room.State.Phase == AltimeterPhase.Disabled);
        });
        Test("explicit local instrument policy retires pressure work while seats and physical controls remain shared", () => {
            var room = new Room(); room.Active(); var groups = room.Host.Snapshot.Groups.ToArray();
            var pending = room.Change(1, 1, 16000); Check(room.Request("host", pending) && room.Apply(pending));
            Check(!room.Host.StopAltimeters("stranger", room.Host.Session, 1));
            Check(room.Send("peer", new AuthorityAltimeterStop(AtomicProtocol.Version, room.Host.Session, 1)));
            Check(room.State.Phase == AltimeterPhase.Local && room.State.Instruments.All(value => value.Pending is null));
            Check(!room.Request("host", pending with { Sequence = 2 }) && !room.Confirm(pending));
            Check(!room.Host.StopAltimeters("peer", room.Host.Session, 1));
            Check(!room.Host.StartAltimeters("host", room.Host.Session, room.Settings, room.Read));
            room.RequestSeat("host"); room.Seat("peer", SeatChangeAction.Accept);
            Check(room.State.Phase == AltimeterPhase.Local && room.State.Seats.PilotPeer == "peer" && room.Host.Snapshot.Groups.SequenceEqual(groups));
        });
        return count;
    }
}
