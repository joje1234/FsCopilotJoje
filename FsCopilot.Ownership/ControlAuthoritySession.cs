namespace FsCopilot.Ownership;

/// <summary>
/// Four independent physical groups. A whole-quadrant request is one atomic
/// transaction across all three engine pairs, including a mixed-owner quadrant.
/// The physics host and seat roles never change during a group transfer.
/// </summary>
public sealed partial class ControlAuthoritySession
{
    private sealed class Slot(AtomicControlGroup group, string host)
    {
        public AtomicGroupState State = new(group, host, 1, false, null);
        public ulong Received, Applied;
        public AtomicControlFrame? Latest;
    }
    private sealed class Pending(Guid id, AtomicTransferRequest request, GroupEpoch[] next, TimeSpan deadline)
    {
        public readonly Guid Id = id;
        public readonly AtomicTransferRequest Request = request;
        public readonly GroupEpoch[] Next = next;
        public readonly TimeSpan Deadline = deadline;
        public readonly HashSet<string> Prepared = new(StringComparer.Ordinal);
        public AtomicTransfer Snapshot => new(Id, Request.Request, Requester, Request.NewOwner, Request.Selection, Next.ToArray()) { Force = Request.Force, Automatic = Request.Automatic };
        public string Requester = "";
    }
    private readonly object _sync = new();
    private readonly string _host, _peer;
    private readonly int _engines;
    private readonly uint _steeringAxes;
    private readonly Func<TimeSpan> _clock;
    private readonly TimeSpan _timeout;
    private readonly Slot[] _slots;
    private readonly Dictionary<Guid, Pending> _pending = new();
    private readonly Dictionary<string, ulong> _requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _automaticRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _seatRequests = new(StringComparer.Ordinal);
    private CockpitSeats _seats;
    private PendingSeatChange? _seatChange;
    private ulong _seatRevision = 1, _seatTicket;
    private TimeSpan _seatDeadline;
    private ulong _revision = 1;
    private bool _connected = true, _ended, _automatic;
    public Guid Session { get; } = Guid.NewGuid();

