using FsCopilot.Ownership;

namespace FsCopilot.Native;

/// <summary>Runs on the shared-session actor. Native evidence, captured inputs
/// and host output completions remain distinct from authority declarations.</summary>
public sealed class NativeGuidanceCoordinator
{
    private sealed record Job(NativeGuidancePacket Packet, TimeSpan At, GuidanceCommand? Command = null);
    private sealed record Input(ulong NativeId, GuidanceCommand Command, TimeSpan At, bool? Accepted = null);
    private readonly string _local, _remote;
    private readonly NativeAuthorityCoordinator _controls;
    private readonly INativeGuidanceClient _client;
    private readonly Func<TimeSpan> _clock;
    private readonly Action<AuthorityMessage> _send;
    private readonly Action<GuidanceState, ulong> _start;
    private readonly Action<ulong, GuidanceState> _observe;
    private readonly Action<GuidanceCommand, ulong, GuidanceState, bool> _confirm;
    private readonly Action<GuidanceCommand, ulong, bool> _dispatch;
    private readonly Func<GuidanceCommand, Func<bool>, bool> _execute;
    private FlightGuidanceSnapshot _state;
    private GuidanceAvailability? _remoteAvailability;
    private TimeSpan _remoteAt, _nativeAt, _availabilityAt = TimeSpan.MinValue, _proofAt = TimeSpan.MinValue;
    private ulong _availability, _proof, _proofRead, _hostRead, _nativeSerial, _nativeCommand, _nativeSequence, _preparation, _inputSequence, _lastInput, _receiptRevision, _failure;
    private Guid _preparationGuid;
    private Job? _gate, _value, _reference, _intentAck;
    private GuidanceState? _referenceState;
    private ulong _referenceInput;
    private Input? _input;
    private GuidanceCommand? _dispatching, _retry;
    private bool _starting, _everManaged, _referenceReady, _failed, _stopSent;
    public FlightGuidanceSnapshot State => Volatile.Read(ref _state);
    public string Error { get; private set; } = "";
    private bool Host => _local == _state.PhysicsHost;

