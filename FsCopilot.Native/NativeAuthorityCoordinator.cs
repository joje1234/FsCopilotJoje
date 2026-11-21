using FsCopilot.Ownership;

namespace FsCopilot.Native;

public sealed record NativeAuthorityIdentity(string Native, string Backend, string Consent, string Aircraft, string Adapter);
public enum NativeAuthorityPhase { WaitingForNative, Binding, Active, Ended }
public sealed record NativeAuthorityProgress(NativeAuthorityPhase Phase, ControlSelection Waiting, string Error, bool Recovering = false);

/// <summary>
/// Serialized coordinator between attributed network authority and the native
/// session. The caller supplies the actual SDK queue and reliable authority
/// transport; all calls, including host input-fence callbacks, use one scheduler.
/// </summary>
public sealed class NativeAuthorityCoordinator
{
    private sealed record Operation(NativeSessionPacket Packet, ControlAuthoritySnapshot State, TimeSpan Sent, bool ForceAttachment);
    private sealed class Intent(ControlSelection selection, string owner, bool force, bool automatic = false) {
        public readonly ControlSelection Selection = selection;
        public readonly string Owner = owner;
        public readonly bool Force = force;
        public readonly bool Automatic = automatic;
        public ulong Request;
        public TimeSpan Sent;
        public Guid? Transfer;
        public bool Committed;
    }
    private readonly string _local;
    private readonly NativeAuthorityIdentity _identity;
    private readonly Func<TimeSpan> _clock;
    private readonly Func<NativeSessionPacket, bool> _sendNative;
    private readonly Action<AuthorityMessage> _sendAuthority;
    private readonly ulong _epoch;
    private readonly List<Intent> _intents = [];
    private readonly HashSet<Guid> _prepared = [];
    private readonly HashSet<(AtomicControlGroup, ulong)> _attached = [];
    private readonly ulong[] _inputSequence = new ulong[4], _inputGeneration = new ulong[4], _inputObservation = new ulong[4];
    private ControlAuthoritySnapshot _state;
    private NativeControlPacket? _observation;
    private NativeSessionPacket? _native;
    private Operation? _operation;
    private NativeAuthorityPhase _phase;
    private string _error = "";
    private ulong _command, _request, _synchronized, _nativeSerial, _observationSerial, _handledIntent;
    private ulong _automaticRequest, _automaticObservation, _feedbackRevision, _seatRequest;
    private uint _feedbackWaiting = uint.MaxValue;
    private readonly double[] _automaticAnchor = Enumerable.Repeat(-1d, 21).ToArray();
    private TimeSpan _nativeAt, _observationAt, _heartbeatAt = TimeSpan.MinValue;
    private bool _bound;
    private TimeSpan _healthyAt, _recoveryAt;
    private bool _recovering, _peerDelayed;
    private uint _unavailableTargets;
    public event Action<string, bool>? RecoveryChanged;
    public event Action<uint, uint>? ControlValuesChanged;
    public void PeerDelayed(bool delayed) => _peerDelayed = delayed;
    private bool NativeFresh(TimeSpan now) => _native is not null && _observation is not null &&
        now >= _nativeAt && now >= _observationAt && now - _nativeAt <= NativeSessionTiming.Fresh && now - _observationAt <= NativeSessionTiming.Fresh &&
        _observation.Flags.HasFlag(NativeStatusFlags.TargetsFresh) &&
        (!_bound || !_worldEnabled || _native.Flags.HasFlag(NativeSessionFlags.WorldFresh));
    private readonly bool _worldEnabled;
    private ulong _worldProduced, _worldReceived;
    private TimeSpan _worldAt;
    public event Action<NativeWorldFrame>? WorldProduced;
    private ControlSelection _pausedAttachment;
    private bool _cancelNativeOperation;
    public event Action? Ended;
    public NativeAuthorityProgress Progress => new(_phase, _intents.Aggregate(ControlSelection.None, (mask, intent) => mask | intent.Selection), _error, _recovering);
    public ControlAuthoritySnapshot Authority => Copy(_state);
    public NativeInstrumentContext? InstrumentContext {
        get {
            var now = _clock();
            if (_recovering || _peerDelayed || _phase != NativeAuthorityPhase.Active || _synchronized != _state.Revision || _native is not { Phase: NativeSessionPhase.Active } native ||
                _observation is not { } observation || now < _nativeAt || now < _observationAt ||
                now - _nativeAt > TimeSpan.FromSeconds(1) || now - _observationAt > TimeSpan.FromSeconds(1) ||
                native.Revision != _state.Revision || native.SeatRevision != _state.SeatRevision ||
                native.Seat != (_state.Seats.PilotPeer == _local ? 1u : 2u)) return null;
            return new(observation.Copy(), native.Copy());
        }
    }
    public NativeInstrumentContext? GuidanceContext {
        get {
            var now = _clock();
            if (_recovering || _peerDelayed || _phase != NativeAuthorityPhase.Active || _synchronized != _state.Revision || _native is not { Phase: NativeSessionPhase.Active } native ||
                _observation is not { } observation || now < _nativeAt || now < _observationAt ||
                now - _nativeAt > TimeSpan.FromSeconds(1) || now - _observationAt > TimeSpan.FromSeconds(1) || native.Revision != _state.Revision) return null;
            return new(observation.Copy(), native.Copy()); // AP ownership is independent of seat confirmation.
        }
    }
    /// <summary>Called on this coordinator's actor, inside the instrument model's final dispatch fence for host writes.</summary>
    public ulong QueueBarometer(NativeBarometerCommand body)
    {
        if (!NativeBarometerCommand.Valid(body) || InstrumentContext is not { } context || _command >= NativeBarometerAck.MaxCommand) return 0;
        var packet = Build(NativeSessionKind.Barometer, _state) with { Axes = 0, Barometer = body };
        if (!NativeSessionSendGate.Allows(packet, _identity.Backend, context.Observation, context.Session)) return 0;
        try { return _sendNative(packet.Copy()) ? packet.Command : 0; }
        catch (Exception) { return 0; } // Instrument dispatch failure is handled by its authority, independently of physical controls.
    }
    public bool ReportInstruments(NativeInstrumentFeedback feedback)
    {
        if (!NativeInstrumentFeedback.Valid(feedback) || InstrumentContext is not { } context) return false;
        var packet = Build(NativeSessionKind.InstrumentFeedback, _state) with { Axes = 0, InstrumentFeedback = feedback };
        if (!NativeSessionSendGate.Allows(packet, _identity.Backend, context.Observation, context.Session)) return false;
        try { return _sendNative(packet.Copy()); }
        catch (Exception) { return false; }
    }
    public NativeAuthorityCoordinator(string local, ControlAuthoritySnapshot initial, NativeAuthorityIdentity identity,
        Func<TimeSpan> clock, Func<NativeSessionPacket, bool> sendNative, Action<AuthorityMessage> sendAuthority, bool worldEnabled = true)
    {
        if (!AtomicProtocol.ValidSnapshot(initial) || initial.Automatic || initial.Ended || !initial.PeerConnected || initial.Revision != 1 ||
            initial.SeatRevision != 1 || initial.SeatChange is not null ||
            initial.Groups.Any(group => group.Generation != 1 || group.Attached || group.Pending is not null || group.CommittedTransfer is not null || group.Owner != initial.PhysicsHost) ||
            local != initial.PhysicsHost && local != initial.Peer || !ValidIdentity(identity)) throw new ArgumentException("A fresh negotiated session and local native consent are required.");
        _local = local; _state = Copy(initial); _identity = identity; _clock = clock; _sendNative = sendNative; _sendAuthority = sendAuthority;
        _worldEnabled = worldEnabled;
        _epoch = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()); if (_epoch == 0) _epoch = 1;
        _healthyAt = _nativeAt = _observationAt = _clock();
    }
    private static bool ValidIdentity(NativeAuthorityIdentity p) => NativeSessionWire.Valid(new NativeSessionPacket {
        Kind = NativeSessionKind.Bind, Native = p.Native, Backend = p.Backend, Consent = p.Consent, Session = Guid.NewGuid().ToString("N"), Epoch = 1, Command = 1
    }) && p.Aircraft.Length != 0 && p.Adapter.Length != 0;
    private static ControlAuthoritySnapshot Copy(ControlAuthoritySnapshot p) => p with {
        Groups = p.Groups.ToArray(), Transfers = p.Transfers.Select(transfer => transfer with { Next = transfer.Next.ToArray() }).ToArray()
    };
    private AtomicGroupState Group(AtomicControlGroup group) => _state.Groups.Single(value => value.Group == group);
    private bool Host => _local == _state.PhysicsHost;
    public void Observe(bool live, string backend, NativeControlPacket? observation, NativeSessionPacket? session)
    {
        if (_phase == NativeAuthorityPhase.Ended) return;
        if (!live || backend != _identity.Backend || observation is null || session is null || !NativeControlWire.Valid(observation) ||
            !NativeSessionWire.Valid(session) || observation.Native != _identity.Native || session.Native != _identity.Native ||
            observation.Aircraft != _identity.Aircraft || observation.Adapter != _identity.Adapter || observation.Engines != _state.Engines ||
            session.Consent != _identity.Consent || session.Phase is NativeSessionPhase.Idle or NativeSessionPhase.Ended) { End("Native connection or local consent changed."); return; }
        if (observation.Serial > _observationSerial) {
            _observation = observation.Copy(); _observationSerial = observation.Serial; _observationAt = _clock();
            uint unavailable = 0;
            for (var axis = 0; axis < 21; ++axis)
                if ((observation.Required & (1u << axis)) != 0 && observation.Targets[axis] < 0) unavailable |= 1u << axis;
            if (unavailable != _unavailableTargets) {
                var before = _unavailableTargets; _unavailableTargets = unavailable;
                ControlValuesChanged?.Invoke(unavailable & ~before, before & ~unavailable);
            }
        }
        if (session.Serial <= _nativeSerial) return; // Copies accompanying observation pushes never refresh a native proof.
        _native = session.Copy(); _nativeSerial = session.Serial; _nativeAt = _clock();
        if (session.Phase is NativeSessionPhase.Binding or NativeSessionPhase.Active &&
            (session.Backend != _identity.Backend || session.Session != _state.Session.ToString("N") || session.Epoch != _epoch ||
                session.Flags.HasFlag(NativeSessionFlags.PhysicsHost) != Host)) {
            End("Native session binding changed."); return;
        }
        CompleteOperation(session);
    }
    public bool UpdateAuthority(ControlAuthoritySnapshot state)
    {
        if (_phase == NativeAuthorityPhase.Ended || !AtomicProtocol.ValidSnapshot(state) || state.Session != _state.Session ||
            state.PhysicsHost != _state.PhysicsHost || state.Peer != _state.Peer || state.Engines != _state.Engines ||
            state.SteeringAxes != _state.SteeringAxes || state.Revision <= _state.Revision) return false;
        if (state.SeatRevision < _state.SeatRevision || state.SeatRevision == _state.SeatRevision &&
            (state.Seats != _state.Seats || state.SeatChange != _state.SeatChange)) return false;
        foreach (var current in _state.Groups) {
            var next = state.Groups.Single(group => group.Group == current.Group);
            if (next.Generation < current.Generation || next.Generation == current.Generation &&
                (next.Owner != current.Owner || next.CommittedTransfer != current.CommittedTransfer)) return false;
        }
        foreach (var previous in _state.Groups) {
            var next = state.Groups.Single(group => group.Group == previous.Group);
            if (_state.Automatic != state.Automatic || previous.Generation != next.Generation || previous.Owner != next.Owner)
                ResetAutomatic(AtomicProtocol.Select(previous.Group));
        }
        _state = Copy(state);
        if (!state.Automatic) _intents.RemoveAll(intent => intent.Automatic);
        _prepared.RemoveWhere(ticket => !_state.Transfers.Any(transfer => transfer.Transfer == ticket));
        _forced.RemoveWhere(ticket => !_state.Transfers.Any(transfer => transfer.Transfer == ticket) && !_state.Groups.Any(group => group.CommittedTransfer == ticket));
        _attached.RemoveWhere(epoch => Group(epoch.Item1).Generation != epoch.Item2);
        foreach (var intent in _intents) {
            var pending = _state.Transfers.SingleOrDefault(transfer => transfer.RequestedBy == _local && transfer.Request == intent.Request);
            if (pending is not null) intent.Transfer = pending.Transfer;
            else if (intent.Transfer is { } ticket) {
                intent.Committed = AtomicProtocol.Members(intent.Selection).All(group => Group(group).Owner == intent.Owner && Group(group).CommittedTransfer == ticket);
                if (!intent.Committed) { intent.Transfer = null; intent.Request = 0; }
            }
        }
        _intents.RemoveAll(intent => AtomicProtocol.Members(intent.Selection).All(group => Group(group).Owner == intent.Owner && Group(group).Attached));
        if (state.Ended || !state.PeerConnected) End(state.Ended ? "Shared session ended." : "The other player disconnected.");
        return true;
    }
    public bool Request(ControlSelection selection, string owner, bool force = false)
    {
        if (_phase == NativeAuthorityPhase.Ended || !AtomicProtocol.ValidSelection(selection) || owner != _state.PhysicsHost && owner != _state.Peer ||
            force && owner != _local || AtomicProtocol.Members(selection).All(group => Group(group).Owner == owner && Group(group).Attached)) return false;
        Cancel(selection); _pausedAttachment &= ~selection; _intents.Add(new(selection, owner, force)); return true;
    }
    public void SetAutomatic(bool enabled) => SendAuthority(new AuthorityAutomatic(AtomicProtocol.Version, _state.Session, checked(++_automaticRequest), enabled));
    public void ChangeSeat(SeatChangeAction action, ulong revision, ulong ticket) => SendAuthority(new AuthoritySeatChange(AtomicProtocol.Version,
        new(_state.Session, checked(++_seatRequest), revision, action, ticket)));
    public void Cancel(ControlSelection selection)
    {
        ResetAutomatic(selection);
        _pausedAttachment |= selection;
        if (_operation is { Packet.Kind: NativeSessionKind.Attach or NativeSessionKind.Prepare } operation &&
            Incoming(operation.Packet) && ((ControlSelection)operation.Packet.Selection & selection) != 0) _cancelNativeOperation = true;
        _intents.RemoveAll(intent => (intent.Selection & selection) != 0);
        foreach (var transfer in _state.Transfers.Where(transfer => (transfer.Selection & selection) != 0))
            SendAuthority(new AuthorityCancellation(AtomicProtocol.Version, new(_state.Session, transfer.Transfer)));
    }
    public void Tick()
    {
        if (_phase == NativeAuthorityPhase.Ended) return;
        var now = _clock();
        if (now < _nativeAt || now < _observationAt) { End("Native observation clock changed."); return; }
        if (_native is null || _observation is null) return;
        if ((_bound || _native.Phase == NativeSessionPhase.Binding) && (_heartbeatAt == TimeSpan.MinValue || now - _heartbeatAt >= TimeSpan.FromMilliseconds(500))) {
            var heartbeat = Build(NativeSessionKind.Heartbeat, _state) with { Revision = _native.Revision };
            if (Send(heartbeat)) _heartbeatAt = now;
        }
        var worldDelayed = _bound && _worldEnabled && !Host && now - _worldAt > NativeSessionTiming.Fresh;
        if (!NativeFresh(now) || worldDelayed || _peerDelayed) {
            var cause = _peerDelayed ? "Peer heartbeat delayed" : worldDelayed ? "Physics host updates delayed" : "Simulator observations delayed";
            if (now - _healthyAt >= NativeSessionTiming.Recovery) { End($"{cause}: recovery deadline exceeded (8 seconds). Stale control data was not replayed."); return; }
            if (!_recovering) {
                _recovering = true; _recoveryAt = now; _error = cause + ". Session retained; control writes and transfers are waiting.";
                RecoveryChanged?.Invoke($"{_error} Observation age {(now-_observationAt).TotalMilliseconds:F0} ms; session age {(now-_nativeAt).TotalMilliseconds:F0} ms.", false);
            }
            return; // Heartbeats retain the lease, never refresh input/world data or attachment proofs.
        }
        _healthyAt = now;
        if (_recovering) {
            if (_operation is { } suspended) _operation = suspended with {Sent = suspended.Sent + (now-_recoveryAt)};
            _recovering = false; _error = "";
            RecoveryChanged?.Invoke($"Shared updates recovered after {(now-_recoveryAt).TotalMilliseconds:F0} ms; same native/session identity retained.", true);
        }
        if (_operation is { } current && now - current.Sent > TimeSpan.FromSeconds(6)) { End("Native handover confirmation timed out."); return; }
        if (!_bound) {
            if (_operation is null && _native.Phase == NativeSessionPhase.Armed && _observation.Flags.HasFlag(NativeStatusFlags.Ready))
                Start(Build(NativeSessionKind.Bind, _state) with { Flags = (Host ? NativeSessionFlags.PhysicsHost : NativeSessionFlags.None) | (_worldEnabled ? NativeSessionFlags.World : NativeSessionFlags.None) });
            return;
        }
        if (_cancelNativeOperation) {
            _cancelNativeOperation = false; Start(Build(NativeSessionKind.State, _state)); return;
        }
        // State supersedes a stale native operation. Native holds/remasks the
        // relevant hardware before acknowledging the newer authority revision.
        if (_synchronized != _state.Revision && (_operation is null || _operation.Packet.Kind != NativeSessionKind.State || _operation.State.Revision != _state.Revision)) {
            Start(Build(NativeSessionKind.State, _state)); return;
        }
        if (_operation is { Packet.Kind: NativeSessionKind.Prepare or NativeSessionKind.Attach } preparing &&
            !preparing.Packet.Flags.HasFlag(NativeSessionFlags.Force) && !preparing.ForceAttachment && Incoming(preparing.Packet) && !Aligned((ControlSelection)preparing.Packet.Selection)) {
            Start(Build(NativeSessionKind.State, _state)); return;
        }
        RouteEfbIntent();
        TrackAutomatic();
        SubmitIntents(now);
        PublishAutomaticWaiting();
        EmitOwnedInputs();
        EmitWorld();
        if (_operation is not null || _synchronized != _state.Revision) return;
        // Outgoing suppression completes promptly. An unaligned incoming pair
        // never occupies the one native operation and starves another pair.
        foreach (var transfer in _state.Transfers.OrderBy(transfer => transfer.NewOwner == _local ? 1 : 0)) {
            if (_prepared.Contains(transfer.Transfer) || transfer.NewOwner == _local &&
                ((_pausedAttachment & transfer.Selection) != 0 || !transfer.Force && !Aligned(transfer.Selection))) continue;
            var flags = transfer.NewOwner == _local ? NativeSessionFlags.Incoming | (transfer.Force ? NativeSessionFlags.Force : NativeSessionFlags.None) : NativeSessionFlags.None;
            Start(Build(NativeSessionKind.Prepare, _state) with { Selection = (uint)transfer.Selection, Ticket = transfer.Transfer.ToString("N"), Flags = flags }); return;
        }
        foreach (var candidate in _state.Groups.Where(group => group.Owner == _local && !group.Attached && group.Pending is null &&
            !_attached.Contains((group.Group, group.Generation)) && (group.CommittedTransfer is not null || group.Generation == 1))
            .GroupBy(group => group.CommittedTransfer ?? _state.Session)) {
            var ready = candidate.Where(group => (_pausedAttachment & AtomicProtocol.Select(group.Group)) == 0 &&
                (Aligned(AtomicProtocol.Select(group.Group)) || PreparedForce(group.CommittedTransfer)))
                .Aggregate(ControlSelection.None, (mask, group) => mask | AtomicProtocol.Select(group.Group));
            if (ready == ControlSelection.None) continue;
            var force = candidate.Where(group => (ready & AtomicProtocol.Select(group.Group)) != 0).All(group => PreparedForce(group.CommittedTransfer));
            Start(Build(NativeSessionKind.Attach, _state) with { Selection = (uint)ready, Ticket = candidate.Key.ToString("N") }, force);
            return;
        }
    }
    public void CommandDropped(ulong command)
    {
        if (_operation?.Packet.Command != command) return;
        _operation = null; if (!_bound) _phase = NativeAuthorityPhase.WaitingForNative;
    }
    private void RouteEfbIntent()
    {
        if (_native is not { IntentId: > 0 } native || _synchronized != _state.Revision) return;
        if (native.IntentId > _handledIntent) {
            var selection = (ControlSelection)native.IntentSelection;
            if (native.IntentAction is >= NativeSessionIntent.SeatRequest and <= NativeSessionIntent.SeatCancel)
                ChangeSeat((SeatChangeAction)((uint)native.IntentAction - 6), native.IntentSeatRevision, native.IntentSeatRequest);
            else if (native.IntentAction is NativeSessionIntent.AutoOn or NativeSessionIntent.AutoOff) SetAutomatic(native.IntentAction == NativeSessionIntent.AutoOn);
            else if (native.IntentAction == NativeSessionIntent.Cancel) Cancel(selection);
            else Request(selection, native.IntentAction == NativeSessionIntent.Other ?
                _local == _state.PhysicsHost ? _state.Peer : _state.PhysicsHost : _local, native.IntentAction == NativeSessionIntent.Force);
            _handledIntent = native.IntentId;
        }
        // Retained status or a pre-execution queue drop can repeat this ack;
        // the local request itself is routed once, without replacing op proofs.
        Send(Build(NativeSessionKind.IntentAck, _state) with { Sequence = native.IntentId });
    }
    private readonly HashSet<Guid> _forced = [];
    private void ResetAutomatic(ControlSelection selection)
    {
        for (var axis = 9; axis < 21; ++axis)
            if ((selection & (ControlSelection)(1 << (1 + (axis - 9) % 3))) != 0) _automaticAnchor[axis] = -1;
    }
    private void TrackAutomatic()
    {
        if (_synchronized != _state.Revision || _observation is null || _native is null || _automaticObservation == _observation.Serial) return;
        _automaticObservation = _observation.Serial;
        foreach (var group in _state.Groups.Where(group => group.Group != AtomicControlGroup.Steering)) {
            var selection = AtomicProtocol.Select(group.Group);
            var mask = AtomicProtocol.RequiredAxes(group.Group, _state.Engines, _state.SteeringAxes) & _observation.Required;
            var axes = Enumerable.Range(9, 12).Where(axis => (mask & (1u << axis)) != 0).ToArray();
            if (!_state.Automatic || axes.Length == 0 || group.Owner is null ||
                (_observation.Fresh & _observation.Learned & mask) != mask || axes.Any(axis => _observation.Input[axis] < 0)) {
                ResetAutomatic(selection); continue;
            }
            // Only hardware observations contribute. Cockpit/world changes and
            // ownership notifications cannot arm automatic pickup.
            if (axes.Any(axis => _automaticAnchor[axis] < 0) || group.Owner == _local) {
                foreach (var axis in axes) _automaticAnchor[axis] = _observation.Input[axis];
                continue;
            }
            if (group.Pending is not null || _intents.Any(intent => (intent.Selection & selection) != 0)) continue;
            if (axes.Any(axis => Math.Abs(_observation.Input[axis] - _automaticAnchor[axis]) > 0.020000000001)) {
                _pausedAttachment &= ~selection;
                _intents.Add(new(selection, _local, false, automatic: true));
            }
        }
    }
    private void PublishAutomaticWaiting()
    {
        if (_synchronized != _state.Revision) return;
        var waiting = _intents.Where(intent => intent.Automatic).Aggregate(0u, (mask, intent) => mask | (uint)intent.Selection);
        if (_feedbackRevision == _state.Revision && _feedbackWaiting == waiting) return;
        if (Send(Build(NativeSessionKind.Feedback, _state) with {
            AutomaticWaiting = waiting, SeatRevision = _state.SeatRevision,
            Seat = _state.Seats.PilotPeer == _local ? 1u : 2u,
            SeatPending = _state.SeatChange is { } pending ? pending.RequestedBy == _local ? pending.Accepted ? 3u : 1u : pending.Accepted ? 4u : 2u : 0,
            SeatRequest = _state.SeatChange?.Ticket ?? 0
        })) {
            _feedbackRevision = _state.Revision; _feedbackWaiting = waiting;
        }
    }
    private void EmitWorld()
    {
        if (!_worldEnabled || !Host || _native is null || _observation is null || _synchronized != _state.Revision ||
            !_native.Flags.HasFlag(NativeSessionFlags.World | NativeSessionFlags.WorldFresh) ||
            !_observation.Flags.HasFlag(NativeStatusFlags.TargetsFresh) || _native.WorldSerial <= _worldProduced) return;
        var frame = new NativeWorldFrame(_state.Session, _state.Revision, _native.WorldSerial,
            _state.Groups.OrderBy(group => group.Group).Select(group => group.Generation).ToArray(), Build(NativeSessionKind.State, _state, allocate: false).Axes,
            _native.World.ToArray(), _observation.Targets.ToArray());
        if (!NativeWorldFrame.Valid(frame)) return;
        _worldProduced = frame.Sequence;
        try { WorldProduced?.Invoke(frame); } catch (Exception) { End("Shared world transport failed."); }
    }
    public bool QueueWorld(string sender, NativeWorldFrame source)
    {
        if (!_worldEnabled || Host || _phase != NativeAuthorityPhase.Active || _synchronized != _state.Revision || _native is null ||
            !_native.Flags.HasFlag(NativeSessionFlags.World | NativeSessionFlags.WorldFrozen) || sender != _state.PhysicsHost || !NativeWorldFrame.Valid(source)) return false;
        var frame = source.Copy();
        if (frame.Session != _state.Session || frame.Revision != _state.Revision || frame.Sequence <= _worldReceived ||
            !frame.Generation.SequenceEqual(_state.Groups.OrderBy(group => group.Group).Select(group => group.Generation))) return false;
        var packet = Build(NativeSessionKind.World, _state) with { World = frame.World, Positions = frame.Targets, Sequence = frame.Sequence };
        if (frame.Axes != packet.Axes) return false;
        _worldReceived = frame.Sequence; _worldAt = _clock();
        return NativeFresh(_clock()) && !_peerDelayed && Send(packet); // Discard during a local stall; the next fresh frame resumes, never a replay.
    }
    private bool PreparedForce(Guid? ticket) => ticket is { } id && _forced.Contains(id);
    private bool Incoming(NativeSessionPacket p) => p.Kind == NativeSessionKind.Attach || p.Flags.HasFlag(NativeSessionFlags.Incoming);
    private bool Aligned(ControlSelection selection)
    {
        if (_native is null) return false;
        if ((selection & ControlSelection.Steering) != 0 && _native.Flags.HasFlag(NativeSessionFlags.Autopilot)) return false;
        uint needed = 0;
        // Steering transfers as one group, but only the two yoke axes gate pickup.
        foreach (var group in AtomicProtocol.Members(selection))
            needed |= group == AtomicControlGroup.Steering ? AtomicProtocol.DefaultSteeringAxes : AtomicProtocol.RequiredAxes(group, _state.Engines, _state.SteeringAxes);
        needed &= _native.Required; // Optional hardware absent on this participant is not an alignment parameter.
        return needed != 0 && (_native.Aligned & needed) == needed && (_native.Fresh & needed) == needed;
    }
    private void SubmitIntents(TimeSpan now)
    {
        if (_synchronized != _state.Revision) return;
        foreach (var intent in _intents.ToArray()) {
            if (intent.Committed) continue;
            if (_state.Transfers.Any(transfer => (transfer.Selection & intent.Selection) != 0)) continue;
            if (intent.Request != 0 && now - intent.Sent < TimeSpan.FromSeconds(1)) continue;
            if (intent.Owner == _local && !intent.Force && !Aligned(intent.Selection)) continue;
            intent.Request = checked(++_request); intent.Sent = now;
            var epochs = AtomicProtocol.Members(intent.Selection).Select(group => new GroupEpoch(group, Group(group).Generation)).ToArray();
            SendAuthority(new AuthorityRequest(AtomicProtocol.Version, new(_state.Session, intent.Request, intent.Selection, epochs, intent.Owner) { Force = intent.Force, Automatic = intent.Automatic }));
        }
    }
    private void EmitOwnedInputs()
    {
        if (_observation is null || _native is null || _synchronized != _state.Revision || !_observation.Flags.HasFlag(NativeStatusFlags.TargetsFresh)) return;
        foreach (var slot in _state.Groups.Where(slot => slot.Owner == _local && slot.Attached && slot.Pending is null)) {
            if (slot.Group == AtomicControlGroup.Steering && _native.Flags.HasFlag(NativeSessionFlags.Autopilot)) continue;
            var index = (int)slot.Group; var selection = AtomicProtocol.Select(slot.Group);
            if ((_native.Blocked & (uint)selection) != 0 || _inputObservation[index] == _observation.Serial) continue;
            var mask = AtomicProtocol.RequiredAxes(slot.Group, _state.Engines, _state.SteeringAxes);
            var axes = new List<AxisPosition>();
            for (var axis = 0; axis < 21; ++axis) if ((mask & (1u << axis)) != 0) {
                var position = _observation.Targets[axis]; if (position < 0 || position > 1 || !double.IsFinite(position)) { axes.Clear(); break; }
                axes.Add(new((ControlAxis)axis, position));
            }
            if (axes.Count == 0) continue;
            if (_inputGeneration[index] != slot.Generation) { _inputGeneration[index] = slot.Generation; _inputSequence[index] = 0; }
            _inputObservation[index] = _observation.Serial;
            SendAuthority(new AuthorityInput(AtomicProtocol.Version, new(_state.Session, slot.Group, slot.Generation, checked(++_inputSequence[index]), axes.ToArray())));
        }
    }
    /// <summary>Host-only. Call inside ControlAuthoritySession.TryApplyAcceptedInput's final callback on the shared scheduler.</summary>
    public bool QueueAcceptedInput(string sender, AtomicControlFrame accepted)
    {
        if (_recovering || _peerDelayed || _phase != NativeAuthorityPhase.Active || !Host || _synchronized != _state.Revision || _clock() - _nativeAt > TimeSpan.FromSeconds(1) ||
            _clock() - _observationAt > TimeSpan.FromSeconds(1) || sender == _local || sender != _state.Peer ||
            !AtomicProtocol.CompleteFrame(accepted, _state.Engines, _state.SteeringAxes) || accepted.Session != _state.Session) return false;
        var slot = Group(accepted.Group);
        if (accepted.Group == AtomicControlGroup.Steering && _native?.Flags.HasFlag(NativeSessionFlags.Autopilot) == true) return false;
        if (slot.Owner != sender || !slot.Attached || slot.Pending is not null || slot.Generation != accepted.Generation) return false;
        var positions = Enumerable.Repeat(-1d, 21).ToArray(); foreach (var axis in accepted.Axes) positions[(int)axis.Axis] = axis.Position;
        return Send(Build(NativeSessionKind.Input, _state) with { Selection = (uint)AtomicProtocol.Select(accepted.Group), Sequence = accepted.Sequence,
            Axes = AtomicProtocol.RequiredAxes(accepted.Group, _state.Engines, _state.SteeringAxes), Positions = positions });
    }
    private NativeSessionPacket Build(NativeSessionKind kind, ControlAuthoritySnapshot state, bool allocate = true)
    {
        var groups = state.Groups.OrderBy(group => group.Group).ToArray();
        return new() {
            Kind = kind, Native = _identity.Native, Backend = _identity.Backend, Consent = _identity.Consent, Session = state.Session.ToString("N"), Epoch = _epoch,
            Command = allocate ? checked(++_command) : 0, Revision = state.Revision, Generation = groups.Select(group => group.Generation).ToArray(),
            Automatic = kind is NativeSessionKind.Bind or NativeSessionKind.State or NativeSessionKind.Feedback && state.Automatic,
            Owner = groups.Select(group => group.Owner is null ? NativeOwner.None : group.Owner == _local ? NativeOwner.Local : NativeOwner.Remote).ToArray(),
            Attached = groups.Where(group => group.Attached).Aggregate(0u, (mask, group) => mask | (uint)AtomicProtocol.Select(group.Group)),
            Pending = groups.Where(group => group.Pending is not null).Aggregate(0u, (mask, group) => mask | (uint)AtomicProtocol.Select(group.Group)),
            PendingTransfer = groups.Select(group => group.Pending?.ToString("N") ?? "").ToArray(),
            CommittedTransfer = groups.Select(group => group.CommittedTransfer?.ToString("N") ?? "").ToArray(),
            Engines = (uint)state.Engines, Axes = Enum.GetValues<AtomicControlGroup>().Aggregate(0u, (mask, group) => mask | AtomicProtocol.RequiredAxes(group, state.Engines, state.SteeringAxes))
        };
    }
    private bool Send(NativeSessionPacket packet)
    {
        try { return _sendNative(packet.Copy()); }
        catch (Exception) { End("Native command transport failed.", sendEnd: false); return false; }
    }
    private void SendAuthority(AuthorityMessage message)
    {
        try { _sendAuthority(message); }
        catch (Exception) { End("Shared authority transport failed."); }
    }
    private void Start(NativeSessionPacket p, bool forceAttachment = false)
    {
        var job = new Operation(p.Copy(), Copy(_state), _clock(), forceAttachment); _operation = job;
        if (p.Kind == NativeSessionKind.Bind) _phase = NativeAuthorityPhase.Binding;
        if (!Send(p) && ReferenceEquals(_operation, job)) { _operation = null; if (!_bound) _phase = NativeAuthorityPhase.WaitingForNative; }
    }
    private void CompleteOperation(NativeSessionPacket native)
    {
        if (_operation is not { } job || native.Ack != job.Packet.Command || native.Result == NativeSessionResult.Pending) return;
        if (job.Packet.Kind != NativeSessionKind.Bind && job.State.Revision != _state.Revision) { _operation = null; return; }
        if (native.Result != NativeSessionResult.Succeeded || native.Phase != NativeSessionPhase.Active || !Matches(native, job.State)) { End("Native handover proof did not match the current operation."); return; }
        var p = job.Packet; var selection = (ControlSelection)p.Selection;
        if (p.Kind is NativeSessionKind.Prepare or NativeSessionKind.Attach && Incoming(p) && (_pausedAttachment & selection) != 0) {
            _operation = null; _cancelNativeOperation = true; return;
        }
        if (p.Kind is NativeSessionKind.Prepare or NativeSessionKind.Attach && (native.Ticket != p.Ticket || native.Selection != p.Selection)) { End("Native handover ticket changed."); return; }
        _operation = null;
        if (p.Kind is NativeSessionKind.Bind or NativeSessionKind.State) {
            if (p.Kind == NativeSessionKind.Bind && native.Blocked != 15) { End("Native initial hardware suppression was not confirmed."); return; }
            if (p.Kind == NativeSessionKind.Bind && _worldEnabled && (!native.Flags.HasFlag(NativeSessionFlags.World) ||
                !Host && !native.Flags.HasFlag(NativeSessionFlags.WorldFrozen))) { End("Native guest physics freeze was not confirmed."); return; }
            if (p.Kind == NativeSessionKind.Bind) _worldAt = _clock();
            _bound = true; _phase = NativeAuthorityPhase.Active; _synchronized = job.State.Revision; return;
        }
        if (p.Kind == NativeSessionKind.Prepare) {
            if ((native.Blocked & p.Selection) != p.Selection || !Guid.TryParseExact(p.Ticket, "N", out var ticket)) { End("Native preparation did not confirm hardware suppression."); return; }
            _prepared.Add(ticket); if (p.Flags.HasFlag(NativeSessionFlags.Force)) _forced.Add(ticket);
            SendAuthority(new AuthorityPreparation(AtomicProtocol.Version, new(_state.Session, ticket, true,
                p.Flags.HasFlag(NativeSessionFlags.Incoming) && !p.Flags.HasFlag(NativeSessionFlags.Force))));
        } else if (p.Kind == NativeSessionKind.Attach) {
            if ((native.Blocked & p.Selection) != 0) { End("Native attachment did not confirm local hardware."); return; }
            var epochs = AtomicProtocol.Members(selection).Select(group => new GroupEpoch(group, p.Generation[(int)group])).ToArray();
            foreach (var epoch in epochs) _attached.Add((epoch.Group, epoch.Generation));
            SendAuthority(new AuthorityAttachment(AtomicProtocol.Version, new(_state.Session, selection, epochs, true)));
        }
    }
    private bool Matches(NativeSessionPacket native, ControlAuthoritySnapshot state)
    {
        if (native.Revision != state.Revision || native.Engines != state.Engines || native.Automatic != state.Automatic) return false;
        var expected = Build(NativeSessionKind.State, state, allocate: false);
        return native.Generation.SequenceEqual(expected.Generation) && native.Owner.SequenceEqual(expected.Owner) &&
            native.Pending == expected.Pending && native.Attached == expected.Attached && native.Axes == expected.Axes &&
            native.PendingTransfer.SequenceEqual(expected.PendingTransfer) && native.CommittedTransfer.SequenceEqual(expected.CommittedTransfer);
    }
    public void End(string error = "Shared session ended.", bool sendEnd = true)
    {
        if (_phase == NativeAuthorityPhase.Ended) return;
        var binding = _bound || _native?.Phase is NativeSessionPhase.Binding or NativeSessionPhase.Active;
        _phase = NativeAuthorityPhase.Ended; _recovering = false; _error = error; _operation = null; _intents.Clear();
        if (sendEnd && binding) { try { _sendNative(Build(NativeSessionKind.End, _state)); } catch (Exception) { } }
        Ended?.Invoke();
    }
}
