namespace FsCopilot.Ownership;

// Generic ownership policy. It never replays GTN touches or treats a route hash
// as proof that an aircraft/avionics adapter can transfer guidance.
public sealed partial class ControlAuthoritySession
{
    private sealed class GuidanceGate(string peer) {
        public readonly string Peer = peer;
        public GuidanceGateProof? Proof;
        public bool Ready;
        public TimeSpan At;
    }
    private GuidanceGate[]? _guidanceGates;
    private GuidanceGate[] GuidanceGates => _guidanceGates ??= [new(_host), new(_peer)];
    private readonly Dictionary<string, ulong> _guidanceProofs = new(), _guidanceReads = new(), _guidanceRequests = new(), _guidanceInputs = new(), _guidanceStops = new();
    private GuidancePhase _guidancePhase;
    private ulong _guidanceRevision = 1, _commandEpoch = 1, _navigationEpoch = 1, _guidanceHostRead, _guidanceRequiredRead;
    private Guid _guidancePreparation;
    private string? _commander, _navigator;
    private GuidanceCapabilities _guidanceCapabilities;
    private uint _guidanceSupported;
    private GuidanceState? _guidanceState;
    private GuidancePendingCommand? _guidancePending;
    private GuidanceTransfer? _guidanceTransfer;
    private TimeSpan _guidanceDeadline, _guidanceCommandDeadline;
    private bool _guidanceNavigationUncertain;
    public FlightGuidanceSnapshot Guidance { get { lock (_sync) return GuidanceCore(); } }
    private FlightGuidanceSnapshot GuidanceCore() => new(GuidanceProtocol.Version, Session, _host, _peer, _guidanceRevision,
        _commandEpoch, _navigationEpoch, _guidancePreparation, _guidancePhase, _commander ?? _host, _navigator ?? _host,
        _guidanceCapabilities, _guidanceState, _guidancePending, _guidanceTransfer, _guidanceSupported);
    public bool StartGuidance(string sender, Guid session, GuidanceState state, ulong readSerial)
    {
        lock (_sync) {
            if (!Member(sender) || sender != _host || !_connected || session != Session || _guidancePhase != GuidancePhase.Disabled ||
                !GuidanceProtocol.Valid(state) || readSerial == 0) return false;
            _guidanceState = state; _guidanceHostRead = readSerial; _guidanceReads[_host] = readSerial;
            PrepareGuidanceCore(GuidancePhase.Preparing); return true;
        }
    }
    private void PrepareGuidanceCore(GuidancePhase phase)
    {
        _guidancePhase = phase; _guidancePreparation = Guid.NewGuid(); _guidanceDeadline = _clock() + TimeSpan.FromSeconds(6);
        foreach (var gate in GuidanceGates) gate.Ready = false;
        ++_guidanceRevision;
    }
    public bool AcknowledgeGuidance(string sender, GuidanceGateProof proof)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || !_connected || !GuidanceProtocol.Valid(proof) || proof.Session != Session ||
                proof.Preparation != _guidancePreparation || proof.CommandEpoch != _commandEpoch || proof.NavigationEpoch != _navigationEpoch ||
                proof.Sequence <= _guidanceProofs.GetValueOrDefault(sender) || proof.ReadSerial <= _guidanceReads.GetValueOrDefault(sender) ||
                sender == _host && (proof.ReadSerial < _guidanceHostRead || proof.ReadSerial == _guidanceHostRead && _guidanceState != proof.State) ||
                _guidancePhase is not (GuidancePhase.Preparing or GuidancePhase.Attaching or GuidancePhase.Active or GuidancePhase.Quiescing)) return false;
            _guidanceProofs[sender] = proof.Sequence;
            if (!proof.InputSuppressed || !proof.SingleHostEngine || !proof.Capabilities.HasFlag(GuidanceCapabilities.Commands) ||
                !GuidanceProtocol.Supported(proof.SupportedSettings, GuidanceSetting.Master)) {
                HoldGuidanceCore(GuidancePhase.Holding); return true;
            }
            var enabled = _guidancePhase is GuidancePhase.Active or GuidancePhase.Attaching && sender == (_commander ?? _host);
            if (proof.OwnerCaptureEnabled != enabled) return false;
            var other = GuidanceGates.Single(gate => gate.Peer != sender);
            if (other.Ready && other.Proof!.Adapter != proof.Adapter) { HoldGuidanceCore(GuidancePhase.Holding); return false; }
            var gate = GuidanceGates.Single(gate => gate.Peer == sender);
            gate.Proof = proof; gate.Ready = true; gate.At = _clock(); _guidanceReads[sender] = proof.ReadSerial;
            if (sender == _host) AcceptGuidanceStateCore(proof.ReadSerial, proof.State);
            _guidanceCapabilities = GuidanceGates.All(value => value.Ready)
                ? GuidanceGates[0].Proof!.Capabilities & GuidanceGates[1].Proof!.Capabilities : GuidanceCapabilities.None;
            _guidanceSupported = GuidanceGates.All(value => value.Ready)
                ? GuidanceGates[0].Proof!.SupportedSettings & GuidanceGates[1].Proof!.SupportedSettings : _guidanceSupported & proof.SupportedSettings;
            if (_guidancePending is { } accepted && !GuidanceProtocol.Supported(_guidanceSupported, accepted.Command.Setting)) {
                HoldGuidanceCore(GuidancePhase.Holding); return true;
            }
            ++_guidanceRevision; CompleteGuidancePreparationCore(); return true;
        }
    }
    private void CompleteGuidancePreparationCore()
    {
        if (GuidanceGates.Any(gate => !gate.Ready) || _guidancePending is not null) return;
        if (_guidancePhase == GuidancePhase.Quiescing && _guidanceTransfer is { } transfer) {
            if ((transfer.Selection & GuidanceSelection.Navigation) != 0) {
                if (!_guidanceCapabilities.HasFlag(GuidanceCapabilities.NavigationTransfer) ||
                    !NavigationWasAppliedCore() || _guidanceState!.Master || !_guidanceState.NavigationReady ||
                    _guidanceState.NavigationFingerprint != transfer.NavigationFingerprint ||
                    !GuidanceGates.Single(gate => gate.Peer == transfer.NewOwner).Proof!.State.NavigationReady ||
                    GuidanceGates.Single(gate => gate.Peer == transfer.NewOwner).Proof!.State.NavigationFingerprint != transfer.NavigationFingerprint) return;
                _navigator = transfer.NewOwner; ++_navigationEpoch;
            }
            if ((transfer.Selection & GuidanceSelection.Commands) != 0) { _commander = transfer.NewOwner; ++_commandEpoch; }
            _guidanceTransfer = null; _guidancePhase = GuidancePhase.Attaching;
            foreach (var gate in GuidanceGates) gate.Ready = false;
            ++_guidanceRevision;
        } else if (_guidancePhase == GuidancePhase.Preparing) {
            if (_guidanceNavigationUncertain) { HoldGuidanceCore(GuidancePhase.Holding); return; }
            _guidancePhase = GuidancePhase.Attaching; foreach (var gate in GuidanceGates) gate.Ready = false; ++_guidanceRevision;
        } else if (_guidancePhase == GuidancePhase.Attaching) { _guidancePhase = GuidancePhase.Active; ++_guidanceRevision; }
    }
    public bool ClaimGuidance(string sender, GuidanceClaim claim)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || !_connected || !GuidanceProtocol.Valid(claim) || claim.Session != Session ||
                claim.Sequence <= _guidanceRequests.GetValueOrDefault(sender)) return false;
            _guidanceRequests[sender] = claim.Sequence;
            if (_guidancePhase != GuidancePhase.Active || _guidancePending is not null ||
                claim.CommandEpoch != _commandEpoch || claim.NavigationEpoch != _navigationEpoch || !Member(claim.NewOwner) ||
                (claim.Selection & GuidanceSelection.Navigation) != 0 && !_guidanceCapabilities.HasFlag(GuidanceCapabilities.NavigationTransfer)) return false;
            if ((claim.Selection & GuidanceSelection.Navigation) != 0 && _guidanceState!.Master) return false;
            var commands = (claim.Selection & GuidanceSelection.Commands) != 0 && claim.NewOwner != (_commander ?? _host);
            var navigation = (claim.Selection & GuidanceSelection.Navigation) != 0 && claim.NewOwner != (_navigator ?? _host);
            if (!commands && !navigation) return false;
            PrepareGuidanceCore(GuidancePhase.Quiescing);
            _guidanceTransfer = new(_guidancePreparation, sender, claim.NewOwner,
                (commands ? GuidanceSelection.Commands : 0) | (navigation ? GuidanceSelection.Navigation : 0),
                navigation ? claim.NavigationFingerprint : "");
            return true;
        }
    }
    public bool CancelGuidanceClaim(string sender, Guid session, Guid ticket)
    {
        lock (_sync) {
            if (!Member(sender) || session != Session || _guidanceTransfer is not { } transfer || transfer.Ticket != ticket ||
                sender != transfer.RequestedBy && sender != transfer.NewOwner) return false;
            if (_guidancePending is not null || (transfer.Selection & GuidanceSelection.Navigation) != 0 && NavigationWasAppliedCore()) {
                _guidanceNavigationUncertain = true; HoldGuidanceCore(GuidancePhase.Holding); return true;
            }
            _guidanceTransfer = null; PrepareGuidanceCore(GuidancePhase.Attaching); return true;
        }
    }
    public bool RequestGuidanceCommand(string sender, GuidanceCommand command)
    {
        lock (_sync) {
            ExpireCore();
            if (!Member(sender) || !_connected || !GuidanceProtocol.Valid(command) || command.Session != Session ||
                command.Preparation != _guidancePreparation || command.CommandEpoch != _commandEpoch || command.NavigationEpoch != _navigationEpoch ||
                command.Sequence <= _guidanceInputs.GetValueOrDefault(sender) ||
                _guidancePhase is not (GuidancePhase.Attaching or GuidancePhase.Active or GuidancePhase.Quiescing) ||
                !GuidanceProtocol.Supported(_guidanceSupported, command.Setting)) return false;
            _guidanceInputs[sender] = command.Sequence;
            var commander = _commander ?? _host;
            var steering = Get(AtomicControlGroup.Steering).State;
            var emergency = command.Setting == GuidanceSetting.Master && command.Value == 0 && (sender == commander ||
                sender == steering.Owner && command.SteeringGeneration == steering.Generation);
            if (sender != commander && !emergency || sender == commander && command.SteeringGeneration != 0 ||
                !emergency && _guidancePhase == GuidancePhase.Quiescing && GuidanceGates.Single(gate => gate.Peer == sender).Ready ||
                !emergency && _guidancePhase == GuidancePhase.Attaching && !GuidanceGates.Single(gate => gate.Peer == sender).Ready ||
                command.Value != 0 && command.Setting is GuidanceSetting.NavHold or GuidanceSetting.ApproachHold && _guidanceState?.NavigationReady != true ||
                command.Setting == GuidanceSetting.Master && command.Value != 0 && _guidanceState is { NavigationReady: false } missingSource &&
                    (missingSource.Modes & (GuidanceModes.NavArmed | GuidanceModes.NavCaptured | GuidanceModes.ApproachArmed | GuidanceModes.Localizer | GuidanceModes.Glideslope)) != 0 ||
                command.Setting == GuidanceSetting.Master && command.Value != 0 && _guidanceTransfer is { } transfer &&
                    (transfer.Selection & GuidanceSelection.Navigation) != 0 ||
                _guidancePending is { } pending && !emergency && pending.Command.Setting != command.Setting) return false;
            if (emergency) {
                _guidanceNavigationUncertain |= _guidanceTransfer is { } interrupted && (interrupted.Selection & GuidanceSelection.Navigation) != 0 &&
                    NavigationWasAppliedCore();
                ++_commandEpoch; _guidanceTransfer = null; PrepareGuidanceCore(GuidancePhase.Preparing);
                command = command with { CommandEpoch = _commandEpoch, Preparation = _guidancePreparation };
            } else if (_guidancePending is null && GuidanceProtocol.Matches(command, _guidanceState!)) return true;
            _guidancePending = new(command, sender, false, emergency); _guidanceCommandDeadline = _clock() + TimeSpan.FromSeconds(3);
            ++_guidanceRevision; return true;
        }
    }
    public bool TryApplyGuidanceCommand(GuidanceCommand command, ulong beforeRead, Func<GuidanceCommand, bool> enqueue, bool retry = false)
    {
        ArgumentNullException.ThrowIfNull(enqueue);
        lock (_sync) {
            ExpireCore();
            if (!_connected || _ended || _guidancePending is not { } pending || pending.Command != command ||
                pending.Dispatched != retry || beforeRead == 0 || beforeRead < _guidanceHostRead) return false;
            if (pending.Emergency && pending.Sender != (_commander ?? _host) && (pending.Sender != Get(AtomicControlGroup.Steering).State.Owner ||
                command.SteeringGeneration != Get(AtomicControlGroup.Steering).State.Generation)) return false;
            _guidancePending = pending with { Dispatched = true }; _guidanceRequiredRead = beforeRead; ++_guidanceRevision;
            try { if (enqueue(command)) return true; }
            catch { HoldGuidanceCore(GuidancePhase.Holding); throw; }
            HoldGuidanceCore(GuidancePhase.Holding); return false;
        }
    }
    // The SDK worker enters this fence again immediately before the actual
    // write. The lock order is model -> transport; a queued AP-on must not
    // survive a later disconnect or authority change on the host actor.
    public bool ExecuteQueuedGuidance(GuidanceCommand command, Func<bool> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_sync) {
            ExpireCore();
            if (!_connected || _ended || _guidancePending is not { Dispatched: true } pending || pending.Command != command ||
                !GuidanceProtocol.Supported(_guidanceSupported, command.Setting)) return false;
            if (pending.Emergency && pending.Sender != (_commander ?? _host) && (pending.Sender != Get(AtomicControlGroup.Steering).State.Owner ||
                command.SteeringGeneration != Get(AtomicControlGroup.Steering).State.Generation)) return false;
            return write();
        }
    }
    public bool ObserveGuidance(string sender, Guid session, ulong readSerial, GuidanceState state)
    {
        lock (_sync) {
            ExpireCore();
            if (sender != _host || !Member(sender) || !_connected || session != Session || !GuidanceProtocol.Valid(state) ||
                readSerial <= _guidanceHostRead || _guidancePhase is GuidancePhase.Disabled or GuidancePhase.Holding or GuidancePhase.Local or GuidancePhase.Ended) return false;
            AcceptGuidanceStateCore(readSerial, state);
            CompleteGuidancePreparationCore(); return true;
        }
    }
    public bool ConfirmGuidance(string sender, GuidanceCommand command, ulong serial, GuidanceState state, bool succeeded = true)
    {
        lock (_sync) {
            ExpireCore();
            if (sender != _host || !Member(sender) || !_connected || _guidancePending is not { Dispatched: true } pending || pending.Command != command) return false;
            if (!succeeded) { HoldGuidanceCore(GuidancePhase.Holding); return true; }
            if (!GuidanceProtocol.Valid(state) || serial <= _guidanceRequiredRead || serial < _guidanceHostRead ||
                serial == _guidanceHostRead && state != _guidanceState || !GuidanceProtocol.Matches(command, state)) return false;
            AcceptGuidanceStateCore(serial, state, true); CompleteGuidancePreparationCore(); return true;
        }
    }
    private void AcceptGuidanceStateCore(ulong serial, GuidanceState state, bool commandConfirmed = false) {
        _guidanceHostRead = serial; var changed = _guidanceState != state; _guidanceState = state;
        if (_guidanceTransfer is { } transfer && (transfer.Selection & GuidanceSelection.Navigation) != 0 && state.Master) {
            _guidanceNavigationUncertain = true; HoldGuidanceCore(GuidancePhase.Holding); return;
        }
        if (commandConfirmed && _guidancePending is { Dispatched: true } pending && serial > _guidanceRequiredRead && GuidanceProtocol.Matches(pending.Command, state)) {
            _guidancePending = null; changed = true;
        }
        if (changed) ++_guidanceRevision;
    }
    private bool NavigationWasAppliedCore() => GuidanceGates[0].Ready && GuidanceGates[0].Proof is { NavigationApplied: true } proof &&
        proof.Preparation == _guidancePreparation;
    private readonly Dictionary<string, ulong> _guidanceFailures = new();
    public bool FailGuidance(string sender, GuidanceFailure failure)
    {
        lock (_sync) {
            if (!Member(sender) || !_connected || failure.Session != Session || failure.Preparation != _guidancePreparation ||
                failure.CommandEpoch != _commandEpoch || failure.NavigationEpoch != _navigationEpoch || failure.Sequence <= _guidanceFailures.GetValueOrDefault(sender) ||
                _guidancePhase is not (GuidancePhase.Preparing or GuidancePhase.Attaching or GuidancePhase.Active or GuidancePhase.Quiescing)) return false;
            _guidanceFailures[sender] = failure.Sequence; HoldGuidanceCore(GuidancePhase.Holding); return true;
        }
    }
    public bool StopGuidance(string sender, Guid session, ulong sequence)
    {
        lock (_sync) {
            if (!Member(sender) || !_connected || session != Session || sequence == 0 || sequence <= _guidanceStops.GetValueOrDefault(sender) ||
                _guidancePhase == GuidancePhase.Local) return false;
            _guidanceStops[sender] = sequence;
            HoldGuidanceCore(GuidancePhase.Local); return true;
        }
    }
    private void HoldGuidanceCore(GuidancePhase phase) {
        _guidancePhase = phase; _guidancePending = null; _guidanceTransfer = null;
        foreach (var gate in GuidanceGates) gate.Ready = false;
        ++_guidanceRevision;
    }
    private void ExpireGuidanceCore() {
        if (_guidancePhase is not (GuidancePhase.Preparing or GuidancePhase.Attaching or GuidancePhase.Active or GuidancePhase.Quiescing)) return;
        var now = _clock();
        if (_guidancePending is not null && now >= _guidanceCommandDeadline ||
            _guidancePhase != GuidancePhase.Active && now >= _guidanceDeadline ||
            GuidanceGates.Any(gate => gate.Ready && now - gate.At > TimeSpan.FromSeconds(1))) HoldGuidanceCore(GuidancePhase.Holding);
    }
}
