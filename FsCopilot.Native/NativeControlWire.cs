using System.Buffers.Binary;
using System.Text;

namespace FsCopilot.Native;

public enum NativePacketKind : uint { Status = 1, Hello = 2, Reply = 3, Backend = 4 }
[Flags]
public enum NativeStatusFlags : uint { Ready = 1, Applying = 2, Advanced = 4, TargetsFresh = 8, StorageFailed = 16 }
public sealed record NativeControlPacket
{
    public NativeAltimeterObservation Altimeters { get; init; } = new(0, 0, 0, 0, false);
    public NativePacketKind Kind { get; init; } = NativePacketKind.Status;
    public string Native { get; init; } = "";
    public string Backend { get; init; } = "";
    public string Aircraft { get; init; } = "";
    public string Adapter { get; init; } = "";
    public ulong Serial { get; init; }
    public ulong Request { get; init; }
    public uint Revision { get; init; }
    public NativeStatusFlags Flags { get; init; }
    public uint Blocked { get; init; }
    public uint Automatic { get; init; }
    public uint Engines { get; init; }
    public uint Required { get; init; }
    public uint Learned { get; init; }
    public uint Fresh { get; init; }
    public uint Aligned { get; init; }
    public uint Capabilities { get; init; }
    public double[] Input { get; init; } = Enumerable.Repeat(-1d, 21).ToArray();
    public double[] Targets { get; init; } = Enumerable.Repeat(-1d, 21).ToArray();
    public uint[] Tolerance { get; init; } = [10, 10, 10, 10];
    public uint ConnectionApi { get; init; }
    public NativeConnectionSettings? Connection { get; init; }
    public ulong ReportConfiguration { get; init; }
    public NativeBackendPhase BackendPhase { get; init; }
    public NativeBackendCode BackendCode { get; init; }
    public NativeControlPacket Copy() => this with { Input = Input.ToArray(), Targets = Targets.ToArray(), Tolerance = Tolerance.ToArray() };
}

