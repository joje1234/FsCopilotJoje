namespace FsCopilot.Ownership;

public enum ControlGroup { Steering, Quadrant }

public enum ControlAxis
{
    Roll, Pitch, Rudder, Nosewheel, LeftBrake, RightBrake, ElevatorTrim, AileronTrim, RudderTrim,
    Throttle1, Propeller1, Mixture1, Throttle2, Propeller2, Mixture2,
    Throttle3, Propeller3, Mixture3, Throttle4, Propeller4, Mixture4
}

public sealed record TransferRequest(Guid Session, ulong Request, ControlGroup Group, ulong Generation, string NewOwner);
public sealed record TransferPreparation(Guid Transfer, ulong Request, string RequestedBy, string NewOwner, ulong Generation);
public sealed record PreparationAck(Guid Session, ControlGroup Group, Guid Transfer, bool InputSuppressed, bool Aligned);
public sealed record AttachmentAck(Guid Session, ControlGroup Group, ulong Generation, bool Applied);
public sealed record TransferCancellation(Guid Session, ControlGroup Group, Guid Transfer);
public sealed record GroupState(ControlGroup Group, string? Owner, ulong Generation, bool Attached, TransferPreparation? Pending);
public sealed record OwnershipSnapshot(int Version, Guid Session, string PhysicsHost, string Copilot, int Engines,
    ulong Revision, GroupState Steering, GroupState Quadrant, bool CopilotConnected);
public readonly record struct AxisPosition(ControlAxis Axis, double Position);
public sealed record ControlFrame(Guid Session, ControlGroup Group, ulong Generation, ulong Sequence, AxisPosition[] Axes);

public static class OwnershipProtocol
{
    public const int Version = 1;
    public static bool ValidGroup(ControlGroup group) => group is ControlGroup.Steering or ControlGroup.Quadrant;
    public static bool ValidPeer(string? peer) => peer is { Length: > 0 and <= 64 } &&
        peer.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    public static bool ValidAxes(ControlFrame frame, int engines)
    {
        if (!ValidGroup(frame.Group) || frame.Axes is not { Length: > 0 and <= 12 }) return false;
        var seen = new HashSet<ControlAxis>();
        foreach (var input in frame.Axes)
        {
            var axis = (int)input.Axis;
            if (axis < 0 || axis >= 9 + 3 * engines || !seen.Add(input.Axis) ||
                !double.IsFinite(input.Position) || input.Position < 0 || input.Position > 1 ||
                (axis < 9 ? ControlGroup.Steering : ControlGroup.Quadrant) != frame.Group) return false;
        }
        return true;
    }

    public static bool ValidSnapshot(OwnershipSnapshot? state)
    {
        if (state is null || state.Version != Version || state.Session == Guid.Empty || state.Revision == 0 ||
            !ValidPeer(state.PhysicsHost) || !ValidPeer(state.Copilot) || state.PhysicsHost == state.Copilot ||
            state.Engines is < 1 or > 4) return false;
        bool Member(string? peer) => peer == state.PhysicsHost || peer == state.Copilot;
        bool Valid(GroupState? group, ControlGroup expected)
        {
            if (group is null || group.Group != expected || group.Generation == 0 ||
                (group.Owner is not null && !Member(group.Owner)) ||
                (group.Attached && group.Owner is null) ||
                (!state.CopilotConnected && group.Owner == state.Copilot)) return false;
            var pending = group.Pending;
            return pending is null || state.CopilotConnected && !group.Attached &&
                pending.Transfer != Guid.Empty && pending.Request != 0 && Member(pending.RequestedBy) &&
                Member(pending.NewOwner) && pending.NewOwner != group.Owner &&
                group.Generation != ulong.MaxValue && pending.Generation == group.Generation + 1;
        }
        return Valid(state.Steering, ControlGroup.Steering) && Valid(state.Quadrant, ControlGroup.Quadrant);
    }
}
