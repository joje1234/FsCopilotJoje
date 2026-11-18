namespace FsCopilot.Ownership;

/// <summary>
/// Host-authoritative two-participant protocol. Sender arguments must come from
/// the transport connection, never from a claimed sender inside a packet.
/// Simulator adapters acknowledge suppression/attachment only after read-back.
/// </summary>
public sealed class OwnershipSession
{
    private sealed class Slot(ControlGroup group, string host)
    {
        public GroupState State = new(group, host, 1, false, null);
        public readonly HashSet<string> Prepared = new(StringComparer.Ordinal);
        public TimeSpan Deadline;
        public ulong Sequence;
        public ulong AppliedSequence;
    }

    private readonly object _sync = new();
    private readonly string _host;
    private readonly string _copilot;
    private readonly int _engines;
    private readonly Func<TimeSpan> _clock;
    private readonly Slot[] _slots;
    private readonly Dictionary<string, ulong> _requests = new(StringComparer.Ordinal);
    private readonly TimeSpan _preparationTimeout;
    private ulong _revision = 1;
    private bool _copilotConnected = true;
    private bool _ended;
    public Guid Session { get; } = Guid.NewGuid();

    public OwnershipSession(string physicsHost, string copilot, int engines, Func<TimeSpan> clock,
        TimeSpan? preparationTimeout = null)
    {
        if (!OwnershipProtocol.ValidPeer(physicsHost) || !OwnershipProtocol.ValidPeer(copilot) || physicsHost == copilot)
            throw new ArgumentException("Two distinct participant identities are required.");
        if (engines is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(engines));
        _host = physicsHost; _copilot = copilot; _engines = engines;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _preparationTimeout = preparationTimeout ?? TimeSpan.FromSeconds(5);
        if (_preparationTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(preparationTimeout));
        _slots = [new(ControlGroup.Steering, _host), new(ControlGroup.Quadrant, _host)];
    }

    public OwnershipSnapshot Snapshot
    {
        get { lock (_sync) return SnapshotCore(); }
    }

    private OwnershipSnapshot SnapshotCore() => new(OwnershipProtocol.Version, Session, _host, _copilot, _engines,
        _revision, _slots[0].State, _slots[1].State, _copilotConnected);
    private bool Member(string sender) => !_ended && (sender == _host || sender == _copilot && _copilotConnected);
    private Slot Get(ControlGroup group) => _slots[(int)group];

    public bool RequestTransfer(string sender, TransferRequest request)
    {
        lock (_sync)
        {
            ExpireCore();
            if (!Member(sender) || request.Session != Session || request.Request == 0 ||
                !OwnershipProtocol.ValidGroup(request.Group) || !Member(request.NewOwner) ||
                request.Request <= _requests.GetValueOrDefault(sender)) return false;
            // Consume even rejected newer requests: a delayed copy must not become
            // valid after a different transfer or cancellation changes the state.
            _requests[sender] = request.Request;
            var slot = Get(request.Group);
            var state = slot.State;
            if (!_copilotConnected || state.Pending is not null || state.Generation != request.Generation ||
                state.Owner == request.NewOwner || state.Generation == ulong.MaxValue ||
                (sender != state.Owner && sender != request.NewOwner)) return false;
            slot.State = state with { Attached = false, Pending = new(Guid.NewGuid(), request.Request, sender,
                request.NewOwner, state.Generation + 1) };
            slot.Prepared.Clear();
            slot.Deadline = _clock() + _preparationTimeout;
            ++_revision;
            return true;
        }
    }

    public bool AcknowledgePreparation(string sender, PreparationAck ack)
    {
        lock (_sync)
        {
            ExpireCore();
            if (!Member(sender) || ack.Session != Session || !OwnershipProtocol.ValidGroup(ack.Group)) return false;
            var slot = Get(ack.Group);
            var pending = slot.State.Pending;
            if (pending is null || pending.Transfer != ack.Transfer || !ack.InputSuppressed ||
                sender == pending.NewOwner && !ack.Aligned || !slot.Prepared.Add(sender)) return false;
            if (slot.Prepared.Count == 2)
            {
                // Both local gates are closed. Grant ownership, but hardware
                // remains detached until the new owner's simulator confirms it.
                slot.State = new(slot.State.Group, pending.NewOwner, pending.Generation, false, null);
                slot.Sequence = 0;
                slot.AppliedSequence = 0;
                slot.Prepared.Clear();
                ++_revision;
            }
            return true;
        }
    }

