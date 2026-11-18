using System.Text.Json;
using System.Text.Json.Serialization;

namespace FsCopilot.Ownership;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AuthorityRequest), "request")]
[JsonDerivedType(typeof(AuthorityPreparation), "prepared")]
[JsonDerivedType(typeof(AuthorityAttachment), "attachment")]
[JsonDerivedType(typeof(AuthorityCancellation), "cancel")]
[JsonDerivedType(typeof(AuthorityInput), "input")]
[JsonDerivedType(typeof(AuthorityState), "state")]
[JsonDerivedType(typeof(AuthorityAlive), "alive")]
[JsonDerivedType(typeof(AuthorityAutomatic), "automatic")]
[JsonDerivedType(typeof(AuthoritySeatChange), "seat")]
[JsonDerivedType(typeof(AuthorityAltimeterChange), "altimeter")]
[JsonDerivedType(typeof(AuthorityAltimeterGate), "altimeter-gate")]
[JsonDerivedType(typeof(AuthorityAltimeterConfirmation), "altimeter-confirmed")]
[JsonDerivedType(typeof(AuthorityAltimeterState), "altimeter-state")]
[JsonDerivedType(typeof(AuthorityAltimeterAvailability), "altimeter-availability")]
[JsonDerivedType(typeof(AuthorityAltimeterStop), "altimeter-stop")]
[JsonDerivedType(typeof(AuthorityGuidanceClaim), "guidance-claim")]
[JsonDerivedType(typeof(AuthorityGuidanceCommand), "guidance-command")]
[JsonDerivedType(typeof(AuthorityGuidanceGate), "guidance-gate")]
[JsonDerivedType(typeof(AuthorityGuidanceCancellation), "guidance-cancel")]
[JsonDerivedType(typeof(AuthorityGuidanceStop), "guidance-stop")]
[JsonDerivedType(typeof(AuthorityGuidanceState), "guidance-state")]
[JsonDerivedType(typeof(AuthorityGuidanceAvailability), "guidance-availability")]
[JsonDerivedType(typeof(AuthorityGuidanceReceipt), "guidance-receipt")]
[JsonDerivedType(typeof(AuthorityGuidanceFailure), "guidance-failure")]
public abstract record AuthorityMessage(int Version);
public sealed record AuthorityRequest(int Version, AtomicTransferRequest Request) : AuthorityMessage(Version);
public sealed record AuthorityPreparation(int Version, AtomicPreparationAck Ack) : AuthorityMessage(Version);
public sealed record AuthorityAttachment(int Version, AtomicAttachmentAck Ack) : AuthorityMessage(Version);
public sealed record AuthorityCancellation(int Version, AtomicCancellation Cancellation) : AuthorityMessage(Version);
public sealed record AuthorityInput(int Version, AtomicControlFrame Frame) : AuthorityMessage(Version);
public sealed record AuthorityState(int Version, ControlAuthoritySnapshot State) : AuthorityMessage(Version);
public sealed record AuthorityAlive(int Version, Guid Session, ulong Sequence) : AuthorityMessage(Version);
public sealed record AuthorityAutomatic(int Version, Guid Session, ulong Sequence, bool Enabled) : AuthorityMessage(Version);
public sealed record AuthoritySeatChange(int Version, SeatChange Change) : AuthorityMessage(Version);
public sealed record AuthorityAltimeterChange(int Version, AltimeterChange Change) : AuthorityMessage(Version);
public sealed record AuthorityAltimeterGate(int Version, AltimeterGateAck Ack) : AuthorityMessage(Version);
public sealed record AuthorityAltimeterConfirmation(int Version, AltimeterConfirmation Confirmation) : AuthorityMessage(Version);
public sealed record AuthorityAltimeterState(int Version, AltimeterAuthoritySnapshot State) : AuthorityMessage(Version);
public sealed record AuthorityAltimeterAvailability(int Version, AltimeterAvailability Availability) : AuthorityMessage(Version);
public sealed record AuthorityAltimeterStop(int Version, Guid Session, ulong Sequence) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceClaim(int Version, GuidanceClaim Claim) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceCommand(int Version, GuidanceCommand Command) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceGate(int Version, GuidanceGateProof Proof) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceCancellation(int Version, Guid Session, Guid Ticket) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceStop(int Version, Guid Session, ulong Sequence) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceState(int Version, FlightGuidanceSnapshot State) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceAvailability(int Version, GuidanceAvailability Availability) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceReceipt(int Version, GuidanceReceipt Receipt) : AuthorityMessage(Version);
public sealed record AuthorityGuidanceFailure(int Version, GuidanceFailure Failure) : AuthorityMessage(Version);

