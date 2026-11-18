using System.Text;
using FsCopilot.Ownership;

internal static class GuidanceAuthorityTests
{
    private const string RouteA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string RouteB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Guidance assertion failed."); }
    private sealed class Room
    {
        public TimeSpan Now;
        public readonly ControlAuthoritySession Host;
        public readonly ControlAuthorityEndpoint Endpoint;
        public readonly GuidanceAuthorityReplica Replica;
        public GuidanceCapabilities Caps = GuidanceCapabilities.Commands;
        public uint PeerSettings = GuidanceProtocol.AllSettings;
        private readonly Dictionary<string, ulong> _proof = new(), _reads = new(), _inputs = new(), _claims = new();
        public Room(bool start = true)
        {
            Host = new("host", "peer", 2, () => Now); Endpoint = new(Host); Replica = new(Host.Snapshot);
            if (start) Check(Host.StartGuidance("host", Host.Session, State, 1));
        }
        public GuidanceState State = new(false, GuidanceModes.None, 90, 5000, 500, true, 1, true, RouteA);
        public FlightGuidanceSnapshot Snapshot => Host.Guidance;
        public bool Observe(ulong serial, GuidanceState state) {
            _reads["host"] = Math.Max(_reads.GetValueOrDefault("host"), serial);
            return Host.ObserveGuidance("host", Host.Session, serial, state);
        }
        public bool Confirm(ulong serial, GuidanceState state) => Host.ConfirmGuidance("host", Snapshot.Pending!.Command, serial, state);
        public bool Send(string who, AuthorityMessage message) => Endpoint.Receive(who, AuthorityWire.Encode(message), out _, out _);
        public GuidanceGateProof Proof(string who, GuidanceState? state = null, bool applied = false)
        {
            _proof[who] = _proof.GetValueOrDefault(who) + 1; _reads[who] = _reads.GetValueOrDefault(who, 1ul) + 1;
            var capture = Snapshot.Phase is GuidancePhase.Active or GuidancePhase.Attaching && Snapshot.Commander == who;
            return new(Host.Session, Snapshot.Preparation, Snapshot.CommandEpoch, Snapshot.NavigationEpoch,
                _proof[who], "test-absolute-ap-v1", Caps, true, capture, true, _reads[who], state ?? State, applied,
                who == "peer" ? PeerSettings : GuidanceProtocol.AllSettings);
        }
        public void Gate(string who, GuidanceState? state = null, bool applied = false) =>
            Check(Send(who, new AuthorityGuidanceGate(AtomicProtocol.Version, Proof(who, state, applied))));
        public void Active()
        {
            Gate("host"); Check(Snapshot.Phase == GuidancePhase.Preparing);
            Gate("peer"); Check(Snapshot.Phase == GuidancePhase.Attaching);
            Gate("host"); Gate("peer"); Check(Snapshot.Phase == GuidancePhase.Active);
        }
        public GuidanceCommand Command(string who, GuidanceSetting setting, double value, ulong steeringGeneration = 0)
        {
            _inputs[who] = _inputs.GetValueOrDefault(who) + 1;
            return new(Host.Session, Snapshot.Preparation, Snapshot.CommandEpoch, Snapshot.NavigationEpoch, _inputs[who], setting, value, steeringGeneration);
        }
        public bool Command(string who, GuidanceCommand command) => Send(who, new AuthorityGuidanceCommand(AtomicProtocol.Version, command));
        public bool Claim(string who, GuidanceSelection selection, string owner, string fingerprint = "")
        {
            _claims[who] = _claims.GetValueOrDefault(who) + 1;
            return Send(who, new AuthorityGuidanceClaim(AtomicProtocol.Version, new(Host.Session, _claims[who], selection, owner,
                Snapshot.CommandEpoch, Snapshot.NavigationEpoch, fingerprint)));
        }
        public void Commander(string who)
        {
            Check(Claim(who, GuidanceSelection.Commands, who));
            Gate("host"); Gate("peer"); Check(Snapshot.Phase == GuidancePhase.Attaching);
            Gate("host"); Gate("peer"); Check(Snapshot.Phase == GuidancePhase.Active && Snapshot.Commander == who);
        }
        public void Valid() => Check(GuidanceProtocol.Valid(Snapshot));
    }
    public static int Run()
    {
        var count = 0;
        void Test(string name, Action test) { test(); ++count; Console.WriteLine("PASS guidance: " + name); }
        Test("disabled by default; only host can start a fresh session", () => {
            var r = new Room(false); r.Valid();
            Check(r.Snapshot.Commander == "host" && r.Snapshot.Navigator == "host");
            Check(!r.Host.StartGuidance("peer", r.Host.Session, r.State, 1));
            Check(!r.Host.StartGuidance("host", Guid.NewGuid(), r.State, 1));
            Check(r.Host.StartGuidance("host", r.Host.Session, r.State, 1));
            Check(!r.Host.StartGuidance("host", r.Host.Session, r.State, 2)); r.Valid();
        });
        Test("both suppressed inputs and capture attachment precede active", () => {
            var r = new Room(); Check(!r.Command("host", r.Command("host", GuidanceSetting.Heading, 120)));
            r.Active(); r.Valid();
        });
        Test("unverified adapter, a second AP engine or input bypass holds guidance", () => {
            foreach (var mode in new[] { 0, 1, 2, 3 }) {
                var r = new Room(); var proof = r.Proof("host");
                if (mode == 0) proof = proof with { InputSuppressed = false };
                if (mode == 1) proof = proof with { SingleHostEngine = false };
                if (mode == 2) proof = proof with { Capabilities = GuidanceCapabilities.None };
                if (mode == 3) { r.Gate("peer"); proof = proof with { Adapter = "different-v1" }; }
                r.Host.AcknowledgeGuidance("host", proof);
                Check(r.Snapshot.Phase == GuidancePhase.Holding); r.Valid();
            }
        });
        Test("commands are absolute, attributed and dispatched once", () => {
            var r = new Room(); r.Active(); var cmd = r.Command("host", GuidanceSetting.Heading, 120);
            Check(!r.Command("peer", cmd)); Check(!r.Command("stranger", cmd)); Check(r.Command("host", cmd));
            Check(!r.Command("host", cmd)); var writes = 0;
            Check(r.Host.TryApplyGuidanceCommand(cmd, 3, _ => ++writes == 1));
            Check(!r.Host.TryApplyGuidanceCommand(cmd, 3, _ => ++writes == 2));
            Check(r.Snapshot.Confirmed!.Heading == 90 && r.Snapshot.Pending is not null && writes == 1);
            Check(!r.Host.ObserveGuidance("peer", r.Host.Session, 4, r.State with { Heading = 120 }));
            Check(r.Observe(4, r.State with { Heading = 120 }));
            Check(r.Snapshot.Pending is not null); Check(r.Confirm(4, r.State with { Heading = 120 }));
            Check(r.Snapshot.Pending is null); r.Valid();
        });
        Test("fresh SDK readback is required; old or mismatched observations cannot confirm", () => {
            var r = new Room(); r.Active(); var cmd = r.Command("host", GuidanceSetting.Altitude, 6000);
            Check(r.Command("host", cmd)); Check(r.Host.TryApplyGuidanceCommand(cmd, 5, _ => true));
            Check(r.Observe(4, r.State with { Altitude = 6000 }));
            Check(!r.Confirm(4, r.State with { Altitude = 6000 }));
            Check(r.Snapshot.Pending is not null);
            Check(r.Observe(6, r.State)); Check(r.Snapshot.Pending is not null);
            Check(r.Observe(7, r.State with { Altitude = 6000 }));
            Check(r.Confirm(7, r.State with { Altitude = 6000 }));
            Check(r.Snapshot.Pending is null); r.Valid();
        });
        Test("coalescing retires queued values without overwriting another setting", () => {
            var r = new Room(); r.Active(); var old = r.Command("host", GuidanceSetting.Heading, 120);
            Check(r.Command("host", old)); var next = r.Command("host", GuidanceSetting.Heading, 130); Check(r.Command("host", next));
            Check(!r.Command("host", r.Command("host", GuidanceSetting.Altitude, 6000)));
            Check(!r.Host.TryApplyGuidanceCommand(old, 3, _ => throw new Exception("Retired command")));
            Check(r.Host.TryApplyGuidanceCommand(next, 3, _ => true)); r.Valid();
        });
        Test("input gate proof cannot replace the SDK output confirmation", () => {
            var r = new Room(); r.Active(); var cmd = r.Command("host", GuidanceSetting.VerticalSpeed, 1000);
            Check(r.Command("host", cmd)); Check(r.Host.TryApplyGuidanceCommand(cmd, 3, _ => true));
            r.Gate("host", r.State with { VerticalSpeed = 1000 }); Check(r.Snapshot.Pending is not null);
            Check(r.Confirm(4, r.State with { VerticalSpeed = 1000 })); Check(r.Snapshot.Pending is null); r.Valid();
        });
        Test("AP authority changes independently of physics, steering, seats and altimeters", () => {
            var r = new Room(); r.Active(); var controls = r.Host.Snapshot; var baro = r.Host.Altimeters;
            r.Commander("peer"); Check(r.Snapshot.Navigator == "host" && r.Snapshot.CommandEpoch == 2 && r.Snapshot.NavigationEpoch == 1);
            Check(r.Host.Snapshot.Revision == controls.Revision && r.Host.Snapshot.Seats == controls.Seats && r.Host.Altimeters.Revision == baro.Revision);
            Check(!r.Command("host", r.Command("host", GuidanceSetting.Heading, 120)));
            Check(r.Command("peer", r.Command("peer", GuidanceSetting.Heading, 120))); r.Valid();
        });
        Test("old owner drains until gated; successor cannot command before attachment", () => {
            var r = new Room(); r.Active(); Check(r.Claim("peer", GuidanceSelection.Commands, "peer"));
            Check(r.Command("host", r.Command("host", GuidanceSetting.Heading, 120)));
            var cmd = r.Snapshot.Pending!.Command; Check(r.Host.TryApplyGuidanceCommand(cmd, 3, _ => true));
            r.Gate("host", r.State with { Heading = 120 });
            Check(r.Confirm(4, r.State with { Heading = 120 }));
            Check(!r.Command("host", r.Command("host", GuidanceSetting.Heading, 130)));
            r.Gate("peer"); Check(r.Snapshot.Phase == GuidancePhase.Attaching);
            Check(!r.Command("peer", r.Command("peer", GuidanceSetting.Heading, 130)));
            r.Gate("peer"); Check(r.Command("peer", r.Command("peer", GuidanceSetting.Heading, 130))); r.Valid();
        });
        Test("AP disconnect retires a queued AP-on and waits for actual off readback", () => {
            var r = new Room(); r.State = r.State with { Master = true }; r.Active();
            var on = r.Command("host", GuidanceSetting.Master, 1);
            Check(r.Observe(4, r.State with { Master = false }));
            Check(r.Command("host", on));
            Check(r.Observe(5, r.State));
            var off = r.Command("host", GuidanceSetting.Master, 0); Check(r.Command("host", off));
            var pending = r.Snapshot.Pending!; Check(pending.Emergency && pending.Command.CommandEpoch == 2);
            Check(r.Snapshot.Confirmed!.Master);
            Check(!r.Host.TryApplyGuidanceCommand(on, 5, _ => throw new Exception("AP on after disconnect")));
            Check(r.Host.TryApplyGuidanceCommand(pending.Command, 5, _ => true));
            Check(r.Observe(6, r.State with { Master = false }));
            Check(r.Confirm(6, r.State with { Master = false }));
            Check(r.Snapshot.Pending is null && !r.Snapshot.Confirmed!.Master); r.Valid();
        });
        Test("steering owner can disconnect AP but cannot engage or retune it", () => {
            var r = new Room(); r.Active(); r.Commander("peer");
            Check(!r.Command("host", r.Command("host", GuidanceSetting.Master, 1)));
            var generation = r.Host.Snapshot.Groups.Single(g => g.Group == AtomicControlGroup.Steering).Generation;
            Check(!r.Command("host", r.Command("host", GuidanceSetting.Master, 0, generation + 1)));
            Check(r.Command("host", r.Command("host", GuidanceSetting.Master, 0, generation)));
            Check(r.Snapshot.Commander == "peer" && r.Snapshot.Pending!.Emergency); r.Valid();
        });
        Test("navigation stays with host without a verified transfer capability", () => {
            var r = new Room(); r.Active(); Check(!r.Claim("peer", GuidanceSelection.Navigation, "peer", RouteB));
            Check(r.Snapshot.Phase == GuidancePhase.Active && r.Snapshot.Navigator == "host"); r.Valid();
        });
        Test("navigation transfer needs AP off and confirmed source on both endpoints", () => {
            var r = new Room { Caps = GuidanceCapabilities.Commands | GuidanceCapabilities.NavigationTransfer };
            r.State = r.State with { Master = true }; r.Active();
            Check(!r.Claim("peer", GuidanceSelection.Navigation, "peer", RouteB));
            Check(r.Observe(4, r.State with { Master = false }));
            r.State = r.State with { Master = false };
            Check(r.Claim("peer", GuidanceSelection.Navigation, "peer", RouteB));
            Check(!r.Command("host", r.Command("host", GuidanceSetting.Master, 1)));
            r.Gate("host", r.State with { NavigationFingerprint = RouteB }, true); r.Gate("peer");
            Check(r.Snapshot.Phase == GuidancePhase.Quiescing && r.Snapshot.Navigator == "host");
            r.Gate("peer", r.State with { NavigationFingerprint = RouteB });
            Check(r.Snapshot.Phase == GuidancePhase.Attaching && r.Snapshot.Navigator == "peer" && r.Snapshot.Commander == "host");
            r.State = r.State with { NavigationFingerprint = RouteB }; r.Gate("host"); r.Gate("peer"); r.Valid();
        });
        Test("unexpected AP engagement during navigation transfer holds guidance", () => {
            var r = new Room { Caps = GuidanceCapabilities.Commands | GuidanceCapabilities.NavigationTransfer }; r.Active();
            Check(r.Claim("peer", GuidanceSelection.Navigation, "peer", RouteB));
            r.Gate("host", r.State with { Master = true }, true);
            Check(r.Snapshot.Phase == GuidancePhase.Holding && r.Snapshot.Navigator == "host"); r.Valid();
        });
        Test("NAV/APR cannot engage without an authoritative navigation source", () => {
            var r = new Room(); r.State = r.State with { NavigationReady = false, NavigationFingerprint = "" }; r.Active();
            Check(!r.Command("host", r.Command("host", GuidanceSetting.NavHold, 1)));
            Check(!r.Command("host", r.Command("host", GuidanceSetting.ApproachHold, 1)));
            Check(r.Command("host", r.Command("host", GuidanceSetting.HeadingHold, 1))); r.Valid();
        });
        Test("cancel restores old command ownership with a new preparation fence", () => {
            var r = new Room(); r.Active(); Check(r.Claim("peer", GuidanceSelection.Commands, "peer"));
            var ticket = r.Snapshot.Preparation; var late = r.Proof("peer");
            Check(!r.Host.CancelGuidanceClaim("stranger", r.Host.Session, ticket));
            Check(r.Host.CancelGuidanceClaim("peer", r.Host.Session, ticket));
            Check(!r.Host.AcknowledgeGuidance("peer", late)); r.Gate("host"); r.Gate("peer");
            Check(r.Snapshot.Phase == GuidancePhase.Active && r.Snapshot.Commander == "host"); r.Valid();
        });
        Test("cancelling after work is queued or a route is applied holds safely", () => {
            foreach (var route in new[] { false, true }) {
                var r = new Room { Caps = GuidanceCapabilities.Commands | GuidanceCapabilities.NavigationTransfer }; r.Active();
                Check(r.Claim("peer", route ? GuidanceSelection.Navigation : GuidanceSelection.Commands, "peer", route ? RouteB : ""));
                var ticket = r.Snapshot.Preparation;
                if (route) r.Gate("host", r.State with { NavigationFingerprint = RouteB }, true);
                else Check(r.Command("host", r.Command("host", GuidanceSetting.Heading, 120)));
                Check(r.Host.CancelGuidanceClaim("peer", r.Host.Session, ticket));
                Check(r.Snapshot.Phase == GuidancePhase.Holding && r.Snapshot.Pending is null); r.Valid();
            }
        });
        Test("expired input proof cannot be renewed by network heartbeats or commands", () => {
            var r = new Room(); r.Active(); r.Now = TimeSpan.FromMilliseconds(1001); Check(r.Host.ExpireTransfers());
            Check(r.Snapshot.Phase == GuidancePhase.Holding);
            Check(!r.Command("host", r.Command("host", GuidanceSetting.Heading, 120))); r.Valid();
        });
        Test("preparation, readback timeout and failed SDK enqueue hold guidance alone", () => {
            foreach (var mode in new[] { 0, 1, 2 }) {
                var r = new Room(); var controls = r.Host.Snapshot;
                if (mode == 0) r.Now = TimeSpan.FromSeconds(6);
                else {
                    r.Active(); var cmd = r.Command("host", GuidanceSetting.Heading, 120); Check(r.Command("host", cmd));
                    if (mode == 1) { Check(r.Host.TryApplyGuidanceCommand(cmd, 3, _ => true)); r.Now = TimeSpan.FromSeconds(3); }
                    else Check(!r.Host.TryApplyGuidanceCommand(cmd, 3, _ => false));
                }
                r.Host.ExpireTransfers(); Check(r.Snapshot.Phase == GuidancePhase.Holding);
                Check(r.Host.Snapshot.Revision == controls.Revision); r.Valid();
            }
        });
        Test("stop, disconnect and end retire all queued AP work", () => {
            foreach (var mode in new[] { 0, 1, 2 }) {
                var r = new Room(); r.Active(); var cmd = r.Command("host", GuidanceSetting.Heading, 120); Check(r.Command("host", cmd));
                if (mode == 0) Check(r.Send("peer", new AuthorityGuidanceStop(AtomicProtocol.Version, r.Host.Session, 1)));
                else if (mode == 1) r.Host.PeerDisconnected(); else r.Host.End();
                Check(!r.Host.TryApplyGuidanceCommand(cmd, 3, _ => throw new Exception("Ended write")));
                Check(!r.Host.StartGuidance("host", r.Host.Session, r.State, 10)); r.Valid();
            }
        });
        Test("serialized protocol rejects malformed, stale and forged host snapshots", () => {
            var r = new Room(); r.Active(); var snapshot = new AuthorityGuidanceState(AtomicProtocol.Version, r.Snapshot);
            Check(AuthorityWire.TryDecode(AuthorityWire.Encode(snapshot), out var decoded) && decoded is AuthorityGuidanceState);
            Check(!r.Send("peer", snapshot));
            Check(!AuthorityWire.TryDecode(Encoding.UTF8.GetBytes("{\"kind\":\"guidance-command\",\"version\":5,\"command\":null}"), out _));
            Check(!AuthorityWire.Valid(new AuthorityGuidanceCommand(AtomicProtocol.Version, r.Command("host", GuidanceSetting.Heading, double.NaN))));
            Check(!GuidanceProtocol.Valid(r.Snapshot with { Navigator = "outsider" }));
            Check(!r.Replica.Receive("peer", r.Snapshot, r.Host.Snapshot));
            Check(r.Replica.Receive("host", r.Snapshot, r.Host.Snapshot));
            Check(!r.Replica.Receive("host", r.Snapshot, r.Host.Snapshot));
            Check(!r.Replica.Receive("host", r.Snapshot with { Revision = r.Snapshot.Revision + 1, Commander = "peer" }, r.Host.Snapshot));
            r.Commander("peer"); Check(r.Replica.Receive("host", r.Snapshot, r.Host.Snapshot));
            r.Host.End(); Check(r.Replica.Receive("host", r.Snapshot, r.Host.Snapshot)); r.Valid();
        });
        Test("EX1 heading quantization and north wrap match without accepting a different target", () => {
            var r = new Room();
            Check(GuidanceProtocol.Matches(r.Command("host", GuidanceSetting.Heading, 359.995), r.State with { Heading = 0 }));
            Check(GuidanceProtocol.Matches(r.Command("host", GuidanceSetting.Heading, 90.01), r.State with { Heading = 90 }));
            Check(!GuidanceProtocol.Matches(r.Command("host", GuidanceSetting.Heading, 90.1), r.State with { Heading = 90 }));
        });
        Test("engaging AP cannot reactivate armed navigation without a verified source", () => {
            var r = new Room(); r.State = r.State with { NavigationReady = false, NavigationFingerprint = "", Modes = GuidanceModes.NavArmed };
            r.Active(); Check(!r.Command("host", r.Command("host", GuidanceSetting.Master, 1)));
            Check(r.Command("host", r.Command("host", GuidanceSetting.Master, 0))); r.Valid();
        });
        Test("both adapters determine supported settings; a lost queued setting holds AP alone", () => {
            var r = new Room { PeerSettings = 4095 }; r.Active();
            Check(r.Snapshot.SupportedSettings == 4095);
            Check(!r.Command("host", r.Command("host", GuidanceSetting.FlightDirector, 1)));
            var command = r.Command("host", GuidanceSetting.Heading, 120); Check(r.Command("host", command));
            Check(r.Host.TryApplyGuidanceCommand(command, 10, _ => true));
            var physical = r.Host.Snapshot;
            r.PeerSettings &= ~(1u << (int)GuidanceSetting.Heading); r.Gate("peer");
            Check(r.Snapshot.Phase == GuidancePhase.Holding && r.Snapshot.Pending is null);
            Check(!r.Host.ExecuteQueuedGuidance(command, () => throw new Exception("Unsupported queued write")));
            Check(r.Host.Snapshot.Revision == physical.Revision); r.Valid();
        });
        Test("actual SDK fence retires queued AP-on; cached Off cannot confirm the new disconnect", () => {
            var r = new Room(); r.Active(); var on = r.Command("host", GuidanceSetting.Master, 1);
            Check(r.Command("host", on)); Check(r.Host.TryApplyGuidanceCommand(on, 10, _ => true));
            Check(r.Command("host", r.Command("host", GuidanceSetting.Master, 0)));
            var off = r.Snapshot.Pending!.Command;
            Check(r.Host.TryApplyGuidanceCommand(off, 10, _ => true));
            Check(!r.Host.ExecuteQueuedGuidance(on, () => throw new Exception("Retired AP-on reached SDK")));
            Check(r.Observe(11, r.State)); Check(r.Snapshot.Pending?.Command == off);
            var writes = 0; Check(r.Host.ExecuteQueuedGuidance(off, () => ++writes == 1));
            Check(r.Host.ConfirmGuidance("host", off, 11, r.State));
            Check(writes == 1 && r.Snapshot.Pending is null); r.Valid();
        });
        return count;
    }
}