    public bool AcknowledgeAttachment(string sender, AttachmentAck ack)
    {
        lock (_sync)
        {
            ExpireCore();
            if (!Member(sender) || ack.Session != Session || !OwnershipProtocol.ValidGroup(ack.Group)) return false;
            var slot = Get(ack.Group);
            var state = slot.State;
            if (state.Owner != sender || state.Generation != ack.Generation || state.Pending is not null ||
                state.Attached == ack.Applied) return false;
            // If alignment was lost after the preparation, the new owner leaves
            // Applied=false and sends no commands; ownership is not attachment.
            if (!ack.Applied) Invalidate(slot, state.Owner);
            else { slot.State = state with { Attached = true }; ++_revision; }
            return true;
        }
    }

    public bool CancelTransfer(string sender, TransferCancellation cancellation)
    {
        lock (_sync)
        {
            ExpireCore();
            if (!Member(sender) || cancellation.Session != Session || !OwnershipProtocol.ValidGroup(cancellation.Group)) return false;
            var slot = Get(cancellation.Group);
            if (slot.State.Pending?.Transfer != cancellation.Transfer) return false;
            Invalidate(slot, slot.State.Owner);
            return true;
        }
    }

    private void Invalidate(Slot slot, string? owner)
    {
        slot.State = new(slot.State.Group, owner, checked(slot.State.Generation + 1), false, null);
        slot.Sequence = 0;
        slot.AppliedSequence = 0;
        slot.Prepared.Clear();
        ++_revision;
    }

    public bool AcceptInput(string sender, ControlFrame frame)
    {
        lock (_sync)
        {
            ExpireCore();
            if (!Member(sender) || frame.Session != Session || !OwnershipProtocol.ValidAxes(frame, _engines)) return false;
            var slot = Get(frame.Group);
            if (slot.State.Owner != sender || !slot.State.Attached || slot.State.Pending is not null ||
                slot.State.Generation != frame.Generation || frame.Sequence == 0 || frame.Sequence <= slot.Sequence) return false;
            slot.Sequence = frame.Sequence;
            return true;
        }
    }

    /// <summary>
    /// Revalidate on the simulator execution queue, after any intervening
    /// transfer, disconnect or newer absolute sample. One accepted frame may be
    /// applied once. The short synchronous action runs under the policy lock.
    /// </summary>
    public bool TryApplyAcceptedInput(string sender, ControlFrame frame, Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        lock (_sync)
        {
            if (!Member(sender) || frame.Session != Session || !OwnershipProtocol.ValidAxes(frame, _engines)) return false;
            var slot = Get(frame.Group);
            if (slot.State.Owner != sender || !slot.State.Attached || slot.State.Pending is not null ||
                slot.State.Generation != frame.Generation || frame.Sequence == 0 || frame.Sequence != slot.Sequence ||
                frame.Sequence <= slot.AppliedSequence) return false;
            slot.AppliedSequence = frame.Sequence;
            apply(); return true;
        }
    }

    /// <summary>End this session permanently, including queued host inputs. A new negotiation is required.</summary>
    public void End()
    {
        lock (_sync)
        {
            if (_ended) return;
            _ended = true; _copilotConnected = false;
            foreach (var slot in _slots) Invalidate(slot, slot.State.Owner == _copilot ? null : slot.State.Owner);
        }
    }

    /// <summary>Called when the transport reports remote loss. No host election or automatic hardware attachment.</summary>
    public void CopilotDisconnected()
    {
        lock (_sync)
        {
            if (!_copilotConnected) return;
            _copilotConnected = false;
            foreach (var slot in _slots)
                if (slot.State.Owner == _copilot || slot.State.Pending is not null)
                    Invalidate(slot, slot.State.Owner == _copilot ? null : slot.State.Owner);
            ++_revision;
        }
    }

    /// <summary>Explicit host-side recovery after peer loss. Hardware still needs attachment confirmation.</summary>
    public bool TakeLocalControlAfterDisconnect(ControlGroup group)
    {
        lock (_sync)
        {
            if (_ended || _copilotConnected || !OwnershipProtocol.ValidGroup(group)) return false;
            var slot = Get(group);
            if (slot.State.Owner == _host) return false;
            Invalidate(slot, _host);
            return true;
        }
    }

    public bool ExpireTransfers() { lock (_sync) return ExpireCore(); }
    private bool ExpireCore()
    {
        var changed = false;
        foreach (var slot in _slots)
            if (slot.State.Pending is not null && _clock() >= slot.Deadline)
            {
                Invalidate(slot, slot.State.Owner);
                changed = true;
            }
        return changed;
    }
}
