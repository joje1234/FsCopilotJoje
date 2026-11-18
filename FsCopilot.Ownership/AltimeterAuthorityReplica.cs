namespace FsCopilot.Ownership;

public sealed class AltimeterAuthorityReplica
{
    private readonly object _sync = new();
    private AltimeterAuthoritySnapshot _state;
    public AltimeterAuthoritySnapshot Snapshot { get { lock (_sync) return Copy(_state); } }
    public AltimeterAuthorityReplica(ControlAuthoritySnapshot negotiated)
    {
        if (!AtomicProtocol.ValidSnapshot(negotiated) || negotiated.Revision != 1 || negotiated.SeatRevision != 1 || negotiated.Ended || !negotiated.PeerConnected)
            throw new ArgumentException("A fresh negotiated session is required.", nameof(negotiated));
        _state = new(AltimeterProtocol.Version, negotiated.Session, negotiated.PhysicsHost, negotiated.Peer, 1, 0, 1, negotiated.Seats,
            AltimeterPhase.Disabled,
            [new(1, negotiated.Seats.PilotPeer, null, null, false), new(2, negotiated.Seats.CopilotPeer, null, null, false)],
            [new(negotiated.PhysicsHost, 0, false, ""), new(negotiated.Peer, 0, false, "")]);
    }
    private static AltimeterAuthoritySnapshot Copy(AltimeterAuthoritySnapshot state) => state with { Instruments = state.Instruments.ToArray(), Gates = state.Gates.ToArray() };
    public bool Receive(string sender, AltimeterAuthoritySnapshot state, ControlAuthoritySnapshot authority)
    {
        lock (_sync) {
            if (sender != _state.PhysicsHost || !AltimeterProtocol.Valid(state) || !AtomicProtocol.ValidSnapshot(authority) ||
                state.Session != _state.Session || state.Session != authority.Session || state.PhysicsHost != _state.PhysicsHost || state.Peer != _state.Peer ||
                state.PhysicsHost != authority.PhysicsHost || state.Peer != authority.Peer || state.Seats != authority.Seats ||
                state.Revision <= _state.Revision || state.Epoch < _state.Epoch || state.Preparation < _state.Preparation || _state.Phase == AltimeterPhase.Ended ||
                _state.Phase is AltimeterPhase.Holding or AltimeterPhase.Unsupported && state.Phase is not (AltimeterPhase.Local or AltimeterPhase.Holding or AltimeterPhase.Unsupported or AltimeterPhase.Ended) ||
                _state.Phase == AltimeterPhase.Local && state.Phase is not (AltimeterPhase.Local or AltimeterPhase.Holding or AltimeterPhase.Ended) ||
                (authority.Ended || !authority.PeerConnected) && state.Phase is not (AltimeterPhase.Holding or AltimeterPhase.Ended) ||
                state.Phase == AltimeterPhase.Quiescing && authority.SeatChange is not { Accepted: true }) return false;
            var sameSeats = state.Seats == _state.Seats;
            if (((state.Epoch - _state.Epoch) % 2 == 0) != sameSeats) return false;
            _state = Copy(state); return true;
        }
    }
}