    public ControlAuthoritySession(string physicsHost, string peer, int engines, Func<TimeSpan> clock,
        uint steeringAxes = AtomicProtocol.DefaultSteeringAxes, TimeSpan? preparationTimeout = null, CockpitSeat hostSeat = CockpitSeat.PilotLeft)
    {
        if (!OwnershipProtocol.ValidPeer(physicsHost) || !OwnershipProtocol.ValidPeer(peer) || physicsHost == peer ||
            engines is < 1 or > 4 || !AtomicProtocol.ValidSteeringAxes(steeringAxes) || !Enum.IsDefined(hostSeat)) throw new ArgumentException("Invalid authority configuration.");
        _host = physicsHost; _peer = peer; _engines = engines; _steeringAxes = steeringAxes;
        _seats = hostSeat == CockpitSeat.PilotLeft ? new(_host, _peer) : new(_peer, _host);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeout = preparationTimeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(preparationTimeout));
        _slots = Enum.GetValues<AtomicControlGroup>().Select(group => new Slot(group, _host)).ToArray();
    }
    public ControlAuthoritySnapshot Snapshot { get { lock (_sync) return SnapshotCore(); } }
    private ControlAuthoritySnapshot SnapshotCore() => new(AtomicProtocol.Version, Session, _host, _peer, _engines, _steeringAxes,
        _revision, _connected, _ended, _slots.Select(slot => slot.State).ToArray(), _pending.Values.Select(value => value.Snapshot).ToArray()) {
        Automatic = _automatic, Seats = _seats, SeatRevision = _seatRevision, SeatChange = _seatChange
    };
    private bool Member(string sender) => !_ended && (sender == _host || sender == _peer && _connected);
    private Slot Get(AtomicControlGroup group) => _slots[(int)group];
    public bool ChangeSeat(string sender, SeatChange change)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || !_connected || change.Session != Session || change.Sequence == 0 ||
                change.Sequence <= _seatRequests.GetValueOrDefault(sender) || !Enum.IsDefined(change.Action)) return false;
            _seatRequests[sender] = change.Sequence;
            if (change.ExpectedRevision != _seatRevision || _seatRevision == uint.MaxValue) return false;
            if (change.Action == SeatChangeAction.Request) {
                if (_seatChange is not null || change.Ticket != 0 || _seatTicket == uint.MaxValue || _seatRevision >= uint.MaxValue - 2) return false;
                _seatChange = new(++_seatTicket, sender); _seatDeadline = _clock() + TimeSpan.FromSeconds(15);
            } else {
                if (_seatChange is not { } pending || change.Ticket != pending.Ticket ||
                    change.Action == SeatChangeAction.Accept && sender == pending.RequestedBy ||
                    change.Action == SeatChangeAction.Cancel && sender != pending.RequestedBy ||
                    change.Action == SeatChangeAction.Decline && sender == pending.RequestedBy) return false;
                if (change.Action == SeatChangeAction.Accept) {
                    if (pending.Accepted || _barometerPhase is not (AltimeterPhase.Disabled or AltimeterPhase.Local or AltimeterPhase.Unsupported or AltimeterPhase.Active)) return false;
                    if (!AcceptAltimeterSeatChangeCore()) { CommitSeatChangeCore(); return true; }
                } else { _seatChange = null; CancelAltimeterSeatChangeCore(); }
            }
            ++_seatRevision; ++_revision; return true;
        }
    }
    private void CommitSeatChangeCore() {
        _seats = new(_seats.CopilotPeer, _seats.PilotPeer); _seatChange = null;
        ++_seatRevision; ++_revision; AltimeterSeatsChangedCore();
    }
    private void ClearSeatChange() { if (_seatChange is not null) { _seatChange = null; ++_seatRevision; CancelAltimeterSeatChangeCore(); } }
    private bool EpochsMatch(ControlSelection selection, GroupEpoch[] expected) => AtomicProtocol.ValidEpochs(selection, expected) &&
        expected.All(epoch => Get(epoch.Group).State.Generation == epoch.Generation);

    public bool SetAutomatic(string sender, Guid session, ulong sequence, bool enabled)
    {
        lock (_sync) {
            if (!Member(sender) || !_connected || session != Session || sequence == 0 || sequence <= _automaticRequests.GetValueOrDefault(sender)) return false;
            _automaticRequests[sender] = sequence;
            if (_automatic == enabled) return true;
            _automatic = enabled;
            if (!enabled) foreach (var pending in _pending.Values.Where(value => value.Request.Automatic).ToArray()) CancelCore(pending);
            ++_revision; return true;
        }
    }

    public bool RequestTransfer(string sender, AtomicTransferRequest request)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || request.Session != Session || !Member(request.NewOwner) || request.Request == 0 ||
                request.Request <= _requests.GetValueOrDefault(sender) || request.Force && request.NewOwner != sender || !AtomicProtocol.ValidEpochs(request.Selection, request.Expected)) return false;
            _requests[sender] = request.Request; // A rejected request cannot become valid after a later change.
            if (!_connected || !EpochsMatch(request.Selection, request.Expected) ||
                request.Automatic && (!_automatic || request.Force || request.NewOwner != sender || !AtomicProtocol.EnginePair(request.Selection))) return false;
            var slots = AtomicProtocol.Members(request.Selection).Select(Get).ToArray();
            if (slots.Any(slot => slot.State.Pending is not null || slot.State.Generation == ulong.MaxValue) ||
                slots.All(slot => slot.State.Owner == request.NewOwner && slot.State.Attached) ||
                sender != request.NewOwner && slots.Any(slot => slot.State.Owner != request.NewOwner && slot.State.Owner != sender)) return false;
            var next = slots.Select(slot => new GroupEpoch(slot.State.Group, slot.State.Generation + 1)).ToArray();
            var pending = new Pending(Guid.NewGuid(), request with { Expected = request.Expected.ToArray() }, next, _clock() + _timeout) { Requester = sender };
            _pending.Add(pending.Id, pending);
            foreach (var slot in slots) slot.State = slot.State with { Attached = false, Pending = pending.Id };
            ++_revision; return true;
        }
    }
    public bool AcknowledgePreparation(string sender, AtomicPreparationAck ack)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || ack.Session != Session || !ack.InputSuppressed || !_pending.TryGetValue(ack.Transfer, out var pending) ||
                sender == pending.Request.NewOwner && !pending.Request.Force && !ack.Aligned || !pending.Prepared.Add(sender)) return false;
            if (pending.Prepared.Count == 2) {
                foreach (var next in pending.Next) {
                    var slot = Get(next.Group);
                    slot.State = new(next.Group, pending.Request.NewOwner, next.Generation, false, null) { CommittedTransfer = pending.Id };
                    slot.Received = slot.Applied = 0; slot.Latest = null;
                }
                _pending.Remove(pending.Id); ++_revision;
            }
            return true;
        }
    }
    public bool AcknowledgeAttachment(string sender, AtomicAttachmentAck ack)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || ack.Session != Session || !EpochsMatch(ack.Selection, ack.Expected)) return false;
            var slots = AtomicProtocol.Members(ack.Selection).Select(Get).ToArray();
            if (slots.Any(slot => slot.State.Owner != sender || slot.State.Pending is not null) || slots.All(slot => slot.State.Attached == ack.Applied)) return false;
            foreach (var slot in slots) {
                if (ack.Applied) slot.State = slot.State with { Attached = true };
                else Invalidate(slot, slot.State.Owner);
            }
            ++_revision; return true;
        }
    }
    public bool Cancel(string sender, AtomicCancellation cancel)
    {
        lock (_sync) {
            if (!Member(sender) || cancel.Session != Session || !_pending.TryGetValue(cancel.Transfer, out var pending)) return false;
            CancelCore(pending); ++_revision; return true;
        }
    }
    private void Invalidate(Slot slot, string? owner)
    {
        slot.State = new(slot.State.Group, owner, checked(slot.State.Generation + 1), false, null); slot.Received = slot.Applied = 0; slot.Latest = null;
    }
    private void CancelCore(Pending pending)
    {
        foreach (var next in pending.Next) { var slot = Get(next.Group); Invalidate(slot, slot.State.Owner); }
        _pending.Remove(pending.Id);
    }
    public bool AcceptInput(string sender, AtomicControlFrame frame)
    {
        lock (_sync) {
            ExpireCore();
            if (frame is null || frame.Axes is null) return false;
            frame = frame with { Axes = frame.Axes.ToArray() };
            if (!Member(sender) || frame.Session != Session || !AtomicProtocol.CompleteFrame(frame, _engines, _steeringAxes)) return false;
            var slot = Get(frame.Group);
            if (slot.State.Owner != sender || !slot.State.Attached || slot.State.Pending is not null ||
                slot.State.Generation != frame.Generation || frame.Sequence <= slot.Received) return false;
            slot.Received = frame.Sequence; slot.Latest = frame; return true;
        }
    }
    /// <summary>
    /// Call at the actual simulator writer, not before posting to its queue.
    /// The callback receives a fresh copy of the accepted sample; caller-owned
    /// arrays cannot replace its values after acceptance or alter retained data.
    /// Claim the sequence before invoking a writer: a partial/throwing native
    /// write must never be retried as though no control had moved.
    /// </summary>
    public bool TryApplyAcceptedInput(string sender, AtomicControlFrame frame, Action<AtomicControlFrame> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        lock (_sync) {
            if (frame is null || frame.Axes is null) return false;
            frame = frame with { Axes = frame.Axes.ToArray() };
            if (!Member(sender) || frame.Session != Session || !AtomicProtocol.CompleteFrame(frame, _engines, _steeringAxes)) return false;
            var slot = Get(frame.Group);
            if (slot.State.Owner != sender || !slot.State.Attached || slot.State.Pending is not null || slot.State.Generation != frame.Generation ||
                frame.Sequence != slot.Received || frame.Sequence <= slot.Applied || slot.Latest is null ||
                !frame.Axes.OrderBy(axis => axis.Axis).SequenceEqual(slot.Latest.Axes.OrderBy(axis => axis.Axis))) return false;
            slot.Applied = frame.Sequence;
            apply(slot.Latest with { Axes = slot.Latest.Axes.ToArray() }); return true;
        }
    }
    public void PeerDisconnected()
    {
        lock (_sync) {
            if (!_connected) return;
            _connected = false;
            HoldAltimetersCore(AltimeterPhase.Holding);
            HoldGuidanceCore(GuidancePhase.Holding);
            ClearSeatChange();
            foreach (var pending in _pending.Values.ToArray()) CancelCore(pending);
            foreach (var slot in _slots) if (slot.State.Owner == _peer) Invalidate(slot, null);
            ++_revision;
        }
    }
    public bool RecoverLocal(ControlSelection selection)
    {
        lock (_sync) {
            if (_ended || _connected || !AtomicProtocol.ValidSelection(selection)) return false;
            var slots = AtomicProtocol.Members(selection).Select(Get).ToArray();
            if (slots.All(slot => slot.State.Owner == _host)) return false;
            foreach (var slot in slots) Invalidate(slot, _host);
            ++_revision; return true;
        }
    }
    public void End()
    {
        lock (_sync) {
            if (_ended) return;
            _ended = true; _connected = false; _pending.Clear(); ClearSeatChange();
            HoldAltimetersCore(AltimeterPhase.Ended);
            HoldGuidanceCore(GuidancePhase.Ended);
            foreach (var slot in _slots) Invalidate(slot, slot.State.Owner == _peer ? null : slot.State.Owner);
            ++_revision;
        }
    }
    public bool ExpireTransfers() { lock (_sync) return ExpireCore(); }
    private bool ExpireCore()
    {
        var before = _revision; var beforeAltimeters = _barometerRevision; var beforeGuidance = _guidanceRevision;
        ExpireAltimetersCore();
        ExpireGuidanceCore();
        var expired = _pending.Values.Where(pending => _clock() >= pending.Deadline).ToArray();
        foreach (var pending in expired) CancelCore(pending);
        var seatExpired = _seatChange is not null && _clock() >= _seatDeadline;
        if (seatExpired) ClearSeatChange();
        if (expired.Length != 0 || seatExpired) ++_revision;
        return expired.Length != 0 || seatExpired || _revision != before || _barometerRevision != beforeAltimeters || _guidanceRevision != beforeGuidance;
    }
}
