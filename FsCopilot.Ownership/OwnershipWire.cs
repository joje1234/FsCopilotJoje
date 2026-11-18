using System.Text.Json;
using System.Text.Json.Serialization;

namespace FsCopilot.Ownership;

// Sender identity is deliberately absent. A transport adapter must supply the
// actual connection identity separately, including for relayed delivery.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RequestMessage), "request")]
[JsonDerivedType(typeof(PreparationMessage), "prepared")]
[JsonDerivedType(typeof(AttachmentMessage), "attachment")]
[JsonDerivedType(typeof(CancellationMessage), "cancel")]
[JsonDerivedType(typeof(SnapshotMessage), "snapshot")]
[JsonDerivedType(typeof(InputMessage), "input")]
public abstract record OwnershipMessage;
public sealed record RequestMessage(TransferRequest Request) : OwnershipMessage;
public sealed record PreparationMessage(PreparationAck Ack) : OwnershipMessage;
public sealed record AttachmentMessage(AttachmentAck Ack) : OwnershipMessage;
public sealed record CancellationMessage(TransferCancellation Cancellation) : OwnershipMessage;
public sealed record SnapshotMessage(OwnershipSnapshot State) : OwnershipMessage;
public sealed record InputMessage(ControlFrame Frame) : OwnershipMessage;

public static class OwnershipWire
{
    public const int MaxBytes = 4096;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static byte[] Encode(OwnershipMessage message)
    {
        if (!Valid(message)) throw new ArgumentException("Invalid ownership message.", nameof(message));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Ownership message is too large.", nameof(message));
        return bytes;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out OwnershipMessage? message)
    {
        message = null;
        if (data.IsEmpty || data.Length > MaxBytes) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<OwnershipMessage>(data, Options);
            if (!Valid(parsed)) return false;
            message = parsed;
            return true;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static bool Valid(OwnershipMessage? message) => message switch
    {
        RequestMessage { Request: { } r } => r.Session != Guid.Empty && r.Request != 0 && r.Generation != 0 &&
            OwnershipProtocol.ValidGroup(r.Group) && OwnershipProtocol.ValidPeer(r.NewOwner),
        PreparationMessage { Ack: { } a } => a.Session != Guid.Empty && a.Transfer != Guid.Empty && OwnershipProtocol.ValidGroup(a.Group),
        AttachmentMessage { Ack: { } a } => a.Session != Guid.Empty && a.Generation != 0 && OwnershipProtocol.ValidGroup(a.Group),
        CancellationMessage { Cancellation: { } c } => c.Session != Guid.Empty && c.Transfer != Guid.Empty && OwnershipProtocol.ValidGroup(c.Group),
        SnapshotMessage { State: { } s } => OwnershipProtocol.ValidSnapshot(s),
        InputMessage { Frame: { } f } => f.Session != Guid.Empty && f.Generation != 0 && f.Sequence != 0 && OwnershipProtocol.ValidAxes(f, 4),
        _ => false
    };
}
