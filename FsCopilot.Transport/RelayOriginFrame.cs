using System.Text;

namespace FsCopilot.Transport;

/// <summary>A server-generated envelope. Clients never supply the origin used for forwarding.</summary>
public static class RelayOriginFrame
{
    public const byte Channel = 2;
    public const int MaxPayload = 65536;
    private static ReadOnlySpan<byte> Magic => "FSR1"u8;
    public static bool ValidPeer(string? value) => value is { Length: > 0 and <= 64 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');

    public static byte[] Encode(string origin, ReadOnlySpan<byte> payload)
    {
        if (!ValidPeer(origin) || payload.IsEmpty || payload.Length > MaxPayload)
            throw new ArgumentException("Invalid relay origin or payload.");
        var result = new byte[5 + origin.Length + payload.Length];
        Magic.CopyTo(result);
        result[4] = (byte)origin.Length;
        Encoding.ASCII.GetBytes(origin, result.AsSpan(5));
        payload.CopyTo(result.AsSpan(5 + origin.Length));
        return result;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out string origin, out byte[] payload)
    {
        origin = string.Empty; payload = [];
        if (data.Length < 7 || !data[..4].SequenceEqual(Magic)) return false;
        var length = data[4];
        var payloadLength = data.Length - 5 - length;
        if (length is 0 or > 64 || payloadLength is <= 0 or > MaxPayload) return false;
        // ASCII.GetString replaces non-ASCII bytes; validate the bytes first.
        foreach (var c in data.Slice(5, length))
            if (!(c is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or
                >= (byte)'0' and <= (byte)'9' or (byte)'_' or (byte)'-')) return false;
        origin = Encoding.ASCII.GetString(data.Slice(5, length));
        payload = data[(5 + length)..].ToArray();
        return true;
    }
}
