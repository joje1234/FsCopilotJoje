using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using FsCopilot.Native;
using FsCopilot.Ownership;
using Microsoft.FlightSimulator.SimConnect;
[assembly: SupportedOSPlatform("windows")]

try {
    HelperInstanceChecks.Run();
    foreach (var ap in new[] { false, true }) await RejoinChecks.Run(ap);
    Console.WriteLine("PASS both production Windows clients: persistent SDK connection, new-session numbering, within-session replay refusal, retired queue, expiry and current write fences. No MSFS access.");
} catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }

static class RejoinChecks
{
    public static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    public static async Task Wait(Func<bool> condition, string reason) {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(5);
        Check(condition(), reason);
    }
    public static async Task Run(bool ap) {
        using var f = new Fixture(ap); await f.Start();
        await f.Write(2000); await f.Drop(2000); await f.Drop(1999);
        f.Detach(); await f.Drop(2000); // Same-session re-fencing is not a replay-counter reset.
        await f.Write(2001);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var before = f.Connection.Writes.Count; var dropped = f.Dropped.Count;
        Check(f.Send(2002, write => { entered.Set(); Check(release.Wait(2000), "Old queued callback was not released"); return write(); }), "Old-scope command was not queued");
        await Wait(() => entered.IsSet, "Actual worker did not reach delayed execution fence");
        f.Session('c'); release.Set();
        await f.Ready();
        Check(f.Connection.Writes.Count == before, "A retired queued command reached the SDK");
        Check(f.Dropped.Count == dropped, "Retired failure notification leaked into the new session");
        await f.Write(1); // Reproduces the original bug: old connection counter was 2001.
        await f.Drop(1);
        dropped = f.Dropped.Count;
        Check(f.Send(2, _ => false), "Declined model-fenced command was not queued");
        await Wait(() => f.Dropped.Count > dropped, "Declined model fence was ignored");
        await f.Write(2); // A command not dispatched must not advance the counter.
        dropped = f.Dropped.Count;
        Check(f.Send(3, write => { Thread.Sleep(300); return write(); }), "Expiry fixture was not queued");
        await Wait(() => f.Dropped.Count > dropped, "Expired command was not dropped");
        await f.Write(3);
        f.Session('d'); await f.Ready(); await f.Write(1);
        Check(!f.SendRetired(), "Old-session packet passed the current primary/fence checks");
        await f.Write(1500);
        f.Detach(); await f.Drop(1); await f.Write(1501);
        Check(SimConnect.Connections.Count(x => x.Name == f.Connection.Name) == 1 && !f.Connection.Disposed,
            "Test reconnected the SDK instead of exercising a persistent connection");
        Console.WriteLine($"PASS {(ap ? "AP" : "ordinary cockpit")}: command 1 dispatched after two rejoins on the same Windows SDK connection; duplicate, retired, expired and declined writes refused.");
    }
}

