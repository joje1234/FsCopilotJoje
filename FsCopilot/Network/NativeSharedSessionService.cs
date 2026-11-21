namespace FsCopilot.Network;

using System.Security.Cryptography;
using FsCopilot.Native;
using FsCopilot.Ownership;
using FsCopilot.Simulation;

public enum SharedServicePhase { WaitingForJoin, WaitingForNative, Negotiating, Binding, Active, Ended }
public sealed record SharedServiceProgress(SharedServicePhase Phase, string Message, Guid? Session = null, bool Recovering = false);
public sealed record SharedSessionOptions(string Local, string Remote, SessionRole Role, CockpitSeat Seat);

/// <summary>
/// Owns one configured two-player connection. A fresh local EFB consent starts
/// each attempt; socket reconnection or retained native data cannot restart it.
/// It does not construct the legacy physics switch or interaction broadcaster.
/// </summary>
public sealed class NativeSharedSessionService : IDisposable, IAsyncDisposable
{
    private readonly INetwork _network;
    private readonly INativeSessionClient _native;
    private readonly SharedSessionOptions _options;
    private readonly Func<NativeControlPacket, string?>? _validateProfile;
    private readonly Func<NativeControlPacket, CockpitDefinitionProfile?>? _cockpitProfile;
    private readonly EventLoopScheduler _scheduler = new();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ISubject<SharedServiceProgress> _progress = Subject.Synchronize(new ReplaySubject<SharedServiceProgress>(1));
    private readonly ISubject<NegotiatedControlSession?> _sessions = Subject.Synchronize(new ReplaySubject<NegotiatedControlSession?>(1));
    private readonly HashSet<string> _usedConsent = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OwnershipNetworkNegotiation? _join;
    private NativeControlAuthorityBridge? _bridge;
    private CompositeDisposable _attemptSubscriptions = new();
    private CancellationTokenSource? _connect;
    private NativeBridgeState _current;
    private NativeAuthorityIdentity? _identity;
    private SessionConfiguration? _configuration;
    private ulong _observationSerial, _sessionSerial, _attempt;
    private TimeSpan _observationAt = TimeSpan.MinValue, _sessionAt = TimeSpan.MinValue;
    private SharedServiceProgress _status = new(SharedServicePhase.WaitingForJoin, "Join shared controls from the local EFB.");
    private int _disposed;
    public IObservable<SharedServiceProgress> Progress => _progress.DistinctUntilChanged();
    public IObservable<NegotiatedControlSession?> Sessions => _sessions;

