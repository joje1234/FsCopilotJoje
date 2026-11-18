using System.Text.Json;
using System.Text.Json.Serialization;

namespace FsCopilot.Ownership;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SessionHello), "hello")]
[JsonDerivedType(typeof(SessionOffer), "offer")]
[JsonDerivedType(typeof(AtomicSessionOffer), "atomic-offer")]
[JsonDerivedType(typeof(SessionAcceptance), "accept")]
[JsonDerivedType(typeof(SessionCommit), "commit")]
[JsonDerivedType(typeof(SessionRejection), "reject")]
[JsonDerivedType(typeof(SessionEnd), "end")]
public abstract record SessionHandshake(int Version);
public sealed record SessionHello(int Version, Guid ClientAttempt, SessionConfiguration Configuration, CockpitSeat Seat) : SessionHandshake(Version);
public sealed record SessionOffer(int Version, Guid HostAttempt, Guid ClientAttempt, SessionConfiguration Configuration, OwnershipSnapshot Initial, CockpitSeats Seats) : SessionHandshake(Version);
public sealed record AtomicSessionOffer(int Version, Guid HostAttempt, Guid ClientAttempt, SessionConfiguration Configuration, ControlAuthoritySnapshot Initial, CockpitSeats Seats) : SessionHandshake(Version);
public sealed record SessionAcceptance(int Version, Guid HostAttempt, Guid ClientAttempt, Guid Session) : SessionHandshake(Version);
public sealed record SessionCommit(int Version, Guid HostAttempt, Guid ClientAttempt, Guid Session) : SessionHandshake(Version);
public sealed record SessionRejection(int Version, Guid ClientAttempt, string Reason) : SessionHandshake(Version);
public sealed record SessionEnd(int Version, Guid HostAttempt, Guid ClientAttempt, Guid Session) : SessionHandshake(Version);

public static class SessionWire
{
    public const int Version = 1;
    public const int MaxBytes = 4096;
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8
    };
    public static bool Valid(SessionHandshake? message) => message?.Version == Version && (message switch {
        SessionHello h => h.ClientAttempt != Guid.Empty && SessionNegotiation.ValidConfiguration(h.Configuration) && Enum.IsDefined(h.Seat),
        SessionOffer o => o.HostAttempt != Guid.Empty && o.ClientAttempt != Guid.Empty && SessionNegotiation.ValidConfiguration(o.Configuration) &&
            o.Configuration.Capabilities == SessionNegotiation.RequiredCapabilities &&
            SessionNegotiation.InitialSnapshot(o.Initial) && o.Initial.Engines == o.Configuration.Engines && o.Seats is not null &&
            (o.Seats.PilotPeer == o.Initial.PhysicsHost && o.Seats.CopilotPeer == o.Initial.Copilot ||
             o.Seats.CopilotPeer == o.Initial.PhysicsHost && o.Seats.PilotPeer == o.Initial.Copilot),
        AtomicSessionOffer o => o.HostAttempt != Guid.Empty && o.ClientAttempt != Guid.Empty && SessionNegotiation.ValidConfiguration(o.Configuration) &&
            o.Configuration.Capabilities == SessionNegotiation.AdvancedCapabilities && AtomicProtocol.ValidSnapshot(o.Initial) &&
            o.Initial.Revision == 1 && o.Initial.SeatRevision == 1 && o.Initial.SeatChange is null && o.Initial.Seats == o.Seats &&
            !o.Initial.Automatic && !o.Initial.Ended && o.Initial.PeerConnected && o.Initial.Transfers.Length == 0 &&
            o.Initial.Groups.All(group => group.Generation == 1 && group.Owner == o.Initial.PhysicsHost && !group.Attached && group.Pending is null) &&
            o.Initial.Engines == o.Configuration.Engines && o.Initial.SteeringAxes == o.Configuration.SteeringAxes && o.Seats is not null &&
            (o.Seats.PilotPeer == o.Initial.PhysicsHost && o.Seats.CopilotPeer == o.Initial.Peer ||
             o.Seats.CopilotPeer == o.Initial.PhysicsHost && o.Seats.PilotPeer == o.Initial.Peer),
        SessionAcceptance a => a.HostAttempt != Guid.Empty && a.ClientAttempt != Guid.Empty && a.Session != Guid.Empty,
        SessionCommit c => c.HostAttempt != Guid.Empty && c.ClientAttempt != Guid.Empty && c.Session != Guid.Empty,
        SessionEnd e => e.HostAttempt != Guid.Empty && e.ClientAttempt != Guid.Empty && e.Session != Guid.Empty,
        SessionRejection r => r.ClientAttempt != Guid.Empty && r.Reason is "configuration-mismatch" or "seat-conflict",
        _ => false
    });
    public static byte[] Encode(SessionHandshake message)
    {
        if (!Valid(message)) throw new ArgumentException("Invalid session handshake.", nameof(message));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Session handshake too large.", nameof(message));
        return bytes;
    }
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out SessionHandshake? message)
    {
        message = null;
        if (bytes.IsEmpty || bytes.Length > MaxBytes) return false;
        try {
            var decoded = JsonSerializer.Deserialize<SessionHandshake>(bytes, Options);
            if (!Valid(decoded)) return false;
            message = decoded; return true;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }
}
