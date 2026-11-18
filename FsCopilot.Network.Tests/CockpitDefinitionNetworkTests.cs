using FsCopilot.Native;
using FsCopilot.Network;
using FsCopilot.Ownership;
using FsCopilot.Simulation;

internal static class CockpitDefinitionNetworkTests
{
    private sealed class FakeNative : INativeSessionClient, INativeCockpitClient {
        private readonly object _sync=new();
        private readonly CockpitDefinitionProfile _profile;
        private readonly NativeBridgeState _state;
        private NativeCockpitFence? _fence;
        private ulong _serial,_command;
        private NativeCockpitResult _result;
        private uint _index;
        private ulong _request;
        public int FailIndex=-1;
        private NativeCockpitProgress _progress=new();
        public NativeCockpitProgress Progress {get{lock(_sync)return _progress;}}
        public void Resync(){lock(_sync)++_request;}
        private readonly double[] _values=new double[512];
        private readonly uint[] _valid=new uint[16];
        public List<string> Expressions {get;}=[];
        public FakeNative(CockpitDefinitionProfile profile,Guid session,string native) {
            _profile=profile;var backend=Guid.NewGuid().ToString("N");
            _state=new(true,true,new(){Native=native,Serial=1,Capabilities=63},"") {Backend=backend,Session=new(){Phase=NativeSessionPhase.Active,Native=native,Backend=backend,Session=session.ToString("N")}};
        }
        public void Set(int index,double value){lock(_sync){
            _values[index]=value;_valid[index/32]|=1u<<(index%32);
            if(value==1 && _profile.Entries[index].Definition.Get is "A:AUTOPILOT HEADING LOCK" or "A:AUTOPILOT NAV1 LOCK") {
                var other=_profile.Entries.Single(e=>e.Definition.Get==(_profile.Entries[index].Definition.Get=="A:AUTOPILOT HEADING LOCK"?"A:AUTOPILOT NAV1 LOCK":"A:AUTOPILOT HEADING LOCK")).Index;
                _values[other]=0;_valid[other/32]|=1u<<(other%32);
            }
        }}
        public double Get(int index){lock(_sync)return _values[index];}
        public NativeBridgeState State=>_state;
        public event Action<NativeBridgeState>? StateChanged {add{}remove{}}
        public event Action<ulong>? SessionCommandDropped {add{}remove{}}
        public event Action<NativeCockpitState>? CockpitChanged {add{}remove{}}
        public bool TrySendSession(NativeSessionPacket command)=>false;
        public NativeCockpitState CockpitState {get{lock(_sync)return new(true,true,new(){Native=_state.Snapshot!.Native,Backend=_state.Backend,Session=_state.Session!.Session,
            Profile=_profile.Identity,Count=(uint)_profile.Entries.Count,Serial=++_serial,Active=true,Command=_command,Result=_result,Index=_index,
            Values=_values.ToArray(),Valid=_valid.ToArray(),ResyncRequest=_request},"");}}
        public void SetCockpitFence(NativeCockpitFence? fence){lock(_sync)_fence=fence;}
        public bool TrySendCockpit(NativeCockpitPacket p){lock(_sync){
            if(_fence?.Allows(p)!=true||!NativeCockpitWire.Valid(p)||p.Index>=_profile.Entries.Count)return false;
            if(p.Kind==NativeCockpitKind.Report){if(p.ResyncAck==_request)_progress=p.Recovery;return true;}
            _command=p.Command;_index=p.Index;
            if(!CockpitDefinitionProfile.Equivalent(_values[p.Index],p.Values[1])){_result=NativeCockpitResult.Rejected;return true;}
            var expected=_profile.Plan((int)p.Index,p.Values[0],p.Values[1]);
            if(expected!=p.Expression)throw new InvalidOperationException("Writer departed from the actual vanilla expression.");
            Expressions.Add(p.Expression);if(p.Index==FailIndex){_result=NativeCockpitResult.Failed;return true;}
            var value=p.Expression.Contains("AP_HEADING_BUG_SET_EX1",StringComparison.Ordinal)?
                double.Parse(p.Expression.Split(' ')[1],System.Globalization.CultureInfo.InvariantCulture)*360/16384:p.Values[0];
            if(p.Expression.Contains("AILERON_TRIM_SET_EX1",StringComparison.Ordinal))value=double.Parse(p.Expression.Split(' ')[0],System.Globalization.CultureInfo.InvariantCulture)/16384;
            if(p.Expression.Contains("RUDDER_TRIM_SET_EX1",StringComparison.Ordinal))value=double.Parse(p.Expression.Split(' ')[0],System.Globalization.CultureInfo.InvariantCulture)/163.84;
            if(p.Expression.Contains("ELEVATOR_TRIM_SET",StringComparison.Ordinal))value=double.Parse(p.Expression.Split(' ')[0],System.Globalization.CultureInfo.InvariantCulture);
            Set((int)p.Index,value);_result=NativeCockpitResult.Succeeded;return true;
        }}
    }
    private static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    private static async Task Wait(Func<bool> ready,string reason){var clock=Stopwatch.StartNew();while(!ready()&&clock.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(20);Check(ready(),reason);}
    public static async Task Run(int port){
        var profile=CockpitDefinitionProfile.Load("bksq-aircraft-baronpropress");
        var left=profile.Entries.Single(e=>e.Definition.Get=="A:KOHLSMAN SETTING MB EX1:1").Index;
        var right=profile.Entries.Single(e=>e.Definition.Get=="A:KOHLSMAN SETTING MB EX1:2").Index;
        using var host=new RelayNetwork("127.0.0.1","normal-host","Normal host",false,port);
        using var peer=new RelayNetwork("127.0.0.1","normal-peer","Normal peer",false,port);
        var registrations=0;using var subscriptions=new CompositeDisposable();
        foreach(var network in new[]{host,peer}){OwnershipNetworkNegotiation.RegisterProtocol(network);subscriptions.Add(network.OriginAvailable.Where(v=>v).Take(1).Subscribe(_=>Interlocked.Increment(ref registrations)));}
        await Wait(()=>registrations==2,"Ordinary cockpit clients not registered.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Check(await host.Connect("normal-peer",timeout.Token)==ConnectionResult.Success,"Ordinary cockpit clients failed to connect.");
        var authority=new ControlAuthoritySession("normal-host","normal-peer",2,()=>TimeSpan.Zero).Snapshot;
        var hn=new FakeNative(profile,authority.Session,"abc");var pn=new FakeNative(profile,authority.Session,"def");
        hn.Set(left,1013.25);hn.Set(right,1000);pn.Set(left,900);pn.Set(right,900);
        using var hs=new EventLoopScheduler();using var ps=new EventLoopScheduler();
        using var hb=new DefinitionCockpitBridge(host,hn,hn,profile,authority,"normal-host",()=>true,hs);
        var pb=new DefinitionCockpitBridge(peer,pn,pn,profile,authority,"normal-peer",()=>true,ps);
        try{
            await Wait(()=>pn.Get(left)==1013.25&&pn.Get(right)==1000,"Initial separate pressures did not sync.");
            pn.Set(left,1014);await Wait(()=>hn.Get(left)==1014,"Copilot's normally shared left-altimeter adjustment did not reach host.");
            Check(hn.Get(right)==1000,"Left adjustment changed right pressure.");
            hn.Set(right,999);await Wait(()=>pn.Get(right)==999,"Pilot's right-altimeter adjustment did not reach follower.");
            pn.Set(left,1015);await Task.Delay(65);pn.Set(left,1016);await Task.Delay(65);pn.Set(left,1017);
            await Wait(()=>hn.Get(left)==1017&&pn.Get(left)==1017,"Rapid absolute adjustments did not converge to the latest setting; current "+hn.Get(left)+" / "+pn.Get(left));
            await Task.Delay(1200);Check(hn.Get(left)==1017&&pn.Get(left)==1017,"Rotary synchronization oscillated after adjustment stopped.");
            Check(hn.Expressions.Count+pn.Expressions.Count<20,"Received values are feeding back into repeated output.");
            Check(hn.Expressions.Concat(pn.Expressions).All(e=>e.Contains("K:2:KOHLSMAN_SET")&&!e.Contains("INC")&&!e.Contains("DEC")),"Normal pressure sync returned to increment chasing.");
            pn.FailIndex=left;hn.Set(left,1021);await Task.Delay(500);
            Check(pn.Get(left)!=1021,"Failure injection did not hold the follower's output.");
            pn.FailIndex=-1;pn.Resync();
            await Wait(()=>pn.Progress.Phase==3&&pn.Get(left)==1021,"Follower-requested recovery did not repair the previously failed revision.");
            Check(pn.Progress.Corrected==1&&pn.Progress.Unavailable>0&&pn.Progress.Issues.Contains("Unavailable:"),"Recovery lost named unavailable readings or corrected count.");
            pn.FailIndex=left;hn.Set(left,1022);await Task.Delay(500);var attempts=pn.Expressions.Count;
            hn.Resync();await Wait(()=>hn.Progress.Phase==4,"Host-requested recovery did not report a bounded write failure.");
            Check(hn.Progress.Failed==1&&hn.Progress.Issues.Contains("Left altimeter")&&pn.Expressions.Count-attempts==3,"Retry count or named failure was incorrect.");
            pn.FailIndex=-1;hn.Resync();await Wait(()=>pn.Get(left)==1022&&hn.Progress.Phase==3,"A later explicit recovery could not retry a failed batch.");
            var flap=profile.Entries.Single(e=>e.Definition.Get=="A:FLAPS HANDLE INDEX").Index;
            hn.Set(flap,0);pn.Set(flap,0);await Task.Delay(200);hn.Set(flap,2);
            await Wait(()=>pn.Get(flap)==2,"Direct 0-to-2 flap target did not cross the relay.");
            Check(pn.Expressions.Last().Contains("FLAPS_DOWN"),"Flaps reverted to a relative single-step setter.");
            var battery=profile.Entries.Single(e=>e.Definition.Get=="B:ELECTRICAL_Battery_1").Index;
            var nav=profile.Entries.Single(e=>e.Definition.Get=="B:LIGHTING_NAV_1").Index;
            var strobe=profile.Entries.Single(e=>e.Definition.Get=="B:LIGHTING_STROBE_1").Index;
            foreach(var index in new[]{battery,nav,strobe}) {hn.Set(index,0);pn.Set(index,0);}
            await Task.Delay(300);var hostSwitchesBefore=hn.Expressions.Count;var peerSwitchesBefore=pn.Expressions.Count;
            hn.Set(battery,1);await Wait(()=>pn.Get(battery)==1,"Pilot battery change did not reach copilot.");
            pn.Set(battery,0);await Wait(()=>hn.Get(battery)==0,"Copilot battery change did not reach pilot.");
            hn.Set(nav,1);await Wait(()=>pn.Get(nav)==1,"Pilot navigation-light change did not reach copilot.");
            pn.Set(nav,0);await Wait(()=>hn.Get(nav)==0,"Copilot navigation-light change did not reach pilot.");
            pn.Set(strobe,1);await Wait(()=>hn.Get(strobe)==1,"Copilot strobe-light change did not reach pilot.");
            await Task.Delay(1000);
            Check(hn.Get(battery)==0&&pn.Get(battery)==0&&hn.Get(nav)==0&&pn.Get(nav)==0&&hn.Get(strobe)==1&&pn.Get(strobe)==1,"Input-event switches oscillated after adjustment stopped.");
            var switchWrites=hn.Expressions.Skip(hostSwitchesBefore).Concat(pn.Expressions.Skip(peerSwitchesBefore)).Where(e=>e.Contains(">B:",StringComparison.Ordinal)).ToArray();
            Check(switchWrites.Length==5,"Input-event toggles were dropped or echoed as new local input: "+string.Join("; ",switchWrites));
            Check(hn.Expressions.Concat(pn.Expressions).Any(e=>e=="1 (>B:ELECTRICAL_Battery_1_Toggle)")&&
                hn.Expressions.Concat(pn.Expressions).Any(e=>e=="0 (>B:LIGHTING_NAV_1_Toggle)"),"Switch sharing bypassed the vanilla value-prefixed input-event setters.");
            var ap=profile.Entries.Where(e=>e.Definition.Get.StartsWith("A:AUTOPILOT ")).ToArray();
            foreach(var e in ap){hn.Set(e.Index,0);pn.Set(e.Index,0);}await Task.Delay(300);
            foreach(var e in ap.Where(e=>e.Definition.Units=="Bool")) {
                pn.Set(e.Index,1);await Wait(()=>hn.Get(e.Index)==1&&pn.Get(e.Index)==1,"Copilot AP change did not reach pilot: "+e.Definition.Get);
                hn.Set(e.Index,0);await Wait(()=>pn.Get(e.Index)==0,"Pilot AP change did not reach copilot: "+e.Definition.Get);
            }
            var heading=ap.Single(e=>e.Definition.Get=="A:AUTOPILOT HEADING LOCK DIR:1").Index;
            var altitude=ap.Single(e=>e.Definition.Get=="A:AUTOPILOT ALTITUDE LOCK VAR:1").Index;
            var vs=ap.Single(e=>e.Definition.Get=="A:AUTOPILOT VERTICAL HOLD VAR:1").Index;
            pn.Set(heading,170.5);await Wait(()=>Math.Abs(hn.Get(heading)-170.5)<.022,"Copilot fractional heading did not converge through SDK precision.");
            hn.Set(heading,210.5);await Wait(()=>Math.Abs(pn.Get(heading)-210.5)<.022,"Pilot fractional heading did not reach copilot.");
            pn.Set(altitude,6500);await Wait(()=>hn.Get(altitude)==6500,"Copilot selected altitude did not reach host.");
            hn.Set(vs,-700);await Wait(()=>pn.Get(vs)==-700,"Signed VS did not reach copilot.");
            var navMode=ap.Single(e=>e.Definition.Get=="A:AUTOPILOT NAV1 LOCK").Index;
            var hdgMode=ap.Single(e=>e.Definition.Get=="A:AUTOPILOT HEADING LOCK").Index;
            hn.Set(navMode,1);await Wait(()=>pn.Get(navMode)==1,"NAV did not engage on both APs.");
            pn.Set(hdgMode,1);await Wait(()=>hn.Get(hdgMode)==1&&hn.Get(navMode)==0&&pn.Get(navMode)==0,"Coupled AP mode changes did not converge.");
            await Task.Delay(500);var apWrites=hn.Expressions.Count+pn.Expressions.Count;await Task.Delay(1000);
            Check(hn.Expressions.Count+pn.Expressions.Count==apWrites,"AP mode/selector readbacks feed back after controls stop moving.");
            Check(hn.Get(left)==1022&&pn.Get(left)==1022&&hn.Get(right)==999&&pn.Get(right)==999,"Shared AP changes altered independent barometers.");
            Console.WriteLine("PASS ordinary shared AP across actual relay: either pilot changes all declared mode buttons, fractional heading, indexed altitude/signed VS, coupled NAV/HDG convergence and zero settled feedback; no AP-owner adapter. SDK/executor simulated.");
            var trims=profile.Entries.Where(e=>e.Definition.Get is "A:AILERON TRIM PCT" or "A:ELEVATOR TRIM PCT" or "A:RUDDER TRIM PCT").ToArray();
            Check(trims.Length==3&&trims.All(e=>e.Definition.Shared),"Trims are not normally shared definitions.");
            foreach(var e in trims){hn.Set(e.Index,0);pn.Set(e.Index,0);}await Task.Delay(300);
            foreach(var e in trims) {
                var target=e.Definition.Get=="A:ELEVATOR TRIM PCT"?1500:e.Definition.Get=="A:RUDDER TRIM PCT"?20:.2;
                hn.Set(e.Index,target);await Wait(()=>Math.Abs(pn.Get(e.Index)-target)<=profile.Tolerances[e.Index],"Host trim did not reach copilot: "+e.Definition.Get);
                pn.Set(e.Index,-target);await Wait(()=>Math.Abs(hn.Get(e.Index)+target)<=profile.Tolerances[e.Index],"Copilot trim did not reach host: "+e.Definition.Get);
            }
            await Task.Delay(400);var trimWrites=hn.Expressions.Count+pn.Expressions.Count;await Task.Delay(1000);
            Check(hn.Expressions.Count+pn.Expressions.Count==trimWrites,"Trim output precision caused settled echo.");
            Console.WriteLine("PASS all three trims use normal bidirectional instrument synchronization with SDK precision and no settled echo, independently of Steering ownership. SDK/executor simulated.");
            pb.Dispose();var stopped=pn.Get(left);hn.Set(left,1020);await Task.Delay(300);Check(pn.Get(left)==stopped,"Disposed cockpit session continued output.");
        }finally{pb.Dispose();}
        Console.WriteLine("PASS ordinary cockpit definitions across actual relay: independent indexed altimeters, adjustments from either participant, rapid latest-value convergence, no rotary feedback and stopped-session isolation. Native SDK is simulated; no MSFS access.");
    }
}
