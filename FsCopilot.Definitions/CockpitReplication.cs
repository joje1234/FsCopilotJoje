namespace FsCopilot.Simulation;

public sealed record CockpitValue(int Index, ulong Revision, double Value, ulong Acknowledged = 0);
public sealed record CockpitChange(bool Request, ulong Sequence, CockpitValue[] Values);
public sealed record CockpitWrite(int Index, ulong Revision, double Desired, double Current, ulong Request = 0);

/// <summary>Host-serialized ordinary cockpit values; both players may adjust shared instruments.</summary>
public sealed class CockpitReplication
{
    private readonly bool _host;
    private readonly bool[] _shared;
    private readonly double[] _tolerances;
    private readonly double?[] _observed, _authority, _desired, _inputAnchor;
    private readonly ulong[] _revision, _desiredRevision, _failedRevision, _localRevision;
    private readonly ulong[] _localRequest, _desiredRequest, _authorityAck;
    private readonly bool[] _initialized;
    private readonly Queue<CockpitChange> _outgoing = new();
    private ulong _sequence, _received;
    private CockpitWrite? _writing;
    private bool _closed;
    public int Count => _shared.Length;
    public bool Writing(int index) => _writing?.Index==index;
    public bool Pending(int index) => _desired[index] is not null || Writing(index);
    public bool Failed(int index) => _revision[index]!=0 && _failedRevision[index]==_revision[index];
    public bool Known(int index) => _observed[index] is not null && _authority[index] is not null;
    private bool Equivalent(int index,double a,double b) => CockpitDefinitionProfile.Equivalent(a,b,_tolerances[index]);
    public bool Matches(int index) => Known(index) && Equivalent(index,_observed[index]!.Value,_authority[index]!.Value);
    public bool Retry(int index) {
        if(_host || _closed || index<0 || index>=Count || !_shared[index] || !Known(index) || Writing(index))return false;
        _failedRevision[index]=0;_desired[index]=_authority[index];_desiredRevision[index]=_revision[index];_localRequest[index]=0;return true;
    }
    public CockpitReplication(bool host, IEnumerable<bool> shared, IEnumerable<double>? tolerances=null) {
        _host=host; _shared=shared.ToArray();
        if(Count is <1 or >512) throw new ArgumentException("Invalid cockpit schema.");
        _tolerances=tolerances?.ToArray()??Enumerable.Repeat(.000001,Count).ToArray();
        if(_tolerances.Length!=Count || _tolerances.Any(t=>!double.IsFinite(t)||t<=0||t>1))throw new ArgumentException("Invalid cockpit tolerances.");
        _observed=new double?[Count];_authority=new double?[Count];_desired=new double?[Count];_inputAnchor=new double?[Count];
        _revision=new ulong[Count];_desiredRevision=new ulong[Count];_failedRevision=new ulong[Count];_localRevision=new ulong[Count];_initialized=new bool[Count];
        _localRequest=new ulong[Count];_desiredRequest=new ulong[Count];_authorityAck=new ulong[Count];
    }
    public static bool Valid(CockpitChange change, int count) => change.Sequence>0 && change.Values is {Length:>0 and <=512} &&
        change.Values.Length<=count && change.Values.Select(v=>v.Index).Distinct().Count()==change.Values.Length &&
        change.Values.All(v=>v.Index>=0 && v.Index<count && v.Revision>0 && (!change.Request || v.Acknowledged==0) && double.IsFinite(v.Value) && Math.Abs(v.Value)<=1e12);
    public void Observe(IReadOnlyList<double?> values) {
        if(_closed || values.Count!=Count) return;
        var changes=new List<CockpitValue>();
        for(var i=0;i<Count;i++) {
            var value=values[i]; if(value is null || !double.IsFinite(value.Value) || Math.Abs(value.Value)>1e12) {
                _observed[i]=null;if(_host)_authority[i]=null;continue;
            }
            var recovered=_observed[i] is null;
            _observed[i]=value;
            if(_writing?.Index==i) continue; // A received setter's readback is never a new knob input.
            if(_host) {
                if(_authority[i] is not { } authoritative || !Equivalent(i,value.Value,authoritative)) {
                    _authority[i]=value; ++_revision[i]; _localRevision[i]=_revision[i];_authorityAck[i]=0;changes.Add(new(i,_revision[i],value.Value));
                    if(_desired[i] is not null && _desiredRevision[i]!=_revision[i]) _desired[i]=null;
                }
            } else if(recovered && _initialized[i] && _desired[i] is null) {
                // Reappearing telemetry is a baseline, not a knob/button action.
                _inputAnchor[i]=value;
            } else if(_initialized[i] && _desired[i] is null && _shared[i] && _inputAnchor[i] is { } before && !Equivalent(i,value.Value,before)) {
                // Accumulate sub-precision movement without resending an unchanged
                // proposal every observation while its host receipt is in flight.
                _inputAnchor[i]=value;changes.Add(new(i,_revision[i],value.Value));
            }
        }
        if(changes.Count>0) Enqueue(!_host,changes.ToArray());
    }
    public bool Receive(CockpitChange change) {
        if(_closed || !Valid(change,Count) || change.Request!=_host || change.Sequence<=_received) return false;
        if(!_host && change.Values.Any(v=>v.Revision<_revision[v.Index] || v.Revision==_revision[v.Index] &&
            _authority[v.Index] is { } current && (!CockpitDefinitionProfile.Equivalent(current,v.Value) || v.Acknowledged!=_authorityAck[v.Index]) || v.Acknowledged>_sequence)) return false;
        _received=change.Sequence;
        var resend=false;
        foreach(var value in change.Values) {
            var i=value.Index;
            if(_host) {
                // A fast new peer adjustment may precede its previous receipt.
                // Rebase across that peer's accepted writes, but never across
                // a newer physical adjustment made on the host.
                if(!_shared[i] || value.Revision<_localRevision[i] || value.Revision>_revision[i]) { resend=true; continue; }
                _desiredRequest[i]=change.Sequence;
            } else {
                var duplicate=value.Revision==_revision[i];
                _revision[i]=value.Revision;_authority[i]=value.Value;_authorityAck[i]=value.Acknowledged;
                // A periodic replay of an already accepted host revision is not
                // a fresh command. In particular it cannot undo a local knob
                // proposal still travelling to the host. Explicit resync uses Retry.
                if(duplicate && _initialized[i])continue;
                // An earlier receipt must not undo a newer rotary movement
                // already sent by this peer. Host-originated changes (ack 0)
                // remain authoritative and can supersede a rejected proposal.
                if(value.Acknowledged!=0 && value.Acknowledged<_localRequest[i])continue;
                _localRequest[i]=0;
            }
            if(_failedRevision[i]==value.Revision || _writing?.Index==i && _writing.Revision==value.Revision && CockpitDefinitionProfile.Equivalent(_writing.Desired,value.Value)) continue;
            _desired[i]=value.Value;_desiredRevision[i]=_host?_revision[i]:value.Revision;
            if(_writing?.Index!=i && _observed[i] is { } observed && Equivalent(i,observed,value.Value)) {
                _desired[i]=null; _initialized[i]=true;_inputAnchor[i]=observed;
                if(_host && _desiredRequest[i]>_authorityAck[i]) {
                    _authority[i]=observed;_authorityAck[i]=_desiredRequest[i];++_revision[i];Enqueue(false,[new(i,_revision[i],observed,_authorityAck[i])]);
                }
            }
        }
        if(resend) Snapshot();
        return true;
    }
    public CockpitWrite? NextWrite() {
        if(_closed || _writing is not null) return null;
        for(var i=0;i<Count;i++) if(_desired[i] is { } desired && _observed[i] is { } current) {
            if(_host && _desiredRevision[i]!=_revision[i]) { _desired[i]=null;continue; }
            if(Equivalent(i,desired,current)) {
                _desired[i]=null;_initialized[i]=true;_inputAnchor[i]=current;
                if(_host && _desiredRequest[i]>_authorityAck[i]) {
                    _authority[i]=current;_authorityAck[i]=_desiredRequest[i];++_revision[i];Enqueue(false,[new(i,_revision[i],current,_authorityAck[i])]);
                }
                continue;
            }
            return _writing=new(i,_desiredRevision[i],desired,current,_desiredRequest[i]);
        }
        return null;
    }
    public void CompleteWrite(bool success, bool stale=false) {
        if(_writing is not { } write) return;
        _writing=null;var i=write.Index;
        if(!success) {
            if(!stale) _failedRevision[i]=write.Revision;
            if(_desiredRevision[i]==write.Revision) _desired[i]=null;
            return;
        }
        _observed[i]=write.Desired;_initialized[i]=true;_inputAnchor[i]=write.Desired;
        if(_host) {
            _authority[i]=write.Desired;++_revision[i];_authorityAck[i]=write.Request;Enqueue(false,[new(i,_revision[i],write.Desired,write.Request)]);
            if(_desiredRevision[i]==write.Revision && _desired[i] is { } newer && !CockpitDefinitionProfile.Equivalent(newer,write.Desired)) _desiredRevision[i]=_revision[i];
        }
        if(_desiredRevision[i]==write.Revision && _desired[i] is { } desired && CockpitDefinitionProfile.Equivalent(desired,write.Desired)) _desired[i]=null;
    }
    public void Snapshot() {
        if(_closed || !_host) return;
        // An accepted peer change is not yet confirmed while its setter is in
        // flight. Periodic snapshots must not send the superseded old value back.
        var values=Enumerable.Range(0,Count).Where(i=>_authority[i] is not null && !Pending(i)).Select(i=>new CockpitValue(i,_revision[i],_authority[i]!.Value,_authorityAck[i])).ToArray();
        if(values.Length>0) Enqueue(false,values);
    }
    public CockpitChange[] Drain() { var values=_outgoing.ToArray();_outgoing.Clear();return values; }
    public void Close() { _closed=true;_writing=null;Array.Clear(_desired);_outgoing.Clear(); }
    private void Enqueue(bool request,CockpitValue[] values) {
        if(_sequence==ulong.MaxValue) {Close();return;}
        ++_sequence;if(request)foreach(var value in values)_localRequest[value.Index]=_sequence;
        _outgoing.Enqueue(new(request,_sequence,values));
    }
}
