namespace FsCopilot.Network;

using FsCopilot.Native;
using FsCopilot.Ownership;
using FsCopilot.Simulation;

public sealed record EfbBackendProgress(NativeBackendNotice Notice, string Message, NativeConnectionSettings? Settings);

/// <summary>Owns the network/service selected in the local EFB. Configuration is never join consent.</summary>
public sealed class EfbConnectionBackend : IDisposable, IAsyncDisposable
{
    private readonly INativeBackendClient _native;
    private readonly Func<NativeConnectionSettings, INetwork> _createNetwork;
    private readonly Func<NativeControlPacket, string?>? _validateProfile;
    private readonly Func<NativeControlPacket, CockpitDefinitionProfile?>? _cockpitProfile;
    private readonly EventLoopScheduler _scheduler = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ISubject<EfbBackendProgress> _progress = Subject.Synchronize(new ReplaySubject<EfbBackendProgress>(1));
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NativeBridgeState _current;
    private NativeConnectionSettings? _settings;
    private INetwork? _network;
    private NativeSharedSessionService? _service;
    private IDisposable? _serviceProgress;
    private SharedServiceProgress? _serviceState;
    private string _nativeId = "";
    private ulong _serial, _serviceGeneration;
    private TimeSpan _observed = TimeSpan.MinValue, _reported = TimeSpan.MinValue;
    private EfbBackendProgress _status = new(new(0, NativeBackendPhase.WaitingForSetup), "Configure the connection in the EFB.", null);
    private int _disposed;
    public IObservable<EfbBackendProgress> Progress => _progress.DistinctUntilChanged();
    public EfbConnectionBackend(INativeBackendClient native, Func<NativeConnectionSettings, INetwork> createNetwork,
        Func<NativeControlPacket, string?>? validateProfile = null, Func<NativeControlPacket, CockpitDefinitionProfile?>? cockpitProfile = null)
    {
        _native = native; _createNetwork = createNetwork; _validateProfile = validateProfile; _cockpitProfile=cockpitProfile; _current = native.State;
        _progress.OnNext(_status); native.StateChanged += OnNative;
        Schedule(() => Observe(_current));
        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(100), _scheduler).Subscribe(_ => Tick()));
    }
    private void Schedule(Action action) {
        if (Volatile.Read(ref _disposed) == 0) _scheduler.Schedule(() => { if (Volatile.Read(ref _disposed) == 0) action(); });
    }
    private void OnNative(NativeBridgeState state) { var copy = state.Copy(); Schedule(() => Observe(copy)); }
    private bool Fresh => _current.Live && _observed != TimeSpan.MinValue && _clock.Elapsed - _observed <= TimeSpan.FromSeconds(1);
    private void Publish(NativeBackendPhase phase, string message, NativeBackendCode code = NativeBackendCode.None)
    {
        var revision = _current.Snapshot?.Connection?.Revision ?? 0;
        _status = new(new(revision, phase, code), message, _settings);
        _progress.OnNext(_status);
    }
    private void Observe(NativeBridgeState state)
    {
        _current = state.Copy();
        if (state.Live && state.Snapshot is { } observation && NativeControlWire.Valid(observation)) {
            if (_nativeId != observation.Native) { _nativeId = observation.Native; _serial = 0; _observed = TimeSpan.MinValue; }
            if (observation.Serial > _serial) { _serial = observation.Serial; _observed = _clock.Elapsed; }
        }
        Tick();
    }
    private void Tick()
    {
        if (!Fresh || _current.Snapshot is not { ConnectionApi: 1 } observation) {
            if (_serviceState is {Phase:SharedServicePhase.Active} && _current.TransportConnected && _current.Live &&
                _clock.Elapsed-_observed < NativeSessionTiming.Recovery)
                Publish(NativeBackendPhase.Active, "Simulator updates delayed; the shared session is retained while control changes wait.", NativeBackendCode.NativeUnavailable);
            else if (Fresh && _current.Snapshot is { ConnectionApi: 0 }) Publish(NativeBackendPhase.Error,
                "The installed EFB package lacks connection setup. Install the matching EFB package before shared flight.", NativeBackendCode.UnsupportedPackage);
            else Publish(NativeBackendPhase.Error, _current.Error.Length == 0 ? "Waiting for the current EFB connection interface." : _current.Error, NativeBackendCode.NativeUnavailable);
        } else if (_validateProfile?.Invoke(observation) is { } profileError) {
            // The existing service observes the same validation failure and
            // retires consent. Keep it alive so restoring a profile cannot
            // construct a fresh service that reuses an already armed token.
            Publish(NativeBackendPhase.Error, profileError, NativeBackendCode.UnsupportedPackage);
        } else if (observation.Connection is not { } settings) {
            if (_settings is not null) CloseService();
            _settings = null;
            Publish(NativeBackendPhase.WaitingForSetup, "Configure the connection in the EFB.");
        } else if (_settings != settings) {
            // The native UI rejects edits while joining/holding. Drain the old
            // service before changing endpoints; its writers cannot overlap.
            CloseService(); _settings = settings; var generation = ++_serviceGeneration;
            try {
                _network = _createNetwork(settings);
                _service = new(_network, _native, new(settings.Local, settings.Peer,
                    settings.Role == 1 ? SessionRole.PhysicsHost : SessionRole.Copilot,
                    settings.Seat == 1 ? CockpitSeat.PilotLeft : CockpitSeat.CopilotRight), _validateProfile,_cockpitProfile);
                _serviceProgress = _service.Progress.ObserveOn(_scheduler).Subscribe(progress => {
                    if (generation == _serviceGeneration && Volatile.Read(ref _disposed) == 0) ServiceChanged(progress);
                });
                Publish(NativeBackendPhase.WaitingForJoin, "Connection saved. Join shared controls from the EFB.");
            } catch (Exception error) {
                CloseService();
                Publish(NativeBackendPhase.Error, "Connection setup failed: " + error.Message, NativeBackendCode.ConnectingFailed);
            }
        } else if (_serviceState is { } previous && _status.Notice.Code is NativeBackendCode.NativeUnavailable or NativeBackendCode.UnsupportedPackage) ServiceChanged(previous);
        if (_reported == TimeSpan.MinValue || _clock.Elapsed - _reported >= TimeSpan.FromMilliseconds(500)) {
            _native.Report(_status.Notice); _reported = _clock.Elapsed;
        }
    }
    private void ServiceChanged(SharedServiceProgress progress)
    {
        _serviceState = progress;
        var phase = progress.Phase switch {
            SharedServicePhase.WaitingForJoin => NativeBackendPhase.WaitingForJoin,
            SharedServicePhase.Negotiating => NativeBackendPhase.Connecting,
            SharedServicePhase.Binding => NativeBackendPhase.Binding,
            SharedServicePhase.Active => NativeBackendPhase.Active,
            SharedServicePhase.Ended => NativeBackendPhase.Ended,
            _ => NativeBackendPhase.Error
        };
        var code = progress.Phase == SharedServicePhase.Ended ? NativeBackendCode.SessionEnded : progress.Recovering ? NativeBackendCode.NativeUnavailable : NativeBackendCode.None;
        if (progress.Phase == SharedServicePhase.WaitingForNative) {
            code = (_current.Snapshot?.Capabilities & (uint)SessionNegotiation.AdvancedCapabilities) != (uint)SessionNegotiation.AdvancedCapabilities ? NativeBackendCode.UnsupportedPackage :
                _current.Snapshot is { } observation && (observation.Learned & observation.Required) != observation.Required ?
                NativeBackendCode.LearnControls : NativeBackendCode.NativeUnavailable;
        }
        Publish(phase, progress.Message, code);
    }
    private void CloseService()
    {
        ++_serviceGeneration; _serviceProgress?.Dispose(); _serviceProgress = null;
        _serviceState = null;
        var service = _service; _service = null;
        // NativeSharedSessionService and its coordinator use their own actors.
        // Completion waits until End has been queued before disposing sockets.
        service?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (_network is IDisposable disposable) disposable.Dispose(); _network = null;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _native.StateChanged -= OnNative; _subscriptions.Dispose();
        _scheduler.Schedule(() => {
            try { CloseService(); _native.Report(new(_settings?.Revision ?? 0, NativeBackendPhase.Stopped)); _progress.OnCompleted(); }
            finally { _scheduler.Dispose(); _closed.TrySetResult(); }
        });
    }
    public ValueTask DisposeAsync() { Dispose(); return new(_closed.Task); }
}