sealed class Fixture : IDisposable
{
    private const string Backend = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Profile = "dddddddddddddddddddddddddddddddd";
    private readonly bool _ap;
    private readonly NativeCockpitClient? _cockpit;
    private readonly NativeGuidanceClient? _guidance;
    private NativeBridgeState _main = null!;
    private Timer? _timer;
    private long _serial;
    public SimConnect Connection { get; private set; } = null!;
    public readonly ConcurrentQueue<ulong> Dropped = new();
    private string Current => Volatile.Read(ref _main).Session!.Session;
    public Fixture(bool ap) {
        _ap = ap; Session('b');
        if (ap) { _guidance = new(() => Volatile.Read(ref _main), "test"); _guidance.Dropped += Dropped.Enqueue; }
        else { _cockpit = new(() => Volatile.Read(ref _main), "test"); _cockpit.Dropped += Dropped.Enqueue; }
    }
    public async Task Start() {
        var name = _ap ? "FS Copilot AP bridge" : "FS Copilot Cockpit bridge";
        await RejoinChecks.Wait(() => SimConnect.Connections.Any(c => c.Name == name), "Production client did not open SDK fixture");
        Connection = SimConnect.Connections.Single(c => c.Name == name);
        await Connection.Registered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        _timer = new(_ => Publish(), null, 0, 100); await Ready();
    }
    public void Session(char id) {
        Detach();
        Volatile.Write(ref _main, new(true, true, new NativeControlPacket { Native = "abc", Capabilities = 63 }, "") {
            Backend = Backend, Session = new() { Native = "abc", Backend = Backend, Session = new string(id,32),
                Consent = new string('c',32), Epoch = 7, Revision = 4, Phase = NativeSessionPhase.Active,
                Flags = NativeSessionFlags.PhysicsHost }
        });
    }
    public void Detach() { _cockpit?.SetFence(null); _guidance?.SetFence(null); }
    private void Publish() {
        var session = Current; var serial = (ulong)Interlocked.Increment(ref _serial);
        if (_ap) Connection.Status(NativeGuidanceWire.Encode(new() {
            Kind = NativeGuidanceKind.Status, Native = "abc", Backend = Backend, Session = session, Consent = new string('c',32),
            Epoch = 7, ControlRevision = 4, Serial = serial, ReadSerial = 12, Action = NativeGuidanceAction.Gate,
            CommandEpoch = 2, NavigationEpoch = 3, Preparation = 4, Revision = 5, Capture = true, Host = true,
            Flags = NativeGuidanceFlags.Transport | NativeGuidanceFlags.Fresh | NativeGuidanceFlags.Profile | NativeGuidanceFlags.GateReady |
                NativeGuidanceFlags.Capture | NativeGuidanceFlags.SingleHostEngine | NativeGuidanceFlags.NavigationReady,
            NavigationFingerprint = new string('a',64), Adapter = "test-absolute-ap-v1", Supported = 8191, Blocked = 8191,
            Slots = NativeGuidanceWire.Slots(1,2,4), TargetSlots = NativeGuidanceWire.Slots(1,1,1)
        }));
        else Connection.Status(NativeCockpitWire.Encode(new() {
            Native = "abc", Backend = Backend, Session = session, Profile = Profile, Serial = serial,
            Active = true, Count = 2, Valid = [3,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]
        }));
    }
    public Task Ready() => RejoinChecks.Wait(() => _ap ? _guidance!.State is {Live:true, Snapshot:{ } apPacket} && apPacket.Session == Current :
        _cockpit!.State is {Live:true, Packet:{ } cockpitPacket} && cockpitPacket.Session == Current, "Current session status did not arrive");
    public bool Send(ulong id, Func<Func<bool>,bool>? execute = null) {
        if (_ap) {
            var p = new NativeGuidancePacket { Native = "abc", Backend = Backend, Session = Current, Consent = new string('c',32), Epoch = 7,
                ControlRevision = 4, Command = id, CommandEpoch = 2, NavigationEpoch = 3, Preparation = 4, Revision = 5,
                Sequence = id, Action = NativeGuidanceAction.Apply, Setting = GuidanceSetting.Heading, Value = 170, Host = true, ReadSerial = 12 };
            _guidance!.SetFence(new(null,p)); return _guidance.TrySend(p,execute);
        }
        var packet = new NativeCockpitPacket {Kind = NativeCockpitKind.Apply, Native = "abc", Backend = Backend, Session = Current,
            Profile = Profile, Command = id, ReadSerial = _cockpit!.State.Packet!.Serial, Expression = "1 (>L:TEST_SWITCH, Number)"};
        _cockpit.SetFence(new("abc",Backend,Current,Profile)); return _cockpit.TrySend(packet,execute);
    }
    public bool SendRetired() {
        if (_ap) return _guidance!.TrySend(new() {Native="abc", Backend=Backend,Session=new string('b',32),Consent=new string('c',32),
            Epoch=7,ControlRevision=4,Command=9999,CommandEpoch=2,NavigationEpoch=3,Preparation=4,Revision=5,Sequence=9999,
            Action=NativeGuidanceAction.Apply,Setting=GuidanceSetting.Heading,Value=170,Host=true,ReadSerial=12},null);
        return _cockpit!.TrySend(new(){Kind=NativeCockpitKind.Apply,Native="abc",Backend=Backend,Session=new string('b',32),Profile=Profile,
            Command=9999,ReadSerial=_cockpit.State.Packet!.Serial,Expression="1 (>L:TEST_SWITCH, Number)"},null);
    }
    public async Task Write(ulong id) {
        var before=Connection.Writes.Count; RejoinChecks.Check(Send(id),"Current command did not enter queue: "+id);
        await RejoinChecks.Wait(()=>Connection.Writes.Count==before+1,"Current command did not dispatch: "+id);
    }
    public async Task Drop(ulong id) {
        var before=Dropped.Count;var writes=Connection.Writes.Count;
        RejoinChecks.Check(Send(id),"Duplicate fixture did not enter queue");
        await RejoinChecks.Wait(()=>Dropped.Count>before,"Duplicate command was not dropped");
        RejoinChecks.Check(Connection.Writes.Count==writes,"Duplicate reached SDK");
    }
    public void Dispose() { _timer?.Dispose(); _cockpit?.Dispose(); _guidance?.Dispose(); }
}
