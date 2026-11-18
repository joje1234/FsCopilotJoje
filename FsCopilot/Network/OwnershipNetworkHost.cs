namespace FsCopilot.Network;

using Ownership;

/// <summary>
/// Experimental transport adapter. Register the packet before connecting peers.
/// Consumers provide simulator gate acknowledgements and apply accepted frames;
/// this adapter does not send simulator events or enable the feature in the app.
/// </summary>
public sealed class OwnershipNetworkHost : IDisposable
{
    private readonly INetwork _network;
    private readonly OwnershipSession _session;
    private readonly OwnershipHostEndpoint _endpoint;
    private readonly EventLoopScheduler _scheduler = new();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ISubject<OwnershipSnapshot> _states = Subject.Synchronize(new ReplaySubject<OwnershipSnapshot>(1));
    private readonly ISubject<ControlFrame> _accepted = Subject.Synchronize(new Subject<ControlFrame>());
    private readonly ConcurrentDictionary<TaskCompletionSource<bool>, byte> _pendingLocal = new();
    private bool _seenCopilot;
    private ulong _publishedRevision;
    private int _disposed;
    public IObservable<OwnershipSnapshot> States => _states;
    public IObservable<ControlFrame> AcceptedInputs => _accepted;

    public static void RegisterProtocol(INetwork network) => network.RegisterPacket<OwnershipPacket, OwnershipPacket.Codec>();

    public OwnershipNetworkHost(INetwork network, OwnershipSession session)
    {
        _network = network; _session = session; _endpoint = new(session);
        _publishedRevision = session.Snapshot.Revision; _states.OnNext(session.Snapshot);
        _subscriptions.Add(network.StreamWithSource<OwnershipPacket>().ObserveOn(_scheduler)
            .Subscribe(packet => Receive(packet.Sender, packet.Packet.Payload)));
        _subscriptions.Add(network.Peers.ObserveOn(_scheduler).Subscribe(peers =>
        {
            if (peers.Any(peer => peer.PeerId == session.Snapshot.Copilot)) _seenCopilot = true;
            else if (_seenCopilot)
            {
                if (session.Snapshot.CopilotConnected) session.CopilotDisconnected();
                if (session.Snapshot.Revision != _publishedRevision) Publish(session.Snapshot);
            }
        }));
        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(100), _scheduler).Subscribe(_ =>
        {
            session.ExpireTransfers();
            if (session.Snapshot.Revision != _publishedRevision) Publish(session.Snapshot);
        }));
    }

    public Task<bool> ApplyLocal(OwnershipMessage message)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var bytes = OwnershipWire.Encode(message);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingLocal.TryAdd(done, 0);
        try
        {
            _scheduler.Schedule(() =>
            {
                try { done.TrySetResult(Receive(_session.Snapshot.PhysicsHost, bytes)); }
                catch (Exception error) { done.TrySetException(error); }
                finally { _pendingLocal.TryRemove(done, out _); }
            });
        }
        catch (Exception error) { _pendingLocal.TryRemove(done, out _); done.TrySetException(error); }
        return done.Task;
    }

    private bool Receive(string sender, byte[] bytes)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var applied = _endpoint.Receive(sender, bytes, out var changed, out var accepted);
        if (changed is not null) Publish(changed);
        if (accepted is not null)
        {
            var snapshot = _session.Snapshot;
            var group = accepted.Group == ControlGroup.Steering ? snapshot.Steering : snapshot.Quadrant;
            // Publishing an expiry snapshot can expose a transport failure and
            // revoke remote ownership. Do not emit a frame accepted before that.
            if (group.Owner == sender && group.Attached && group.Pending is null && group.Generation == accepted.Generation)
                _accepted.OnNext(accepted);
        }
        return applied;
    }

    private void Publish(OwnershipSnapshot snapshot)
    {
        _publishedRevision = snapshot.Revision;
        _states.OnNext(snapshot);
        try { _network.SendAll(new OwnershipPacket(OwnershipWire.Encode(new SnapshotMessage(snapshot)))); }
        catch (Exception error)
        {
            // Transport failure cannot leave remote authority live in policy.
            Log.Error(error, "[Ownership] Could not send state; revoking remote authority");
            _session.CopilotDisconnected();
            _publishedRevision = _session.Snapshot.Revision;
            _states.OnNext(_session.Snapshot);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _subscriptions.Dispose();
        _scheduler.Dispose();
        foreach (var pending in _pendingLocal.Keys) pending.TrySetException(new ObjectDisposedException(nameof(OwnershipNetworkHost)));
        _pendingLocal.Clear();
        _accepted.OnCompleted();
        _states.OnCompleted();
    }
}
