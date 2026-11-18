namespace FsCopilot.Ownership;

public readonly record struct AltimeterSetting(uint Pressure16, bool Standard);
public enum AltimeterPhase { Disabled, Synchronizing, Active, Quiescing, Holding, Unsupported, Ended, Attaching, Local }
public sealed record AltimeterChange(Guid Session, ulong Epoch, ulong Preparation, ulong Sequence, uint Instrument, AltimeterSetting Setting);
public sealed record AltimeterAvailability(Guid Session, ulong Sequence, bool Supported, string Adapter, int Protocol = AltimeterProtocol.Version);
public sealed record AltimeterGateAck(Guid Session, ulong Epoch, ulong Preparation, ulong Sequence, bool Supported,
    string Adapter, uint Blocked, ulong ReadSerial, AltimeterSetting[] Settings);
public sealed record AltimeterConfirmation(AltimeterChange Change, ulong ReadSerial, bool Succeeded, AltimeterSetting? Observed);
public sealed record AltimeterInstrument(uint Index, string Owner, AltimeterSetting? Confirmed, AltimeterChange? Pending, bool Dispatched);
public sealed record AltimeterGate(string Peer, uint Blocked, bool Ready, string Adapter);
public sealed record AltimeterAuthoritySnapshot(int Version, Guid Session, string PhysicsHost, string Peer, ulong Epoch, ulong Preparation,
    ulong Revision, CockpitSeats Seats, AltimeterPhase Phase, AltimeterInstrument[] Instruments, AltimeterGate[] Gates);

public static class AltimeterProtocol
{
    public const int Version = 3;
    public static bool Valid(AltimeterSetting value) => value.Pressure16 is >= 8000 and <= 19200;
    public static bool Values(AltimeterSetting[]? values) => values is { Length: 2 } && values.All(Valid);
    public static bool Adapter(string? value) => value is { Length: > 0 and <= 63 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.');
    public static bool Valid(AltimeterChange? value) => value is not null && value.Session != Guid.Empty &&
        value.Epoch != 0 && value.Preparation != 0 && value.Sequence != 0 && value.Instrument is 1 or 2 && Valid(value.Setting);
    public static bool Valid(AltimeterAvailability? value) => value is not null && value.Protocol == Version && value.Session != Guid.Empty && value.Sequence != 0 &&
        (value.Supported ? Adapter(value.Adapter) : value.Adapter == "");
    public static bool Valid(AltimeterGateAck? value) => value is not null && value.Session != Guid.Empty && value.Epoch != 0 &&
        value.Preparation != 0 && value.Sequence != 0 && (value.Supported ? Adapter(value.Adapter) && value.Blocked <= 3 && value.ReadSerial != 0 && Values(value.Settings) :
            value.Adapter == "" && value.Blocked == 0 && value.ReadSerial == 0 && value.Settings is { Length: 0 });
    public static bool Valid(AltimeterConfirmation? value) => value is not null && Valid(value.Change) &&
        (value.Succeeded ? value.ReadSerial != 0 && value.Observed is { } observed && Valid(observed) : value.Observed is null || Valid(value.Observed.Value));
    public static string Owner(CockpitSeats seats, uint instrument) => instrument == 1 ? seats.PilotPeer : seats.CopilotPeer;
    public static uint Blocked(CockpitSeats seats, string local) => seats.PilotPeer == local ? 2u : 1u;
    public static bool Valid(AltimeterAuthoritySnapshot? value)
    {
        if (value is null || value.Version != Version || value.Session == Guid.Empty || value.Epoch == 0 || value.Revision == 0 ||
            !OwnershipProtocol.ValidPeer(value.PhysicsHost) || !OwnershipProtocol.ValidPeer(value.Peer) || value.PhysicsHost == value.Peer ||
            !Enum.IsDefined(value.Phase) || value.Seats is null || value.Seats.PilotPeer == value.Seats.CopilotPeer ||
            value.Instruments is not { Length: 2 } || value.Gates is not { Length: 2 }) return false;
        bool Member(string? peer) => peer == value.PhysicsHost || peer == value.Peer;
        if (!Member(value.Seats.PilotPeer) || !Member(value.Seats.CopilotPeer)) return false;
        if (value.Phase is not (AltimeterPhase.Disabled or AltimeterPhase.Local or AltimeterPhase.Ended or AltimeterPhase.Holding) && value.Preparation == 0) return false;
        var seeded = value.Instruments.All(instrument => instrument?.Confirmed is { } setting && Valid(setting));
        if (value.Phase is AltimeterPhase.Synchronizing or AltimeterPhase.Attaching or AltimeterPhase.Active or AltimeterPhase.Quiescing or AltimeterPhase.Unsupported && !seeded) return false;
        if (!seeded && value.Instruments.Any(instrument => instrument?.Confirmed is not null)) return false;
        if (seeded && value.Phase == AltimeterPhase.Disabled) return false;
        for (var index = 0; index < 2; ++index) {
            var instrument = value.Instruments[index];
            if (instrument is null || instrument.Index != index + 1 || instrument.Owner != Owner(value.Seats, instrument.Index) ||
                instrument.Pending is { } pending && (!Valid(pending) || pending.Session != value.Session || pending.Epoch != value.Epoch || pending.Instrument != instrument.Index ||
                    pending.Preparation > value.Preparation || value.Phase is AltimeterPhase.Active or AltimeterPhase.Attaching && pending.Preparation != value.Preparation) ||
                instrument.Dispatched && instrument.Pending is null || instrument.Pending is not null &&
                value.Phase is not (AltimeterPhase.Active or AltimeterPhase.Attaching or AltimeterPhase.Quiescing or AltimeterPhase.Synchronizing)) return false;
            var gate = value.Gates[index];
            if (gate is null || gate.Adapter is null || gate.Peer != (index == 0 ? value.PhysicsHost : value.Peer) || gate.Blocked > 3 ||
                gate.Adapter.Length != 0 && !Adapter(gate.Adapter) || gate.Ready && !Adapter(gate.Adapter)) return false;
            if (gate.Ready && (value.Phase is AltimeterPhase.Disabled or AltimeterPhase.Local or AltimeterPhase.Holding or AltimeterPhase.Unsupported or AltimeterPhase.Ended ||
                gate.Blocked != (value.Phase is AltimeterPhase.Synchronizing or AltimeterPhase.Quiescing ? 3u : Blocked(value.Seats, gate.Peer)))) return false;
            if (value.Phase == AltimeterPhase.Active && (!gate.Ready || gate.Blocked != Blocked(value.Seats, gate.Peer))) return false;
        }
        return !value.Gates.All(gate => gate.Ready) || value.Gates[0].Adapter == value.Gates[1].Adapter;
    }
}
