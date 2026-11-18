namespace FsCopilot.Ownership;

[Flags] public enum GuidanceSelection { None = 0, Commands = 1, Navigation = 2, Both = 3 }
[Flags] public enum GuidanceCapabilities { None = 0, Commands = 1, NavigationTransfer = 2, FlightPlanExchange = 4 }
public enum GuidancePhase { Disabled, Preparing, Attaching, Active, Quiescing, Holding, Local, Ended }
public enum GuidanceSetting { Master, Heading, Altitude, VerticalSpeed, HeadingHold, AltitudeHold, VerticalHold, NavHold, ApproachHold, Backcourse, FlightLevelChange, YawDamper, FlightDirector }
[Flags] public enum GuidanceModes : uint { None = 0, Heading = 1, Altitude = 2, VerticalSpeed = 4, NavArmed = 8, NavCaptured = 16,
    ApproachArmed = 32, Localizer = 64, Glideslope = 128, Backcourse = 256, FlightLevelChange = 512, YawDamper = 1024, FlightDirector = 2048 }
public sealed record GuidanceState(bool Master, GuidanceModes Modes, double Heading, double Altitude, double VerticalSpeed,
    bool Gps, uint NavReceiver, bool NavigationReady, string NavigationFingerprint);
public sealed record GuidanceCommand(Guid Session, Guid Preparation, ulong CommandEpoch, ulong NavigationEpoch, ulong Sequence,
    GuidanceSetting Setting, double Value, ulong SteeringGeneration = 0);
public sealed record GuidanceClaim(Guid Session, ulong Sequence, GuidanceSelection Selection, string NewOwner,
    ulong CommandEpoch, ulong NavigationEpoch, string NavigationFingerprint = "");
public sealed record GuidanceTransfer(Guid Ticket, string RequestedBy, string NewOwner, GuidanceSelection Selection, string NavigationFingerprint);
public sealed record GuidanceGateProof(Guid Session, Guid Preparation, ulong CommandEpoch, ulong NavigationEpoch, ulong Sequence,
    string Adapter, GuidanceCapabilities Capabilities, bool InputSuppressed, bool OwnerCaptureEnabled, bool SingleHostEngine,
    ulong ReadSerial, GuidanceState State, bool NavigationApplied, uint SupportedSettings = 0);
public sealed record GuidancePendingCommand(GuidanceCommand Command, string Sender, bool Dispatched, bool Emergency);
public sealed record FlightGuidanceSnapshot(int Version, Guid Session, string PhysicsHost, string Peer, ulong Revision,
    ulong CommandEpoch, ulong NavigationEpoch, Guid Preparation, GuidancePhase Phase, string Commander, string Navigator,
    GuidanceCapabilities Capabilities, GuidanceState? Confirmed, GuidancePendingCommand? Pending, GuidanceTransfer? Transfer, uint SupportedSettings = 0);
public sealed record GuidanceAvailability(Guid Session, ulong Sequence, bool Supported, string Adapter, uint SupportedSettings,
    int Protocol = GuidanceProtocol.Version);
public sealed record GuidanceReceipt(string Peer, GuidanceCommand Command, bool Accepted, ulong Revision);
public sealed record GuidanceFailure(Guid Session, Guid Preparation, ulong CommandEpoch, ulong NavigationEpoch, ulong Sequence);

