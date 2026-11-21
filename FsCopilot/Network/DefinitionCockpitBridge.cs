namespace FsCopilot.Network;
using FsCopilot.Native;
using FsCopilot.Simulation;
using FsCopilot.Ownership;

/// <summary>Normal instruments share absolute definition values. No seats, physics, hardware masks or GTN click traffic.</summary>
public sealed partial class DefinitionCockpitBridge : IDisposable
{
    private readonly INetwork _network;
    private readonly INativeCockpitClient _client;
    private readonly INativeSessionClient _primary;
    private readonly CockpitDefinitionProfile _profile;
    private readonly CockpitReplication _model;
    private readonly string _peer;
    private readonly Guid _session;
    private readonly Func<bool> _active;
    private readonly CompositeDisposable _subscriptions=new();
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private TimeSpan _peerAt=TimeSpan.MinValue,_helloAt=TimeSpan.MinValue,_nativeAt=TimeSpan.MinValue,_writeAt;
    private TimeSpan _reportedAt=TimeSpan.MinValue;
    private ulong _serial,_command,_pending;
    private int _disposed;
    private bool _fenced;
    public string Error {get;private set;}="Waiting for ordinary instrument synchronization.";
    public DefinitionCockpitBridge(INetwork network,INativeSessionClient primary,INativeCockpitClient client,
        CockpitDefinitionProfile profile,ControlAuthoritySnapshot initial,string local,Func<bool> active,EventLoopScheduler scheduler) {
        _network=network;_primary=primary;_client=client;_profile=profile;_session=initial.Session;_active=active;
        _peer=local==initial.PhysicsHost?initial.Peer:initial.PhysicsHost;
        _host=local==initial.PhysicsHost;
        _model=new(local==initial.PhysicsHost,profile.Entries.Select(e=>e.Definition.Shared),profile.Tolerances);
        _subscriptions.Add(network.StreamWithSource<CockpitDefinitionPacket>().ObserveOn(scheduler).Subscribe(packet=> {
            if(!_active() || packet.Sender!=_peer || packet.Packet.Session!=_session) return;
            if(packet.Packet.Profile!=_profile.Identity) {Fail("The two ordinary cockpit definition catalogs differ.");return;}
            _peerAt=_clock.Elapsed;
            if(packet.Packet.Change is { } change) _model.Receive(change);
        }));
        SubscribeRecovery(scheduler);
        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(50),scheduler).Subscribe(_=>Tick()));
    }
    private void Tick() {
        if(Volatile.Read(ref _disposed)!=0) return;
        var primary=_primary.State;var state=_client.CockpitState;
        if(!_active() || !primary.Live || primary.Session is not {Phase:NativeSessionPhase.Active} session ||
            !state.Live || state.Packet is not {Active:true} observed || observed.Session!=_session.ToString("N") ||
            observed.Native!=primary.Snapshot?.Native || observed.Backend!=primary.Backend) {Unfence();return;}
        if(observed.Profile!=_profile.Identity || observed.Count!=_model.Count) {Fail("Native and helper cockpit definitions differ.");return;}
        if(_profile.Required.Any(i=>!observed.Known(i))) {Fail("Waiting for required instrument readings: "+string.Join(", ",_profile.Required.Where(i=>!observed.Known(i)).Select(i=>_profile.Entries[i].Definition.Get)));return;}
        if(_helloAt==TimeSpan.MinValue || _clock.Elapsed-_helloAt>=TimeSpan.FromSeconds(1)) {
            _helloAt=_clock.Elapsed;Send(new(_session,_profile.Identity,null));_model.Snapshot();
        }
        if(_peerAt==TimeSpan.MinValue || _clock.Elapsed-_peerAt>TimeSpan.FromSeconds(3)) {Unfence();return;}
        if(!_fenced) {_client.SetCockpitFence(new(observed.Native,observed.Backend,observed.Session,observed.Profile));_fenced=true;}
        if(observed.Serial>_serial) {
            _serial=observed.Serial;_nativeAt=_clock.Elapsed;
            if(_pending!=0 && observed.Command==_pending && observed.Result is NativeCockpitResult.Succeeded or NativeCockpitResult.Rejected or NativeCockpitResult.Failed) {
                var succeeded=observed.Result==NativeCockpitResult.Succeeded;
                _model.CompleteWrite(succeeded,observed.Result==NativeCockpitResult.Rejected);
                if(!succeeded) Log.Warning("[Cockpit] {Definition}: {Result}; current {Current}",_profile.Entries[(int)observed.Index].Definition.Get,observed.Result,observed.Known((int)observed.Index)?observed.Values[observed.Index]:null);
                _pending=0;
            }
            _model.Observe(Enumerable.Range(0,_model.Count).Select(i=>observed.Known(i)?(double?)observed.Values[i]:null).ToArray());
        }
        if(_pending!=0 && _clock.Elapsed-_writeAt>TimeSpan.FromSeconds(2)) {
            Log.Warning("[Cockpit] Command {Command} timed out in session {Session}; last native command/result {NativeCommand}/{Result}. Physical sharing continues.",
                _pending,_session,observed.Command,observed.Result);
            _model.CompleteWrite(false);_pending=0;Error="An instrument write was not confirmed; physical control sharing is unaffected.";
        }
        if(_nativeAt==TimeSpan.MinValue || _clock.Elapsed-_nativeAt>TimeSpan.FromMilliseconds(250)) {Unfence();return;}
        TickRecovery(observed);
        foreach(var change in _model.Drain()) Send(new(_session,_profile.Identity,change));
        if(_reportedAt==TimeSpan.MinValue || _clock.Elapsed-_reportedAt>=TimeSpan.FromMilliseconds(500)) {
            var report=new NativeCockpitPacket {Kind=NativeCockpitKind.Report,Native=observed.Native,Backend=observed.Backend,Session=observed.Session,
                Profile=observed.Profile,Command=++_command,ReadSerial=observed.Serial,Linked=true,
                ResyncAck=observed.ResyncRequest,Recovery=_progress};
            if(_client.TrySendCockpit(report)) _reportedAt=_clock.Elapsed;
        }
        if(_pending!=0 || _model.NextWrite() is not { } write) return;
        string expression;
        try {expression=_profile.Plan(write.Index,write.Desired,write.Current);} catch(Exception error) {Log.Warning(error,"[Cockpit] Definition refused");_model.CompleteWrite(false);return;}
        if(expression.Length==0) {_model.CompleteWrite(false);return;}
        var values=new double[512];values[0]=write.Desired;values[1]=write.Current;
        var command=new NativeCockpitPacket {Kind=NativeCockpitKind.Apply,Native=observed.Native,Backend=observed.Backend,Session=observed.Session,
            Profile=observed.Profile,Command=++_command,ReadSerial=observed.Serial,Index=(uint)write.Index,Values=values,Expression=expression};
        if(_client.TrySendCockpit(command)) {_pending=command.Command;_writeAt=_clock.Elapsed;Error="";}
        else _model.CompleteWrite(false,true);
    }
    private void Send(CockpitDefinitionPacket packet) {try {_network.SendAll(packet);}catch(Exception error){Fail("Ordinary instrument transport failed: "+error.Message);}}
    private void Fail(string error) {if(Error!=error)Log.Warning("[Cockpit] {Error}",error);Error=error;Unfence();}
    private void Unfence() {
        if(_progress.Phase is 1 or 2) {
            _recovery?.Abort("Instrument connection interrupted.");
            _progress=_progress with {Phase=4,Failed=_progress.Failed+_progress.Pending,Pending=0,Issues="Instrument connection interrupted; request resync again after reconnecting."};
            _requested=_queuedStart=Guid.Empty;
        }
        if(_fenced){_client.SetCockpitFence(null);_fenced=false;}
    }
    public void Dispose() {if(Interlocked.Exchange(ref _disposed,1)!=0)return;Unfence();_model.Close();_subscriptions.Dispose();}
}
