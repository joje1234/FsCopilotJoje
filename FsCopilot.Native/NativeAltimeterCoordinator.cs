using FsCopilot.Ownership;

namespace FsCopilot.Native;

/// <summary>
/// Runs on the physical-control coordinator's actor. Only that coordinator
/// allocates native command IDs. Host dispatch callbacks must enter the model's
/// final pending-frame fence before calling QueueAccepted.
/// </summary>
public sealed class NativeAltimeterCoordinator
{
    private sealed record GateJob(ulong Command, ulong Epoch, ulong Preparation, uint Mask, ulong PhysicalRevision, TimeSpan At);
    private sealed record ValueJob(ulong Command, NativeBarometerAction Action, uint Index, AltimeterSetting Setting,
        ulong Epoch, ulong Preparation, ulong BeforeRead, ulong PhysicalRevision, TimeSpan At, AltimeterChange? Change);
    private readonly string _local, _remote;
    private readonly NativeAuthorityCoordinator _controls;
    private readonly Func<TimeSpan> _clock;
    private readonly Action<AuthorityMessage> _send;
    private readonly Action<AltimeterSetting[], ulong> _start;
    private readonly Action<AltimeterChange, ulong, bool> _dispatch;
    private AltimeterAuthoritySnapshot _state;
    private readonly HashSet<AltimeterChange> _dispatching = [];
    private readonly ulong[] _inputs = new ulong[2], _nativeSequence = new ulong[2];
    private readonly bool[] _mirrorDirty = new bool[2];
    private AltimeterSetting? _ownerValue;
    private GateJob? _gate;
    private ValueJob? _value;
    private AltimeterAvailability? _remoteAvailability;
    private TimeSpan _remoteAt, _availabilityAt = TimeSpan.MinValue, _proofAt = TimeSpan.MinValue, _startedAt;
    private TimeSpan _supportedAt = TimeSpan.MinValue;
    private TimeSpan _feedbackAt = TimeSpan.MinValue;
    private ulong _feedbackRevision;
    private bool _localStopSent;
    private string _adapter = "";
    private ulong _availability, _proof, _proofRead, _inputRead;
    private uint _seeded;
    private int _syncRetries;
    private bool _starting, _everMasked, _failed, _stopped;
    public string Error { get; private set; } = "";
    public AltimeterAuthoritySnapshot State {
        get { var state = Volatile.Read(ref _state); return state with { Instruments = state.Instruments.ToArray(), Gates = state.Gates.ToArray() }; }
    }
    private bool Host => _local == _state.PhysicsHost;
    private uint Seat => _state.Seats.PilotPeer == _local ? 1u : 2u;
    public NativeAltimeterCoordinator(string local, NativeAuthorityCoordinator controls, Func<TimeSpan> clock,
        Action<AuthorityMessage> send, Action<AltimeterSetting[], ulong> start, Action<AltimeterChange, ulong, bool> dispatch)
    {
        var initial = controls.Authority;
        if (local != initial.PhysicsHost && local != initial.Peer) throw new ArgumentException("Local instrument identity is not in the session.");
        _local = local; _remote = local == initial.PhysicsHost ? initial.Peer : initial.PhysicsHost;
        _controls = controls; _clock = clock; _send = send; _start = start; _dispatch = dispatch;
        _state = new AltimeterAuthorityReplica(initial).Snapshot;
    }
    public bool Update(AltimeterAuthoritySnapshot state)
    {
        var main = _controls.Authority;
        if (!AltimeterProtocol.Valid(state) || state.Session != _state.Session || state.PhysicsHost != _state.PhysicsHost ||
            state.Peer != _state.Peer || state.Seats != main.Seats || state.Revision <= _state.Revision ||
            state.Epoch < _state.Epoch || state.Preparation < _state.Preparation || _state.Phase == AltimeterPhase.Ended) return false;
        if (state.Epoch != _state.Epoch || state.Preparation != _state.Preparation) {
            _seeded = 0; _syncRetries = 0; _proofRead = 0; _ownerValue = null;
            if (state.Epoch != _state.Epoch) { _value = null; _mirrorDirty[0] = _mirrorDirty[1] = false; }
        }
        _state = state with { Instruments = state.Instruments.ToArray(), Gates = state.Gates.ToArray() };
        _dispatching.RemoveWhere(change => !_state.Instruments.Any(value => value.Pending == change));
        return true;
    }
    public bool ReceiveAvailability(string sender, AltimeterAvailability availability)
    {
        if (sender != _remote || !AltimeterProtocol.Valid(availability) || availability.Session != _state.Session ||
            availability.Sequence <= (_remoteAvailability?.Sequence ?? 0)) return false;
        _remoteAvailability = availability; _remoteAt = _clock(); return true;
    }
    private bool ReadyPeer(NativeInstrumentContext context) => _remoteAvailability is { Supported: true } remote &&
        remote.Adapter == context.Observation.Adapter && _clock() >= _remoteAt && _clock() - _remoteAt <= TimeSpan.FromSeconds(2);
    private static AltimeterSetting[] Settings(NativeInstrumentContext context) {
        var values = context.Observation.Altimeters;
        return [new(values.Pressure1, (values.StandardMask & 1) != 0), new(values.Pressure2, (values.StandardMask & 2) != 0)];
    }
    private NativeBarometerCommand Body(NativeInstrumentContext context, NativeBarometerAction action, uint index = 0,
        AltimeterSetting setting = default, uint mask = 0) => new(_state.Epoch, Math.Max(1, _state.Preparation), _state.Revision,
            context.Session.SeatRevision, action is NativeBarometerAction.Apply or NativeBarometerAction.Observe ? ++_nativeSequence[index - 1] : 0,
            action, index, setting.Pressure16, setting.Standard, mask, context.Session.Seat, context.Observation.Altimeters.Serial);
    public void Tick()
    {
        if (_controls.Progress.Phase == NativeAuthorityPhase.Ended) { _stopped = true; return; }
        var now = _clock();
        var context = _controls.InstrumentContext;
        if (context is not null && _state.Seats == _controls.Authority.Seats &&
            (_feedbackAt == TimeSpan.MinValue || _feedbackRevision != _state.Revision || now - _feedbackAt >= TimeSpan.FromMilliseconds(400)) &&
            _controls.ReportInstruments(new(_state.Epoch, _state.Preparation, _state.Revision, context.Session.SeatRevision,
                (uint)_state.Phase, _state.Instruments.Aggregate(0u, (mask, value) => value.Pending is null ? mask : mask | (1u << ((int)value.Index - 1))),
                AltimeterProtocol.Version, _failed ? 1u : 0u))) { _feedbackAt = now; _feedbackRevision = _state.Revision; }
        if (context?.Observation.Altimeters.LocalOnly == true) {
            if (!_localStopSent) { _localStopSent = true; Send(new AuthorityAltimeterStop(AtomicProtocol.Version, _state.Session, 1)); }
        }
        if (context is { Observation.Altimeters: { Fresh: true, Transport: true, Profile: true, LocalOnly: false, Held: false, Recovering: false } }) {
            _supportedAt = now; _adapter = context.Observation.Adapter;
        } else if (context is not null) _supportedAt = TimeSpan.MinValue;
        if (_availabilityAt == TimeSpan.MinValue || now - _availabilityAt >= TimeSpan.FromMilliseconds(500)) {
            var supported = !_failed && _supportedAt != TimeSpan.MinValue && now >= _supportedAt && now - _supportedAt <= TimeSpan.FromSeconds(1);
            Send(new AuthorityAltimeterAvailability(AtomicProtocol.Version,
                new(_state.Session, ++_availability, supported, supported ? _adapter : "")));
            _availabilityAt = now;
        }
        if (context?.Observation.Altimeters.LocalOnly == true) return;
        if (_failed || _state.Phase is AltimeterPhase.Local or AltimeterPhase.Holding or AltimeterPhase.Unsupported or AltimeterPhase.Ended) { StopNative(context); return; }
        if (context is null) return; // Physical revision/seat feedback may be synchronizing; instrument proof leases still expire normally.
        if (context is not { Observation.Altimeters: { Fresh: true, Transport: true, Profile: true, Held: false, Recovering: false } }) {
            if (_everMasked) Fail("Native instrument observations or the verified input adapter became unavailable.");
            return;
        }
        Complete(context);
        if (_failed) return;
        if (_everMasked && !ReadyPeer(context)) { Fail("The other player's verified instrument adapter is no longer ready."); return; }
        if (_state.Phase == AltimeterPhase.Disabled) {
            if (!Host || !ReadyPeer(context)) return; // No knob is masked on an unsupported or missing peer's behalf.
            if (!EnsureGate(context, 3)) return;
            if (!_starting) {
                _starting = true; _startedAt = now; _start(Settings(context), context.Observation.Altimeters.Serial);
            } else if (now - _startedAt >= TimeSpan.FromSeconds(6)) Fail("Instrument initialization was not accepted.");
            return;
        }
        if (_state.Seats != _controls.Authority.Seats) return;
        CaptureOwner(context); // Includes the final local movement before a quiescing gate proof.
        if (_state.Phase == AltimeterPhase.Quiescing) {
            if (!EnsureGate(context, 3)) return;
            DispatchPending(context);
            PublishProof(context, 3); return;
        }
        if (_state.Phase == AltimeterPhase.Synchronizing) {
            if (_seeded != 3) {
                if (!EnsureGate(context, 3)) return;
                if (DispatchPending(context) || _value is not null || _state.Instruments.Any(value => value.Pending is not null)) return;
                var index = (_seeded & 1) == 0 ? 1u : 2u;
                StartValue(context, NativeBarometerAction.Seed, index, _state.Instruments[index - 1].Confirmed!.Value, null);
                return;
            }
            if (!EnsureGate(context, 3)) return;
            if (!Settings(context).SequenceEqual(_state.Instruments.Select(value => value.Confirmed!.Value))) {
                if (++_syncRetries > 2) { Fail("Instrument values changed repeatedly during initial pickup."); return; }
                _seeded = 0; _ownerValue = null; return;
            }
            _ownerValue ??= Settings(context)[Seat - 1];
            PublishProof(context, 3); return;
        }
        if (_state.Phase is not (AltimeterPhase.Active or AltimeterPhase.Attaching)) return;
        if (!EnsureGate(context, AltimeterProtocol.Blocked(_state.Seats, _local))) return;
        DispatchPending(context);
        if (!Host && _value is null) {
            var index = Seat == 1 ? 2u : 1u;
            var canonical = _state.Instruments[index - 1].Confirmed!.Value;
            if (_mirrorDirty[index - 1] || Settings(context)[index - 1] != canonical)
                StartValue(context, NativeBarometerAction.Apply, index, canonical, null);
        }
        PublishProof(context, AltimeterProtocol.Blocked(_state.Seats, _local));
    }
    private bool EnsureGate(NativeInstrumentContext context, uint mask)
    {
        if (_gate is { } current && current.Epoch == _state.Epoch && current.Preparation == Math.Max(1, _state.Preparation) && current.Mask == mask) {
            var ack = context.Session.BarometerGateAck;
            return NativeBarometerAck.Command(ack) == current.Command && NativeBarometerAck.Result(ack) == NativeBarometerResult.Succeeded &&
                context.Observation.Altimeters is { GateReady: true } observation && observation.Blocked == mask;
        }
        var command = _controls.QueueBarometer(Body(context, NativeBarometerAction.Gate, mask: mask));
        if (command == 0) return false;
        _everMasked = true;
        _gate = new(command, _state.Epoch, Math.Max(1, _state.Preparation), mask, context.Session.Revision, _clock());
        return false;
    }
    private void StartValue(NativeInstrumentContext context, NativeBarometerAction action, uint index, AltimeterSetting setting, AltimeterChange? change)
    {
        var command = _controls.QueueBarometer(Body(context, action, index, setting));
        if (command == 0) { Fail("Native instrument command was refused before queueing."); return; }
        if (action == NativeBarometerAction.Apply && !Host) _mirrorDirty[index - 1] = true;
        _value = new(command, action, index, setting, _state.Epoch, _state.Preparation, context.Observation.Altimeters.Serial,
            context.Session.Revision, _clock(), change);
    }
    private bool DispatchPending(NativeInstrumentContext context)
    {
        if (!Host) return false;
        var pending = _state.Instruments.FirstOrDefault(value => value.Pending is { } change && !value.Dispatched &&
            !_dispatching.Contains(change) && (_value is null || _value.Index == value.Index));
        if (pending?.Pending is not { } next) return false;
        _dispatching.Add(next); _dispatch(next, context.Observation.Altimeters.Serial, false); return true;
    }
    public bool QueueAccepted(AltimeterChange change)
    {
        _dispatching.Remove(change);
        if (!Host || _failed || _controls.InstrumentContext is not { } context || _state.Epoch != change.Epoch ||
            _state.Instruments[change.Instrument - 1].Pending != change ||
            _value is not null && _value.Index != change.Instrument) return false;
        StartValue(context, change.Instrument == Seat ? NativeBarometerAction.Observe : NativeBarometerAction.Apply,
            change.Instrument, change.Setting, change);
        return !_failed && _value?.Change == change;
    }
    public void DispatchDeclined(AltimeterChange change) => _dispatching.Remove(change);
    public void StartDeclined() => Fail("The physics host refused instrument initialization.");
    private void CaptureOwner(NativeInstrumentContext context)
    {
        if (_state.Phase is not (AltimeterPhase.Active or AltimeterPhase.Attaching or AltimeterPhase.Quiescing) || context.Observation.Altimeters.Serial <= _inputRead ||
            _state.Phase == AltimeterPhase.Attaching && context.Observation.Altimeters.Blocked != AltimeterProtocol.Blocked(_state.Seats, _local)) return;
        if (_state.Phase == AltimeterPhase.Quiescing && _state.Gates.Single(value => value.Peer == _local).Ready) return;
        _inputRead = context.Observation.Altimeters.Serial;
        var setting = Settings(context)[Seat - 1];
        if (_ownerValue is null) { _ownerValue = setting; return; }
        if (_ownerValue == setting) return;
        _ownerValue = setting;
        Send(new AuthorityAltimeterChange(AtomicProtocol.Version,
            new(_state.Session, _state.Epoch, _state.Preparation, ++_inputs[Seat - 1], Seat, setting)));
    }
    private void PublishProof(NativeInstrumentContext context, uint mask)
    {
        var observation = context.Observation.Altimeters;
        if (!observation.GateReady || observation.Blocked != mask || observation.Serial <= _proofRead ||
            _proofAt != TimeSpan.MinValue && _clock() - _proofAt < TimeSpan.FromMilliseconds(200)) return;
        _proofRead = observation.Serial; _proofAt = _clock();
        Send(new AuthorityAltimeterGate(AtomicProtocol.Version,
            new(_state.Session, _state.Epoch, _state.Preparation, ++_proof, true, context.Observation.Adapter,
                mask, observation.Serial, Settings(context))));
    }
    private void Complete(NativeInstrumentContext context)
    {
        if (_gate is { } gate) {
            var ack = context.Session.BarometerGateAck;
            if (NativeBarometerAck.Command(ack) == gate.Command && NativeBarometerAck.Result(ack) is NativeBarometerResult.Rejected or NativeBarometerResult.Failed or NativeBarometerResult.Unsupported) {
                if (NativeBarometerAck.Result(ack) == NativeBarometerResult.Rejected && context.Session.Revision != gate.PhysicalRevision) _gate = null;
                else Fail("Native instrument input-gate confirmation failed.");
            } else if (_clock() - gate.At >= TimeSpan.FromSeconds(3) &&
                !(NativeBarometerAck.Command(ack) == gate.Command && NativeBarometerAck.Result(ack) == NativeBarometerResult.Succeeded &&
                    context.Observation.Altimeters.GateReady && context.Observation.Altimeters.Blocked == gate.Mask))
                Fail("Native instrument input-gate confirmation timed out.");
        }
        if (_value is not { } value) return;
        if (value.Epoch != _state.Epoch || value.Change is { } old && _state.Instruments[value.Index - 1].Pending != old) {
            _value = null; return; // Retired writes may finish at the SDK, but cannot confirm old authority.
        }
        if (value.Change is null && value.Action != NativeBarometerAction.Seed &&
            _state.Instruments[value.Index - 1].Confirmed != value.Setting) { _value = null; return; }
        var result = context.Session.BarometerValueAck;
        if (NativeBarometerAck.Command(result) != value.Command || NativeBarometerAck.Result(result) == NativeBarometerResult.Pending) {
            if (_clock() - value.At >= TimeSpan.FromSeconds(3)) Fail("Native instrument read-back timed out.");
            return;
        }
        if (NativeBarometerAck.Result(result) == NativeBarometerResult.Rejected && context.Session.Revision != value.PhysicalRevision) {
            Retry(value, context); return;
        }
        if (NativeBarometerAck.Result(result) != NativeBarometerResult.Succeeded) { Fail("Native instrument read-back did not match its command."); return; }
        // Session acknowledgements and pressure observations are independent
        // ClientData streams. An acknowledged write cannot make an older cached
        // pressure packet into a confirmation; wait for its actual read-back.
        if (context.Observation.Altimeters.Serial <= value.BeforeRead || Settings(context)[value.Index - 1] != value.Setting) {
            if (_clock() - value.At >= TimeSpan.FromSeconds(3)) Fail("Native instrument pressure observations did not catch up with its acknowledgement.");
            return;
        }
        _value = null;
        if (value.Change is { } change) Send(new AuthorityAltimeterConfirmation(AtomicProtocol.Version,
            new(change, context.Observation.Altimeters.Serial, true, value.Setting)));
        else if (value.Action == NativeBarometerAction.Seed && value.Preparation == _state.Preparation) _seeded |= 1u << ((int)value.Index - 1);
        else _mirrorDirty[value.Index - 1] = false;
    }
    public void CommandDropped(ulong command)
    {
        if (_gate?.Command == command) _gate = null;
        if (_value?.Command == command && _controls.InstrumentContext is { } context) Retry(_value, context);
    }
    private void Retry(ValueJob value, NativeInstrumentContext context)
    {
        _value = null;
        if (value.Change is not { } change) return; // Seed/mirror retries are generated from current state on Tick.
        if (_failed || _state.Instruments[value.Index - 1].Pending != change) return;
        _dispatching.Add(change); _dispatch(change, context.Observation.Altimeters.Serial, true);
    }
    private void Fail(string reason)
    {
        if (_failed) return;
        _failed = true; Error = reason;
        if (_state.Phase is AltimeterPhase.Synchronizing or AltimeterPhase.Attaching or AltimeterPhase.Active or AltimeterPhase.Quiescing)
            Send(new AuthorityAltimeterGate(AtomicProtocol.Version,
                new(_state.Session, _state.Epoch, _state.Preparation, ++_proof, false, "", 0, 0, [])));
        StopNative(_controls.InstrumentContext);
    }
    private void StopNative(NativeInstrumentContext? context)
    {
        if (_stopped || !_everMasked || context is null) return;
        if (_controls.QueueBarometer(Body(context, NativeBarometerAction.Stop)) != 0) _stopped = true;
    }
    private void Send(AuthorityMessage message)
    {
        try { _send(message); }
        catch (Exception) { _failed = true; Error = "Instrument authority transport failed."; StopNative(_controls.InstrumentContext); }
    }
}
