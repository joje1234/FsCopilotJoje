namespace FsCopilot.Network;
using FsCopilot.Native;
using FsCopilot.Simulation;

public sealed partial class DefinitionCockpitBridge
{
    private readonly bool _host;
    private Guid _batch,_requested,_queuedStart;
    private ulong _localRequest;
    private ulong _startSerial;
    private uint[]? _available;
    private uint _loggedPhase;
    private TimeSpan _batchAt,_recoveryReportAt=TimeSpan.MinValue;
    private NativeCockpitProgress _progress=new();
    private CockpitRecovery? _recovery;
    private readonly HashSet<Guid> _seenRecovery=new();
    private readonly Queue<Guid> _seenOrder=new();
    private void SubscribeRecovery(EventLoopScheduler scheduler) {
        _subscriptions.Add(_network.StreamWithSource<CockpitRecoveryPacket>().ObserveOn(scheduler).Subscribe(message=>{
            var p=message.Packet;
            if(!_active()||message.Sender!=_peer||p.Session!=_session||p.Profile!=_profile.Identity||p.Id==Guid.Empty)return;
            if(p.Kind==CockpitRecoveryKind.Request && _host) {
                if(!_seenRecovery.Add(p.Id))return;
                _seenOrder.Enqueue(p.Id);if(_seenOrder.Count>64)_seenRecovery.Remove(_seenOrder.Dequeue());
                if(_progress.Phase is 1 or 2) {
                    SendRecovery(new(_session,_profile.Identity,p.Id,CockpitRecoveryKind.Report,new(4,Issues:"Cockpit resync is already running on the host. Wait for it to finish, then retry.")));
                    return;
                }
                _batch=_requested=p.Id;_progress=new(1);_batchAt=_clock.Elapsed;
            } else if(p.Kind==CockpitRecoveryKind.Start && !_host) {
                if(_seenRecovery.Contains(p.Id))return;
                _seenRecovery.Add(p.Id);_seenOrder.Enqueue(p.Id);if(_seenOrder.Count>64)_seenRecovery.Remove(_seenOrder.Dequeue());
                _batch=_queuedStart=p.Id;_progress=new(1);_batchAt=_clock.Elapsed;_recovery=null;_available=p.Available;
            } else if(p.Kind==CockpitRecoveryKind.Report && p.Id==_batch && p.Progress is {Valid:true} progress) {
                if(_host && progress.Phase>=2 && (_progress.Phase<3 || progress.Phase>=3) && progress.Checked>=_progress.Checked)_progress=progress;
                else if(!_host && progress.Phase==4){_recovery?.Abort(progress.Issues);_progress=progress;_queuedStart=Guid.Empty;}
            }
        }));
    }
    private void TickRecovery(NativeCockpitPacket observed) {
        if(observed.ResyncRequest>_localRequest) {
            _localRequest=observed.ResyncRequest;
            if(_progress.Phase is not(1 or 2)) {
                _batch=Guid.NewGuid();_batchAt=_clock.Elapsed;_progress=new(1);_recovery=null;
                if(_host)_requested=_batch;else SendRecovery(new(_session,_profile.Identity,_batch,CockpitRecoveryKind.Request));
            }
            _reportedAt=TimeSpan.MinValue;
        }
        if(_host && _requested!=Guid.Empty && _pending==0) {
            _model.Snapshot();foreach(var change in _model.Drain())Send(new(_session,_profile.Identity,change));
            SendRecovery(new(_session,_profile.Identity,_requested,CockpitRecoveryKind.Start,Available:observed.Valid.ToArray()));
            _requested=Guid.Empty;_progress=new(2,Pending:(uint)_profile.Entries.Count(e=>e.Definition.Shared));_reportedAt=TimeSpan.MinValue;
        }
        if(!_host && _queuedStart!=Guid.Empty && _pending==0) {
            _recovery=new(_profile,_model,_clock.Elapsed.TotalSeconds,_available);_queuedStart=Guid.Empty;
            _startSerial=observed.Serial;
        }
        if(_recovery is not null && observed.Serial>_startSerial && _progress.Phase is 1 or 2) {
            _recovery.Tick(_model,_clock.Elapsed.TotalSeconds);
            _progress=new(_recovery.Phase,_recovery.Checked,_recovery.Corrected,_recovery.Pending,_recovery.Failed,_recovery.Unavailable,_recovery.Issues);
        }
        if(_progress.Phase is 1 or 2 && _clock.Elapsed-_batchAt>TimeSpan.FromSeconds(125)) {
            _recovery?.Abort("Peer did not finish cockpit resync.");
            _progress=_progress with {Phase=4,Failed=_progress.Failed+_progress.Pending,Pending=0,Issues="Peer did not finish cockpit resync."};
            SendRecovery(new(_session,_profile.Identity,_batch,CockpitRecoveryKind.Report,_progress));
        }
        if(!_host && _batch!=Guid.Empty && _progress.Phase>=2 &&
            (_recoveryReportAt==TimeSpan.MinValue||_clock.Elapsed-_recoveryReportAt>=TimeSpan.FromMilliseconds(300))) {
            _recoveryReportAt=_clock.Elapsed;
            SendRecovery(new(_session,_profile.Identity,_batch,CockpitRecoveryKind.Report,_progress));
        }
        if(_progress.Phase is 1 or 2 && (_reportedAt==TimeSpan.MinValue||_clock.Elapsed-_reportedAt>=TimeSpan.FromMilliseconds(200)))_reportedAt=TimeSpan.MinValue;
        if(_loggedPhase!=_progress.Phase){
            _loggedPhase=_progress.Phase;
            Log.Information("[Cockpit] Resync {Id}: phase {Phase}, checked {Checked}, corrected {Corrected}, pending {Pending}, failed {Failed}, unavailable {Unavailable}. {Issues}",
                _batch,_progress.Phase,_progress.Checked,_progress.Corrected,_progress.Pending,_progress.Failed,_progress.Unavailable,_progress.Issues);
        }
    }
    private void SendRecovery(CockpitRecoveryPacket packet) {
        try{_network.SendAll(packet);}catch(Exception error){Fail("Cockpit recovery transport failed: "+error.Message);}
    }
}