public static class AuthorityWire
{
    public const int MaxBytes = 8192;
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8
    };
    public static bool Valid(AuthorityMessage? message) => message?.Version == AtomicProtocol.Version && (message switch {
        AuthorityRequest { Request: { } r } => r.Session != Guid.Empty && r.Request != 0 && OwnershipProtocol.ValidPeer(r.NewOwner) &&
            (!r.Automatic || !r.Force && AtomicProtocol.EnginePair(r.Selection)) && AtomicProtocol.ValidEpochs(r.Selection, r.Expected),
        AuthorityPreparation { Ack: { } a } => a.Session != Guid.Empty && a.Transfer != Guid.Empty,
        AuthorityAttachment { Ack: { } a } => a.Session != Guid.Empty && AtomicProtocol.ValidEpochs(a.Selection, a.Expected),
        AuthorityCancellation { Cancellation: { } c } => c.Session != Guid.Empty && c.Transfer != Guid.Empty,
        AuthorityInput { Frame: { } f } => AtomicProtocol.ValidFrameShape(f),
        AuthorityState { State: { } s } => AtomicProtocol.ValidSnapshot(s),
        AuthorityAlive a => a.Session != Guid.Empty && a.Sequence != 0,
        AuthorityAutomatic a => a.Session != Guid.Empty && a.Sequence != 0,
        AuthoritySeatChange s => s.Change is { } c && c.Session != Guid.Empty && c.Sequence != 0 &&
            c.ExpectedRevision is > 0 and <= uint.MaxValue && Enum.IsDefined(c.Action) && c.Ticket <= uint.MaxValue &&
            (c.Action == SeatChangeAction.Request ? c.Ticket == 0 : c.Ticket != 0),
        AuthorityAltimeterChange a => AltimeterProtocol.Valid(a.Change),
        AuthorityAltimeterGate a => AltimeterProtocol.Valid(a.Ack),
        AuthorityAltimeterConfirmation a => AltimeterProtocol.Valid(a.Confirmation),
        AuthorityAltimeterState a => AltimeterProtocol.Valid(a.State),
        AuthorityAltimeterAvailability a => AltimeterProtocol.Valid(a.Availability),
        AuthorityAltimeterStop a => a.Session != Guid.Empty && a.Sequence != 0,
        AuthorityGuidanceClaim a => GuidanceProtocol.Valid(a.Claim),
        AuthorityGuidanceCommand a => GuidanceProtocol.Valid(a.Command),
        AuthorityGuidanceGate a => GuidanceProtocol.Valid(a.Proof),
        AuthorityGuidanceCancellation a => a.Session != Guid.Empty && a.Ticket != Guid.Empty,
        AuthorityGuidanceStop a => a.Session != Guid.Empty && a.Sequence != 0,
        AuthorityGuidanceState a => GuidanceProtocol.Valid(a.State),
        AuthorityGuidanceAvailability a => GuidanceProtocol.Valid(a.Availability),
        AuthorityGuidanceReceipt { Receipt: { } r } => OwnershipProtocol.ValidPeer(r.Peer) && GuidanceProtocol.Valid(r.Command) && r.Revision != 0,
        AuthorityGuidanceFailure { Failure: { } f } => f.Session != Guid.Empty && f.Preparation != Guid.Empty && f.CommandEpoch != 0 && f.NavigationEpoch != 0 && f.Sequence != 0,
        _ => false
    });
    public static byte[] Encode(AuthorityMessage message)
    {
        if (!Valid(message)) throw new ArgumentException("Invalid control authority message.", nameof(message));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Control authority message too large.", nameof(message));
        return bytes;
    }
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out AuthorityMessage? message)
    {
        message = null;
        if (bytes.IsEmpty || bytes.Length > MaxBytes) return false;
        try {
            var value = JsonSerializer.Deserialize<AuthorityMessage>(bytes, Options);
            if (!Valid(value)) return false;
            message = value; return true;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }
}

