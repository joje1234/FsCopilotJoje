namespace FsCopilot.Network;

using Ownership;

public sealed record AttributedControlFrame(string Sender, AtomicControlFrame Frame);

/// <summary>Sender-aware four-group authority. Simulator execution remains a separate, fenced adapter.</summary>
public sealed class ControlAuthorityNetworkHost : IDisposable
{
    private readonly INetwork _network;
    private readonly ControlAuthoritySession _session;
    private readonly ControlAuthorityEndpoint _endpoint;
    private readonly EventLoopScheduler _scheduler;
    private readonly bool _ownsScheduler;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ISubject<ControlAuthoritySnapshot> _states = Subject.Synchronize(new ReplaySubject<ControlAuthoritySnapshot>(1));
    private readonly ISubject<AttributedControlFrame> _inputs = Subject.Synchronize(new Subject<AttributedControlFrame>());
    private readonly ISubject<AltimeterAuthoritySnapshot> _altimeters = Subject.Synchronize(new ReplaySubject<AltimeterAuthoritySnapshot>(1));
    private readonly ISubject<FlightGuidanceSnapshot> _guidance = Subject.Synchronize(new ReplaySubject<FlightGuidanceSnapshot>(1));
    private readonly ISubject<GuidanceReceipt> _guidanceReceipts = Subject.Synchronize(new Subject<GuidanceReceipt>());
    private readonly ConcurrentDictionary<TaskCompletionSource<bool>, byte> _pending = new();
    private bool _seenPeer;
    private ulong _publishedRevision;
    private ulong _publishedAltimeterRevision;
    private ulong _publishedGuidanceRevision;
    private int _disposed;
    public IObservable<ControlAuthoritySnapshot> States => _states;
    public IObservable<AttributedControlFrame> AcceptedInputs => _inputs;
    public IObservable<AltimeterAuthoritySnapshot> Altimeters => _altimeters;
    public IObservable<FlightGuidanceSnapshot> Guidance => _guidance;
    public IObservable<GuidanceReceipt> GuidanceReceipts => _guidanceReceipts;
    public bool ExecuteQueuedGuidance(GuidanceCommand command, Func<bool> write) => _session.ExecuteQueuedGuidance(command, write);
    public static void RegisterProtocol(INetwork network) => network.RegisterPacket<ControlAuthorityPacket, ControlAuthorityPacket.Codec>();

