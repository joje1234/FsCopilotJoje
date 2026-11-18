namespace FsCopilot.Ownership;

// Shares the parent's lock so execution fences, confirmed seat commits and
// pressure confirmation cannot race. Instrument revisions are independent of
// control-group revisions; tuning a knob must not starve steering/world frames.
public sealed partial class ControlAuthoritySession
{
    private sealed class Barometer
    {
        public AltimeterSetting? Confirmed;
        public AltimeterChange? Pending;
        public bool Dispatched;
        public ulong RequiredRead;
        public TimeSpan Deadline;
    }
    private sealed class BarometerGate(string peer)
    {
        public readonly string Peer = peer;
        public AltimeterGateAck? Proof;
        public TimeSpan At;
        public bool Ready;
    }
    private readonly Barometer[] _barometers = [new(), new()];
    private BarometerGate[]? _barometerGates;
    private readonly Dictionary<(string, uint), ulong> _barometerInputs = new();
    private readonly Dictionary<string, ulong> _barometerProofs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _barometerReadSerials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _barometerStops = new(StringComparer.Ordinal);
    public bool StopAltimeters(string sender, Guid session, ulong sequence) {
        lock (_sync) {
            if (!Member(sender) || !_connected || session != Session || sequence == 0 || sequence <= _barometerStops.GetValueOrDefault(sender)) return false;
            _barometerStops[sender] = sequence;
            if (_barometerPhase == AltimeterPhase.Local) return true;
            HoldAltimetersCore(AltimeterPhase.Local); return true;
        }
    }
    private AltimeterPhase _barometerPhase;
    private ulong _barometerRevision = 1, _barometerEpoch = 1, _barometerPreparation, _barometerPreviousPreparation, _barometerHostRead, _barometerQuiesceFloor;
    private bool _barometerWasActive, _barometerWasGated;
    private TimeSpan _barometerDeadline;
    private BarometerGate[] BarometerGates => _barometerGates ??= [new(_host), new(_peer)];
    public AltimeterAuthoritySnapshot Altimeters { get { lock (_sync) return AltimetersCore(); } }
    public (ControlAuthoritySnapshot Controls, AltimeterAuthoritySnapshot Altimeters, FlightGuidanceSnapshot Guidance) Publication {
        get { lock (_sync) return (SnapshotCore(), AltimetersCore(), GuidanceCore()); }
    }
    private AltimeterAuthoritySnapshot AltimetersCore() => new(AltimeterProtocol.Version, Session, _host, _peer, _barometerEpoch,
        _barometerPreparation, _barometerRevision, _seats, _barometerPhase,
        _barometers.Select((value, index) => new AltimeterInstrument((uint)index + 1, AltimeterProtocol.Owner(_seats, (uint)index + 1),
            value.Confirmed, value.Pending, value.Dispatched)).ToArray(),
        BarometerGates.Select(value => new AltimeterGate(value.Peer, value.Proof?.Blocked ?? 0, value.Ready, value.Proof?.Adapter ?? "")).ToArray());
    public bool StartAltimeters(string sender, Guid session, AltimeterSetting[] settings, ulong readSerial, bool initialInputHeld = false)
    {
        lock (_sync) {
            if (sender != _host || !Member(sender) || !_connected || session != Session || _barometerPhase != AltimeterPhase.Disabled ||
                !AltimeterProtocol.Values(settings) || readSerial == 0) return false;
            for (var index = 0; index < 2; ++index) _barometers[index].Confirmed = settings[index];
            _barometerHostRead = readSerial; _barometerReadSerials[_host] = readSerial;
            _barometerWasGated = initialInputHeld;
            PrepareAltimetersCore(AltimeterPhase.Synchronizing); return true;
        }
    }
    private void PrepareAltimetersCore(AltimeterPhase phase)
    {
        _barometerPreviousPreparation = _barometerPreparation;
        ++_barometerPreparation; _barometerPhase = phase; _barometerDeadline = _clock() + TimeSpan.FromSeconds(6);
        foreach (var gate in BarometerGates) gate.Ready = false;
        ++_barometerRevision;
    }
    public bool AcknowledgeAltimeterGate(string sender, AltimeterGateAck proof)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || !_connected || !AltimeterProtocol.Valid(proof) || proof.Session != Session || proof.Epoch != _barometerEpoch ||
                proof.Preparation != _barometerPreparation || proof.Sequence <= _barometerProofs.GetValueOrDefault(sender) ||
                _barometerPhase is not (AltimeterPhase.Synchronizing or AltimeterPhase.Attaching or AltimeterPhase.Active or AltimeterPhase.Quiescing)) return false;
            _barometerProofs[sender] = proof.Sequence;
            if (!proof.Supported) { HoldAltimetersCore(_barometerWasActive || _barometerWasGated ? AltimeterPhase.Holding : AltimeterPhase.Unsupported); return true; }
            var gate = BarometerGates.Single(value => value.Peer == sender);
            var blocked = _barometerPhase is AltimeterPhase.Synchronizing or AltimeterPhase.Quiescing ? 3u : AltimeterProtocol.Blocked(_seats, sender);
            if (proof.Blocked != blocked || proof.ReadSerial <= _barometerReadSerials.GetValueOrDefault(sender) ||
                sender == _host && proof.ReadSerial < _barometerHostRead) return false;
            if (_barometerPhase == AltimeterPhase.Synchronizing && !_barometers.Select(value => value.Confirmed!.Value).SequenceEqual(proof.Settings)) return false;
            var other = BarometerGates.Single(value => value.Peer != sender);
            if (other.Ready && other.Proof!.Adapter != proof.Adapter) { HoldAltimetersCore(AltimeterPhase.Holding); return false; }
            var changed = !gate.Ready || gate.Proof?.Blocked != proof.Blocked || gate.Proof?.Adapter != proof.Adapter;
            gate.Proof = proof with { Settings = proof.Settings.ToArray() }; gate.Ready = true; gate.At = _clock();
            _barometerWasGated |= proof.Blocked != 0;
            _barometerReadSerials[sender] = proof.ReadSerial;
            if (sender == _host) _barometerHostRead = Math.Max(_barometerHostRead, proof.ReadSerial);
            if (changed) ++_barometerRevision;
            CompleteAltimeterPreparationCore(); return true;
        }
    }
    public bool RequestAltimeter(string sender, AltimeterChange change)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || !_connected || !AltimeterProtocol.Valid(change) || change.Session != Session ||
                change.Sequence <= _barometerInputs.GetValueOrDefault((sender, change.Instrument))) return false;
            _barometerInputs[(sender, change.Instrument)] = change.Sequence;
            if (change.Epoch != _barometerEpoch || sender != AltimeterProtocol.Owner(_seats, change.Instrument) ||
                _barometerPhase is not (AltimeterPhase.Active or AltimeterPhase.Attaching or AltimeterPhase.Quiescing) ||
                (change.Preparation != _barometerPreparation && (_barometerPhase != AltimeterPhase.Quiescing || change.Preparation != _barometerPreviousPreparation)) ||
                _barometerPhase == AltimeterPhase.Quiescing && BarometerGates.Single(value => value.Peer == sender) is { Ready: true, Proof.Blocked: 3 }) return false;
            var instrument = _barometers[change.Instrument - 1];
            if (instrument.Pending?.Setting == change.Setting || instrument.Pending is null && instrument.Confirmed == change.Setting) return true;
            instrument.Pending = change; instrument.Dispatched = false; instrument.Deadline = _clock() + TimeSpan.FromSeconds(3);
            ++_barometerRevision; return true;
        }
    }
    public bool TryApplyAltimeter(AltimeterChange change, ulong beforeReadSerial, Func<AltimeterChange, bool> apply)
        => DispatchAltimeter(change, beforeReadSerial, apply, false);
    // Only the local native dispatcher calls this after a proven pre-execution
    // queue drop. It rechecks the same pending owner/frame without extending its
    // original deadline. This is not exposed as a remote authority message.
    public bool TryRetryAltimeter(AltimeterChange change, ulong beforeReadSerial, Func<AltimeterChange, bool> apply)
        => DispatchAltimeter(change, beforeReadSerial, apply, true);
    private bool DispatchAltimeter(AltimeterChange change, ulong beforeReadSerial, Func<AltimeterChange, bool> apply, bool retry)
    {
        ArgumentNullException.ThrowIfNull(apply);
        lock (_sync) {
            ExpireCore();
            if (!AltimeterProtocol.Valid(change) || !Member(_host) || !_connected || change.Session != Session || change.Epoch != _barometerEpoch ||
                _barometerPhase is not (AltimeterPhase.Active or AltimeterPhase.Attaching or AltimeterPhase.Quiescing or AltimeterPhase.Synchronizing) || beforeReadSerial < _barometerHostRead ||
                beforeReadSerial == 0) return false;
            var instrument = _barometers[change.Instrument - 1];
            if (instrument.Pending != change || instrument.Dispatched != retry) return false;
            instrument.Dispatched = true; instrument.RequiredRead = beforeReadSerial; ++_barometerRevision;
            try { if (apply(change)) return true; }
            catch { HoldAltimetersCore(AltimeterPhase.Holding); throw; }
            HoldAltimetersCore(AltimeterPhase.Holding); return false;
        }
    }
    public bool ConfirmAltimeter(string sender, AltimeterConfirmation confirmation)
    {
        lock (_sync) {
            ExpireCore();
            if (sender != _host || !Member(sender) || !_connected || !AltimeterProtocol.Valid(confirmation) ||
                confirmation.Change.Session != Session || confirmation.Change.Epoch != _barometerEpoch) return false;
            var instrument = _barometers[confirmation.Change.Instrument - 1];
            if (instrument.Pending != confirmation.Change || !instrument.Dispatched) return false;
            if (!confirmation.Succeeded) { HoldAltimetersCore(AltimeterPhase.Holding); return true; }
            if (confirmation.ReadSerial <= instrument.RequiredRead || confirmation.ReadSerial < _barometerHostRead || confirmation.Observed != confirmation.Change.Setting) return false;
            instrument.Confirmed = confirmation.Observed; instrument.Pending = null; instrument.Dispatched = false;
            _barometerHostRead = Math.Max(_barometerHostRead, confirmation.ReadSerial); ++_barometerRevision;
            if (_barometerPhase == AltimeterPhase.Synchronizing) foreach (var gate in BarometerGates) gate.Ready = false;
            CompleteAltimeterPreparationCore(); return true;
        }
    }
    private void CompleteAltimeterPreparationCore()
    {
        if (_barometers.Any(value => value.Pending is not null) || BarometerGates.Any(value => !value.Ready)) return;
        if (_barometerPhase == AltimeterPhase.Synchronizing) {
            _barometerPhase = AltimeterPhase.Attaching;
            foreach (var gate in BarometerGates) gate.Ready = false;
            ++_barometerRevision;
        } else if (_barometerPhase == AltimeterPhase.Attaching) {
            _barometerPhase = AltimeterPhase.Active; _barometerWasActive = true; ++_barometerRevision;
        } else if (_barometerPhase == AltimeterPhase.Quiescing && _seatChange is { Accepted: true }) {
            var proof = BarometerGates[0].Proof!;
            if (proof.ReadSerial <= _barometerQuiesceFloor || proof.ReadSerial < _barometerHostRead ||
                !_barometers.Select(value => value.Confirmed!.Value).SequenceEqual(proof.Settings)) return;
            CommitSeatChangeCore();
        }
    }
    private bool AcceptAltimeterSeatChangeCore()
    {
        if (_barometerPhase is AltimeterPhase.Disabled or AltimeterPhase.Local or AltimeterPhase.Unsupported) return false;
        if (_barometerPhase != AltimeterPhase.Active) return true;
        _seatChange = _seatChange! with { Accepted = true };
        _barometerQuiesceFloor = _barometerHostRead;
        PrepareAltimetersCore(AltimeterPhase.Quiescing); return true;
    }
    private void CancelAltimeterSeatChangeCore()
    {
        if (_barometerPhase == AltimeterPhase.Quiescing) PrepareAltimetersCore(AltimeterPhase.Synchronizing);
    }
    private void AltimeterSeatsChangedCore()
    {
        ++_barometerEpoch;
        foreach (var gate in BarometerGates) { gate.Ready = false; gate.Proof = null; }
        if (_barometerPhase == AltimeterPhase.Quiescing) PrepareAltimetersCore(AltimeterPhase.Synchronizing);
        else ++_barometerRevision;
    }
    private void HoldAltimetersCore(AltimeterPhase phase)
    {
        _barometerPhase = phase;
        foreach (var instrument in _barometers) { instrument.Pending = null; instrument.Dispatched = false; }
        foreach (var gate in BarometerGates) gate.Ready = false;
        ++_barometerRevision;
        if (_seatChange is { Accepted: true }) { _seatChange = null; ++_seatRevision; ++_revision; }
    }
    private void ExpireAltimetersCore()
    {
        if (_barometerPhase is not (AltimeterPhase.Synchronizing or AltimeterPhase.Attaching or AltimeterPhase.Active or AltimeterPhase.Quiescing)) return;
        var now = _clock();
        if (_barometers.Any(value => value.Pending is not null && now >= value.Deadline) ||
            (_barometerPhase is AltimeterPhase.Synchronizing or AltimeterPhase.Attaching or AltimeterPhase.Quiescing) && now >= _barometerDeadline ||
            BarometerGates.Any(value => value.Ready && now - value.At > TimeSpan.FromSeconds(1))) HoldAltimetersCore(AltimeterPhase.Holding);
    }
}
