using FsCopilot.Native;
using FsCopilot.Network;
using FsCopilot.Ownership;

internal static class NativeGuidanceNetworkTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> ready, string message, Func<string> details) {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(20);
        Check(ready(), "Native AP timeout: " + message + "; " + details());
    }
    public static async Task Run(int port, bool peerSupported = true)
    {
        using var hostNative = new CppNativeSessionClient(sharedCapabilities: true, verifiedGuidance: true);
        using var peerNative = new CppNativeSessionClient(sharedCapabilities: true, verifiedGuidance: peerSupported);
        var hostId = peerSupported ? "ap-native-host" : "ap-missing-host";
        var peerId = peerSupported ? "ap-native-peer" : "ap-missing-peer";
        using var a = new RelayNetwork("127.0.0.1", hostId, "AP host", false, port);
        using var b = new RelayNetwork("127.0.0.1", peerId, "AP peer", false, port);
        using var subscriptions = new CompositeDisposable(); var registrations = 0;
        foreach (var network in new[] { a, b }) {
            OwnershipNetworkNegotiation.RegisterProtocol(network);
            subscriptions.Add(network.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref registrations)));
        }
        await Wait(() => registrations == 2, "relay registration", () => "registered=" + registrations);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Check(await a.Connect(peerId, timeout.Token) == ConnectionResult.Success, "AP clients did not connect");
        var clock = Stopwatch.StartNew(); var authority = new ControlAuthoritySession(hostId, peerId, 2, () => clock.Elapsed, 7);
        var initial = authority.Snapshot;
        var hs = new NegotiatedControlSession(SessionRole.PhysicsHost, initial.Seats, null, null, Authority: authority);
        var ps = new NegotiatedControlSession(SessionRole.Copilot, initial.Seats, null, null, AuthorityReplica: new(hostId, initial));
        await using var peer = new NativeControlAuthorityBridge(b, peerNative, peerId, ps);
        await using var host = new NativeControlAuthorityBridge(a, hostNative, hostId, hs);
        NativeAuthorityProgress? hp = null, pp = null;
        subscriptions.Add(host.Progress.Subscribe(value => hp = value)); subscriptions.Add(peer.Progress.Subscribe(value => pp = value));
        string AP(CppNativeSessionClient client) {
            var p = client.GuidanceState.Snapshot;
            return p is null ? "none" : $"{p.Flags}/{p.CommandEpoch}/{p.Preparation}/g{p.GateAck}/r{p.ReferenceAck}/v{p.ValueAck}/i{p.Sequence}";
        }
        string Detail() => $"model={authority.Guidance.Phase}/{authority.Guidance.Revision}/{authority.Guidance.CommandEpoch}; " +
            $"host={host.Guidance?.Phase}/{host.GuidanceError}/{AP(hostNative)} peer={peer.Guidance?.Phase}/{peer.GuidanceError}/{AP(peerNative)} physical={hp}/{pp}";
        await Wait(() => authority.Snapshot.Groups.All(group => group.Attached), "initial physical attachment", Detail);
        if (!peerSupported) {
            await Task.Delay(1000);
            Check(authority.Guidance.Phase == GuidancePhase.Disabled && hostNative.GuidanceState.Snapshot!.Blocked == 0 &&
                peerNative.GuidanceState.Snapshot!.Blocked == 0 && hostNative.GuidanceOutputs == 0, "Missing AP profile allowed startup or masking");
            Console.WriteLine("PASS native AP preflight refuses startup before masking when either peer lacks a verified input/reference adapter. No simulator access."); return;
        }
        await Wait(() => authority.Guidance.Phase == GuidancePhase.Active && peer.Guidance?.Phase == GuidancePhase.Active, "verified AP activation", Detail);
        var physicalRevision = authority.Snapshot.Revision; var seats = authority.Snapshot.Seats;
        Check(authority.Guidance.Commander == hostId && authority.Guidance.Navigator == hostId && authority.Guidance.SupportedSettings == 4095,
            "Initial AP role or supported-setting negotiation changed");
        peer.ClaimGuidance(true);
        await Wait(() => authority.Guidance.Phase == GuidancePhase.Active && authority.Guidance.Commander == peerId &&
            peer.Guidance?.Phase == GuidancePhase.Active, "peer claims AP commands independently of navigation", Detail);
        peerNative.GuidanceInput(GuidanceSetting.Heading, 120);
        await Wait(() => authority.Guidance.Pending is null && Math.Abs(hostNative.GuidanceState.Snapshot!.Heading - 120) < .012 &&
            Math.Abs(authority.Guidance.Confirmed!.Heading - 120) < .012, "captured peer knob becomes one host absolute target", Detail);
        Check(peerNative.GuidanceOutputs == 0 && authority.Guidance.Navigator == hostId, "Guest ran an AP writer or took navigation implicitly");
        var outputs = hostNative.GuidanceOutputs;
        var reference = peerNative.GuidanceState.Snapshot!.ReferenceAck;
        hostNative.PauseGuidanceValues(true);
        peerNative.GuidanceTap(GuidanceSetting.Master);
        await Wait(() => authority.Guidance.Pending is { Dispatched: true, Command.Setting: GuidanceSetting.Master, Command.Value: 1 } &&
            peerNative.GuidanceState.Snapshot!.ReferenceAck != reference, "AP-on queued and authoritative input reference reaches peer", Detail);
        peerNative.GuidanceTap(GuidanceSetting.Master); // Guest actual AP is still Off; reference must produce Off, not another On.
        await Wait(() => authority.Guidance.Pending is { Dispatched: true, Command.Setting: GuidanceSetting.Master, Command.Value: 0 },
            "second press disconnects pending AP-on", Detail);
        await Wait(() => hostNative.QueuedGuidanceCommand(GuidanceSetting.Master, 0) is var queued && queued != 0 &&
            hostNative.GuidanceState.Snapshot!.Command > queued, "a newer lease packet overtakes the delayed disconnect", Detail);
        hostNative.PauseGuidanceValues(false);
        await Wait(() => authority.Guidance.Phase == GuidancePhase.Active && authority.Guidance.Pending is null && !authority.Guidance.Confirmed!.Master,
            "retired AP-on cannot reach SDK after disconnect", Detail);
        Check(hostNative.GuidanceOutputs == outputs + 1 && (hostNative.GuidanceState.Snapshot!.Switches & 1) == 0,
            "Delayed AP-on executed, or Off duplicated");
        host.ClaimGuidance(true);
        await Wait(() => authority.Guidance.Phase == GuidancePhase.Active && authority.Guidance.Commander == hostId &&
            peer.Guidance?.Phase == GuidancePhase.Active, "AP controls return to host", Detail);
        outputs = hostNative.GuidanceOutputs;
        peerNative.GuidanceInput(GuidanceSetting.Heading, 180);
        await Task.Delay(250);
        Check(hostNative.GuidanceOutputs == outputs && Math.Abs(hostNative.GuidanceState.Snapshot!.Heading - 120) < .012,
            "Retired AP owner changed the host target");
        peerNative.GuidanceAdapter(false);
        await Wait(() => authority.Guidance.Phase == GuidancePhase.Holding, "AP adapter loss holds AP independently", Detail);
        Check(authority.Snapshot.Revision == physicalRevision && authority.Snapshot.Seats == seats && authority.Snapshot.PhysicsHost == hostId &&
            hp?.Phase == NativeAuthorityPhase.Active && pp?.Phase == NativeAuthorityPhase.Active, "AP handover/loss changed physical authority or seats");
        Console.WriteLine("PASS real relay + both production C++ AP sessions: supported-setting preflight, source-independent AP claims, captured absolute input, explicit SDK completion, authoritative button reference, retired queued AP-on, old-owner refusal and AP-only failure. SDK/input providers are simulated; no MSFS access.");
    }
}
