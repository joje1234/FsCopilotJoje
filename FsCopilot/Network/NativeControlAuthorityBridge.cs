namespace FsCopilot.Network;

using FsCopilot.Native;
using FsCopilot.Ownership;
using FsCopilot.Simulation;

/// <summary>Actual transport adapter. Native callbacks, network snapshots,
/// accepted-input fences and coordinator ticks share one serialized scheduler.</summary>
public sealed class NativeControlAuthorityBridge : IDisposable, IAsyncDisposable
{
    private readonly INetwork _network;
    private readonly INativeSessionClient _native;
    private readonly string _local;
    private readonly EventLoopScheduler _scheduler = new();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ControlAuthorityNetworkHost? _host;
    private readonly ControlAuthorityReplica? _replica;
    private readonly NativeAuthorityCoordinator _coordinator;
    private readonly NativeAltimeterCoordinator _instruments;
    private readonly AltimeterAuthorityReplica _instrumentReplica;
    private readonly INativeGuidanceClient? _guidanceClient;
    private readonly NativeGuidanceCoordinator? _guidance;
    private readonly DefinitionCockpitBridge? _cockpit;
    private readonly GuidanceAuthorityReplica _guidanceReplica;
    private readonly ISubject<NativeAuthorityProgress> _progress = Subject.Synchronize(new ReplaySubject<NativeAuthorityProgress>(1));
    private int _disposed;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _peerClock = Stopwatch.StartNew();
    private TimeSpan _peerSeen, _peerSent = TimeSpan.MinValue;
    private ulong _peerSequence, _aliveSequence;
    private bool _seenPhysicsHost;
    private NativeBridgeState? _diagnosticNative;
    private NativeAuthorityPhase? _diagnosticPhase;
    private TimeSpan _diagnosticAt = TimeSpan.MinValue;
    private ulong _worldSent, _worldReceived, _worldAccepted, _worldSentSequence, _worldReceivedSequence;
    public IObservable<NativeAuthorityProgress> Progress => _progress;
    public AltimeterAuthoritySnapshot Instruments => _instruments.State;
    public string InstrumentError => _instruments.Error;
    public FlightGuidanceSnapshot? Guidance => _guidance?.State;
    public string GuidanceError => _guidance?.Error ?? "AP transport is unavailable.";
    public NativeControlAuthorityBridge(INetwork network, INativeSessionClient native, string local, NegotiatedControlSession negotiated, bool automatic = false, CockpitDefinitionProfile? cockpitProfile = null)
    {
        var initial = negotiated.Authority?.Snapshot ?? negotiated.AuthorityReplica?.Snapshot
            ?? throw new ArgumentException("Four-group authority must be negotiated first.", nameof(negotiated));
        if ((negotiated.Authority is null) == (negotiated.AuthorityReplica is null) ||
            negotiated.Authority is not null && local != initial.PhysicsHost || negotiated.AuthorityReplica is not null && local != initial.Peer)
            throw new ArgumentException("Authority role does not match the local transport identity.", nameof(local));
        var current = native.State;
        if (!current.Live || current.Snapshot is not { } observation || current.Session is not { Phase: NativeSessionPhase.Armed } session)
            throw new InvalidOperationException("Join from the local EFB before binding native controls.");
        var axes = Enum.GetValues<AtomicControlGroup>().Aggregate(0u, (mask, group) => mask | AtomicProtocol.RequiredAxes(group, initial.Engines, initial.SteeringAxes));
        var needed = negotiated.Authority is not null ? axes : observation.Required;
        if ((observation.Required & ~axes) != 0 || (observation.Learned & needed) != needed)
            throw new InvalidOperationException("Finish learning the required controls in local Setup before joining shared controls.");
        _network = network; _native = native; _local = local; _replica = negotiated.AuthorityReplica;
        var clock = Stopwatch.StartNew();
        _coordinator = new(local, initial, new(observation.Native, current.Backend, session.Consent, observation.Aircraft, observation.Adapter),
            () => clock.Elapsed, native.TrySendSession, SendAuthority);
        _instrumentReplica = new(initial);
        if (native is INativeCockpitClient cockpitClient && cockpitProfile is not null)
            _cockpit = new(network,native,cockpitClient,cockpitProfile,initial,local,()=>_coordinator.Progress is {Phase:NativeAuthorityPhase.Active,Recovering:false},_scheduler);
        _coordinator.RecoveryChanged += OnRecovery;
        _coordinator.ControlValuesChanged += OnControlValues;
        _instruments = new(local, _coordinator, () => clock.Elapsed, SendInstrumentAuthority, StartAltimeters, DispatchAltimeter);
        _guidanceReplica = new(initial);
        _guidanceClient = native as INativeGuidanceClient;
        if (_guidanceClient is not null) {
            _guidance = new(local, _coordinator, _guidanceClient, () => clock.Elapsed, SendGuidanceAuthority,
                StartGuidance, ObserveGuidance, ConfirmGuidance, DispatchGuidance,
                (command, write) => _host?.ExecuteQueuedGuidance(command, write) == true);
            _guidanceClient.GuidanceCommandDropped += OnGuidanceDropped;
        }
        _coordinator.WorldProduced += frame => {
            network.SendAll(new SharedWorldPacket(frame.Copy()), unreliable: true);
            ++_worldSent; _worldSentSequence = frame.Sequence;
        };
        if (local != initial.PhysicsHost) _subscriptions.Add(network.StreamWithSource<SharedWorldPacket>().ObserveOn(_scheduler).Subscribe(packet => {
            ++_worldReceived; _worldReceivedSequence = packet.Packet.Frame.Sequence;
            if (_coordinator.QueueWorld(packet.Sender, packet.Packet.Frame.Copy())) ++_worldAccepted;
        }));
        if (negotiated.Authority is { } authority) {
            _host = new(network, authority, _scheduler);
            _subscriptions.Add(_host.States.ObserveOn(_scheduler).Subscribe(state => { _coordinator.UpdateAuthority(state); Publish(); }));
            _subscriptions.Add(_host.Altimeters.ObserveOn(_scheduler).Subscribe(state => _instruments.Update(state)));
            if (_guidance is not null) {
                _subscriptions.Add(_host.Guidance.ObserveOn(_scheduler).Subscribe(state => _guidance.Update(state)));
                _subscriptions.Add(_host.GuidanceReceipts.ObserveOn(_scheduler).Subscribe(receipt => _guidance.Receipt(initial.PhysicsHost, receipt)));
            }
            _subscriptions.Add(_host.AcceptedInputs.ObserveOn(_scheduler).Subscribe(input => {
                if (input.Sender == _local) return; // Local hardware already drives the physics host.
                var copy = input with { Frame = input.Frame with { Axes = input.Frame.Axes.ToArray() } };
                // This callback executes on the same actor as subsequent host
                // mutations. The authority copies and rechecks accepted values
                // once more immediately before the actual native queue.
                _ = _host.ApplyAcceptedInput(copy, accepted => _coordinator.QueueAcceptedInput(copy.Sender, accepted));
            }));
        } else {
            _subscriptions.Add(network.StreamWithSource<ControlAuthorityPacket>().ObserveOn(_scheduler).Subscribe(packet => {
                if (packet.Sender == initial.PhysicsHost && AuthorityWire.TryDecode(packet.Packet.Payload, out var message) &&
                    message is AuthorityState state && _replica!.Receive(packet.Sender, state.State)) {
                    _coordinator.UpdateAuthority(_replica.Snapshot); Publish();
                }
            }));
            _subscriptions.Add(network.Peers.ObserveOn(_scheduler).Subscribe(peers => {
                // Peers is a replaying stream; its initial empty collection can
                // precede the connect notification even after negotiation.
                // Heartbeat/world deadlines still bound a host never observed.
                if (peers.Any(peer => peer.PeerId == initial.PhysicsHost)) _seenPhysicsHost = true;
                else if (_seenPhysicsHost) _coordinator.End("The physics host disconnected.");
            }));
        }
        _coordinator.Ended += OnEnded;
        _subscriptions.Add(network.StreamWithSource<ControlAuthorityPacket>().ObserveOn(_scheduler).Subscribe(packet => {
            if (!AuthorityWire.TryDecode(packet.Packet.Payload, out var message)) return;
            if (message is AuthorityAltimeterAvailability available) _instruments.ReceiveAvailability(packet.Sender, available.Availability);
            else if (_host is null && message is AuthorityAltimeterState instruments &&
                _instrumentReplica.Receive(packet.Sender, instruments.State, _coordinator.Authority)) _instruments.Update(_instrumentReplica.Snapshot);
            if (message is AuthorityGuidanceAvailability availability) _guidance?.Availability(packet.Sender, availability.Availability);
            else if (_host is null && message is AuthorityGuidanceState guidance &&
                _guidanceReplica.Receive(packet.Sender, guidance.State, _coordinator.Authority)) _guidance?.Update(_guidanceReplica.Snapshot);
            else if (_host is null && message is AuthorityGuidanceReceipt receipt) _guidance?.Receipt(packet.Sender, receipt.Receipt);
        }));
        var remote = local == initial.PhysicsHost ? initial.Peer : initial.PhysicsHost;
        _subscriptions.Add(network.StreamWithSource<ControlAuthorityPacket>().ObserveOn(_scheduler).Subscribe(packet => {
            if (packet.Sender == remote && AuthorityWire.TryDecode(packet.Packet.Payload, out var message) && message is AuthorityAlive alive &&
                alive.Session == initial.Session && alive.Sequence > _peerSequence) {
                _peerSequence = alive.Sequence; _peerSeen = _peerClock.Elapsed;
            }
        }));
        native.StateChanged += OnNative;
        native.SessionCommandDropped += OnDropped;
        _scheduler.Schedule(() => Observe(current));
        var initializeAutomatic = automatic && local == initial.PhysicsHost;
        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(33), _scheduler).Subscribe(_ => {
            // Binding retains the proven Auto-off suppression contract. Apply
            // the host's selected default only after native ownership is active.
            if (initializeAutomatic && _coordinator.Progress.Phase == NativeAuthorityPhase.Active) {
                initializeAutomatic = false; _coordinator.SetAutomatic(true);
            }
            if (_coordinator.Progress.Phase != NativeAuthorityPhase.Ended) {
                _coordinator.PeerDelayed(_peerClock.Elapsed - _peerSeen > NativeSessionTiming.PeerFresh);
                if (_peerClock.Elapsed - _peerSeen >= NativeSessionTiming.Recovery) _coordinator.End("The other player's heartbeat recovery deadline exceeded (8 seconds).");
                else if (_peerSent == TimeSpan.MinValue || _peerClock.Elapsed - _peerSent >= TimeSpan.FromMilliseconds(500)) {
                    _peerSent = _peerClock.Elapsed;
                    try { _network.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(new AuthorityAlive(AtomicProtocol.Version, initial.Session, ++_aliveSequence)))); }
                    catch (Exception) { _coordinator.End("Shared session transport failed."); }
                }
            }
            _coordinator.Tick(); _instruments.Tick(); _guidance?.Tick(); Publish();
        }));
        Publish();
    }
    public void Request(ControlSelection selection, bool me, bool force = false) => Schedule(() => {
        var state = _coordinator.Authority;
        _coordinator.Request(selection, me ? _local : _local == state.PhysicsHost ? state.Peer : state.PhysicsHost, force);
        Publish();
    });
    public void Cancel(ControlSelection selection) => Schedule(() => { _coordinator.Cancel(selection); Publish(); });
    public void ClaimGuidance(bool me) => Schedule(() => _guidance?.Claim(me));
    private void Schedule(Action action) {
        if (Volatile.Read(ref _disposed) == 0) _scheduler.Schedule(() => { if (Volatile.Read(ref _disposed) == 0) action(); });
    }
    private void OnNative(NativeBridgeState state) { var copy = state.Copy(); Schedule(() => Observe(copy)); }
    private void OnRecovery(string message, bool recovered) {
        if (recovered) Log.Information("[Shared recovery] {Message}", message);
        else Log.Warning("[Shared recovery] {Message}", message);
    }
    private void OnControlValues(uint unavailable, uint recovered) {
        static string Names(uint mask) => string.Join(", ", Enumerable.Range(0, 21)
            .Where(axis => (mask & (1u << axis)) != 0).Select(axis => ((ControlAxis)axis).ToString()));
        if (unavailable != 0) Log.Warning("[Control values] Unavailable: {Axes}. Affected group output/pickup waits; fresh aircraft updates and other groups continue.", Names(unavailable));
        if (recovered != 0) Log.Information("[Control values] Readings recovered: {Axes}.", Names(recovered));
    }
    private void OnDropped(ulong command) => Schedule(() => {
        Log.Warning("[Shared diagnostic] Native command {Command} was dropped before SDK dispatch", command);
        _coordinator.CommandDropped(command); _instruments.CommandDropped(command);
    });
    private void Observe(NativeBridgeState state) {
        _diagnosticNative = state;
        if ((!state.TransportConnected || !state.Live || state.Session is null) && state.Error.Length > 0)
            _coordinator.End("Native connection unavailable: " + state.Error);
        else _coordinator.Observe(state.Live, state.Backend, state.Snapshot, state.Session);
        Publish();
    }
    private void Publish() {
        var progress = _coordinator.Progress;
        // One summary per second and on phase changes. No per-axis event log,
        // location coordinates, consent token or extra simulator commands.
        var now = _peerClock.Elapsed;
        if (_diagnosticPhase != progress.Phase || _diagnosticAt == TimeSpan.MinValue || now - _diagnosticAt >= TimeSpan.FromSeconds(1)) {
            _diagnosticPhase = progress.Phase; _diagnosticAt = now;
            var observed = _diagnosticNative?.Snapshot; var native = _diagnosticNative?.Session;
            var missing = observed is null ? "unavailable" : string.Join(",", Enumerable.Range(0, 21)
                .Where(axis => (observed.Required & (1u << axis)) != 0 && observed.Targets[axis] < 0));
            Log.Information("[Shared diagnostic] Local {Local}; host {Host}; session {Session}; phase {Phase}; reason {Reason}; world sent/received/accepted {Sent}/{Received}/{Accepted}; last sequences {SentSequence}/{ReceivedSequence}",
                _local, _coordinator.Authority.PhysicsHost, _coordinator.Authority.Session, progress.Phase, progress.Error,
                _worldSent, _worldReceived, _worldAccepted, _worldSentSequence, _worldReceivedSequence);
            Log.Information("[Shared diagnostic] Native {Native}; live {Live}; serials observation/session/world {ObservationSerial}/{SessionSerial}/{WorldSerial}; phases native/backend {NativePhase}/{BackendPhase}; result/error {Result}/{Error}; command/ack {Command}/{Ack}; flags {Flags}; blocked/attached {Blocked}/{Attached}; required/learned/fresh {Required}/{Learned}/{Fresh}; missing targets {Missing}; native error {NativeError}",
                observed?.Native, _diagnosticNative?.Live, observed?.Serial, native?.Serial, native?.WorldSerial,
                native?.Phase, observed?.BackendPhase, native?.Result, native?.Code, native?.Command, native?.Ack,
                native?.Flags, native?.Blocked, native?.Attached, observed?.Required, observed?.Learned, observed?.Fresh,
                missing, _diagnosticNative?.Error);
        }
        _progress.OnNext(progress);
    }
    private void SendAuthority(AuthorityMessage message) {
        if (_host is not null) _ = _host.ApplyLocal(message);
        else _network.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(message)));
    }
    private void SendInstrumentAuthority(AuthorityMessage message) {
        if (message is AuthorityAltimeterAvailability) _network.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(message)));
        else SendAuthority(message);
    }
    private void SendGuidanceAuthority(AuthorityMessage message) {
        if (message is AuthorityGuidanceAvailability) _network.SendAll(new ControlAuthorityPacket(AuthorityWire.Encode(message)));
        else SendAuthority(message);
    }
    private void OnGuidanceDropped(ulong command) => Schedule(() => _guidance?.Dropped(command));
    private void StartGuidance(GuidanceState state, ulong readSerial) {
        if (_host is null) { _guidance?.StartDeclined(); return; }
        _ = _host.StartGuidance(state, readSerial).ContinueWith(task => Schedule(() => {
            if (!task.IsCompletedSuccessfully || !task.Result) _guidance?.StartDeclined();
        }), TaskScheduler.Default);
    }
    private void ObserveGuidance(ulong readSerial, GuidanceState state) { if (_host is not null) _ = _host.ObserveGuidance(readSerial, state); }
    private void ConfirmGuidance(GuidanceCommand command, ulong readSerial, GuidanceState state, bool succeeded) {
        if (_host is not null) _ = _host.ConfirmGuidance(command, readSerial, state, succeeded);
    }
    private void DispatchGuidance(GuidanceCommand command, ulong beforeRead, bool retry) {
        if (_host is null || _guidance is null) return;
        _ = _host.ApplyGuidance(command, beforeRead, _guidance.QueueAccepted, retry).ContinueWith(task => Schedule(() => {
            if (!task.IsCompletedSuccessfully || !task.Result) _guidance?.DispatchDeclined(command);
        }), TaskScheduler.Default);
    }
    private void StartAltimeters(AltimeterSetting[] settings, ulong readSerial) {
        if (_host is null) { _instruments.StartDeclined(); return; }
        _ = _host.StartAltimeters(settings, readSerial, initialInputHeld: true).ContinueWith(task => Schedule(() => {
            if (!task.IsCompletedSuccessfully || !task.Result) _instruments.StartDeclined();
        }), TaskScheduler.Default);
    }
    private void DispatchAltimeter(AltimeterChange change, ulong beforeReadSerial, bool retry) {
        if (_host is null) { _instruments.DispatchDeclined(change); return; }
        _ = _host.ApplyAltimeter(change, beforeReadSerial, _instruments.QueueAccepted, retry).ContinueWith(task => Schedule(() => {
            if (!task.IsCompletedSuccessfully || !task.Result) _instruments.DispatchDeclined(change);
        }), TaskScheduler.Default);
    }
    private void OnEnded() {
        if (Volatile.Read(ref _disposed) != 0 || _coordinator.Progress.Error == "Shared session ended.")
            Log.Information("[Shared ended] {Reason}", _coordinator.Progress.Error);
        else Log.Error("[Shared ended] {Reason}", _coordinator.Progress.Error);
        if (_host is not null) _ = _host.EndSession();
        else { try { _network.Disconnect(); } catch (Exception error) { Log.Warning(error, "[Ownership] Shared session transport already closed"); } }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _native.StateChanged -= OnNative; _native.SessionCommandDropped -= OnDropped;
        if (_guidanceClient is not null) { _guidanceClient.GuidanceCommandDropped -= OnGuidanceDropped; _guidanceClient.SetGuidanceFence(null); }
        _subscriptions.Dispose();
        _scheduler.Schedule(() => {
            _cockpit?.Dispose();
            _coordinator.Ended -= OnEnded;
            _coordinator.RecoveryChanged -= OnRecovery;
        _coordinator.ControlValuesChanged -= OnControlValues;
            _coordinator.End();
            var ended = _host?.EndSession() ?? Task.FromResult(true);
            _ = ended.ContinueWith(_ => {
                try { _host?.Dispose(); _scheduler.Dispose(); _progress.OnNext(_coordinator.Progress); _progress.OnCompleted(); }
                finally { _closed.TrySetResult(); }
            }, TaskScheduler.Default);
        });
    }
    public ValueTask DisposeAsync() { Dispose(); return new(_closed.Task); }
}
