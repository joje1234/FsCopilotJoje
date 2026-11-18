namespace FsCopilot.Native;

public sealed record NativeBridgeState(bool TransportConnected, bool Live, NativeControlPacket? Snapshot, string Error)
{
    public string Backend { get; init; } = "";
    public NativeSessionPacket? Session { get; init; }
    // Retaining a live session during a brief stall does not authorize aircraft writes.
    public bool Fresh { get; init; } = true;
    public NativeBridgeState Copy() => this with { Snapshot = Snapshot?.Copy(), Session = Session?.Copy() };
}

public interface INativeSessionClient
{
    NativeBridgeState State { get; }
    event Action<NativeBridgeState>? StateChanged;
    event Action<ulong>? SessionCommandDropped;
    bool TrySendSession(NativeSessionPacket command);
}
