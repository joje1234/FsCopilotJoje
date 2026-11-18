namespace FsCopilot.Native;

/// <summary>
/// A retained ClientData snapshot is not evidence of a live native instance.
/// Only a reply to this connection's fresh challenge can bind it. Both current
/// observations and challenge replies must remain fresh before use.
/// Caller serializes calls on the SimConnect reader thread.
/// </summary>
public sealed class NativeBridgeMonitor
{
    private sealed class Challenge(TimeSpan sent) { public readonly TimeSpan Sent = sent; public string? Native; }
    private readonly Dictionary<ulong, Challenge> _pending = new();
    private readonly HashSet<string> _retired = new(StringComparer.Ordinal);
    private readonly Func<TimeSpan> _clock;
    private readonly TimeSpan _timeout;
    private TimeSpan? _observed, _answered;
    private NativeControlPacket? _snapshot;
    private ulong _request;
    public string Backend { get; } = Guid.NewGuid().ToString("N");
    public bool ConflictingInstances { get; private set; }
    public NativeControlPacket? Snapshot => _snapshot?.Copy();
    public bool ObservationsFresh => Live && _observed is { } seen && _clock() >= seen && _clock() - seen <= NativeSessionTiming.Fresh;
    public bool Live => !ConflictingInstances && _observed is { } seen && _answered is { } answer &&
        _clock() - seen <= _timeout && _clock() - answer <= _timeout;
    public NativeBridgeMonitor(Func<TimeSpan> clock, TimeSpan? timeout = null) {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }
    public NativeControlPacket Hello() {
        var now = _clock();
        foreach (var expired in _pending.Where(pair => now - pair.Value.Sent > _timeout).Select(pair => pair.Key).ToArray()) _pending.Remove(expired);
        if (_pending.Count >= 8) _pending.Remove(_pending.Keys.Min());
        var id = checked(++_request); _pending[id] = new(now);
        return new() { Kind = NativePacketKind.Hello, Backend = Backend, Request = id };
    }
    public bool Receive(NativeControlPacket packet) {
        if (!NativeControlWire.Valid(packet) || ConflictingInstances || packet.Kind == NativePacketKind.Hello) return false;
        var now = _clock();
        if (packet.Kind == NativePacketKind.Reply) {
            if (packet.Backend != Backend || !_pending.TryGetValue(packet.Request, out var challenge) || now - challenge.Sent > _timeout) return false;
            if (challenge.Native is { } first && first != packet.Native) { ConflictingInstances = true; return false; }
            if (challenge.Native is not null || _retired.Contains(packet.Native)) return false;
            challenge.Native = packet.Native;
            if (_snapshot is not null && _snapshot.Native != packet.Native) { _retired.Add(_snapshot.Native); _snapshot = null; }
            _answered = now;
        }
        else if (!Live || _snapshot is null || _snapshot.Native != packet.Native) return false;
        if (_snapshot is not null && packet.Serial <= _snapshot.Serial) return false;
        _observed = now; _snapshot = packet.Copy(); return true;
    }
}
