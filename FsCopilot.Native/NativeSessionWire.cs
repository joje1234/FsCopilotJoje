using System.Buffers.Binary;
using System.Text;

namespace FsCopilot.Native;

public enum NativeSessionKind : uint { Status = 1, Bind, State, Prepare, Attach, Input, End, Heartbeat, IntentAck, World, Feedback, Barometer, InstrumentFeedback }
public enum NativeSessionIntent : uint { None, Me, Other, Cancel, Force, AutoOn, AutoOff, SeatRequest, SeatAccept, SeatDecline, SeatCancel }
public enum NativeSessionPhase : uint { Idle, Armed, Binding, Active, Ended }
public enum NativeSessionResult : uint { Idle, Pending, Succeeded, Rejected, Closed }
public enum NativeSessionError : uint { None, InvalidState, StaleAuthority, NotPrepared, NativeFailed, Expired, TransportFailed }
public enum NativeOwner : uint { None, Local, Remote }
[Flags] public enum NativeSessionFlags : uint { None = 0, PhysicsHost = 1, Incoming = 2, Force = 4, Autopilot = 8, World = 16, WorldFresh = 32, WorldFrozen = 64 }

public sealed record NativeSessionPacket
{
    public NativeSessionKind Kind { get; init; } = NativeSessionKind.Status;
    public string Native { get; init; } = "";
    public string Backend { get; init; } = "";
    public string Session { get; init; } = "";
    public string Consent { get; init; } = "";
    public ulong Epoch { get; init; }
    public ulong Command { get; init; }
    public ulong Revision { get; init; }
    public string Ticket { get; init; } = "";
    public ulong[] Generation { get; init; } = new ulong[4];
    public NativeOwner[] Owner { get; init; } = new NativeOwner[4];
    public uint Attached { get; init; }
    public uint Pending { get; init; }
    public uint Engines { get; init; }
    public uint Axes { get; init; }
    public uint Selection { get; init; }
    public NativeSessionFlags Flags { get; init; }
    public ulong Sequence { get; init; }
    public double[] Positions { get; init; } = Enumerable.Repeat(-1d, 21).ToArray();
    public ulong Serial { get; init; }
    public ulong Ack { get; init; }
    public NativeSessionResult Result { get; init; }
    public uint Blocked { get; init; }
    public uint Barrier { get; init; }
    public uint Required { get; init; }
    public uint Fresh { get; init; }
    public uint Aligned { get; init; }
    public string[] PendingTransfer { get; init; } = ["", "", "", ""];
    public NativeSessionPhase Phase { get; init; }
    public NativeSessionError Code { get; init; }
    public string[] CommittedTransfer { get; init; } = ["", "", "", ""];
    public ulong IntentId { get; init; }
    public uint IntentSelection { get; init; }
    public NativeSessionIntent IntentAction { get; init; }
    public uint IntentWaiting { get; init; }
    public uint IntentOther { get; init; }
    public double[] World { get; init; } = new double[12];
    public ulong WorldSerial { get; init; }
    public bool Automatic { get; init; }
    public uint AutomaticWaiting { get; init; }
    public ulong SeatRevision { get; init; }
    public uint Seat { get; init; }
    public uint SeatPending { get; init; } // 1/2 local/remote request, 3/4 accepted and waiting for instrument confirmation.
    public ulong SeatRequest { get; init; }
    public ulong IntentSeatRevision { get; init; }
    public ulong IntentSeatRequest { get; init; }
    public NativeBarometerCommand? Barometer { get; init; }
    public NativeInstrumentFeedback? InstrumentFeedback { get; init; }
    public uint BarometerGateAck { get; init; }
    public uint BarometerValueAck { get; init; }
    public NativeSessionPacket Copy() => this with {
        Generation = Generation.ToArray(), Owner = Owner.ToArray(), Positions = Positions.ToArray(), PendingTransfer = PendingTransfer.ToArray(), CommittedTransfer = CommittedTransfer.ToArray(), World = World.ToArray()
    };
}

