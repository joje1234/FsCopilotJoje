namespace FsCopilot.Ownership;

public enum AtomicControlGroup { Steering, Throttle, Propeller, Mixture }
[Flags]
public enum ControlSelection { None = 0, Steering = 1, Throttle = 2, Propeller = 4, Mixture = 8, Quadrant = 14, All = 15 }
public readonly record struct GroupEpoch(AtomicControlGroup Group, ulong Generation);
public enum SeatChangeAction { Request = 1, Accept, Decline, Cancel }
public sealed record SeatChange(Guid Session, ulong Sequence, ulong ExpectedRevision, SeatChangeAction Action, ulong Ticket);
public sealed record PendingSeatChange(ulong Ticket, string RequestedBy) { public bool Accepted { get; init; } }
public sealed record AtomicTransferRequest(Guid Session, ulong Request, ControlSelection Selection, GroupEpoch[] Expected, string NewOwner)
{
    public bool Force { get; init; }
    public bool Automatic { get; init; }
}
public sealed record AtomicPreparationAck(Guid Session, Guid Transfer, bool InputSuppressed, bool Aligned);
public sealed record AtomicAttachmentAck(Guid Session, ControlSelection Selection, GroupEpoch[] Expected, bool Applied);
public sealed record AtomicCancellation(Guid Session, Guid Transfer);
public sealed record AtomicControlFrame(Guid Session, AtomicControlGroup Group, ulong Generation, ulong Sequence, AxisPosition[] Axes);
public sealed record AtomicGroupState(AtomicControlGroup Group, string? Owner, ulong Generation, bool Attached, Guid? Pending)
{
    // Cancellation also advances a generation. Preserve the committing ticket
    // explicitly so native attachment cannot mistake cancellation for commit.
    public Guid? CommittedTransfer { get; init; }
}
public sealed record AtomicTransfer(Guid Transfer, ulong Request, string RequestedBy, string NewOwner, ControlSelection Selection, GroupEpoch[] Next)
{
    public bool Force { get; init; }
    public bool Automatic { get; init; }
}
public sealed record ControlAuthoritySnapshot(int Version, Guid Session, string PhysicsHost, string Peer, int Engines, uint SteeringAxes,
    ulong Revision, bool PeerConnected, bool Ended, AtomicGroupState[] Groups, AtomicTransfer[] Transfers)
{
    public bool Automatic { get; init; }
    public CockpitSeats Seats { get; init; } = new(PhysicsHost, Peer);
    public ulong SeatRevision { get; init; } = 1;
    public PendingSeatChange? SeatChange { get; init; }
}