    public NativeSharedSessionService(INetwork network, INativeSessionClient native, SharedSessionOptions options,
        Func<NativeControlPacket, string?>? validateProfile = null, Func<NativeControlPacket, CockpitDefinitionProfile?>? cockpitProfile = null)
    {
        if (!OwnershipProtocol.ValidPeer(options.Local) || !OwnershipProtocol.ValidPeer(options.Remote) || options.Local == options.Remote ||
            !Enum.IsDefined(options.Role) || !Enum.IsDefined(options.Seat)) throw new ArgumentException("Invalid shared session participants or seat.", nameof(options));
        _network = network; _native = native; _options = options; _validateProfile = validateProfile; _cockpitProfile=cockpitProfile; _current = native.State;
        // Register on both processes before Connect; registration order is part
        // of the actual transport's codec schema.
        OwnershipNetworkNegotiation.RegisterProtocol(network);
        _progress.OnNext(_status); _sessions.OnNext(null);
        native.StateChanged += OnNative;
        Schedule(() => Observe(_current));
        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(100), _scheduler).Subscribe(_ => Tick()));
    }
    private void Schedule(Action action)
    {
        if (Volatile.Read(ref _disposed) == 0) _scheduler.Schedule(() => { if (Volatile.Read(ref _disposed) == 0) action(); });
    }
    private void OnNative(NativeBridgeState state) { var copy = state.Copy(); Schedule(() => Observe(copy)); }
    private void Publish(SharedServicePhase phase, string message, Guid? session = null, bool recovering = false) => _progress.OnNext(_status = new(phase, message, session, recovering));
    private void Observe(NativeBridgeState state)
    {
        if (_current.Backend != state.Backend || _current.Snapshot?.Native != state.Snapshot?.Native) {
            _observationSerial = _sessionSerial = 0; _observationAt = _sessionAt = TimeSpan.MinValue;
        }
        _current = state.Copy();
        if (state.Live && state.Snapshot is { } observation && NativeControlWire.Valid(observation) && observation.Serial > _observationSerial) {
            _observationSerial = observation.Serial; _observationAt = _clock.Elapsed;
        }
        if (state.Live && state.Session is { } native && NativeSessionWire.Valid(native) && native.Serial > _sessionSerial) {
            _sessionSerial = native.Serial; _sessionAt = _clock.Elapsed;
        }
        Tick();
    }
    private TimeSpan Age(TimeSpan observed) => observed == TimeSpan.MinValue ? TimeSpan.MaxValue : _clock.Elapsed - observed;
    private bool Fresh => _current.Live && Age(_observationAt) <= TimeSpan.FromSeconds(1) && Age(_sessionAt) <= TimeSpan.FromSeconds(1);
    // Backend GUIDs change on SDK reconnect. That must not make already-used
    // local consent reusable while native is still advertising its armed token.
    private static string ConsentKey(NativeBridgeState state) => state.Session!.Native + ":" + state.Session.Consent;

    private void Tick()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_current.Live && _current.Snapshot is { } profiled && _validateProfile?.Invoke(profiled) is { } profileError) {
            if (_identity is not null) EndAttempt(profileError);
            else Publish(SharedServicePhase.WaitingForNative, profileError);
            return;
        }
        if (_identity is not null) {
            var observation = _current.Snapshot; var native = _current.Session;
            // Grace retains an already constructed native session; it never
            // extends a fresh join/consent proof using retained discovery data.
            var retained = _bridge is null ? Fresh : _current.TransportConnected && _current.Live &&
                Age(_observationAt) < NativeSessionTiming.Recovery && Age(_sessionAt) < NativeSessionTiming.Recovery;
            if (!retained || observation is null || native is null || !NativeControlWire.Valid(observation) || !NativeSessionWire.Valid(native) ||
                observation.Native != _identity.Native || native.Native != _identity.Native ||
                _current.Backend != _identity.Backend || native.Consent != _identity.Consent ||
                observation.Aircraft != _identity.Aircraft || observation.Adapter != _identity.Adapter ||
                observation.Engines != _configuration!.Engines || observation.Required != _required ||
                (observation.Capabilities & (uint)_configuration.Capabilities) != (uint)_configuration.Capabilities || (observation.Learned & _required) != _required ||
                native.Phase is NativeSessionPhase.Idle or NativeSessionPhase.Ended) {
                var changedReason = "Native connection, aircraft or local join changed. Join again from the EFB.";
                if (!retained) changedReason = _current.Error.Length > 0 ? _current.Error : "Fresh native observations did not return within the session recovery window.";
                if (observation is not null && observation.Native == _identity.Native && observation.Aircraft == _identity.Aircraft) {
                    if (observation.Required != _required) changedReason = $"Aircraft control definitions changed from 0x{_required:x} to 0x{observation.Required:x}. Check matching packages and definitions on both PCs before joining again.";
                    else if ((observation.Learned & _required) != _required) changedReason = "A required hardware mapping changed or is no longer learned. Return to Me and finish learning before joining again.";
                }
                EndAttempt(changedReason); return;
            }
            return;
        }
        if (_current.Live && _current.Snapshot is { } available && NativeControlWire.Valid(available) &&
            (available.Capabilities & (uint)SessionNegotiation.AdvancedCapabilities) != (uint)SessionNegotiation.AdvancedCapabilities) {
            if (_status.Phase != SharedServicePhase.Ended) Publish(SharedServicePhase.WaitingForNative,
                "This native package has not enabled verified shared-control output. Keep using its local test mode.");
            return;
        }
        if (!Fresh || _current.Snapshot is null || _current.Session is null) {
            if (_status.Phase != SharedServicePhase.Ended) Publish(SharedServicePhase.WaitingForNative,
                _current.Error.Length == 0 ? "Waiting for current native aircraft and session observations." : _current.Error);
            return;
        }
        if (_current.Session is not { Phase: NativeSessionPhase.Armed } armed || armed.Consent.Length == 0) {
            if (_status.Phase != SharedServicePhase.Ended) Publish(SharedServicePhase.WaitingForJoin, "Join shared controls from the local EFB.");
            return;
        }
        if (_usedConsent.Contains(ConsentKey(_current))) return;
        if (!TryConfiguration(_current, out var configuration, out var reason)) { Publish(SharedServicePhase.WaitingForNative, reason); return; }
        Begin(configuration!);
    }
    private uint _required;
    public static bool TryConfiguration(NativeBridgeState state, out SessionConfiguration? configuration, out string reason)
    {
        configuration = null; reason = "Waiting for the native aircraft/control bridge.";
        if (!state.Live || state.Snapshot is not { } observation || !NativeControlWire.Valid(observation) ||
            state.Session is not { Phase: NativeSessionPhase.Armed } native || !NativeSessionWire.Valid(native) ||
            state.Backend.Length != 32 || !state.Backend.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9') || native.Consent.Length != 32 ||
            native.Native != observation.Native || !observation.Flags.HasFlag(NativeStatusFlags.Ready | NativeStatusFlags.TargetsFresh) ||
            observation.Flags.HasFlag(NativeStatusFlags.Applying)) return false;
        if ((observation.Capabilities & (uint)SessionNegotiation.AdvancedCapabilities) != (uint)SessionNegotiation.AdvancedCapabilities) {
            reason = "This native package has not enabled verified shared-control output. Keep using its local test mode."; return false;
        }
        if (!native.Flags.HasFlag(NativeSessionFlags.WorldFresh)) { reason = "Waiting for current native world/physics observations."; return false; }
        if ((observation.Learned & observation.Required) != observation.Required) {
            reason = "Learn the required controls while Me is active before joining shared controls."; return false;
        }
        var steering = observation.Required & 0x1ff;
        var aircraft = "aircraft-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(observation.Aircraft)));
        var proposed = new SessionConfiguration(aircraft, (int)observation.Engines, observation.Adapter, NativeWorldFrame.BehaviorVersion,
            SessionNegotiation.AdvancedCapabilities, steering);
        if (!SessionNegotiation.ValidConfiguration(proposed)) { reason = "The aircraft/control schema is unavailable or unsupported."; return false; }
        var needed = Enum.GetValues<AtomicControlGroup>().Aggregate(0u, (mask, group) => mask | AtomicProtocol.RequiredAxes(group, proposed.Engines, steering));
        if (observation.Required != needed || (observation.Learned & needed) != needed) {
            reason = "Finish learning the complete steering and engine-control schema before joining."; return false;
        }
        configuration = proposed; reason = ""; return true;
    }
    private void Begin(SessionConfiguration configuration)
    {
        var observed = _current.Snapshot!; var native = _current.Session!;
        _usedConsent.Add(ConsentKey(_current)); _identity = new(observed.Native, _current.Backend, native.Consent, observed.Aircraft, observed.Adapter);
        _configuration = configuration; _required = observed.Required; var attempt = ++_attempt;
        Publish(SharedServicePhase.Negotiating, "Connecting the two simulator sessions.");
        _join = new(_network, _options.Local, _options.Remote, _options.Role, configuration, TimeSpan.FromSeconds(15), _options.Seat);
        _attemptSubscriptions.Add(_join.States.ObserveOn(_scheduler).Subscribe(state => {
            if (attempt != _attempt || _identity is null) return;
            if (state.Phase is NegotiationPhase.Rejected or NegotiationPhase.TimedOut or NegotiationPhase.Closed or NegotiationPhase.PeerLost)
                EndAttempt(state.Error.Length == 0 ? "Shared connection ended. Join again from the EFB." : state.Error);
        }));
        _attemptSubscriptions.Add(_join.Sessions.ObserveOn(_scheduler).Subscribe(session => {
            if (attempt != _attempt || _identity is null || session is null || _bridge is not null) return;
            if (!Fresh || _current.Session?.Phase != NativeSessionPhase.Armed) { EndAttempt("Local EFB consent expired during negotiation."); return; }
            try {
                _bridge = new(_network, _native, _options.Local, session, automatic: observed.Automatic != 0,cockpitProfile:_cockpitProfile?.Invoke(observed));
                _sessions.OnNext(session);
                _attemptSubscriptions.Add(_bridge.Progress.ObserveOn(_scheduler).Subscribe(progress => {
                    if (attempt != _attempt || _identity is null) return;
                    if (progress.Phase == NativeAuthorityPhase.Ended) EndAttempt(progress.Error);
                    else Publish(progress.Phase == NativeAuthorityPhase.Active ? SharedServicePhase.Active : SharedServicePhase.Binding,
                        progress.Recovering ? progress.Error : progress.Phase == NativeAuthorityPhase.Active ? "Shared controls connected." : "Confirming native hardware suppression.",
                        session.Authority?.Snapshot.Session ?? session.AuthorityReplica?.Snapshot.Session, progress.Recovering);
                }));
            } catch (Exception error) { EndAttempt("Shared control binding failed: " + error.Message); }
        }));
        if (_options.Role == SessionRole.Copilot) {
            _connect = new(TimeSpan.FromSeconds(10)); var cancellation = _connect.Token;
            _ = Connect(attempt, cancellation);
        }
    }
    private async Task Connect(ulong attempt, CancellationToken cancellation)
    {
        try {
            // The other process may still be registering with the relay. Retry
            // only within this consent-bound connection deadline, before any
            // native binding; an ended attempt never reconnects itself.
            while (!cancellation.IsCancellationRequested) {
                if (await _network.Connect(_options.Remote, cancellation).ConfigureAwait(false) == ConnectionResult.Success) return;
                await Task.Delay(250, cancellation).ConfigureAwait(false);
            }
            Schedule(() => { if (attempt == _attempt && _identity is not null) EndAttempt("Could not connect to the other pilot. Join again from the EFB."); });
        } catch (Exception error) { Schedule(() => { if (attempt == _attempt && _identity is not null) EndAttempt("Shared connection failed: " + error.Message); }); }
    }
    private void EndAttempt(string reason)
    {
        if (_identity is null) return;
        if (reason.StartsWith("Shared backend stopped", StringComparison.Ordinal)) Log.Information("[Shared stopped] {Reason}", reason);
        else Log.Error("[Shared stopped] {Reason}; observation/session ages {ObservationAge}/{SessionAge} ms", reason,
            Age(_observationAt).TotalMilliseconds, Age(_sessionAt).TotalMilliseconds);
        _identity = null; ++_attempt;
        _connect?.Cancel(); _connect?.Dispose(); _connect = null;
        _attemptSubscriptions.Dispose(); _attemptSubscriptions = new();
        _join?.Dispose(); _join = null;
        var oldBridge = _bridge; _bridge = null;
        if (oldBridge is not null) _ = oldBridge.DisposeAsync().AsTask();
        _sessions.OnNext(null); Publish(SharedServicePhase.Ended, reason);
        try { _network.Disconnect(); } catch (Exception error) { Log.Debug(error, "[Ownership] Connection already ended"); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _native.StateChanged -= OnNative; _subscriptions.Dispose();
        _scheduler.Schedule(() => {
            var bridge = _bridge; EndAttempt("Shared backend stopped. Local recovery remains explicit.");
            var stopped = bridge?.DisposeAsync().AsTask() ?? Task.CompletedTask;
            _ = stopped.ContinueWith(_ => {
                try { _attemptSubscriptions.Dispose(); _progress.OnCompleted(); _sessions.OnCompleted(); _scheduler.Dispose(); }
                finally { _closed.TrySetResult(); }
            }, TaskScheduler.Default);
        });
    }
    public ValueTask DisposeAsync() { Dispose(); return new(_closed.Task); }
}