public sealed class ControlAuthorityEndpoint(ControlAuthoritySession session)
{
    private readonly object _sync = new();
    public ControlAuthoritySession Session => session;
    public bool Receive(string transportSender, ReadOnlySpan<byte> bytes, out ControlAuthoritySnapshot? changed, out AtomicControlFrame? accepted)
    {
        lock (_sync) {
            changed = null; accepted = null;
            if (!AuthorityWire.TryDecode(bytes, out var message)) return false;
            var revision = session.Snapshot.Revision;
            bool applied;
            switch (message) {
                case AuthorityRequest request: applied = session.RequestTransfer(transportSender, request.Request); break;
                case AuthorityPreparation preparation: applied = session.AcknowledgePreparation(transportSender, preparation.Ack); break;
                case AuthorityAttachment attachment: applied = session.AcknowledgeAttachment(transportSender, attachment.Ack); break;
                case AuthorityCancellation cancellation: applied = session.Cancel(transportSender, cancellation.Cancellation); break;
                case AuthorityAutomatic automatic: applied = session.SetAutomatic(transportSender, automatic.Session, automatic.Sequence, automatic.Enabled); break;
                case AuthoritySeatChange seat: applied = session.ChangeSeat(transportSender, seat.Change); break;
                case AuthorityAltimeterChange altimeter: applied = session.RequestAltimeter(transportSender, altimeter.Change); break;
                case AuthorityAltimeterGate altimeter: applied = session.AcknowledgeAltimeterGate(transportSender, altimeter.Ack); break;
                case AuthorityAltimeterConfirmation altimeter: applied = session.ConfirmAltimeter(transportSender, altimeter.Confirmation); break;
                case AuthorityAltimeterStop stop: applied = session.StopAltimeters(transportSender, stop.Session, stop.Sequence); break;
                case AuthorityGuidanceClaim guidance: applied = session.ClaimGuidance(transportSender, guidance.Claim); break;
                case AuthorityGuidanceCommand guidance: applied = session.RequestGuidanceCommand(transportSender, guidance.Command); break;
                case AuthorityGuidanceGate guidance: applied = session.AcknowledgeGuidance(transportSender, guidance.Proof); break;
                case AuthorityGuidanceCancellation guidance: applied = session.CancelGuidanceClaim(transportSender, guidance.Session, guidance.Ticket); break;
                case AuthorityGuidanceStop guidance: applied = session.StopGuidance(transportSender, guidance.Session, guidance.Sequence); break;
                case AuthorityGuidanceFailure guidance: applied = session.FailGuidance(transportSender, guidance.Failure); break;
                case AuthorityInput input: applied = session.AcceptInput(transportSender, input.Frame); if (applied) accepted = input.Frame; break;
                default: return false;
            }
            var snapshot = session.Snapshot;
            if (snapshot.Revision != revision) changed = snapshot;
            return applied;
        }
    }
}

public sealed class ControlAuthorityReplica
{
    private readonly object _sync = new();
    private readonly string _host;
    private ControlAuthoritySnapshot _state;
    public ControlAuthoritySnapshot Snapshot { get { lock (_sync) return Copy(_state); } }
    public ControlAuthorityReplica(string transportHost, ControlAuthoritySnapshot negotiated)
    {
        if (!AtomicProtocol.ValidSnapshot(negotiated) || negotiated.PhysicsHost != transportHost) throw new ArgumentException("Invalid negotiated authority.");
        _host = transportHost; _state = Copy(negotiated);
    }
    private static ControlAuthoritySnapshot Copy(ControlAuthoritySnapshot state) => state with {
        Groups = state.Groups.ToArray(), Transfers = state.Transfers.Select(transfer => transfer with { Next = transfer.Next.ToArray() }).ToArray()
    };
    public bool Receive(string transportSender, ControlAuthoritySnapshot state)
    {
        lock (_sync) {
            if (transportSender != _host || !AtomicProtocol.ValidSnapshot(state) || state.Session != _state.Session || state.PhysicsHost != _host ||
                state.Peer != _state.Peer || state.Engines != _state.Engines || state.SteeringAxes != _state.SteeringAxes || state.Revision <= _state.Revision ||
                _state.Ended || !_state.PeerConnected && state.PeerConnected) return false;
            if (state.SeatRevision < _state.SeatRevision || state.SeatRevision == _state.SeatRevision &&
                (state.Seats != _state.Seats || state.SeatChange != _state.SeatChange)) return false;
            foreach (var current in _state.Groups) {
                var next = state.Groups.Single(group => group.Group == current.Group);
                if (next.Generation < current.Generation || next.Generation == current.Generation &&
                    (next.Owner != current.Owner || next.CommittedTransfer != current.CommittedTransfer)) return false;
            }
            _state = Copy(state); return true;
        }
    }
}