public static class AtomicProtocol
{
    public const int Version = 5;
    public static bool EnginePair(ControlSelection selection) => selection is ControlSelection.Throttle or ControlSelection.Propeller or ControlSelection.Mixture;
    public const uint DefaultSteeringAxes = 3; // Roll/pitch; caller includes configured pedal/trim axes.
    public static bool ValidGroup(AtomicControlGroup group) => group is >= AtomicControlGroup.Steering and <= AtomicControlGroup.Mixture;
    public static bool ValidSelection(ControlSelection selection) => selection != ControlSelection.None && (selection & ~ControlSelection.All) == 0;
    public static ControlSelection Select(AtomicControlGroup group) => ValidGroup(group) ? (ControlSelection)(1 << (int)group) : ControlSelection.None;
    public static IEnumerable<AtomicControlGroup> Members(ControlSelection selection) => Enum.GetValues<AtomicControlGroup>().Where(group => (selection & Select(group)) != 0);
    public static bool ValidSteeringAxes(uint axes) => (axes & DefaultSteeringAxes) == DefaultSteeringAxes && (axes >> 9) == 0;
    public static uint RequiredAxes(AtomicControlGroup group, int engines, uint steeringAxes)
    {
        if (!ValidGroup(group) || engines is < 1 or > 4 || !ValidSteeringAxes(steeringAxes)) return 0;
        if (group == AtomicControlGroup.Steering) return steeringAxes;
        uint mask = 0;
        for (var engine = 0; engine < engines; ++engine) mask |= 1u << (9 + engine * 3 + (int)group - 1);
        return mask;
    }
    public static bool AxisBelongs(ControlAxis axis, AtomicControlGroup group, int engines)
    {
        var number = (int)axis;
        return ValidGroup(group) && number >= 0 && number < 9 + 3 * engines &&
            (number < 9 ? AtomicControlGroup.Steering : (AtomicControlGroup)(1 + (number - 9) % 3)) == group;
    }
    public static bool ValidFrameShape(AtomicControlFrame? frame)
    {
        if (frame is null || frame.Session == Guid.Empty || frame.Generation == 0 || frame.Sequence == 0 ||
            !ValidGroup(frame.Group) || frame.Axes is not { Length: > 0 and <= 9 }) return false;
        uint seen = 0;
        foreach (var input in frame.Axes) {
            if (!AxisBelongs(input.Axis, frame.Group, 4) || !double.IsFinite(input.Position) || input.Position < 0 || input.Position > 1) return false;
            var bit = 1u << (int)input.Axis;
            if ((seen & bit) != 0) return false;
            seen |= bit;
        }
        return true;
    }
    public static bool CompleteFrame(AtomicControlFrame frame, int engines, uint steeringAxes)
    {
        if (!ValidFrameShape(frame)) return false;
        uint mask = 0; foreach (var input in frame.Axes) mask |= 1u << (int)input.Axis;
        return mask == RequiredAxes(frame.Group, engines, steeringAxes);
    }
    public static bool ValidEpochs(ControlSelection selection, GroupEpoch[]? epochs)
    {
        if (!ValidSelection(selection) || epochs is null || epochs.Length != Members(selection).Count()) return false;
        var seen = ControlSelection.None;
        foreach (var epoch in epochs) {
            var bit = Select(epoch.Group);
            if (bit == ControlSelection.None || (selection & bit) == 0 || (seen & bit) != 0 || epoch.Generation == 0) return false;
            seen |= bit;
        }
        return seen == selection;
    }
    public static bool ValidSnapshot(ControlAuthoritySnapshot? state)
    {
        if (state is null || state.Version != Version || state.Session == Guid.Empty || state.Revision == 0 ||
            !OwnershipProtocol.ValidPeer(state.PhysicsHost) || !OwnershipProtocol.ValidPeer(state.Peer) || state.PhysicsHost == state.Peer ||
            state.Engines is < 1 or > 4 || !ValidSteeringAxes(state.SteeringAxes) || state.Groups is not { Length: 4 } ||
            state.Transfers is not { Length: <= 4 } || state.Groups.Any(group => group is null || !ValidGroup(group.Group)) ||
            state.Groups.Select(group => group.Group).Distinct().Count() != 4 || state.Ended && state.PeerConnected) return false;
        bool Member(string? peer) => peer == state.PhysicsHost || peer == state.Peer;
        if (state.Seats is null || !Member(state.Seats.PilotPeer) || !Member(state.Seats.CopilotPeer) ||
            state.Seats.PilotPeer == state.Seats.CopilotPeer || state.SeatRevision is 0 or > uint.MaxValue ||
            state.SeatChange is { } seat && (seat.Ticket is 0 or > uint.MaxValue || !Member(seat.RequestedBy) || !state.PeerConnected || state.Ended)) return false;
        foreach (var group in state.Groups) {
            if (group.Generation == 0 || group.CommittedTransfer == Guid.Empty || group.Owner is not null && !Member(group.Owner) || group.Attached &&
                (group.Owner is null || group.Pending is not null || state.Ended) || !state.PeerConnected && group.Owner == state.Peer) return false;
            if (group.Pending is not null && (!state.PeerConnected || state.Ended || group.Pending == Guid.Empty ||
                state.Transfers.Count(transfer => transfer is not null && transfer.Transfer == group.Pending) != 1)) return false;
        }
        var selected = ControlSelection.None;
        foreach (var transfer in state.Transfers) {
            if (transfer is null || transfer.Transfer == Guid.Empty || transfer.Request == 0 || !Member(transfer.RequestedBy) ||
                !Member(transfer.NewOwner) || transfer.Force && transfer.RequestedBy != transfer.NewOwner ||
                transfer.Automatic && (!state.Automatic || transfer.Force || transfer.RequestedBy != transfer.NewOwner || !EnginePair(transfer.Selection)) ||
                !ValidEpochs(transfer.Selection, transfer.Next) || (selected & transfer.Selection) != 0) return false;
            selected |= transfer.Selection;
            foreach (var next in transfer.Next) {
                var group = state.Groups.Single(value => value.Group == next.Group);
                if (group.Pending != transfer.Transfer || group.Attached || group.Generation == ulong.MaxValue || next.Generation != group.Generation + 1) return false;
            }
        }
        return state.PeerConnected && !state.Ended || state.Transfers.Length == 0;
    }
}