/// <summary>Fixed little-endian wire contract shared with control_bridge_protocol.h.</summary>
public static class NativeControlWire
{
    public const int Size = 1024;
    public const uint Magic = 0x31434246, Version = 1;
    public const string StatusName = "FSC_CONTROLS_NATIVE_STATUS_V1";
    public const string CommandName = "FSC_CONTROLS_NATIVE_COMMAND_V1";
    public const string ReplyName = "FSC_CONTROLS_NATIVE_REPLY_V1";
    private static bool Token(string? value, int maximum, int exact = 0) => value is { Length: > 0 } && value.Length <= maximum &&
        (exact == 0 || value.Length == exact) && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');
    private static bool Text(string? value, int capacity) => value is not null && value.Length < capacity && value.All(c => c is >= ' ' and <= '~');
    public static bool Valid(NativeControlPacket? p)
    {
        if (p is null || !Enum.IsDefined(p.Kind) || !Text(p.Native, 24) || !Text(p.Backend, 40) ||
            !Text(p.Aircraft, 256) || !Text(p.Adapter, 64) || p.Input is not { Length: 21 } || p.Targets is not { Length: 21 } || p.Tolerance is not { Length: 4 }) return false;
        if (!NativeAltimeterObservation.Valid(p.Altimeters) ||
            p.Kind is NativePacketKind.Hello or NativePacketKind.Backend && (p.Altimeters.Serial != 0 || p.Altimeters.Flags != 0)) return false;
        if (p.ConnectionApi > 1 || !Enum.IsDefined(p.BackendPhase) || !Enum.IsDefined(p.BackendCode) ||
            p.Connection is not null && !NativeConnectionSettings.Valid(p.Connection) ||
            p.ConnectionApi == 0 && (p.Connection is not null || p.ReportConfiguration != 0 || p.BackendPhase != 0 || p.BackendCode != 0)) return false;
        if (p.Kind == NativePacketKind.Backend) return p.ConnectionApi == 1 && p.Connection is null && p.Serial == 0 &&
            p.Request != 0 && Token(p.Backend, 32, 32) && Token(p.Native, 23) && p.Aircraft.Length == 0 && p.Adapter.Length == 0 &&
            p.Flags == 0 && p.Blocked == 0 && p.Automatic == 0 && p.Engines == 0 && p.Required == 0 && p.Learned == 0 && p.Fresh == 0 && p.Aligned == 0 && p.Capabilities == 0;
        if (p.ReportConfiguration != 0) return false;
        if (p.Kind == NativePacketKind.Hello) return p.Connection is null && p.BackendPhase == 0 && p.BackendCode == 0 &&
            p.Request != 0 && Token(p.Backend, 32, 32) && (p.Native.Length == 0 || Token(p.Native, 23));
        if (p.Serial == 0 || !Token(p.Native, 23) || ((uint)p.Flags & ~31u) != 0 || (p.Blocked & ~15u) != 0 ||
            (p.Automatic & ~14u) != 0 || p.Engines > 4 || (p.Required >> 21) != 0 || (p.Learned >> 21) != 0 ||
            (p.Fresh >> 21) != 0 || (p.Aligned >> 21) != 0 || (p.Capabilities & ~63u) != 0) return false;
        if (p.Kind == NativePacketKind.Reply && (p.Request == 0 || !Token(p.Backend, 32, 32))) return false;
        if (p.Kind == NativePacketKind.Status && (p.Request != 0 || p.Backend.Length != 0)) return false;
        if (p.Flags.HasFlag(NativeStatusFlags.Ready) && (p.Aircraft.Length == 0 || p.Adapter.Length == 0 || p.Engines == 0)) return false;
        static bool Position(double value) => double.IsFinite(value) && (value == -1 || value is >= 0 and <= 1);
        return p.Input.All(Position) && p.Targets.All(Position) && p.Tolerance.All(value => value is 1 or 2 or 3 or 5 or 10 or 20 or 30);
    }
    public static byte[] Encode(NativeControlPacket p)
    {
        if (!Valid(p)) throw new ArgumentException("Invalid native control packet.", nameof(p));
        var bytes = new byte[Size];
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        void U64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, 8), value);
        void String(int offset, string value) => Encoding.ASCII.GetBytes(value, bytes.AsSpan(offset, value.Length));
        U32(0, Magic); U32(4, Version); U32(8, (uint)p.Kind); U32(12, Size);
        String(16, p.Native); String(40, p.Backend); String(80, p.Aircraft); String(336, p.Adapter);
        U64(400, p.Serial); U64(408, p.Request);
        uint[] fields = [p.Revision, (uint)p.Flags, p.Blocked, p.Automatic, p.Engines, p.Required, p.Learned, p.Fresh, p.Aligned, p.Capabilities];
        for (var index = 0; index < fields.Length; ++index) U32(416 + index * 4, fields[index]);
        for (var axis = 0; axis < 21; ++axis) {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(456 + axis * 8, 8), p.Input[axis]);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(624 + axis * 8, 8), p.Targets[axis]);
        }
        for (var index = 0; index < 4; ++index) U32(792 + index * 4, p.Tolerance[index]);
        U64(808, p.Connection?.Revision ?? p.ReportConfiguration);
        if (p.Connection is { } connection) {
            String(816, connection.Relay); String(944, connection.Local); String(960, connection.Peer);
            U32(976, connection.Port); U32(980, connection.Role); U32(984, connection.Seat);
        }
        U32(988, (uint)p.BackendPhase); U32(992, (uint)p.BackendCode); U32(996, p.ConnectionApi);
        U32(1000, p.Altimeters.Pressure1); U32(1004, p.Altimeters.Pressure2); U32(1008, p.Altimeters.StandardMask);
        U32(1012, p.Altimeters.Flags); U64(1016, p.Altimeters.Serial);
        return bytes;
    }
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out NativeControlPacket? packet)
    {
        packet = null;
        if (bytes.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != Version || BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) != Size) return false;
        static string? String(ReadOnlySpan<byte> data) {
            var end = data.IndexOf((byte)0);
            if (end < 0 || data[end..].ContainsAnyExcept((byte)0)) return null;
            var text = data[..end];
            foreach (var value in text) if (value is < 32 or > 126) return null;
            return Encoding.ASCII.GetString(text);
        }
        var native = String(bytes.Slice(16, 24)); var backend = String(bytes.Slice(40, 40));
        var aircraft = String(bytes.Slice(80, 256)); var adapter = String(bytes.Slice(336, 64));
        if (native is null || backend is null || aircraft is null || adapter is null) return false;
        var values = new uint[10];
        for (var index = 0; index < values.Length; ++index) values[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(416 + index * 4)..]);
        var input = new double[21]; var targets = new double[21]; var tolerance = new uint[4];
        for (var axis = 0; axis < 21; ++axis) {
            input[axis] = BinaryPrimitives.ReadDoubleLittleEndian(bytes[(456 + axis * 8)..]);
            targets[axis] = BinaryPrimitives.ReadDoubleLittleEndian(bytes[(624 + axis * 8)..]);
        }
        for (var index = 0; index < 4; ++index) tolerance[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(792 + index * 4)..]);
        var relay = String(bytes.Slice(816, 128)); var local = String(bytes.Slice(944, 16)); var peer = String(bytes.Slice(960, 16));
        if (relay is null || local is null || peer is null) return false;
        var connectionRevision = BinaryPrimitives.ReadUInt64LittleEndian(bytes[808..]);
        var port = BinaryPrimitives.ReadUInt32LittleEndian(bytes[976..]);
        var role = BinaryPrimitives.ReadUInt32LittleEndian(bytes[980..]);
        var seat = BinaryPrimitives.ReadUInt32LittleEndian(bytes[984..]);
        var report = (NativePacketKind)BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) == NativePacketKind.Backend;
        if ((report || connectionRevision == 0) && (relay.Length != 0 || local.Length != 0 || peer.Length != 0 || port != 0 || role != 0 || seat != 0)) return false;
        var barometerFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[1012..]);
        if ((barometerFlags & ~1023u) != 0) return false;
        var result = new NativeControlPacket {
            Kind = (NativePacketKind)BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), Native = native, Backend = backend, Aircraft = aircraft, Adapter = adapter,
            Serial = BinaryPrimitives.ReadUInt64LittleEndian(bytes[400..]), Request = BinaryPrimitives.ReadUInt64LittleEndian(bytes[408..]),
            Revision = values[0], Flags = (NativeStatusFlags)values[1], Blocked = values[2], Automatic = values[3], Engines = values[4],
            Required = values[5], Learned = values[6], Fresh = values[7], Aligned = values[8], Capabilities = values[9], Input = input, Targets = targets, Tolerance = tolerance,
            Connection = report || connectionRevision == 0 ? null : new(connectionRevision, relay, port, local, peer, role, seat), ReportConfiguration = report ? connectionRevision : 0,
            BackendPhase = (NativeBackendPhase)BinaryPrimitives.ReadUInt32LittleEndian(bytes[988..]), BackendCode = (NativeBackendCode)BinaryPrimitives.ReadUInt32LittleEndian(bytes[992..]), ConnectionApi = BinaryPrimitives.ReadUInt32LittleEndian(bytes[996..]),
            Altimeters = new(BinaryPrimitives.ReadUInt64LittleEndian(bytes[1016..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[1000..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[1004..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[1008..]), (barometerFlags & 1) != 0,
                (barometerFlags & 2) != 0, (barometerFlags & 4) != 0, (barometerFlags & 8) != 0, (barometerFlags >> 4) & 3,
                (barometerFlags & 64) != 0, (barometerFlags & 128) != 0, (barometerFlags & 256) != 0, (barometerFlags & 512) != 0)
        };
        if (!Valid(result)) return false;
        packet = result; return true;
    }
}
