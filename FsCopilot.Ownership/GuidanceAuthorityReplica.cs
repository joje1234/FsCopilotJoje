namespace FsCopilot.Ownership;

public sealed class GuidanceAuthorityReplica
{
    private readonly object _sync = new();
    private FlightGuidanceSnapshot _state;
    public FlightGuidanceSnapshot Snapshot { get { lock (_sync) return _state; } }
    public GuidanceAuthorityReplica(ControlAuthoritySnapshot negotiated)
    {
        if (!AtomicProtocol.ValidSnapshot(negotiated) || negotiated.Revision != 1 || negotiated.Ended || !negotiated.PeerConnected)
            throw new ArgumentException("A fresh negotiated session is required.", nameof(negotiated));
        _state = new(GuidanceProtocol.Version, negotiated.Session, negotiated.PhysicsHost, negotiated.Peer, 1, 1, 1,
            Guid.Empty, GuidancePhase.Disabled, negotiated.PhysicsHost, negotiated.PhysicsHost, GuidanceCapabilities.None, null, null, null);
    }
    public bool Receive(string sender, FlightGuidanceSnapshot state, ControlAuthoritySnapshot authority)
    {
        lock (_sync) {
            if (sender != _state.PhysicsHost || !GuidanceProtocol.Valid(state) || !AtomicProtocol.ValidSnapshot(authority) ||
                state.Session != _state.Session || state.Session != authority.Session || state.PhysicsHost != _state.PhysicsHost ||
                state.Peer != _state.Peer || state.PhysicsHost != authority.PhysicsHost || state.Peer != authority.Peer ||
                state.Revision <= _state.Revision || state.CommandEpoch < _state.CommandEpoch || state.NavigationEpoch < _state.NavigationEpoch ||
                state.CommandEpoch == _state.CommandEpoch && state.Commander != _state.Commander ||
                state.NavigationEpoch == _state.NavigationEpoch && state.Navigator != _state.Navigator ||
                _state.Phase == GuidancePhase.Ended ||
                _state.Phase is GuidancePhase.Holding or GuidancePhase.Local && state.Phase is not (GuidancePhase.Holding or GuidancePhase.Local or GuidancePhase.Ended) ||
                (authority.Ended || !authority.PeerConnected) && state.Phase is not (GuidancePhase.Holding or GuidancePhase.Ended)) return false;
            _state = state; return true;
        }
    }
}
