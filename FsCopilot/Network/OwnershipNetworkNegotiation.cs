namespace FsCopilot.Network;

using Ownership;

public sealed record NegotiatedControlSession(SessionRole Role, CockpitSeats Seats, OwnershipSession? Host, OwnershipReplica? Replica,
    ControlAuthoritySession? Authority = null, ControlAuthorityReplica? AuthorityReplica = null);

/// <summary>Negotiates metadata only. It never configures or attaches simulator inputs.</summary>
public sealed class OwnershipNetworkNegotiation : IDisposable
{
    private readonly INetwork _network;
    private readonly string _remote;
    private readonly SessionNegotiation _negotiation;
    private readonly SessionRole _role;
    private readonly object _lifetime = new();
    private readonly EventLoopScheduler _scheduler = new();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ISubject<NegotiationState> _states = Subject.Synchronize(new ReplaySubject<NegotiationState>(1));
    private readonly ISubject<NegotiatedControlSession?> _sessions = Subject.Synchronize(new ReplaySubject<NegotiatedControlSession?>(1));
    private bool _linked, _seenRemote, _publishedEstablished;
    private int _disposed;
    public IObservable<NegotiationState> States => _states.DistinctUntilChanged();
    public IObservable<NegotiatedControlSession?> Sessions => _sessions;

    // Call before connecting, in the same registration order on both clients.
    public static void RegisterProtocol(INetwork network)
    {
        OwnershipNetworkHost.RegisterProtocol(network);
        network.RegisterPacket<SessionHandshakePacket, SessionHandshakePacket.Codec>();
        ControlAuthorityNetworkHost.RegisterProtocol(network);
        network.RegisterPacket<SharedWorldPacket, SharedWorldPacket.Codec>();
        network.RegisterPacket<CockpitDefinitionPacket, CockpitDefinitionPacket.Codec>();
        network.RegisterPacket<CockpitRecoveryPacket, CockpitRecoveryPacket.Codec>();
    }

    public OwnershipNetworkNegotiation(INetwork network, string local, string remote, SessionRole role,
        SessionConfiguration configuration, TimeSpan? timeout = null, CockpitSeat? seat = null)
    {
        _network = network; _remote = remote; _role = role;
        var clock = Stopwatch.StartNew();
        _negotiation = new(local, remote, role, configuration, () => clock.Elapsed, timeout, seat);
        _negotiation.Start();
        _states.OnNext(_negotiation.State);
        _sessions.OnNext(null);
        _subscriptions.Add(network.Peers.ObserveOn(_scheduler).Subscribe(peers => Run(() => {
            _linked = peers.Any(peer => peer.PeerId == _remote);
            if (_linked) { _seenRemote = true; Send(_negotiation.Tick()); }
            else if (_seenRemote) { _negotiation.PeerDisconnected(); Publish(); }
        })));
        _subscriptions.Add(network.StreamWithSource<SessionHandshakePacket>().ObserveOn(_scheduler).Subscribe(packet => Run(() => {
            if (!_linked || packet.Sender != _remote || !SessionWire.TryDecode(packet.Packet.Payload, out var message)) return;
            Send(_negotiation.Receive(packet.Sender, message!)); Publish();
        })));
        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(250), _scheduler).Subscribe(_ => Run(() => {
            Send(_negotiation.Tick()); Publish();
        })));
    }

    private void Run(Action action) { lock (_lifetime) { if (Volatile.Read(ref _disposed) == 0) action(); } }

    private void Send(SessionHandshake? message, bool closing = false)
    {
        if (!_linked || message is null || !closing && Volatile.Read(ref _disposed) != 0) return;
        try { _network.SendAll(new SessionHandshakePacket(SessionWire.Encode(message))); }
        catch (Exception error) { Log.Error(error, "[Ownership] Negotiation transport failure"); _negotiation.Close("Session transport failed."); }
    }
    private void Publish()
    {
        _states.OnNext(_negotiation.State);
        if (!_publishedEstablished && _negotiation.State.Phase == NegotiationPhase.Established) {
            _publishedEstablished = true;
            _sessions.OnNext(new(_role, _negotiation.Seats!, _negotiation.HostSession, _negotiation.Replica,
                _negotiation.AuthoritySession, _negotiation.AuthorityReplica));
        }
        else if (_publishedEstablished && _negotiation.State.Phase != NegotiationPhase.Established &&
            !(_role == SessionRole.PhysicsHost && _negotiation.State.Phase == NegotiationPhase.PeerLost)) {
            _publishedEstablished = false; _sessions.OnNext(null);
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _subscriptions.Dispose(); _scheduler.Dispose();
        lock (_lifetime) {
            Send(_negotiation.End(), closing: true); _sessions.OnNext(null); _states.OnNext(_negotiation.State);
            _sessions.OnCompleted(); _states.OnCompleted();
        }
    }
}
