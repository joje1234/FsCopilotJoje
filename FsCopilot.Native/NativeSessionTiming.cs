namespace FsCopilot.Native;

/// <summary>Freshness permits writes; the longer deadline only retains the existing session.</summary>
public static class NativeSessionTiming
{
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Recovery = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan PeerFresh = TimeSpan.FromSeconds(3);
}
