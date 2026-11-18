using System.Buffers.Binary;
using FsCopilot.Native;

try {
var count = 0;
void Check(bool value) { if (!value) throw new InvalidOperationException("Native bridge assertion failed"); }
void Test(string name, Action run) { run(); ++count; Console.WriteLine("PASS " + name); }
NativeControlPacket Status(string native = "abc", ulong serial = 1) => new() {
    Native = native, Serial = serial, Aircraft = "simobjects\\airplanes\\bksq-aircraft-baronpropress\\aircraft.cfg", Adapter = "bksq-baron58p-cockpit-v1",
    Flags = NativeStatusFlags.Ready | NativeStatusFlags.TargetsFresh, Engines = 2, Required = 0x7e03, Capabilities = 11
};
NativeControlPacket Reply(NativeControlPacket hello, string native = "abc", ulong serial = 1) => Status(native, serial) with {
    Kind = NativePacketKind.Reply, Backend = hello.Backend, Request = hello.Request
};
Test("fixed wire packet round-trips all 21 normalized positions and unknown markers", () => {
    var p = Status(); p.Input[0] = 0.125; p.Targets[20] = 0.875; p.Tolerance[3] = 5;
    var bytes = NativeControlWire.Encode(p); Check(bytes.Length == 1024 && NativeControlWire.TryDecode(bytes, out var copy));
    Check(NativeControlWire.TryDecode(bytes, out var result) && result!.Input[0] == 0.125 && result.Targets[20] == 0.875 && result.Input[20] == -1 && result.Tolerance[3] == 5);
    Check(bytes.SequenceEqual(NativeControlWire.Encode(result!)));
});
Test("ordinary cockpit IPC preserves independent values and refuses malformed/stale scope", () => {
    var state=new NativeCockpitPacket {Native="abc",Backend="def",Session=new string('1',32),Profile=new string('2',32),Serial=12,Count=512,Active=true};
    state.Values[0]=1013.25;state.Values[511]=999.5;state.Valid[0]=1;state.Valid[15]=1u<<31;
    var bytes=NativeCockpitWire.Encode(state);Check(bytes.Length==8192);Check(NativeCockpitWire.TryDecode(bytes,out var copy));Check(copy!.Known(511)&&copy.Values[511]==999.5);
    Check(bytes.SequenceEqual(NativeCockpitWire.Encode(copy!)));
    bytes[8191]=1;Check(!NativeCockpitWire.TryDecode(bytes,out _));
    var values=new double[512];values[0]=1014;values[1]=1013.25;
    var command=new NativeCockpitPacket {Kind=NativeCockpitKind.Apply,Native=state.Native,Backend=state.Backend,Session=state.Session,Profile=state.Profile,
        Command=1,ReadSerial=12,Values=values,Expression="1 16224 (>K:2:KOHLSMAN_SET)"};
    var main=new NativeBridgeState(true,true,Status(),""){Backend=state.Backend,Session=new(){Phase=NativeSessionPhase.Active,Backend=state.Backend,Session=state.Session}};
    Check(NativeCockpitSendGate.Allows(command,main,new(true,true,state,"")));
    Check(!NativeCockpitSendGate.Allows(command,main with {Fresh=false},new(true,true,state,"")));
    Check(!NativeCockpitSendGate.Allows(command with{Session=new string('3',32)},main,new(true,true,state,"")));
    Check(!NativeCockpitSendGate.Allows(command with{ReadSerial=1},main,new(true,true,state,"")));
    Check(!NativeCockpitSendGate.Allows(command,main with{Session=main.Session with{Phase=NativeSessionPhase.Ended}},new(true,true,state,"")));
    Check(!NativeCockpitWire.Valid(command with{Expression="x\n"}));
});
Test("ordinary recovery IPC matches the production native layout", () => {
    var bytes=File.ReadAllBytes("artifacts/cockpit-recovery/tests/native-cockpit-v2.bin");
    Check(NativeCockpitWire.TryDecode(bytes,out var packet));
    Check(packet!.ResyncRequest==1&&packet.ResyncAck==1&&packet.Recovery==new NativeCockpitProgress(2,1,1,1,0,0,"Unavailable: test instrument"));
    Check(bytes.SequenceEqual(NativeCockpitWire.Encode(packet)));
    var corrupt=bytes.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(6452),5);Check(!NativeCockpitWire.TryDecode(corrupt,out _));
    Check(!NativeCockpitWire.Valid(packet with{Recovery=new(2,1,2)}));
});
Test("EFB connection preferences and backend status preserve the independent seat and physics choices", () => {
    var settings = new NativeConnectionSettings(7, "127.0.0.1", 3600, "pilot001", "copil001", 1, 2);
    var packet = Status() with { ConnectionApi = 1, Connection = settings, BackendPhase = NativeBackendPhase.WaitingForJoin };
    Check(NativeControlWire.TryDecode(NativeControlWire.Encode(packet), out var copy) && copy!.Connection == settings && copy.BackendPhase == NativeBackendPhase.WaitingForJoin);
    Check(!NativeControlWire.Valid(packet with { ConnectionApi = 0 }));
    foreach (var invalid in new[] { settings with { Local = settings.Peer }, settings with { Port = 65536 }, settings with { Role = 3 },
        settings with { Seat = 0 }, settings with { Relay = "bad/host" }, settings with { Revision = 0 } }) Check(!NativeControlWire.Valid(packet with { Connection = invalid }));
    var bytes = NativeControlWire.Encode(packet); bytes[959] = 1; Check(!NativeControlWire.TryDecode(bytes, out _));
});
Test("backend UI reports remain distinct from observation and aircraft commands", () => {
    var report = new NativeControlPacket { Kind = NativePacketKind.Backend, Native = "abc", Backend = new string('a', 32), Request = 1,
        ConnectionApi = 1, ReportConfiguration = 8, BackendPhase = NativeBackendPhase.Ended, BackendCode = NativeBackendCode.SessionEnded };
    Check(NativeControlWire.TryDecode(NativeControlWire.Encode(report), out var copy) && copy!.ReportConfiguration == 8 && copy.Connection is null);
    Check(!NativeControlWire.Valid(report with { ConnectionApi = 0 })); Check(!NativeControlWire.Valid(report with { BackendPhase = (NativeBackendPhase)8 }));
    Check(!NativeControlWire.Valid(report with { Capabilities = 15 })); Check(!NativeControlWire.Valid(report with { Blocked = 1 }));
    Check(!NativeSessionWire.TryDecode(NativeControlWire.Encode(report), out _));
});
Test("malformed sizes, versions, kinds, positions, masks and reserved fields are rejected", () => {
    var good = NativeControlWire.Encode(Status());
    foreach (var length in new[] { 0, 1, 808, 1023, 1025 }) Check(!NativeControlWire.TryDecode(new byte[length], out _));
    foreach (var offset in new[] { 0, 4, 8, 12, 424, 432, 452, 808 }) {
        var bad = good.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(offset), uint.MaxValue);
        Check(!NativeControlWire.TryDecode(bad, out _));
    }
    var nan = good.ToArray(); BinaryPrimitives.WriteDoubleLittleEndian(nan.AsSpan(456), double.NaN); Check(!NativeControlWire.TryDecode(nan, out _));
    var text = good.ToArray(); Array.Fill(text, (byte)'a', 16, 24); Check(!NativeControlWire.TryDecode(text, out _));
});
Test("tight medium and loose tolerance percentages cross the native wire unchanged", () => {
    var p = Status() with { Tolerance = [10, 20, 30, 10] };
    Check(NativeControlWire.TryDecode(NativeControlWire.Encode(p), out var decoded));
    Check(decoded!.Tolerance.SequenceEqual(p.Tolerance));
    p.Tolerance[0] = 99; Check(!NativeControlWire.Valid(p));
});
Test("retained ClientData alone never proves a live native instance", () => {
    var monitor = new NativeBridgeMonitor(() => TimeSpan.Zero);
    Check(!monitor.Receive(Status()) && !monitor.Live && monitor.Snapshot is null);
    var hello = monitor.Hello(); Check(NativeControlWire.Valid(hello));
    Check(!monitor.Receive(Reply(hello) with { Backend = Guid.NewGuid().ToString("N") }));
    Check(!monitor.Receive(Reply(hello) with { Request = hello.Request + 1 }));
    Check(monitor.Receive(Reply(hello)) && monitor.Live);
});
Test("hello replies and observations expire independently and cannot refresh by replay", () => {
    var now = TimeSpan.Zero; var monitor = new NativeBridgeMonitor(() => now); var hello = monitor.Hello();
    Check(monitor.Receive(Reply(hello)));
    now = TimeSpan.FromSeconds(2); Check(!monitor.Receive(Reply(hello))); Check(monitor.Receive(Status(serial: 2)));
    now = TimeSpan.FromSeconds(3.1); Check(!monitor.Live);
    Check(!monitor.Receive(Status(serial: 3))); // Periodic packets cannot rebind a stale channel.
    var fresh = monitor.Hello(); Check(monitor.Receive(Reply(fresh, serial: 4)) && monitor.Live);
    now = TimeSpan.FromSeconds(6.2); Check(!monitor.Receive(Reply(fresh, serial: 5)) && !monitor.Live);
});
Test("fresh restart challenge retires old native identity and rejects delayed old status", () => {
    var monitor = new NativeBridgeMonitor(() => TimeSpan.Zero); var hello = monitor.Hello(); Check(monitor.Receive(Reply(hello, serial: 50)));
    var next = monitor.Hello(); Check(monitor.Receive(Reply(next, "def", 1)));
    Check(!monitor.Receive(Status(serial: 51)) && monitor.Snapshot!.Native == "def");
    Check(monitor.Receive(Status("def", 2)));
    var old = monitor.Hello(); Check(!monitor.Receive(Reply(old, serial: 99)));
});
Test("two native instances answering one challenge invalidate the link", () => {
    var monitor = new NativeBridgeMonitor(() => TimeSpan.Zero); var hello = monitor.Hello();
    Check(monitor.Receive(Reply(hello))); Check(!monitor.Receive(Reply(hello, "def")));
    Check(monitor.ConflictingInstances && !monitor.Live && !monitor.Receive(Status(serial: 10)));
});
Test("caller and snapshot arrays cannot change retained observations", () => {
    var monitor = new NativeBridgeMonitor(() => TimeSpan.Zero); var reply = Reply(monitor.Hello()); reply.Input[9] = 0.4;
    Check(monitor.Receive(reply)); reply.Input[9] = 0.9;
    var copy = monitor.Snapshot!; copy.Input[9] = 0.8; copy.Tolerance[0] = 1;
    Check(monitor.Snapshot!.Input[9] == 0.4 && monitor.Snapshot.Tolerance[0] == 10);
});
Test("independent monitors cannot accept each other's native challenge reply", () => {
    var a = new NativeBridgeMonitor(() => TimeSpan.Zero); var b = new NativeBridgeMonitor(() => TimeSpan.Zero);
    var first = a.Hello(); b.Hello(); Check(!b.Receive(Reply(first)) && !b.Live);
});
var fixture = args.FirstOrDefault() ?? "artifacts/efb-control-test/tests/native-bridge-status.bin";
Test("actual startup packet normalizes internal unknown axes before crossing the native wire", () => {
    var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(fixture)!, "native-bridge-unobserved.bin"));
    Check(NativeControlWire.TryDecode(bytes, out var p));
    Check(p!.Input.All(value => value == -1) && p.Fresh == 0 && p.Learned == 0);
    Check(NativeControlWire.Encode(p).SequenceEqual(bytes));
});
Test("actual WASM-module packet decodes in C# and re-encodes byte for byte", () => {
    var bytes = File.ReadAllBytes(fixture); Check(NativeControlWire.TryDecode(bytes, out var p));
    Check(p!.Kind == NativePacketKind.Reply && p.Request == 314 && p.Blocked == 14 && p.Engines == 4 && p.Capabilities == 59);
    Check((p.Capabilities & 4) == 0 && p.Required == (((1u << 21) - 1) & ~(7u << 6))); // Trim slots are reserved, not physically owned.
    Check(NativeControlWire.Encode(p).SequenceEqual(bytes));
});
Test("actual native session status and heartbeat round-trip across C++ and C#", () => {
    foreach (var name in new[] { "session-status.bin", "session-command.bin" }) {
        var bytes = File.ReadAllBytes(Path.Combine("artifacts/native-session/tests", name));
        Check(NativeSessionWire.TryDecode(bytes, out var p) && NativeSessionWire.Encode(p!).SequenceEqual(bytes));
        Check(p!.Epoch == 7 && p.Consent.Length == 32 && p.Consent.EndsWith("0000000000000001", StringComparison.Ordinal) && p.Engines == 2 && p.Axes == 0x7e03 && p.Generation.All(value => value == 1));
        if (name == "session-status.bin") Check(p.Phase == NativeSessionPhase.Active && p.Result == NativeSessionResult.Succeeded && p.Blocked == 15 && p.Ack == 1);
        else Check(p.Kind == NativeSessionKind.Heartbeat && p.Command == 2);
    }
});
NativeSessionPacket Shared() => new() {
    Native = "abc", Backend = new string('a', 32), Session = new string('b', 32), Consent = new string('1', 32), Epoch = 7,
    Revision = 3, Generation = [1, 2, 1, 1], Owner = [NativeOwner.Local, NativeOwner.Remote, NativeOwner.Local, NativeOwner.Local],
    Engines = 2, Axes = 0x7e03, Attached = 2, Flags = NativeSessionFlags.PhysicsHost, Phase = NativeSessionPhase.Active,
    Result = NativeSessionResult.Succeeded, Serial = 10, CommittedTransfer = ["", new string('4', 32), "", ""]
};
NativeSessionPacket Command(NativeSessionPacket state, NativeSessionKind kind = NativeSessionKind.Input) => state.Copy() with {
    Kind = kind, Command = 42, Serial = 0, Ack = 0, Phase = NativeSessionPhase.Idle, Result = NativeSessionResult.Idle,
    Flags = NativeSessionFlags.None, Selection = 2, Sequence = 1, Axes = (1u << 9) | (1u << 12), Blocked = 0,
    Positions = Enumerable.Repeat(0.5d, 21).ToArray()
};
Test("native session wire rejects malformed metadata, reserved bytes and noncanonical strings", () => {
    var good = NativeSessionWire.Encode(Shared());
    foreach (var offset in new[] { 0, 4, 8, 12, 256, 272, 280, 292, 488, 672, 676, 680, 1023 }) {
        var bad = good.ToArray(); bad[offset] = 255; Check(!NativeSessionWire.TryDecode(bad, out _));
    }
    var padding = good.ToArray(); padding[79] = 1; Check(!NativeSessionWire.TryDecode(padding, out _));
    var nan = good.ToArray(); BinaryPrimitives.WriteDoubleLittleEndian(nan.AsSpan(304), double.NaN); Check(!NativeSessionWire.TryDecode(nan, out _));
    Check(!NativeSessionWire.Valid(Shared() with { Pending = 2 }));
    Check(!NativeSessionWire.Valid(Shared() with { PendingTransfer = ["", new string('2', 32), "", ""] }));
    Check(!NativeSessionWire.Valid(Command(Shared()) with { Ack = 1 }));
});
Test("all command kinds keep discovery and session protocols separate", () => {
    foreach (var kind in Enum.GetValues<NativeSessionKind>().Where(value => value != NativeSessionKind.Status)) {
        var command = Command(Shared(), kind);
        if (kind is NativeSessionKind.World or NativeSessionKind.Feedback) command = command with { Selection = 0 };
        if (kind == NativeSessionKind.Barometer) command = command with {
            Selection = 0, Sequence = 0, Axes = 0, Positions = Enumerable.Repeat(-1d, 21).ToArray(),
            Barometer = new(1, 2, 3, 4, 0, NativeBarometerAction.Gate, 0, 0, false, 3, 1, 18)
        };
        if (kind == NativeSessionKind.InstrumentFeedback) command = command with {
            Selection = 0, Sequence = 0, Axes = 0, Positions = Enumerable.Repeat(-1d, 21).ToArray(),
            InstrumentFeedback = new(1, 2, 3, 4, 2, 0, 3, 0)
        };
        var bytes = NativeSessionWire.Encode(command);
        Check(NativeSessionWire.TryDecode(bytes, out var copy) && copy!.Kind == kind);
        Check(!NativeControlWire.TryDecode(bytes, out _));
    }
    Check(!NativeSessionWire.TryDecode(NativeControlWire.Encode(Status()), out _));
});
Test("Windows writer gate rejects queued values after ownership, native instance or backend changes", () => {
    var state = Shared(); var command = Command(state); var observation = Status();
    Check(NativeSessionSendGate.Allows(command, state.Backend, observation, state));
    Check(!NativeSessionSendGate.Allows(command, new string('c', 32), observation, state));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, Status("def"), state));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, observation, state with { Phase = NativeSessionPhase.Ended }));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, observation, state with { Owner = [NativeOwner.Local, NativeOwner.Local, NativeOwner.Local, NativeOwner.Local] }));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, observation, state with { Generation = [1, 3, 1, 1] }));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, observation, state with { Revision = 4 }));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, observation, state with { Pending = 2, PendingTransfer = ["", new string('2', 32), "", ""] }));
    Check(!NativeSessionSendGate.Allows(command with { Axes = 1u << 9 }, state.Backend, observation, state));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, observation, state with { Flags = NativeSessionFlags.None }));
});
Test("Windows bind gate requires armed local consent and current ready native data", () => {
    var state = Shared() with { Phase = NativeSessionPhase.Armed, Backend = "", Session = "", Epoch = 0 };
    var command = Command(Shared(), NativeSessionKind.Bind);
    Check(NativeSessionSendGate.Allows(command, command.Backend, Status(), state));
    Check(!NativeSessionSendGate.Allows(command, command.Backend, Status(), state with { Consent = new string('2', 32) }));
    Check(!NativeSessionSendGate.Allows(command, command.Backend, Status(), state with { Phase = NativeSessionPhase.Idle }));
    Check(!NativeSessionSendGate.Allows(command, command.Backend, Status() with { Flags = NativeStatusFlags.Applying }, state));
});
Test("session arrays are copied and C# emits a command for the actual native parser", () => {
    var source = Shared(); var copy = source.Copy();
    source.Generation[0] = 99; source.Owner[1] = NativeOwner.None; source.Positions[9] = 0.1; source.PendingTransfer[1] = new string('f', 32); source.CommittedTransfer[1] = "";
    Check(copy.Generation[0] == 1 && copy.Owner[1] == NativeOwner.Remote && copy.Positions[9] == -1 && copy.PendingTransfer[1] == "");
    Check(copy.CommittedTransfer[1] == new string('4', 32));
    var packet = Command(Shared(), NativeSessionKind.Prepare) with {
        Flags = NativeSessionFlags.Incoming, Axes = 0x7e03, Attached = 0, Pending = 2, Ticket = new string('2', 32), PendingTransfer = ["", new string('2', 32), "", ""]
    };
    File.WriteAllBytes("artifacts/native-session/tests/session-from-csharp.bin", NativeSessionWire.Encode(packet));
});
Test("shared Auto feedback round trips without accepting steering or noncanonical booleans", () => {
    var state = Shared() with { Automatic = true, AutomaticWaiting = 2, IntentId = 8, IntentSelection = 14, IntentAction = NativeSessionIntent.AutoOff };
    Check(NativeSessionWire.TryDecode(NativeSessionWire.Encode(state), out var copy) && copy!.Automatic && copy.AutomaticWaiting == 2 && copy.IntentAction == NativeSessionIntent.AutoOff);
    Check(!NativeSessionWire.Valid(state with { AutomaticWaiting = 1 }));
    Check(!NativeSessionWire.Valid(state with { Automatic = false }));
    Check(!NativeSessionWire.Valid(state with { IntentSelection = 2 }));
    var bytes = NativeSessionWire.Encode(state); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(968), 2);
    Check(!NativeSessionWire.TryDecode(bytes, out _));
    var feedback = Command(Shared(), NativeSessionKind.Feedback) with { Selection = 0, Automatic = true, AutomaticWaiting = 2 };
    Check(NativeSessionSendGate.Allows(feedback, state.Backend, Status(), state));
    Check(!NativeSessionSendGate.Allows(feedback with { Revision = state.Revision - 1 }, state.Backend, Status(), state));
    Check(!NativeSessionSendGate.Allows(feedback, state.Backend, Status(), state with { Automatic = false }));
});
Test("autopilot status crosses the session wire without becoming a command flag", () => {
    var state = Shared() with { Flags = NativeSessionFlags.PhysicsHost | NativeSessionFlags.Autopilot };
    Check(NativeSessionWire.TryDecode(NativeSessionWire.Encode(state), out var copy) && copy!.Flags.HasFlag(NativeSessionFlags.Autopilot));
    Check(!NativeSessionWire.Valid(Command(state) with { Flags = NativeSessionFlags.Autopilot }));
});
Test("EFB intentions retain their scope and cannot be forged on command packets", () => {
    var state = Shared() with { IntentId = 42, IntentSelection = 14, IntentAction = NativeSessionIntent.Me, IntentWaiting = 14 };
    Check(NativeSessionWire.TryDecode(NativeSessionWire.Encode(state), out var copy) && copy!.IntentId == 42 && copy.IntentSelection == 14 && copy.IntentWaiting == 14);
    Check(!NativeSessionWire.Valid(state with { IntentId = 0 }));
    Check(!NativeSessionWire.Valid(state with { IntentOther = 2 }));
    Check(!NativeSessionWire.Valid(state with { IntentAction = (NativeSessionIntent)7 }));
    Check(!NativeSessionWire.Valid(Command(state) with { IntentId = 42, IntentSelection = 14, IntentAction = NativeSessionIntent.Me }));
    var ack = Command(Shared(), NativeSessionKind.IntentAck) with { Sequence = 42 };
    Check(NativeSessionSendGate.Allows(ack, state.Backend, Status(), state));
    Check(!NativeSessionSendGate.Allows(ack with { Sequence = 41 }, state.Backend, Status(), state));
    Check(!NativeSessionSendGate.Allows(ack, state.Backend, Status(), state with { Phase = NativeSessionPhase.Ended }));
});
Test("world status and commands preserve physical units, copy values and fence the physics host", () => {
    double[] values = [0.8, 0.15, 4321, 0.1, -0.1, 2.5, 5, 3, 160, 0.1, 0.2, 0.3];
    var state = Shared() with { Flags = NativeSessionFlags.World | NativeSessionFlags.WorldFresh | NativeSessionFlags.WorldFrozen, World = values, WorldSerial = 99 };
    Check(NativeSessionWire.TryDecode(NativeSessionWire.Encode(state), out var copy) && copy!.World.SequenceEqual(values) && copy.WorldSerial == 99);
    var privateCopy = state.Copy(); values[2] = 9999; Check(privateCopy.World[2] == 4321);
    var command = Command(Shared(), NativeSessionKind.World) with { Selection = 0, Axes = state.Axes, World = privateCopy.World };
    Check(NativeSessionSendGate.Allows(command, state.Backend, Status(), state));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, Status(), state with { Flags = state.Flags | NativeSessionFlags.PhysicsHost }));
    Check(!NativeSessionSendGate.Allows(command with { Generation = [1, 99, 1, 1] }, state.Backend, Status(), state));
    Check(!NativeSessionWire.Valid(state with { WorldSerial = 0 }));
    var invalid = privateCopy.World.ToArray(); invalid[0] = double.NaN;
    Check(!NativeSessionWire.Valid(command with { World = invalid }));
    invalid[0] = 2; Check(!NativeSessionWire.Valid(command with { World = invalid }));
    Check(!NativeSessionWire.Valid(Command(Shared()) with { World = privateCopy.World }));
});
Test("seat feedback and FIFO intentions round-trip without becoming control commands", () => {
    var state = Shared() with { SeatRevision = 2, Seat = 1, SeatPending = 2, SeatRequest = 7,
        IntentId = 40, IntentAction = NativeSessionIntent.SeatAccept, IntentSelection = 0, IntentSeatRevision = 2, IntentSeatRequest = 7 };
    Check(NativeSessionWire.TryDecode(NativeSessionWire.Encode(state), out var copy) && copy!.Seat == 1 && copy.SeatRequest == 7 && copy.IntentSeatRequest == 7);
    Check(!NativeSessionWire.Valid(state with { IntentSelection = 1 }));
    Check(!NativeSessionWire.Valid(state with { SeatPending = 0 }));
    Check(!NativeSessionWire.Valid(state with { IntentSeatRevision = 0 }));
    Check(!NativeSessionWire.Valid(state with { IntentSeatRequest = 0 }));
    var feedback = Command(Shared(), NativeSessionKind.Feedback) with { Selection = 0, SeatRevision = 2, Seat = 1, SeatPending = 2, SeatRequest = 7 };
    Check(NativeSessionWire.Valid(feedback) && NativeSessionSendGate.Allows(feedback, state.Backend, Status(), state));
    Check(!NativeSessionWire.Valid(feedback with { Kind = NativeSessionKind.Input }));
    var bytes = NativeSessionWire.Encode(state); bytes[1016] = 1; Check(!NativeSessionWire.TryDecode(bytes, out _));
});
Test("indexed tuned pressures and STD flags cross discovery without granting instrument authority", () => {
    var packet = Status() with { Altimeters = new(18, 15986, 16200, 1, true) };
    Check(NativeControlWire.TryDecode(NativeControlWire.Encode(packet), out var copy) && copy!.Altimeters == packet.Altimeters);
    Check(copy!.Capabilities == 11 && copy.Altimeters.Pressure1 != copy.Altimeters.Pressure2);
    Check(NativeControlWire.Valid(packet with { Altimeters = packet.Altimeters with { Fresh = false } }));
    Check(!NativeControlWire.Valid(packet with { Altimeters = packet.Altimeters with { Serial = 0 } }));
    Check(!NativeControlWire.Valid(packet with { Altimeters = packet.Altimeters with { StandardMask = 4 } }));
    Check(!NativeControlWire.Valid(packet with { Altimeters = packet.Altimeters with { Pressure2 = 0 } }));
    var bytes = NativeControlWire.Encode(packet); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1012), 1024);
    Check(!NativeControlWire.TryDecode(bytes, out _));
    Check(!NativeControlWire.Valid(packet with { Kind = NativePacketKind.Hello }));
    Check(!NativeControlWire.Valid(packet with { Kind = NativePacketKind.Backend }));
});
Test("barometer commands have a typed body, canonical padding and independent acknowledgements", () => {
    var body = new NativeBarometerCommand(1, 2, 3, 4, 8, NativeBarometerAction.Apply, 2, 16200, true, 0, 1, 18);
    var command = Command(Shared(), NativeSessionKind.Barometer) with {
        Selection = 0, Sequence = 0, Axes = 0, Positions = Enumerable.Repeat(-1d, 21).ToArray(), Barometer = body
    };
    var bytes = NativeSessionWire.Encode(command);
    Check(NativeSessionWire.TryDecode(bytes, out var copy) && copy!.Barometer == body && copy.World.All(value => value == 0));
    Check(!NativeSessionSendGate.Allows(command, command.Backend, Status(), Shared())); // No verified production input adapter yet.
    foreach (var offset in new[] { 936, 959 }) {
        var bad = bytes.ToArray(); bad[offset] = 1; Check(!NativeSessionWire.TryDecode(bad, out _));
    }
    var boolean = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(boolean.AsSpan(916), 2);
    Check(!NativeSessionWire.TryDecode(boolean, out _));
    Check(!NativeSessionWire.Valid(command with { Barometer = body with { SeatRevision = 0 } }));
    Check(!NativeSessionWire.Valid(command with { Barometer = body with { Sequence = 0 } }));
    Check(!NativeSessionWire.Valid(command with { Command = NativeBarometerAck.MaxCommand + 1UL }));
    Check(!NativeSessionWire.Valid(command with { BarometerGateAck = NativeBarometerAck.Pack(1, NativeBarometerResult.Pending) }));
    var state = Shared() with {
        Command = 44, Ack = 7,
        BarometerGateAck = NativeBarometerAck.Pack(44, NativeBarometerResult.Succeeded),
        BarometerValueAck = NativeBarometerAck.Pack(43, NativeBarometerResult.Pending)
    };
    Check(NativeSessionWire.TryDecode(NativeSessionWire.Encode(state), out copy) && copy!.Ack == 7 &&
        NativeBarometerAck.Command(copy.BarometerGateAck) == 44 && NativeBarometerAck.Command(copy.BarometerValueAck) == 43);
    Check(!NativeSessionWire.Valid(state with { Command = 42 }));
    Check(NativeBarometerAck.Valid(NativeBarometerAck.Pack(NativeBarometerAck.MaxCommand, NativeBarometerResult.Unsupported)));
    Directory.CreateDirectory("artifacts/altimeter-native-command/tests");
    File.WriteAllBytes("artifacts/altimeter-native-command/tests/barometer-from-csharp.bin", bytes);
    File.WriteAllBytes("artifacts/altimeter-native-command/tests/barometer-status-from-csharp.bin", NativeSessionWire.Encode(state));
});
Test("native instrument metadata distinguishes transport from verified input gates", () => {
    var baseline = Status() with { Altimeters = new(18, 16000, 16200, 0, true, true, true, true, 2) };
    Check(NativeControlWire.TryDecode(NativeControlWire.Encode(baseline), out var copy) && copy!.Altimeters == baseline.Altimeters);
    Check(!NativeControlWire.Valid(baseline with { Altimeters = baseline.Altimeters with { Transport = false } }));
    Check(!NativeControlWire.Valid(baseline with { Altimeters = baseline.Altimeters with { Profile = false } }));
    Check(!NativeControlWire.Valid(baseline with { Altimeters = baseline.Altimeters with { Fresh = false } }));
    Check(!NativeControlWire.Valid(baseline with { Altimeters = baseline.Altimeters with { Blocked = 4 } }));
    var startup = Status() with { Altimeters = new(0, 0, 0, 0, false, true) };
    Check(NativeControlWire.TryDecode(NativeControlWire.Encode(startup), out copy) && copy!.Altimeters.Transport && !copy.Altimeters.Profile);
    Check(!NativeControlWire.Valid(startup with { Kind = NativePacketKind.Hello }));
    Check(!NativeControlWire.Valid(startup with { Kind = NativePacketKind.Backend }));
    File.WriteAllBytes("artifacts/altimeter-native-command/tests/barometer-observation-from-csharp.bin", NativeControlWire.Encode(baseline));
});
Test("instrument writer preflight requires current seat and verified fresh native gate proof", () => {
    var state = Shared() with { SeatRevision = 4, Seat = 1 };
    var proof = Status() with { Altimeters = new(18, 16000, 16200, 0, true, true, true, true, 2) };
    var body = new NativeBarometerCommand(1, 2, 3, 4, 8, NativeBarometerAction.Apply, 2, 16200, true, 0, 1, 18);
    var command = Command(state, NativeSessionKind.Barometer) with {
        SeatRevision = 0, Seat = 0, Selection = 0, Sequence = 0, Axes = 0, Positions = Enumerable.Repeat(-1d, 21).ToArray(), Barometer = body
    };
    bool Allows(NativeSessionPacket p, NativeControlPacket? observation = null, NativeSessionPacket? current = null) =>
        NativeSessionSendGate.Allows(p, state.Backend, observation ?? proof, current ?? state);
    Check(Allows(command));
    Check(!Allows(command, proof with { Altimeters = proof.Altimeters with { Profile = false, GateReady = false } }));
    Check(!Allows(command, proof with { Altimeters = proof.Altimeters with { Fresh = false, GateReady = false } }));
    Check(!Allows(command, current: state with { SeatRevision = 5 }));
    Check(!Allows(command with { Barometer = body with { Seat = 2 } }));
    Check(!Allows(command with { Barometer = body with { ReadSerial = 19 } }));
    Check(!Allows(command with { Barometer = body with { Instrument = 1 } }));
    Check(Allows(command with { Barometer = body with { Instrument = 1, Action = NativeBarometerAction.Observe } }));
    Check(!Allows(command with { Barometer = body with { Action = NativeBarometerAction.Observe } }));
    var seed = command with { Barometer = body with { Action = NativeBarometerAction.Seed, Sequence = 0 } };
    Check(!Allows(seed));
    Check(Allows(seed, proof with { Altimeters = proof.Altimeters with { Blocked = 3 } }));
    Check(!Allows(command, proof with { Altimeters = proof.Altimeters with { GateReady = false } }));
    var gate = command with { Barometer = body with { Action = NativeBarometerAction.Gate, Instrument = 0, Pressure16 = 0, Standard = false, Sequence = 0, Mask = 3 } };
    Check(Allows(gate, proof with { Altimeters = proof.Altimeters with { GateReady = false } }));
    Check(!Allows(gate with { Barometer = gate.Barometer! with { Mask = 1 } }));
    Check(Allows(gate with { Barometer = gate.Barometer! with { Action = NativeBarometerAction.Stop, Mask = 0 } }));
});
Test("actual native instrument status retains separate command acknowledgements", () => {
    var bytes = File.ReadAllBytes("artifacts/altimeter-native-command/tests/barometer-status-from-cpp.bin");
    Check(NativeSessionWire.TryDecode(bytes, out var state) && NativeSessionWire.Encode(state!).SequenceEqual(bytes));
    Check(state!.Ack == 1 && NativeBarometerAck.Command(state.BarometerGateAck) == 3 &&
        NativeBarometerAck.Command(state.BarometerValueAck) == 5 &&
        NativeBarometerAck.Result(state.BarometerValueAck) == NativeBarometerResult.Succeeded);
    bytes = File.ReadAllBytes("artifacts/altimeter-native-command/tests/barometer-observation-from-cpp.bin");
    Check(NativeControlWire.TryDecode(bytes, out var observation) && NativeControlWire.Encode(observation!).SequenceEqual(bytes));
    Check(observation!.Altimeters.Transport && !observation.Altimeters.Profile && !observation.Altimeters.GateReady);
});
Test("instrument display reports have a typed scoped body and cannot be used as simulator proof", () => {
    var state = Shared() with { SeatRevision = 4, Seat = 1 };
    var command = Command(Shared(), NativeSessionKind.InstrumentFeedback) with {
        Selection = 0, Sequence = 0, Axes = 0, Positions = Enumerable.Repeat(-1d, 21).ToArray(),
        InstrumentFeedback = new(1, 2, 3, 4, 2, 3, 3, 0)
    };
    var bytes = NativeSessionWire.Encode(command);
    Check(NativeSessionWire.TryDecode(bytes, out var copy) && copy!.InstrumentFeedback == command.InstrumentFeedback && copy.World.All(value => value == 0));
    var observation = Status() with { Altimeters = new(18, 16000, 16200, 0, true, true) };
    Check(NativeSessionSendGate.Allows(command, state.Backend, observation, state));
    Check(!NativeSessionSendGate.Allows(command, state.Backend, observation, state with { SeatRevision = 5 }));
    bytes[959] = 1; Check(!NativeSessionWire.TryDecode(bytes, out _));
    Check(!NativeSessionWire.Valid(command with { InstrumentFeedback = command.InstrumentFeedback! with { Phase = 9 } }));
    Check(!NativeSessionWire.Valid(command with { InstrumentFeedback = command.InstrumentFeedback! with { Protocol = 2 } }));
    File.WriteAllBytes("artifacts/altimeter-native-command/tests/instrument-feedback-from-csharp.bin", NativeSessionWire.Encode(command));
});
Test("local input flags cross the wire and block delayed backend instrument commands", () => {
    var local = Status() with { Altimeters = new(18, 16000, 16200, 0, true, true, false, false, 0, true, false, false, true) };
    Check(NativeControlWire.TryDecode(NativeControlWire.Encode(local), out var copy) && copy!.Altimeters == local.Altimeters);
    Check(copy!.Altimeters.LocalOnly && copy.Altimeters.InputReady && !copy.Altimeters.Profile && copy.Altimeters.Flags == 579);
    Check(!NativeControlWire.Valid(local with { Altimeters = local.Altimeters with { Recovering = true } }));
    var state = Shared() with { SeatRevision = 4, Seat = 1 };
    var command = Command(Shared(), NativeSessionKind.Barometer) with {
        Selection = 0, Sequence = 0, Axes = 0, Positions = Enumerable.Repeat(-1d, 21).ToArray(),
        Barometer = new(1, 2, 3, 4, 8, NativeBarometerAction.Apply, 2, 16200, false, 0, 1, 18)
    };
    Check(!NativeSessionSendGate.Allows(command, state.Backend, local, state));
    var held = local with { Altimeters = local.Altimeters with { LocalOnly = false, Profile = true, Held = true, Blocked = 3 } };
    Check(!NativeSessionSendGate.Allows(command, state.Backend, held, state));
});
Test("session retention does not turn delayed observations into fresh aircraft data", () => {
    var now=TimeSpan.Zero;
    var monitor=new NativeBridgeMonitor(()=>now,NativeSessionTiming.Recovery);
    Check(monitor.Receive(Reply(monitor.Hello())) && monitor.Live && monitor.ObservationsFresh);
    now=TimeSpan.FromSeconds(2);
    Check(monitor.Live && !monitor.ObservationsFresh && monitor.Snapshot!.Native=="abc");
    Check(monitor.Receive(Status("abc",2)) && monitor.ObservationsFresh);
    now=TimeSpan.FromSeconds(10.01);
    Check(!monitor.Live && !monitor.ObservationsFresh && !monitor.Receive(Status("abc",3)));
    Check(monitor.Receive(Reply(monitor.Hello(),serial:4)) && monitor.Live && monitor.ObservationsFresh);
});
count += GuidanceWireTests.Run();
Console.WriteLine($"{count} native bridge/session contract scenarios passed. No simulator controls were written.");
} catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
