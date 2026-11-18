using System.Text.Json;
using FsCopilot.Simulation;

static class SharedAutopilotChecks {
    public static void Run(Action<bool,string> check) {
        const string aircraft="bksq-aircraft-baronpropress";
        var profile=CockpitDefinitionProfile.Load(aircraft);
        var modes=new Dictionary<string,(string On,string Off)> {
            ["MASTER"]=("AUTOPILOT_ON","AUTOPILOT_OFF"),["HEADING LOCK"]=("AP_HDG_HOLD_ON","AP_HDG_HOLD_OFF"),
            ["ALTITUDE LOCK"]=("AP_ALT_HOLD_ON","AP_ALT_HOLD_OFF"),["VERTICAL HOLD"]=("AP_VS_ON","AP_VS_OFF"),
            ["NAV1 LOCK"]=("AP_NAV1_HOLD_ON","AP_NAV1_HOLD_OFF"),["APPROACH HOLD"]=("AP_APR_HOLD_ON","AP_APR_HOLD_OFF"),
            ["BACKCOURSE HOLD"]=("AP_BC_HOLD_ON","AP_BC_HOLD_OFF"),["FLIGHT LEVEL CHANGE"]=("FLIGHT_LEVEL_CHANGE_ON","FLIGHT_LEVEL_CHANGE_OFF"),
            ["YAW DAMPER"]=("YAW_DAMPER_ON","YAW_DAMPER_OFF")
        };
        foreach(var (name,keys) in modes) {
            var entry=profile.Entries.Single(e=>e.Definition.Get=="A:AUTOPILOT "+name);
            check(entry.Definition.Shared,"Both pilots share AP state: "+name);
            check(profile.Plan(entry.Index,1,0)=="(>K:"+keys.On+")" && profile.Plan(entry.Index,0,1)=="(>K:"+keys.Off+")","AP uses explicit ON/OFF definition setters: "+name);
            foreach(var value in new[]{0d,1d})check(entry.Definition.Set(value,value)=="" && profile.Plan(entry.Index,value,value)=="","Equal AP states never toggle: "+name);
            check(entry.Definition.Set(2d,0d)=="","Invalid AP boolean is refused: "+name);
        }
        var fd=profile.Entries.Single(e=>e.Definition.Get=="A:AUTOPILOT FLIGHT DIRECTOR ACTIVE:1");
        check(fd.Definition.Shared && profile.Plan(fd.Index,1,0)=="(>K:TOGGLE_FLIGHT_DIRECTOR)" && fd.Definition.Set(0d,0d)=="","FD retains vanilla toggle behind equality and current-readback guards.");
        var heading=profile.Entries.Single(e=>e.Definition.Get=="A:AUTOPILOT HEADING LOCK DIR:1");
        check(heading.Definition.Shared && heading.Definition.Units=="Degrees" && profile.Tolerances[heading.Index]==.022,"Heading has one indexed shared definition and explicit SDK precision.");
        foreach(var value in new[]{0d,.01,170d,170.5,359.999}) {
            var raw=Math.Min(16383,Math.Round(value*16384/360,MidpointRounding.AwayFromZero));
            check(profile.Plan(heading.Index,value,value>1?0:100)==$"1 {raw} (>K:2:AP_HEADING_BUG_SET_EX1)","Heading uses the standard indexed high-precision event.");
            check(profile.Plan(heading.Index,value,raw*360/16384)=="","Quantized heading readback cannot create a rotary feedback loop.");
        }
        foreach(var (name,unit,target,key) in new[]{("ALTITUDE LOCK VAR:1","Feet",6500d,"AP_ALT_VAR_SET_ENGLISH"),("VERTICAL HOLD VAR:1","Feet per minute",-700d,"AP_VS_VAR_SET_ENGLISH")}) {
            var e=profile.Entries.Single(e=>e.Definition.Get=="A:AUTOPILOT "+name);
            check(e.Definition.Shared && e.Definition.Units==unit && profile.Plan(e.Index,target,0)==$"1 {target} (>K:2:{key})","AP selector uses the correct indexed event and units: "+name);
        }
        check(!profile.Entries.Any(e=>e.Definition.Get=="A:AUTOPILOT VERTICAL SPEED VAR:1"),"Invalid legacy VS variable cannot enter synchronization.");
        var policy=JsonSerializer.Deserialize<CockpitDefinitionPolicy>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Definitions","cockpit",aircraft+".json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        check(Definitions.TryLoadTree(aircraft+".yaml",out var tree),"Full vanilla include tree is available for AP policy checks.");
        var controls=ControlDefinitionProfile.Load("simobjects/airplanes/"+aircraft+"/aircraft.cfg");
        var separate=CockpitDefinitionProfile.Create(aircraft,tree,controls,policy with{SharedAutopilot=false,Tolerances=null});
        check(separate.Entries.All(e=>!e.Definition.Get.StartsWith("A:AUTOPILOT ")) && separate.Identity!=profile.Identity,"Other aircraft retain AP exclusion until their definitions explicitly opt in.");
        var host=new CockpitReplication(true,[true],[.022]);var peer=new CockpitReplication(false,[true],[.022]);
        host.Observe([170.5]);peer.Observe([90]);foreach(var p in host.Drain())peer.Receive(p);
        var write=peer.NextWrite();check(write is not null,"Fractional heading creates one absolute peer write.");peer.CompleteWrite(true);
        peer.Observe([170.5078125]);check(peer.Drain().Length==0 && peer.Matches(0),"SDK rounding produces no peer echo.");
        peer.Observe([170.517]);peer.Observe([170.527]);
        check(peer.Drain().Length>0,"Small cumulative knob changes are compared with authoritative state, not lost between samples.");
        peer.Observe([170.527]);check(peer.Drain().Length==0,"An unchanged peer knob proposal is not resent while awaiting its host receipt.");
        Console.WriteLine("PASS shared AP definitions: both pilots, explicit mode states, indexed selectors, SDK precision, equality guards and default opt-out. No simulator access.");
    }
}
