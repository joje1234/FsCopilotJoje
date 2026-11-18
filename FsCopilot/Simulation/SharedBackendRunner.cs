namespace FsCopilot.Simulation;

using FsCopilot.Native;
using FsCopilot.Network;
using System.Runtime.Versioning;

internal static class SharedBackendRunner
{
    [SupportedOSPlatform("windows")]
    public static async Task RunFromEfb(EfbCompanionOptions options)
    {
        Log.Information("[EFB] Helper uses connection settings saved in the tablet.");
        if (options.DryRun) {
            var roles = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Definitions", "controls"), "*.json");
            if (roles.Length == 0) throw new InvalidDataException("The helper has no packaged aircraft control roles.");
            foreach (var file in roles) {
                var profile = ControlDefinitionProfile.Load("simobjects/airplanes/" + Path.GetFileNameWithoutExtension(file) + "/aircraft.cfg");
                Log.Information("[EFB] Validated definitions: {Aircraft}, {Adapter}, {Count} roles", profile.Aircraft, profile.Adapter, profile.Axes.Count);
                var cockpit = CockpitDefinitionProfile.Load(profile.Aircraft);
                Log.Information("[EFB] Validated ordinary cockpit definitions: {Count}, profile {Profile}; local/AP/control exclusions {Excluded}",cockpit.Entries.Count,cockpit.Identity,cockpit.Excluded.Count);
            }
            Log.Information("[EFB] Helper options/definitions validated; no simulator or network clients were started."); return;
        }
        using var single = new Semaphore(1, 1, "Local\\FsCopilot.EfbBackend");
        if (!single.WaitOne(0)) { Log.Information("[EFB] The EFB helper is already running."); return; }
        try {
            using var stop = options.RunSeconds is { } seconds ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; stop.Cancel(); };
            Console.CancelKeyPress += cancel;
            try {
                using var native = new NativeControlClient(enableSessions: true);
                string? observedInstance = null;
                string? observedLayout = null;
                void NativeStatus(NativeBridgeState state) {
                    if (state.Live && state.Snapshot is { } observation && observedInstance != state.Backend + ":" + observation.Native) {
                        observedInstance = state.Backend + ":" + observation.Native;
                        Log.Information("[EFB] Fresh native discovery: {Native}, connection API {Api}, capabilities {Capabilities}", observation.Native, observation.ConnectionApi, observation.Capabilities);
                    }
                    if (state.Live && state.Snapshot is { } layout) {
                        var key = layout.Native + ":" + layout.Aircraft + ":" + layout.Required + ":" + layout.Learned + ":" + layout.Capabilities;
                        if (observedLayout != key) {
                            observedLayout = key;
                            Log.Information("[EFB] Control layout native {Native}; aircraft {Aircraft}; required 0x{Required:X}; learned 0x{Learned:X}; capabilities {Capabilities}",
                                layout.Native, layout.Aircraft, layout.Required, layout.Learned, layout.Capabilities);
                        }
                    }
                }
                native.StateChanged += NativeStatus;
                var definitions = new ControlDefinitionSelection();
                CockpitDefinitionProfile? cockpitProfile=null;
                string? Validate(NativeControlPacket observation) {
                    var error=definitions.Validate(observation.Native,observation.Aircraft,observation.Adapter,observation.Engines);
                    if(error is not null) return error;
                    try {
                        if(cockpitProfile?.Aircraft!=definitions.Profile!.Aircraft) cockpitProfile=CockpitDefinitionProfile.Load(definitions.Profile!.Aircraft);
                        return null;
                    } catch(Exception loadError) {return "Ordinary cockpit definitions unavailable: "+loadError.Message;}
                }
                await using var backend = new EfbConnectionBackend(native, settings => settings.Port == 3600 ?
                    new HybridNetwork(settings.Relay, settings.Local, options.Name) :
                    new RelayNetwork(settings.Relay, settings.Local, options.Name, false, (int)settings.Port),
                    Validate,_=>cockpitProfile);
                using var progress = backend.Progress.Subscribe(state => Log.Information("[EFB] {Phase}: {Message}", state.Notice.Phase, state.Message));
                try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            } finally { Console.CancelKeyPress -= cancel; }
        } finally { single.Release(); }
    }
    [SupportedOSPlatform("windows")]
    public static async Task Run(SharedBackendOptions options)
    {
        Log.Information("[EFB] Backend code={Local}, peer={Remote}, physics={Role}, seat={Seat}, relay={Host}:{Port}",
            options.Session.Local, options.Session.Remote, options.Session.Role, options.Session.Seat, options.RelayHost, options.RelayPort);
        if (options.DryRun) {
            Log.Information("[EFB] Backend options validated; no simulator or network clients were started."); return;
        }
        using var stop = options.RunSeconds is { } seconds ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try {
            // This path deliberately constructs neither the vanilla Coordinator
            // nor MasterSwitch: their interaction/axis/rotary and host-switch
            // writers cannot run alongside native per-group authority.
            using var network = new RelayNetwork(options.RelayHost, options.Session.Local, options.Name, false, options.RelayPort);
            using var native = new NativeControlClient(enableSessions: true);
            var definitions = new ControlDefinitionSelection();
            await using var service = new NativeSharedSessionService(network, native, options.Session,
                observation => definitions.Validate(observation.Native, observation.Aircraft, observation.Adapter, observation.Engines));
            using var progress = service.Progress.Subscribe(state => Log.Information("[EFB] {Phase}: {Message} ({Session})", state.Phase, state.Message, state.Session));
            try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        } finally { Console.CancelKeyPress -= cancel; }
    }
}
