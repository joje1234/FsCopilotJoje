using FsCopilot.Efb;

static class HelperInstanceChecks
{
    public static void Run() {
        var root = Path.Combine(AppContext.BaseDirectory, "startup-" + Guid.NewGuid().ToString("N"));
        var version = new Version(0,9,12,0); var notes = new List<string>();
        using (var first = HelperInstance.Acquire(root,version,()=>[],notes.Add)) {
            RejoinChecks.Check(first is not null,"First helper did not acquire the lease");
            using var second = HelperInstance.Acquire(root,version,()=>[],notes.Add);
            RejoinChecks.Check(second is null,"Duplicate helper acquired an active lease");
        }
        var old = HelperInstance.Acquire(root,new Version(0,9,10,0),()=>[],notes.Add)!;
        var retired = false;
        IEnumerable<HelperInstance.Peer> Older() => retired ? [] : [new(7,"old/FsCopilot.Efb.exe",new(0,9,10,0),()=>{retired=true;old.Dispose();return true;})];
        using(var current=HelperInstance.Acquire(root,version,Older,notes.Add))
            RejoinChecks.Check(current is not null && retired,"Older lease owner was not replaced");
        using(var current=HelperInstance.Acquire(root,version,()=>[],notes.Add))
        using(var duplicate=HelperInstance.Acquire(root,version,()=>[new(8,"same/FsCopilot.Efb.exe",version,()=>throw new Exception("Same version killed"))],notes.Add))
            RejoinChecks.Check(current is not null && duplicate is null,"Same version duplication was not refused");
        foreach(var other in new Version?[]{null,new(0,9,13,0)}) {
            using var current=HelperInstance.Acquire(root,version,()=>[new(9,"unknown-or-newer",other,()=>throw new Exception("Unverified/newer helper killed"))],notes.Add);
            RejoinChecks.Check(current is null,"Unknown/newer helper was ignored");
        }
        using(var refused=HelperInstance.Acquire(root,version,()=>[new(10,"old",new(0,9,1,0),()=>false)],notes.Add))
            RejoinChecks.Check(refused is null,"A failed older-helper stop allowed a duplicate");
        using(var restarted=HelperInstance.Acquire(root,version,()=>[],notes.Add))
            RejoinChecks.Check(restarted is not null,"An old lock file prevented clean restart");
        Console.WriteLine("PASS production helper startup: exclusive lease, duplicate refusal, older-version replacement, failed-stop/newer/unknown refusal and restart without deleting lock files.");
    }
}
