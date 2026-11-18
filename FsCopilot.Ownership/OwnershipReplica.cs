namespace FsCopilot.Ownership;

/// <summary>
/// A participant explicitly binds to a negotiated host/session snapshot. New
/// session packets cannot silently rebind a replica or restore old authority.
/// </summary>
public sealed class OwnershipReplica
{
    private readonly object _sync = new();
    private readonly string _host;
    private readonly Guid _session;
    private OwnershipSnapshot _snapshot;
    public OwnershipSnapshot Snapshot { get { lock (_sync) return _snapshot; } }

    public OwnershipReplica(string transportHost, OwnershipSnapshot negotiated)
    {
        if (!OwnershipProtocol.ValidSnapshot(negotiated) || negotiated.PhysicsHost != transportHost)
            throw new ArgumentException("The negotiated snapshot must come from its transport host.");
        _host = transportHost; _session = negotiated.Session; _snapshot = negotiated;
    }

    public bool Receive(string transportSender, OwnershipSnapshot update)
    {
        lock (_sync)
        {
            if (transportSender != _host || !OwnershipProtocol.ValidSnapshot(update) || update.Session != _session ||
                update.PhysicsHost != _host || update.Copilot != _snapshot.Copilot || update.Engines != _snapshot.Engines ||
                update.Revision <= _snapshot.Revision || update.Steering.Generation < _snapshot.Steering.Generation ||
                update.Quadrant.Generation < _snapshot.Quadrant.Generation ||
                update.Steering.Generation == _snapshot.Steering.Generation && update.Steering.Owner != _snapshot.Steering.Owner ||
                update.Quadrant.Generation == _snapshot.Quadrant.Generation && update.Quadrant.Owner != _snapshot.Quadrant.Owner) return false;
            _snapshot = update;
            return true;
        }
    }
}