    public ControlAuthorityNetworkHost(INetwork network, ControlAuthoritySession session, EventLoopScheduler? scheduler = null)
    {
        _scheduler = scheduler ?? new(); _ownsScheduler = scheduler is null;
        _network = network; _session = session; _endpoint = new(session);
        var initial = session.Publication;
        _publishedRevision = initial.Controls.Revision; _states.OnNext(initial.Controls);
        _publishedAltimeterRevision = initial.Altimeters.Revision; _altimeters.OnNext(initial.Altimeters);
        _publishedGuidanceRevision = initial.Guidance.Revision; _guidance.OnNext(initial.Guidance);
        _subscriptions.Add(network.StreamWithSource<ControlAuthorityPacket>().ObserveOn(_scheduler).Subscribe(packet => Receive(packet.Sender, packet.Packet.Payload)));
        _subscriptions.Add(network.Peers.ObserveOn(_scheduler).Subscribe(peers => {
            if (peers.Any(peer => peer.PeerId == session.Snapshot.Peer)) _seenPeer = true;
            else if (_seenPeer) {
                if (session.Snapshot.PeerConnected) session.PeerDisconnected();
                PublishIfChanged();
            }
        }));
        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(100), _scheduler).Subscribe(_ => {
            session.ExpireTransfers(); PublishIfChanged();
        }));
    }
    public Task<bool> ApplyLocal(AuthorityMessage message)
    {
        var bytes = AuthorityWire.Encode(message);
        return Local(() => Receive(_session.Snapshot.PhysicsHost, bytes));
    }
    public Task<bool> RecoverLocal(ControlSelection selection) => Local(() => {
        if (!_session.RecoverLocal(selection)) return false;
        Publish(); return true;
    });
    public Task<bool> ApplyAcceptedInput(AttributedControlFrame queued, Func<AtomicControlFrame, bool> enqueue) => Local(() => {
        var written = false;
        var valid = _session.TryApplyAcceptedInput(queued.Sender, queued.Frame, accepted => written = enqueue(accepted));
        return valid && written;
    });
    public Task<bool> EndSession() => Local(() => { _session.End(); Publish(); return true; });
    public Task<bool> StartAltimeters(AltimeterSetting[] settings, ulong readSerial, bool initialInputHeld = false)
    {
        var copy = settings.ToArray();
        return Local(() => {
            var result = _session.StartAltimeters(_session.Snapshot.PhysicsHost, _session.Session, copy, readSerial, initialInputHeld);
            PublishIfChanged(); return result;
        });
    }
    public Task<bool> ApplyAltimeter(AltimeterChange change, ulong beforeReadSerial, Func<AltimeterChange, bool> enqueue, bool retry = false) => Local(() => {
        try { return retry ? _session.TryRetryAltimeter(change, beforeReadSerial, enqueue) : _session.TryApplyAltimeter(change, beforeReadSerial, enqueue); }
        finally { PublishIfChanged(); }
    });
    public Task<bool> StartGuidance(GuidanceState state, ulong readSerial) => Local(() => {
        var result = _session.StartGuidance(_session.Snapshot.PhysicsHost, _session.Session, state, readSerial);
        PublishIfChanged(); return result;
    });
    public Task<bool> ObserveGuidance(ulong readSerial, GuidanceState state) => Local(() => {
        var result = _session.ObserveGuidance(_session.Snapshot.PhysicsHost, _session.Session, readSerial, state);
        PublishIfChanged(); return result;
    });
    public Task<bool> ConfirmGuidance(GuidanceCommand command, ulong readSerial, GuidanceState state, bool succeeded = true) => Local(() => {
        var result = _session.ConfirmGuidance(_session.Snapshot.PhysicsHost, command, readSerial, state, succeeded);
        PublishIfChanged(); return result;
    });
    public Task<bool> ApplyGuidance(GuidanceCommand command, ulong beforeReadSerial, Func<GuidanceCommand, bool> enqueue, bool retry = false) => Local(() => {
        try { return _session.TryApplyGuidanceCommand(command, beforeReadSerial, enqueue, retry); }
        finally { PublishIfChanged(); }
    });
    private Task<bool> Local(Func<bool> apply)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.TryAdd(done, 0);
        try {
            _scheduler.Schedule(() => {
                try { done.TrySetResult(Volatile.Read(ref _disposed) == 0 && apply()); }
                catch (Exception error) { done.TrySetException(error); }
                finally { _pending.TryRemove(done, out _); }
            });
        }
        catch (Exception error) { _pending.TryRemove(done, out _); done.TrySetException(error); }
        return done.Task;
    }
    private bool Receive(string sender, byte[] bytes)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var applied = _endpoint.Receive(sender, bytes, out var changed, out var accepted);
        if (changed is not null) Publish();
        if (accepted is not null) {
            var state = _session.Snapshot.Groups.Single(group => group.Group == accepted.Group);
            if (state.Owner == sender && state.Attached && state.Pending is null && state.Generation == accepted.Generation)
                _inputs.OnNext(new(sender, accepted));
        }
        PublishIfChanged();
        if (AuthorityWire.TryDecode(bytes, out var message) && message is AuthorityGuidanceCommand input &&
            (sender == _session.Snapshot.PhysicsHost || sender == _session.Snapshot.Peer)) {
            var receipt = new GuidanceReceipt(sender, input.Command, applied, _session.Guidance.Revision);
            _guidanceReceipts.OnNext(receipt); SendState(new AuthorityGuidanceReceipt(AtomicProtocol.Version, receipt));
        }
        return applied;
    }
    private void Publish() => PublishStates(forceMain: true);
    private void PublishStates(bool forceMain = false)
    {
        // The negotiation owner may close the session from its own actor.
        // Capture the streams under the model lock so one publication cannot
        // pair an old seat assignment with a new instrument owner.
        var publication = _session.Publication;
        if (forceMain || publication.Controls.Revision != _publishedRevision) {
            _publishedRevision = publication.Controls.Revision; _states.OnNext(publication.Controls);
            if (!SendState(new AuthorityState(AtomicProtocol.Version, publication.Controls))) return;
        }
        if (publication.Altimeters.Revision != _publishedAltimeterRevision) {
            _publishedAltimeterRevision = publication.Altimeters.Revision; _altimeters.OnNext(publication.Altimeters);
            if (!SendState(new AuthorityAltimeterState(AtomicProtocol.Version, publication.Altimeters))) return;
        }
        if (publication.Guidance.Revision != _publishedGuidanceRevision) {
            _publishedGuidanceRevision = publication.Guidance.Revision; _guidance.OnNext(publication.Guidance);
            SendState(new AuthorityGuidanceState(AtomicProtocol.Version, publication.Guidance));
        }
    }
    private bool SendState(AuthorityMessage message)
    {
        var bytes = AuthorityWire.Encode(message);
        try { _network.SendAll(new ControlAuthorityPacket(bytes)); return true; }
        catch (Exception error) {
            Log.Error(error, "[Ownership] Authority transport failed; revoking remote input"); _session.PeerDisconnected();
            var closed = _session.Publication;
            _publishedRevision = closed.Controls.Revision; _publishedAltimeterRevision = closed.Altimeters.Revision;
            _publishedGuidanceRevision = closed.Guidance.Revision;
            _states.OnNext(closed.Controls); _altimeters.OnNext(closed.Altimeters); _guidance.OnNext(closed.Guidance); return false;
        }
    }
    private void PublishIfChanged() => PublishStates();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _subscriptions.Dispose(); if (_ownsScheduler) _scheduler.Dispose(); _session.End();
        foreach (var done in _pending.Keys) done.TrySetException(new ObjectDisposedException(nameof(ControlAuthorityNetworkHost)));
        _pending.Clear(); _states.OnNext(_session.Snapshot); _states.OnCompleted(); _inputs.OnCompleted(); _altimeters.OnNext(_session.Altimeters); _altimeters.OnCompleted();
        _guidance.OnNext(_session.Guidance); _guidance.OnCompleted();
        _guidanceReceipts.OnCompleted();
    }
}