public static class GuidanceProtocol
{
    public const int Version = 2;
    public const uint AllSettings = 8191;
    public static bool Supported(uint mask, GuidanceSetting setting) => Enum.IsDefined(setting) && (mask & (1u << (int)setting)) != 0;
    public static bool Valid(GuidanceAvailability? value) => value is not null && value.Session != Guid.Empty && value.Sequence != 0 && value.Protocol == Version &&
        (value.SupportedSettings & ~AllSettings) == 0 && (value.Supported ? AltimeterProtocol.Adapter(value.Adapter) && Supported(value.SupportedSettings, GuidanceSetting.Master) :
            value.Adapter == "" && value.SupportedSettings == 0);
    public static bool Selection(GuidanceSelection value) => value is > GuidanceSelection.None and <= GuidanceSelection.Both;
    public static bool Fingerprint(string? value, bool optional = false) => value is not null && (optional && value.Length == 0 ||
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));
    public static bool Valid(GuidanceState? value) => value is not null && ((uint)value.Modes & ~4095u) == 0 &&
        double.IsFinite(value.Heading) && value.Heading is >= 0 and < 360 && double.IsFinite(value.Altitude) &&
        value.Altitude is >= -2000 and <= 100000 && double.IsFinite(value.VerticalSpeed) && value.VerticalSpeed is >= -12000 and <= 12000 &&
        value.NavReceiver <= 2 && Fingerprint(value.NavigationFingerprint, !value.NavigationReady);
    public static bool Value(GuidanceSetting setting, double value) => Enum.IsDefined(setting) && double.IsFinite(value) &&
        (setting switch {
            GuidanceSetting.Heading => value is >= 0 and < 360,
            GuidanceSetting.Altitude => value is >= -2000 and <= 100000,
            GuidanceSetting.VerticalSpeed => value is >= -12000 and <= 12000,
            _ => value is 0 or 1
        });
    public static bool Valid(GuidanceCommand? value) => value is not null && value.Session != Guid.Empty && value.Preparation != Guid.Empty &&
        value.CommandEpoch != 0 && value.NavigationEpoch != 0 && value.Sequence != 0 && Value(value.Setting, value.Value) &&
        (value.SteeringGeneration == 0 || value.Setting == GuidanceSetting.Master && value.Value == 0);
    public static bool Valid(GuidanceClaim? value) => value is not null && value.Session != Guid.Empty && value.Sequence != 0 &&
        Selection(value.Selection) && OwnershipProtocol.ValidPeer(value.NewOwner) && value.CommandEpoch != 0 && value.NavigationEpoch != 0 &&
        Fingerprint(value.NavigationFingerprint, (value.Selection & GuidanceSelection.Navigation) == 0);
    public static bool Valid(GuidanceGateProof? value) => value is not null && value.Session != Guid.Empty && value.Preparation != Guid.Empty &&
        value.CommandEpoch != 0 && value.NavigationEpoch != 0 && value.Sequence != 0 && value.ReadSerial != 0 &&
        AltimeterProtocol.Adapter(value.Adapter) && ((uint)value.Capabilities & ~7u) == 0 && (value.SupportedSettings & ~AllSettings) == 0 && Valid(value.State);
    public static bool Matches(GuidanceCommand command, GuidanceState state) => command.Setting switch {
        GuidanceSetting.Master => state.Master == (command.Value != 0),
        GuidanceSetting.Heading => Math.Min(Math.Abs(state.Heading - command.Value), 360 - Math.Abs(state.Heading - command.Value)) <= .012,
        GuidanceSetting.Altitude => Math.Abs(state.Altitude - command.Value) <= 1,
        GuidanceSetting.VerticalSpeed => Math.Abs(state.VerticalSpeed - command.Value) <= 1,
        _ => HasMode(state.Modes, command.Setting) == (command.Value != 0)
    };
    private static bool HasMode(GuidanceModes modes, GuidanceSetting setting) => (modes & (setting switch {
        GuidanceSetting.HeadingHold => GuidanceModes.Heading, GuidanceSetting.AltitudeHold => GuidanceModes.Altitude,
        GuidanceSetting.VerticalHold => GuidanceModes.VerticalSpeed, GuidanceSetting.NavHold => GuidanceModes.NavArmed | GuidanceModes.NavCaptured,
        GuidanceSetting.ApproachHold => GuidanceModes.ApproachArmed | GuidanceModes.Localizer | GuidanceModes.Glideslope,
        GuidanceSetting.Backcourse => GuidanceModes.Backcourse, GuidanceSetting.FlightLevelChange => GuidanceModes.FlightLevelChange,
        GuidanceSetting.YawDamper => GuidanceModes.YawDamper, GuidanceSetting.FlightDirector => GuidanceModes.FlightDirector, _ => GuidanceModes.None
    })) != 0;
    public static bool Valid(FlightGuidanceSnapshot? value)
    {
        if (value is null || value.Version != Version || value.Session == Guid.Empty || value.Revision == 0 || value.CommandEpoch == 0 ||
            value.NavigationEpoch == 0 || !OwnershipProtocol.ValidPeer(value.PhysicsHost) || !OwnershipProtocol.ValidPeer(value.Peer) ||
            value.PhysicsHost == value.Peer || !Enum.IsDefined(value.Phase) || ((uint)value.Capabilities & ~7u) != 0 || (value.SupportedSettings & ~AllSettings) != 0 ||
            value.Phase == GuidancePhase.Active && (!value.Capabilities.HasFlag(GuidanceCapabilities.Commands) || !Supported(value.SupportedSettings, GuidanceSetting.Master))) return false;
        bool Member(string who) => who == value.PhysicsHost || who == value.Peer;
        if (!Member(value.Commander) || !Member(value.Navigator) || value.Confirmed is not null && !Valid(value.Confirmed) ||
            value.Phase is GuidancePhase.Preparing or GuidancePhase.Attaching or GuidancePhase.Active or GuidancePhase.Quiescing &&
                (value.Confirmed is null || value.Preparation == Guid.Empty)) return false;
        if (value.Transfer is { } transfer && (value.Phase != GuidancePhase.Quiescing || transfer.Ticket != value.Preparation ||
            !Member(transfer.RequestedBy) || !Member(transfer.NewOwner) || !Selection(transfer.Selection) ||
            !Fingerprint(transfer.NavigationFingerprint, (transfer.Selection & GuidanceSelection.Navigation) == 0))) return false;
        if ((value.Phase == GuidancePhase.Quiescing) != (value.Transfer is not null)) return false;
        if (value.Pending is { } pending && (!Valid(pending.Command) || !Supported(value.SupportedSettings, pending.Command.Setting) || !Member(pending.Sender) || pending.Command.Session != value.Session ||
            pending.Command.Preparation != value.Preparation || pending.Command.CommandEpoch != value.CommandEpoch ||
            pending.Command.NavigationEpoch != value.NavigationEpoch || pending.Emergency &&
                (pending.Command.Setting != GuidanceSetting.Master || pending.Command.Value != 0) ||
            value.Phase is not (GuidancePhase.Preparing or GuidancePhase.Attaching or GuidancePhase.Active or GuidancePhase.Quiescing))) return false;
        return true;
    }
}