    public NativeGuidanceCoordinator(string local, NativeAuthorityCoordinator controls, INativeGuidanceClient client, Func<TimeSpan> clock,
        Action<AuthorityMessage> send, Action<GuidanceState, ulong> start, Action<ulong, GuidanceState> observe,
        Action<GuidanceCommand, ulong, GuidanceState, bool> confirm, Action<GuidanceCommand, ulong, bool> dispatch,
        Func<GuidanceCommand, Func<bool>, bool> execute)
    {
        var initial = controls.Authority;
        if (local != initial.PhysicsHost && local != initial.Peer) throw new ArgumentException("Guidance identity is not in the session.");
        _local = local; _remote = local == initial.PhysicsHost ? initial.Peer : initial.PhysicsHost;
        _controls = controls; _client = client; _clock = clock; _send = send; _start = start; _observe = observe; _confirm = confirm;
        _dispatch = dispatch; _execute = execute; _state = new GuidanceAuthorityReplica(initial).Snapshot;
    }
    public bool Update(FlightGuidanceSnapshot state)
    {
        if (!GuidanceProtocol.Valid(state) || state.Session != _state.Session || state.PhysicsHost != _state.PhysicsHost || state.Peer != _state.Peer ||
            state.Revision <= _state.Revision || state.CommandEpoch < _state.CommandEpoch || state.NavigationEpoch < _state.NavigationEpoch) return false;
        if (state.Preparation != _preparationGuid || state.CommandEpoch != _state.CommandEpoch || state.NavigationEpoch != _state.NavigationEpoch) {
            if (state.Preparation != _preparationGuid) { _preparation = checked(_preparation + 1); _preparationGuid = state.Preparation; }
            _gate = _value = _reference = _intentAck = null; _input = null; _referenceState = null;
            _referenceReady = false; _nativeSequence = _lastInput = _proofRead = 0; _dispatching = _retry = null;
        }
        Volatile.Write(ref _state, state);
        if (_value?.Command is { } accepted && state.Pending?.Command != accepted) _value = null;
        if (_dispatching is { } queued && state.Pending?.Command != queued) _dispatching = null;
        Fence(); return true;
    }
    public bool Availability(string sender, GuidanceAvailability value)
    {
        if (sender != _remote || !GuidanceProtocol.Valid(value) || value.Session != _state.Session || value.Sequence <= (_remoteAvailability?.Sequence ?? 0)) return false;
        _remoteAvailability = value; _remoteAt = _clock(); return true;
    }
    public bool Receipt(string sender, GuidanceReceipt receipt)
    {
        if (sender != _state.PhysicsHost || receipt.Peer != _local || _input is not { } input || receipt.Command != input.Command) return false;
        if (input.Accepted == true) return true;
        _input = input with { Accepted = receipt.Accepted }; _lastInput = input.NativeId;
        _receiptRevision = Math.Max(_receiptRevision, receipt.Revision); return true;
    }
    private ulong _claim;
    public void Claim(bool me)
    {
        if (_failed || _state.Phase != GuidancePhase.Active) return;
        _send(new AuthorityGuidanceClaim(AtomicProtocol.Version, new(_state.Session, ++_claim, GuidanceSelection.Commands,
            me ? _local : _remote, _state.CommandEpoch, _state.NavigationEpoch)));
    }
    private (NativeInstrumentContext Main, NativeGuidancePacket AP)? Context()
    {
        if (_controls.GuidanceContext is not { } main || _client.GuidanceState is not { Live: true, Snapshot: { } ap } ||
            !NativeGuidanceWire.Valid(ap) || ap.Native != main.Session.Native || ap.Backend != main.Session.Backend || ap.Session != main.Session.Session ||
            ap.Consent != main.Session.Consent || ap.Epoch != main.Session.Epoch || ap.ControlRevision != main.Session.Revision) return null;
        if (ap.Serial > _nativeSerial) { _nativeSerial = ap.Serial; _nativeAt = _clock(); }
        return _clock() >= _nativeAt && _clock() - _nativeAt <= TimeSpan.FromSeconds(1) ? (main, ap) : null;
    }
    private bool Exact(NativeGuidancePacket ap) => ap.CommandEpoch == _state.CommandEpoch && ap.NavigationEpoch == _state.NavigationEpoch && ap.Preparation == _preparation;
    private NativeGuidancePacket Body(NativeInstrumentContext main, NativeGuidancePacket ap, NativeGuidanceAction action) => new() {
        Native = main.Session.Native, Backend = main.Session.Backend, Session = main.Session.Session, Consent = main.Session.Consent,
        Epoch = main.Session.Epoch, ControlRevision = main.Session.Revision, Command = checked(++_nativeCommand), CommandEpoch = _state.CommandEpoch,
        NavigationEpoch = _state.NavigationEpoch, Preparation = _preparation, Revision = _state.Revision, Action = action, ReadSerial = ap.ReadSerial, Host = Host
    };
    private void Fence() => _client.SetGuidanceFence(new(_gate?.Packet, _value?.Packet, _intentAck?.Packet, _reference?.Packet));
    private bool Queue(NativeGuidancePacket packet, Func<Func<bool>, bool>? execute = null)
    {
        Fence();
        try { return _client.TrySendGuidance(packet, execute); } catch (Exception) { return false; }
    }
    public void Tick()
    {
        var now = _clock(); var context = Context();
        var supported = !_failed && context is { } available && available.AP.Flags.HasFlag(NativeGuidanceFlags.Fresh | NativeGuidanceFlags.Profile) &&
            (available.AP.Flags & (NativeGuidanceFlags.Held | NativeGuidanceFlags.LocalOnly)) == 0 && GuidanceProtocol.Supported(available.AP.Supported, GuidanceSetting.Master);
        if (_availabilityAt == TimeSpan.MinValue || now - _availabilityAt >= TimeSpan.FromMilliseconds(500)) {
            var ap = context?.AP;
            _send(new AuthorityGuidanceAvailability(AtomicProtocol.Version, new(_state.Session, ++_availability, supported,
                supported ? ap!.Adapter : "", supported ? ap!.Supported : 0))); _availabilityAt = now;
        }
        if (_controls.Progress.Phase == NativeAuthorityPhase.Ended || _failed || _state.Phase is GuidancePhase.Holding or GuidancePhase.Local or GuidancePhase.Ended) {
            StopNative(context); return;
        }
        if (_state.Phase == GuidancePhase.Disabled) {
            if (Host && !_starting && supported && context is { } initial && _remoteAvailability is { Supported: true } remote &&
                remote.Adapter == initial.AP.Adapter && now - _remoteAt <= TimeSpan.FromSeconds(2)) {
                _starting = true; _start(Read(initial.AP), initial.AP.ReadSerial);
            }
            return;
        }
        if (context is not { } current) {
            if (_everManaged && now - _nativeAt > TimeSpan.FromSeconds(1)) Fail("Native AP observations are unavailable.");
            return;
        }
        var (main, apState) = current;
        if (!supported) { Fail("The native AP adapter is no longer ready."); return; }
        if (_remoteAvailability is not { Supported: true } peer || peer.Adapter != apState.Adapter || now - _remoteAt > TimeSpan.FromSeconds(2)) {
            Fail("The other player's AP adapter is unavailable."); return;
        }
        var exact = Exact(apState);
        if (exact) EnsureReference(main, apState, now);
        var referenceMatches = _reference is { } reference && NativeBarometerAck.Command(apState.ReferenceAck) == reference.Packet.Command &&
            NativeBarometerAck.Result(apState.ReferenceAck) == NativeBarometerResult.Succeeded;
        if (referenceMatches) _referenceReady = true;
        var wantsCapture = _state.Phase is GuidancePhase.Active or GuidancePhase.Attaching && _state.Commander == _local;
        var capture = exact && wantsCapture && (_referenceReady || apState.Flags.HasFlag(NativeGuidanceFlags.Capture));
        EnsureGate(main, apState, capture, now);
        if (!exact || _failed) return;
        if (Host && apState.ReadSerial > _hostRead) { _hostRead = apState.ReadSerial; _observe(apState.ReadSerial, Read(apState)); }
        CompleteValue(apState, now);
        if (_failed) return;
        var ready = apState.Flags.HasFlag(NativeGuidanceFlags.GateReady) && apState.Flags.HasFlag(NativeGuidanceFlags.Capture) == wantsCapture;
        if (_referenceReady && ready && apState.ReadSerial > _proofRead && (_proofRead == 0 || now - _proofAt >= TimeSpan.FromMilliseconds(250))) {
            _proofRead = apState.ReadSerial; _proofAt = now;
            _send(new AuthorityGuidanceGate(AtomicProtocol.Version, new(_state.Session, _state.Preparation, _state.CommandEpoch, _state.NavigationEpoch,
                ++_proof, apState.Adapter, GuidanceCapabilities.Commands, apState.Blocked == 8191, wantsCapture,
                apState.Flags.HasFlag(NativeGuidanceFlags.SingleHostEngine), apState.ReadSerial, Read(apState), false, apState.Supported)));
        }
        ForwardInput(main, apState, now);
        if (Host && _state.Pending is { } pending && (_value is null || _value.Command != pending.Command)) {
            var emergency = pending.Emergency && apState.Blocked == 8191 && apState.Flags.HasFlag(NativeGuidanceFlags.SingleHostEngine);
            if ((ready || emergency) && _dispatching != pending.Command && (!pending.Dispatched || _retry == pending.Command)) {
                _dispatching = pending.Command; var retry = pending.Dispatched || _retry == pending.Command; _retry = null;
                _dispatch(pending.Command, apState.ReadSerial, retry);
            }
        }
    }
    private void EnsureGate(NativeInstrumentContext main, NativeGuidancePacket ap, bool capture, TimeSpan now)
    {
        if (_gate is { } gate) {
            var ack = ap.GateAck;
            if (NativeBarometerAck.Command(ack) == gate.Packet.Command && NativeBarometerAck.Result(ack) is NativeBarometerResult.Failed or NativeBarometerResult.Unsupported) {
                Fail("Native AP input capture failed."); return;
            }
            if (now - gate.At > TimeSpan.FromSeconds(2) && NativeBarometerAck.Command(ack) < gate.Packet.Command) { Fail("Native AP gate confirmation timed out."); return; }
            if (gate.Packet.Capture == capture && now - gate.At < TimeSpan.FromMilliseconds(300)) return;
        }
        var packet = Body(main, ap, NativeGuidanceAction.Gate) with { Capture = capture };
        _gate = new(packet, now); _everManaged = true;
        if (!Queue(packet)) { _gate = null; Fence(); }
    }
    private void EnsureReference(NativeInstrumentContext main, NativeGuidancePacket ap, TimeSpan now)
    {
        if (_state.Confirmed is not { } confirmed || _state.Revision < _receiptRevision) return;
        var effective = Effective(confirmed, _state.Pending?.Command);
        if (_reference is { } existing && _referenceState == effective && _referenceInput == _lastInput) {
            if (NativeBarometerAck.Command(ap.ReferenceAck) == existing.Packet.Command) {
                if (NativeBarometerAck.Result(ap.ReferenceAck) == NativeBarometerResult.Succeeded) return;
                Fail("The AP adapter refused its input reference."); return;
            }
            if (now - existing.At < TimeSpan.FromMilliseconds(500)) return;
        }
        var packet = Reference(Body(main, ap, NativeGuidanceAction.Reference), effective) with { Sequence = _lastInput };
        _reference = new(packet, now); _referenceState = effective; _referenceInput = _lastInput;
        if (!Queue(packet)) { _reference = null; Fence(); }
    }
    public bool QueueAccepted(GuidanceCommand command)
    {
        _dispatching = null;
        if (!Host || _failed || _state.Pending?.Command != command || Context() is not { } context || !Exact(context.AP)) return false;
        var packet = Body(context.Main, context.AP, NativeGuidanceAction.Apply) with { Sequence = ++_nativeSequence, Setting = command.Setting, Value = command.Value,
            SteeringGeneration = command.SteeringGeneration, SteeringOwner = command.SteeringGeneration == 0 ? NativeOwner.None : context.Main.Session.Owner[0] };
        _value = new(packet, _clock(), command);
        if (Queue(packet, write => _execute(command, write))) return true;
        _value = null; Fence(); return false;
    }
    public void StartDeclined() { if (_state.Phase == GuidancePhase.Disabled) Fail("Shared AP startup was refused."); }
    public void DispatchDeclined(GuidanceCommand command)
    {
        if (_dispatching == command) _dispatching = null;
        if (_state.Pending?.Command == command && !_failed) Fail("The AP command could not enter the native queue.");
    }
    private void CompleteValue(NativeGuidancePacket ap, TimeSpan now)
    {
        if (_value is not { Command: { } command } job) return;
        if (_state.Pending?.Command != command) { _value = null; Fence(); return; }
        if (NativeBarometerAck.Command(ap.ValueAck) != job.Packet.Command) {
            if (now - job.At > TimeSpan.FromSeconds(2)) Fail("Native AP output confirmation timed out."); return;
        }
        var result = NativeBarometerAck.Result(ap.ValueAck);
        if (result == NativeBarometerResult.Pending) return;
        if (result == NativeBarometerResult.Rejected) { _value = null; _retry = command; Fence(); return; } // Native refused before execution.
        if (result != NativeBarometerResult.Succeeded) { _confirm(command, ap.ReadSerial, Read(ap), false); Fail("Native AP output failed."); return; }
        if (ap.ReadSerial <= job.Packet.ReadSerial || !GuidanceProtocol.Matches(command, Read(ap))) return;
        _value = null; Fence(); _confirm(command, ap.ReadSerial, Read(ap), true);
    }
    private void ForwardInput(NativeInstrumentContext main, NativeGuidancePacket ap, TimeSpan now)
    {
        if (_input is { Accepted: not null } accepted) {
            if (ap.Sequence != accepted.NativeId) { _input = null; _intentAck = null; Fence(); }
            else if (_intentAck is null || now - _intentAck.At >= TimeSpan.FromMilliseconds(500)) {
                var packet = Body(main, ap, NativeGuidanceAction.IntentAck) with { Sequence = accepted.NativeId };
                _intentAck = new(packet, now); if (!Queue(packet)) { _intentAck = null; Fence(); }
            }
        }
        if (_input is not null || ap.Sequence == 0 || !ap.Flags.HasFlag(NativeGuidanceFlags.GateReady) || _state.Revision < _receiptRevision ||
            _state.Phase is not (GuidancePhase.Active or GuidancePhase.Attaching or GuidancePhase.Quiescing)) return;
        var emergency = ap.Setting == GuidanceSetting.Master && ap.Value == 0 && ap.SteeringGeneration != 0;
        if (!emergency && _state.Commander != _local) return;
        if (!emergency && _state.Pending is { } pending && pending.Command.Setting != ap.Setting) return;
        var command = new GuidanceCommand(_state.Session, _state.Preparation, _state.CommandEpoch, _state.NavigationEpoch,
            ++_inputSequence, ap.Setting, ap.Value, _state.Commander == _local ? 0 : ap.SteeringGeneration);
        _input = new(ap.Sequence, command, now); _send(new AuthorityGuidanceCommand(AtomicProtocol.Version, command));
    }
    public void Dropped(ulong command)
    {
        if (_gate?.Packet.Command == command) { if (_gate.Packet.Action == NativeGuidanceAction.Stop) _stopSent = false; _gate = null; }
        if (_reference?.Packet.Command == command) _reference = null;
        if (_intentAck?.Packet.Command == command) _intentAck = null;
        if (_value is { Command: { } network } value && value.Packet.Command == command) { _value = null; _retry = network; }
        Fence();
    }
    private void StopNative((NativeInstrumentContext Main, NativeGuidancePacket AP)? context)
    {
        _value = _reference = _intentAck = null; _input = null;
        if (!_stopSent) _gate = null;
        Fence();
        if (!_everManaged || _stopSent || context is not { } current || !Exact(current.AP)) return;
        var packet = Body(current.Main, current.AP, NativeGuidanceAction.Stop); _gate = new(packet, _clock());
        if (Queue(packet)) _stopSent = true;
    }
    private void Fail(string error)
    {
        if (_failed) return;
        _failed = true; Error = error; _client.SetGuidanceFence(null);
        if (_state.Preparation != Guid.Empty) _send(new AuthorityGuidanceFailure(AtomicProtocol.Version,
            new(_state.Session, _state.Preparation, _state.CommandEpoch, _state.NavigationEpoch, ++_failure)));
    }
    public static GuidanceState Read(NativeGuidancePacket p)
    {
        GuidanceModes modes = 0;
        var map = new[] { (2u, GuidanceModes.Heading), (4u, GuidanceModes.Altitude), (8u, GuidanceModes.VerticalSpeed),
            (16u, GuidanceModes.NavArmed), (32u | 64u | 128u, GuidanceModes.ApproachArmed), (256u, GuidanceModes.Glideslope),
            (512u, GuidanceModes.Backcourse), (1024u, GuidanceModes.FlightLevelChange), (2048u, GuidanceModes.YawDamper), (4096u, GuidanceModes.FlightDirector) };
        foreach (var (mask, mode) in map) if ((p.Switches & mask) != 0) modes |= mode;
        var nav = p.Flags.HasFlag(NativeGuidanceFlags.NavigationReady);
        return new((p.Switches & 1) != 0, modes, p.Heading, p.Altitude, p.VerticalSpeed, p.Gps, p.Nav, nav, nav ? p.NavigationFingerprint : "");
    }
    private static GuidanceState Effective(GuidanceState state, GuidanceCommand? pending)
    {
        if (pending is null) return state;
        if (pending.Setting == GuidanceSetting.Master) return state with { Master = pending.Value != 0 };
        if (pending.Setting == GuidanceSetting.Heading) return state with { Heading = pending.Value };
        if (pending.Setting == GuidanceSetting.Altitude) return state with { Altitude = pending.Value };
        if (pending.Setting == GuidanceSetting.VerticalSpeed) return state with { VerticalSpeed = pending.Value };
        var mask = pending.Setting switch { GuidanceSetting.HeadingHold => GuidanceModes.Heading, GuidanceSetting.AltitudeHold => GuidanceModes.Altitude,
            GuidanceSetting.VerticalHold => GuidanceModes.VerticalSpeed, GuidanceSetting.NavHold => GuidanceModes.NavArmed | GuidanceModes.NavCaptured,
            GuidanceSetting.ApproachHold => GuidanceModes.ApproachArmed | GuidanceModes.Localizer | GuidanceModes.Glideslope, GuidanceSetting.Backcourse => GuidanceModes.Backcourse,
            GuidanceSetting.FlightLevelChange => GuidanceModes.FlightLevelChange, GuidanceSetting.YawDamper => GuidanceModes.YawDamper,
            GuidanceSetting.FlightDirector => GuidanceModes.FlightDirector, _ => GuidanceModes.None };
        return state with { Modes = pending.Value != 0 ? state.Modes | mask : state.Modes & ~mask };
    }
    private static NativeGuidancePacket Reference(NativeGuidancePacket packet, GuidanceState state)
    {
        uint switches = state.Master ? 1u : 0;
        var map = new[] { (GuidanceModes.Heading, 2u), (GuidanceModes.Altitude, 4u), (GuidanceModes.VerticalSpeed, 8u),
            (GuidanceModes.NavArmed | GuidanceModes.NavCaptured, 16u), (GuidanceModes.ApproachArmed | GuidanceModes.Localizer, 32u),
            (GuidanceModes.Glideslope, 256u), (GuidanceModes.Backcourse, 512u), (GuidanceModes.FlightLevelChange, 1024u),
            (GuidanceModes.YawDamper, 2048u), (GuidanceModes.FlightDirector, 4096u) };
        foreach (var (mode, mask) in map) if ((state.Modes & mode) != 0) switches |= mask;
        return packet with { Switches = switches, Heading = state.Heading, Altitude = state.Altitude, VerticalSpeed = state.VerticalSpeed, Gps = state.Gps, Nav = state.NavReceiver };
    }
}