/// <summary>Separate consent-bound session channel; discovery cannot submit commands here.</summary>
public static class NativeSessionWire
{
    public const int Size = 1024;
    public const uint Magic = 0x31534346, Version = 1;
    public const string StatusName = "FSC_CONTROLS_NATIVE_SESSION_STATUS_V1";
    public const string CommandName = "FSC_CONTROLS_NATIVE_SESSION_COMMAND_V1";
    private static bool Hex(string? value, int maximum, int exact = 0, bool optional = false) =>
        value is not null && (optional && value.Length == 0 || value.Length > 0 && value.Length <= maximum &&
            (exact == 0 || value.Length == exact) && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9'));
    public static bool Valid(NativeSessionPacket? p)
    {
        if (p is null || !Enum.IsDefined(p.Kind) || !Enum.IsDefined(p.Phase) || !Enum.IsDefined(p.Result) || !Enum.IsDefined(p.Code) ||
            p.Generation is not { Length: 4 } || p.Owner is not { Length: 4 } || p.PendingTransfer is not { Length: 4 } || p.CommittedTransfer is not { Length: 4 } ||
            p.Positions is not { Length: 21 } || !Hex(p.Native, 23)) return false;
        var status = p.Kind == NativeSessionKind.Status;
        var barometer = p.Kind == NativeSessionKind.Barometer;
        var instrumentFeedback = p.Kind == NativeSessionKind.InstrumentFeedback;
        if ((instrumentFeedback ? !NativeInstrumentFeedback.Valid(p.InstrumentFeedback) || p.Selection != 0 || p.Flags != 0 || p.Axes != 0 ||
                p.Sequence != 0 || p.Ticket != "" || p.Positions.Any(value => value != -1) : p.InstrumentFeedback is not null)) return false;
        if ((barometer ? !NativeBarometerCommand.Valid(p.Barometer) || p.Command > NativeBarometerAck.MaxCommand || p.Selection != 0 ||
                p.Flags != 0 || p.Axes != 0 || p.Sequence != 0 || p.Ticket != "" || p.Positions.Any(value => value != -1) : p.Barometer is not null) ||
            !NativeBarometerAck.Valid(p.BarometerGateAck) || !NativeBarometerAck.Valid(p.BarometerValueAck) ||
            !status && (p.BarometerGateAck != 0 || p.BarometerValueAck != 0) ||
            status && (NativeBarometerAck.Command(p.BarometerGateAck) > p.Command || NativeBarometerAck.Command(p.BarometerValueAck) > p.Command)) return false;
        var seatIntent = p.IntentAction is >= NativeSessionIntent.SeatRequest and <= NativeSessionIntent.SeatCancel;
        if (p.SeatRevision > uint.MaxValue || p.SeatRequest > uint.MaxValue ||
            (p.SeatRevision == 0 ? p.Seat != 0 || p.SeatPending != 0 || p.SeatRequest != 0 :
                p.Seat is not (1 or 2) || p.SeatPending > 4 || (p.SeatPending == 0) != (p.SeatRequest == 0)) ||
            !status && p.Kind != NativeSessionKind.Feedback && (p.SeatRevision != 0 || p.Seat != 0 || p.SeatPending != 0 || p.SeatRequest != 0) ||
            (seatIntent ? !status || p.IntentSelection != 0 || p.IntentSeatRevision is 0 or > uint.MaxValue ||
                p.IntentSeatRequest > uint.MaxValue || (p.IntentAction == NativeSessionIntent.SeatRequest) != (p.IntentSeatRequest == 0) :
                p.IntentSeatRevision != 0 || p.IntentSeatRequest != 0)) return false;
        if (!Hex(p.Backend, 32, 32, status) || !Hex(p.Session, 32, 32, status) || !Hex(p.Consent, 32, 32, status) || !Hex(p.Ticket, 32, 32, true) ||
            (p.Attached & ~15u) != 0 || (p.Pending & ~15u) != 0 || (p.Selection & ~15u) != 0 || ((uint)p.Flags & ~(status ? 127u : 23u)) != 0 ||
            p.Engines > 4 || (p.Axes >> 21) != 0 || (p.Blocked & ~15u) != 0 || (p.Required >> 21) != 0 || (p.Fresh >> 21) != 0 || (p.Aligned >> 21) != 0) return false;
        for (var index = 0; index < 4; ++index) {
            var pending = (p.Pending & (1u << index)) != 0;
            if (!Enum.IsDefined(p.Owner[index]) || !Hex(p.PendingTransfer[index], 32, 32, !pending) ||
                !Hex(p.CommittedTransfer[index], 32, 32, true) || !pending && p.PendingTransfer[index].Length != 0) return false;
        }
        if (p.Positions.Any(value => !double.IsFinite(value) || value != -1 && (value < 0 || value > 1))) return false;
        if (!NativeWorldFrame.ValidWorld(p.World) || !status && p.WorldSerial != 0 ||
            !status && p.Kind != NativeSessionKind.World && p.World.Any(value => value != 0) ||
            status && p.Flags.HasFlag(NativeSessionFlags.WorldFresh) && p.WorldSerial == 0 ||
            !status && p.Kind != NativeSessionKind.Bind && p.Flags.HasFlag(NativeSessionFlags.World) ||
            p.Kind == NativeSessionKind.World && (p.Sequence == 0 || p.Selection != 0 || p.Flags != 0)) return false;
        if (!Enum.IsDefined(p.IntentAction) || (p.IntentWaiting & ~15u) != 0 || (p.IntentOther & ~15u) != 0 ||
            (p.IntentWaiting & p.IntentOther) != 0 || (p.IntentId == 0 ? p.IntentSelection != 0 || p.IntentAction != NativeSessionIntent.None :
                !seatIntent && p.IntentSelection == 0 || (p.IntentSelection & ~15u) != 0 || p.IntentAction == NativeSessionIntent.None)) return false;
        if ((p.AutomaticWaiting & ~14u) != 0 || p.AutomaticWaiting != 0 && !p.Automatic ||
            p.Kind is not (NativeSessionKind.Status or NativeSessionKind.Bind or NativeSessionKind.State or NativeSessionKind.Feedback) && p.Automatic ||
            p.Kind is not (NativeSessionKind.Status or NativeSessionKind.Feedback) && p.AutomaticWaiting != 0 ||
            p.IntentAction is NativeSessionIntent.AutoOn or NativeSessionIntent.AutoOff && p.IntentSelection != 14 ||
            p.Kind == NativeSessionKind.Feedback && (p.Selection != 0 || p.Flags != 0)) return false;
        if (status) return p.Serial != 0;
        if (p.IntentId != 0 || p.IntentSelection != 0 || p.IntentAction != NativeSessionIntent.None || p.IntentWaiting != 0 || p.IntentOther != 0 ||
            p.Kind == NativeSessionKind.IntentAck && p.Sequence == 0) return false;
        return p.Epoch != 0 && p.Command != 0 && p.Serial == 0 && p.Ack == 0 && p.Result == NativeSessionResult.Idle &&
            p.Phase == NativeSessionPhase.Idle && p.Code == NativeSessionError.None && p.Blocked == 0 && p.Barrier == 0 && p.Required == 0 && p.Fresh == 0 && p.Aligned == 0;
    }
    public static byte[] Encode(NativeSessionPacket p)
    {
        if (!Valid(p)) throw new ArgumentException("Invalid native session packet.", nameof(p));
        var bytes = new byte[Size];
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        void U64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
        void Text(int offset, string value) => Encoding.ASCII.GetBytes(value, bytes.AsSpan(offset, value.Length));
        U32(0, Magic); U32(4, Version); U32(8, (uint)p.Kind); U32(12, Size);
        Text(16, p.Native); Text(40, p.Backend); Text(80, p.Session); Text(120, p.Consent);
        U64(160, p.Epoch); U64(168, p.Command); U64(176, p.Revision); Text(184, p.Ticket);
        for (var index = 0; index < 4; ++index) {
            U64(224 + 8 * index, p.Generation[index]); U32(256 + 4 * index, (uint)p.Owner[index]);
            Text(512 + 40 * index, p.PendingTransfer[index]);
            Text(680 + 40 * index, p.CommittedTransfer[index]);
        }
        U32(272, p.Attached); U32(276, p.Pending); U32(280, p.Engines); U32(284, p.Axes);
        U32(288, p.Selection); U32(292, (uint)p.Flags); U64(296, p.Sequence);
        for (var axis = 0; axis < 21; ++axis) BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(304 + axis * 8), p.Positions[axis]);
        U64(472, p.Serial); U64(480, p.Ack); U32(488, (uint)p.Result); U32(492, p.Blocked); U32(496, p.Barrier);
        U32(500, p.Required); U32(504, p.Fresh); U32(508, p.Aligned); U32(672, (uint)p.Phase); U32(676, (uint)p.Code);
        U64(840, p.IntentId); U32(848, p.IntentSelection); U32(852, (uint)p.IntentAction); U32(856, p.IntentWaiting); U32(860, p.IntentOther);
        if (p.InstrumentFeedback is { } feedback) {
            U64(864, feedback.Epoch); U64(872, feedback.Preparation); U64(880, feedback.Revision); U64(888, feedback.SeatRevision);
            U32(896, feedback.Phase); U32(900, feedback.Pending); U32(904, feedback.Protocol); U32(908, feedback.Code);
        } else if (p.Barometer is { } baro) {
            U64(864, baro.Epoch); U64(872, baro.Preparation); U64(880, baro.Revision); U64(888, baro.SeatRevision); U64(896, baro.Sequence);
            U32(904, (uint)baro.Action); U32(908, baro.Instrument); U32(912, baro.Pressure16); U32(916, baro.Standard ? 1u : 0);
            U32(920, baro.Mask); U32(924, baro.Seat); U64(928, baro.ReadSerial);
        } else for (var index = 0; index < 12; ++index) BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(864 + index * 8), p.World[index]);
        U64(960, p.WorldSerial);
        U32(968, p.Automatic ? 1u : 0); U32(972, p.AutomaticWaiting);
        U64(976, p.SeatRevision); U32(984, p.Seat); U32(988, p.SeatPending); U64(992, p.SeatRequest);
        U64(1000, p.IntentSeatRevision); U64(1008, p.IntentSeatRequest);
        U32(1016, p.BarometerGateAck); U32(1020, p.BarometerValueAck);
        return bytes;
    }
    public static bool TryDecode(ReadOnlySpan<byte> data, out NativeSessionPacket? packet)
    {
        packet = null;
        if (data.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != Version ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) != Size ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[968..]) > 1) return false;
        // A private copy also avoids capturing a caller's mutable buffer in the
        // offset readers. Every fixed string has a canonical zero-filled tail.
        var bytes = data.ToArray();
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        ulong U64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
        string? Text(int offset, int size) {
            var value = bytes.AsSpan(offset, size); var end = value.IndexOf((byte)0);
            return end < 0 || value[end..].ContainsAnyExcept((byte)0) ? null : Encoding.ASCII.GetString(value[..end]);
        }
        var native = Text(16, 24); var backend = Text(40, 40); var session = Text(80, 40); var consent = Text(120, 40); var ticket = Text(184, 40);
        if (native is null || backend is null || session is null || consent is null || ticket is null) return false;
        var generations = new ulong[4]; var owners = new NativeOwner[4]; var pending = new string[4]; var committed = new string[4]; var positions = new double[21];
        for (var index = 0; index < 4; ++index) {
            generations[index] = U64(224 + index * 8); owners[index] = (NativeOwner)U32(256 + index * 4);
            var text = Text(512 + 40 * index, 40); if (text is null) return false; pending[index] = text;
            var commit = Text(680 + 40 * index, 40); if (commit is null) return false; committed[index] = commit;
        }
        for (var axis = 0; axis < 21; ++axis) positions[axis] = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(304 + axis * 8));
        var kind = (NativeSessionKind)U32(8);
        NativeBarometerCommand? baro = null; NativeInstrumentFeedback? feedback = null; var world = new double[12];
        if (kind == NativeSessionKind.InstrumentFeedback) {
            if (bytes.AsSpan(912, 48).ContainsAnyExcept((byte)0)) return false;
            feedback = new(U64(864), U64(872), U64(880), U64(888), U32(896), U32(900), U32(904), U32(908));
        } else if (kind == NativeSessionKind.Barometer) {
            if (U32(916) > 1 || bytes.AsSpan(936, 24).ContainsAnyExcept((byte)0)) return false;
            baro = new(U64(864), U64(872), U64(880), U64(888), U64(896), (NativeBarometerAction)U32(904), U32(908), U32(912), U32(916) != 0, U32(920), U32(924), U64(928));
        } else for (var index = 0; index < 12; ++index) world[index] = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(864 + index * 8));
        var p = new NativeSessionPacket {
            Kind = (NativeSessionKind)U32(8), Native = native, Backend = backend, Session = session, Consent = consent,
            Epoch = U64(160), Command = U64(168), Revision = U64(176), Ticket = ticket, Generation = generations, Owner = owners,
            Attached = U32(272), Pending = U32(276), Engines = U32(280), Axes = U32(284), Selection = U32(288), Flags = (NativeSessionFlags)U32(292),
            Sequence = U64(296), Positions = positions, Serial = U64(472), Ack = U64(480), Result = (NativeSessionResult)U32(488), Blocked = U32(492),
            Barrier = U32(496), Required = U32(500), Fresh = U32(504), Aligned = U32(508), PendingTransfer = pending, Phase = (NativeSessionPhase)U32(672), Code = (NativeSessionError)U32(676), CommittedTransfer = committed,
            IntentId = U64(840), IntentSelection = U32(848), IntentAction = (NativeSessionIntent)U32(852), IntentWaiting = U32(856), IntentOther = U32(860),
            World = world, WorldSerial = U64(960), Automatic = U32(968) != 0, AutomaticWaiting = U32(972),
            SeatRevision = U64(976), Seat = U32(984), SeatPending = U32(988), SeatRequest = U64(992), IntentSeatRevision = U64(1000), IntentSeatRequest = U64(1008),
            Barometer = baro, InstrumentFeedback = feedback, BarometerGateAck = U32(1016), BarometerValueAck = U32(1020)
        };
        if (!Valid(p)) return false;
        packet = p; return true;
    }
}
