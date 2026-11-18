using System.Buffers.Binary;
using System.Text;
using FsCopilot.Ownership;

namespace FsCopilot.Native;

public enum NativeGuidanceKind : uint { Command = 1, Status }
public enum NativeGuidanceAction : uint { Gate = 1, Apply, Observe, Stop, Poll, IntentAck, Reference }
[Flags] public enum NativeGuidanceFlags : uint { None = 0, Fresh = 1, Transport = 2, Profile = 4, GateReady = 8,
    Held = 16, Capture = 32, SingleHostEngine = 64, Pending = 128, LocalOnly = 256, NavigationReady = 512 }
public sealed record NativeGuidancePacket
{
    public NativeGuidanceKind Kind { get; init; } = NativeGuidanceKind.Command;
    public string Native { get; init; } = "";
    public string Backend { get; init; } = "";
    public string Session { get; init; } = "";
    public string Consent { get; init; } = "";
    public ulong Epoch { get; init; }
    public ulong ControlRevision { get; init; }
    public ulong Command { get; init; }
    public ulong CommandEpoch { get; init; }
    public ulong NavigationEpoch { get; init; }
    public ulong Preparation { get; init; }
    public ulong Revision { get; init; }
    public ulong Sequence { get; init; }
    public NativeGuidanceAction Action { get; init; } = NativeGuidanceAction.Gate;
    public GuidanceSetting Setting { get; init; }
    public double Value { get; init; }
    public bool Capture { get; init; }
    public bool Host { get; init; }
    public ulong ReadSerial { get; init; }
    public uint GateAck { get; init; }
    public uint ValueAck { get; init; }
    public ulong Serial { get; init; }
    public NativeGuidanceFlags Flags { get; init; }
    public uint Supported { get; init; }
    public uint Blocked { get; init; }
    public uint Slots { get; init; }
    public uint TargetSlots { get; init; }
    public uint Switches { get; init; }
    public double Heading { get; init; }
    public double Altitude { get; init; }
    public double VerticalSpeed { get; init; }
    public bool Gps { get; init; }
    public uint Nav { get; init; }
    public uint Error { get; init; }
    public ulong SteeringGeneration { get; init; }
    public NativeOwner SteeringOwner { get; init; }
    public string NavigationFingerprint { get; init; } = "";
    public string Adapter { get; init; } = "";
    public uint ReferenceAck { get; init; }
}
public static class NativeGuidanceWire
{
    public const int Size = 512;
    public const uint Magic = 0x31414746, Version = 1;
    public const string StatusName = "FSC_CONTROLS_GUIDANCE_STATUS_V1", CommandName = "FSC_CONTROLS_GUIDANCE_COMMAND_V1";
    private static bool Hex(string? value, int maximum, int exact = 0, bool optional = false) => value is not null &&
        (optional && value.Length == 0 || value.Length > 0 && value.Length <= maximum && (exact == 0 || value.Length == exact) &&
            value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9'));
    public static uint Slots(uint first, uint second, uint third) => first | second << 3 | third << 6;
    private static bool ValidSlots(uint value, uint maximum) => value >> 9 == 0 && (value & 7) <= maximum &&
        ((value >> 3) & 7) <= maximum && ((value >> 6) & 7) <= maximum;
    public static bool Valid(NativeGuidancePacket? p)
    {
        if (p is null || !Enum.IsDefined(p.Kind) || !Enum.IsDefined(p.Action) || !Hex(p.Native, 23) ||
            !Hex(p.Backend, 32, 32, p.Kind == NativeGuidanceKind.Status) ||
            !Hex(p.Session, 32, 32, p.Kind == NativeGuidanceKind.Status) ||
            !Hex(p.Consent, 32, 32, p.Kind == NativeGuidanceKind.Status) ||
            ((uint)p.Flags & ~1023u) != 0 || (p.Supported & ~8191u) != 0 || (p.Blocked & ~8191u) != 0 || (p.Switches & ~8191u) != 0 ||
            p.Nav > 2 || p.Error > 6 || !ValidSlots(p.Slots, 4) || !ValidSlots(p.TargetSlots, 3) ||
            !GuidanceProtocol.Value(p.Setting, p.Value) || !GuidanceProtocol.Value(GuidanceSetting.Heading, p.Heading) ||
            !GuidanceProtocol.Value(GuidanceSetting.Altitude, p.Altitude) || !GuidanceProtocol.Value(GuidanceSetting.VerticalSpeed, p.VerticalSpeed) ||
            !NativeBarometerAck.Valid(p.GateAck) || !NativeBarometerAck.Valid(p.ValueAck) || !NativeBarometerAck.Valid(p.ReferenceAck) ||
            NativeBarometerAck.Command(p.GateAck) > p.Command || NativeBarometerAck.Command(p.ValueAck) > p.Command ||
            NativeBarometerAck.Command(p.ReferenceAck) > p.Command || p.Command > NativeBarometerAck.MaxCommand) return false;
        if (p.Flags.HasFlag(NativeGuidanceFlags.Profile) ? !AltimeterProtocol.Adapter(p.Adapter) : p.Adapter != "") return false;
        if (p.Flags.HasFlag(NativeGuidanceFlags.NavigationReady) ? !p.Flags.HasFlag(NativeGuidanceFlags.GateReady) || !Hex(p.NavigationFingerprint, 64, 64) :
            p.NavigationFingerprint != "") return false;
        var bound = p.Backend.Length != 0 && p.Session.Length != 0 && p.Consent.Length != 0 && p.Epoch != 0 && p.ControlRevision != 0;
        if (!bound && (p.Backend.Length != 0 || p.Session.Length != 0 || p.Consent.Length != 0 || p.Epoch != 0 || p.ControlRevision != 0)) return false;
        var context = p.CommandEpoch != 0 && p.NavigationEpoch != 0 && p.Preparation != 0 && p.Revision != 0;
        if (!context && (p.CommandEpoch != 0 || p.NavigationEpoch != 0 || p.Preparation != 0 || p.Revision != 0)) return false;
        if (p.SteeringGeneration != 0 ? p.Setting != GuidanceSetting.Master || p.Value != 0 ||
            (p.Kind == NativeGuidanceKind.Status ? p.Sequence == 0 || p.SteeringOwner != NativeOwner.Local :
                p.Action != NativeGuidanceAction.Apply || p.SteeringOwner is not (NativeOwner.Local or NativeOwner.Remote)) : p.SteeringOwner != NativeOwner.None) return false;
        if (p.Kind == NativeGuidanceKind.Status) {
            if (p.Serial == 0 || p.Action != NativeGuidanceAction.Gate || p.Sequence == 0 && (p.Value != 0 || p.Setting != GuidanceSetting.Master)) return false;
            if (p.Sequence != 0 && (!p.Flags.HasFlag(NativeGuidanceFlags.GateReady) || !p.Flags.HasFlag(NativeGuidanceFlags.Capture) && p.SteeringGeneration == 0)) return false;
            if (p.Flags.HasFlag(NativeGuidanceFlags.Fresh) && (!p.Flags.HasFlag(NativeGuidanceFlags.Transport) || p.ReadSerial == 0)) return false;
            if (p.Flags.HasFlag(NativeGuidanceFlags.Profile) && (!p.Flags.HasFlag(NativeGuidanceFlags.Transport) || p.Supported == 0)) return false;
            if (p.Flags.HasFlag(NativeGuidanceFlags.GateReady) && (!bound || !context ||
                !p.Flags.HasFlag(NativeGuidanceFlags.Fresh | NativeGuidanceFlags.Profile | NativeGuidanceFlags.SingleHostEngine) || p.Blocked != 8191 ||
                (p.Flags & (NativeGuidanceFlags.Held | NativeGuidanceFlags.LocalOnly)) != 0)) return false;
            if (p.Flags.HasFlag(NativeGuidanceFlags.Capture) && (!p.Flags.HasFlag(NativeGuidanceFlags.GateReady) || !p.Capture)) return false;
            return p.ReadSerial != 0 || !p.Flags.HasFlag(NativeGuidanceFlags.Fresh) && p.Switches == 0 && p.Heading == 0 && p.Altitude == 0 &&
                p.VerticalSpeed == 0 && !p.Gps && p.Nav == 0 && p.Slots == 0;
        }
        if (!bound || p.Command == 0 || p.Action != NativeGuidanceAction.Poll && !context || p.Serial != 0 || p.Flags != 0 || p.Supported != 0 ||
            p.Blocked != 0 || p.Slots != 0 || p.TargetSlots != 0 || p.Error != 0 || p.GateAck != 0 || p.ValueAck != 0 || p.ReferenceAck != 0) return false;
        if (p.Action == NativeGuidanceAction.Reference) return p.Setting == GuidanceSetting.Master && p.Value == 0 && !p.Capture;
        if (p.Switches != 0 || p.Heading != 0 || p.Altitude != 0 || p.VerticalSpeed != 0 || p.Gps || p.Nav != 0) return false;
        if (p.Action == NativeGuidanceAction.IntentAck) return p.Sequence != 0 && p.Setting == GuidanceSetting.Master && p.Value == 0 && !p.Capture;
        return p.Action is NativeGuidanceAction.Apply or NativeGuidanceAction.Observe ? p.Sequence != 0 && !p.Capture :
            p.Sequence == 0 && p.Setting == GuidanceSetting.Master && p.Value == 0 && (p.Action == NativeGuidanceAction.Gate || !p.Capture);
    }
    public static byte[] Encode(NativeGuidancePacket p)
    {
        if (!Valid(p)) throw new ArgumentException("Invalid native guidance packet.", nameof(p));
        var bytes = new byte[Size];
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        void U64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
        void F64(int offset, double value) => BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(offset), value);
        void Text(int offset, string value) => Encoding.ASCII.GetBytes(value, bytes.AsSpan(offset, value.Length));
        U32(0, Magic); U32(4, Version); U32(8, (uint)p.Kind); U32(12, Size);
        Text(16, p.Native); Text(40, p.Backend); Text(80, p.Session); Text(120, p.Consent);
        U64(160, p.Epoch); U64(168, p.ControlRevision); U64(176, p.Command); U64(184, p.CommandEpoch);
        U64(192, p.NavigationEpoch); U64(200, p.Preparation); U64(208, p.Revision); U64(216, p.Sequence);
        U32(224, (uint)p.Action); U32(228, (uint)p.Setting); F64(232, p.Value); U32(240, p.Capture ? 1u : 0); U32(244, p.Host ? 1u : 0);
        U64(248, p.ReadSerial); U32(256, p.GateAck); U32(260, p.ValueAck); U64(264, p.Serial); U32(272, (uint)p.Flags);
        U32(276, p.Supported); U32(280, p.Blocked); U32(284, p.Slots); U32(288, p.TargetSlots); U32(292, p.Switches);
        F64(296, p.Heading); F64(304, p.Altitude); F64(312, p.VerticalSpeed); U32(320, p.Gps ? 1u : 0); U32(324, p.Nav); U32(328, p.Error);
        U64(332, p.SteeringGeneration); U32(340, (uint)p.SteeringOwner);
        Text(344, p.NavigationFingerprint);
        Text(409, p.Adapter);
        U32(473, p.ReferenceAck);
        return bytes;
    }
    public static bool TryDecode(ReadOnlySpan<byte> data, out NativeGuidancePacket? packet)
    {
        packet = null;
        if (data.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != Version ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) != Size || data[477..].ContainsAnyExcept((byte)0)) return false;
        var bytes = data.ToArray();
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        ulong U64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
        double F64(int offset) => BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset));
        string? Text(int offset, int size) {
            var value = bytes.AsSpan(offset, size); var end = value.IndexOf((byte)0);
            return end < 0 || value[end..].ContainsAnyExcept((byte)0) ? null : Encoding.ASCII.GetString(value[..end]);
        }
        var native = Text(16, 24); var backend = Text(40, 40); var session = Text(80, 40); var consent = Text(120, 40);
        var fingerprint = Text(344, 65);
        var adapter = Text(409, 64);
        if (native is null || backend is null || session is null || consent is null || fingerprint is null || adapter is null || U32(240) > 1 || U32(244) > 1 || U32(320) > 1) return false;
        var value = new NativeGuidancePacket { Kind = (NativeGuidanceKind)U32(8), Native = native, Backend = backend, Session = session, Consent = consent,
            Epoch = U64(160), ControlRevision = U64(168), Command = U64(176), CommandEpoch = U64(184), NavigationEpoch = U64(192),
            Preparation = U64(200), Revision = U64(208), Sequence = U64(216), Action = (NativeGuidanceAction)U32(224), Setting = (GuidanceSetting)U32(228),
            Value = F64(232), Capture = U32(240) != 0, Host = U32(244) != 0, ReadSerial = U64(248), GateAck = U32(256), ValueAck = U32(260),
            Serial = U64(264), Flags = (NativeGuidanceFlags)U32(272), Supported = U32(276), Blocked = U32(280), Slots = U32(284), TargetSlots = U32(288),
            Switches = U32(292), Heading = F64(296), Altitude = F64(304), VerticalSpeed = F64(312), Gps = U32(320) != 0, Nav = U32(324), Error = U32(328),
            SteeringGeneration = U64(332), SteeringOwner = (NativeOwner)U32(340), NavigationFingerprint = fingerprint, Adapter = adapter, ReferenceAck = U32(473) };
        if (!Valid(value)) return false;
        packet = value; return true;
    }
}
