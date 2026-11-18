namespace FsCopilot.Simulation;

/// <summary>A finite reconciliation of shared settings against the latest host state.</summary>
public sealed class CockpitRecovery
{
    private readonly CockpitDefinitionProfile _profile;
    private readonly int[] _state, _attempts;
    private readonly bool[] _changed;
    private readonly double[] _retryAt;
    private readonly double _deadline;
    private string _error="";
    public uint Phase {get;private set;}=2;
    public uint Checked => (uint)_state.Count(s=>s==2);
    public uint Corrected => (uint)Enumerable.Range(0,_state.Length).Count(i=>_state[i]==2&&_changed[i]);
    public uint Pending => (uint)_state.Count(s=>s==1);
    public uint Failed => (uint)_state.Count(s=>s==3);
    public uint Unavailable => (uint)_state.Count(s=>s==4);
    public bool Active => Phase==2;
    public string Issues {
        get {
            var parts=new List<string>();if(_error.Length!=0)parts.Add(_error);
            foreach(var state in new[]{3,4}) {
                var names=Enumerable.Range(0,_state.Length).Where(i=>_state[i]==state).Select(_profile.Label).ToArray();
                if(names.Length>0)parts.Add((state==3?"Needs attention: ":"Unavailable: ")+string.Join(", ",names));
            }
            var value=string.Join(". ",parts);return value.Length<=1000?value:value[..997]+"...";
        }
    }
    public CockpitRecovery(CockpitDefinitionProfile profile,CockpitReplication model,double now,uint[]? available=null) {
        if(!double.IsFinite(now)||profile.Entries.Count!=model.Count)throw new ArgumentException("Invalid recovery schema or time.");
        _profile=profile;_state=new int[model.Count];_attempts=new int[model.Count];_changed=new bool[model.Count];_retryAt=new double[model.Count];_deadline=now+120;
        foreach(var entry in profile.Entries.Where(e=>e.Definition.Shared)) {
            var i=entry.Index;var known=model.Known(i)&&(available is null||available.Length==16&&(available[i/32]&(1u<<(i%32)))!=0);
            _state[i]=known?1:4;_changed[i]=known&&!model.Matches(i);
            if(_changed[i]&&model.Retry(i)){_attempts[i]=1;_retryAt[i]=now+.25;}
        }
    }
    public void Tick(CockpitReplication model,double now) {
        if(!Active)return;
        if(!double.IsFinite(now)||now>=_deadline){Abort("Resync timed out.");return;}
        for(var i=0;i<_state.Length;i++)if(_state[i]==1) {
            if(!model.Known(i)){_state[i]=4;continue;}
            if(model.Matches(i)&&!model.Pending(i)){_state[i]=2;continue;}
            if(model.Pending(i)||now<_retryAt[i])continue;
            var maximum=_profile.RetrySafe.Contains(i)?3:1;
            if(_attempts[i]>=maximum){_state[i]=3;continue;}
            if(model.Retry(i)){++_attempts[i];_retryAt[i]=now+.25;}
        }
        if(Pending==0)Phase=Failed==0?3u:4u;
    }
    public void Abort(string error) {
        _error=new string(error.Where(c=>c is >= ' ' and <= '~').Take(150).ToArray());
        for(var i=0;i<_state.Length;i++)if(_state[i]==1)_state[i]=3;
        Phase=4;
    }
}
