namespace FsCopilot.Native;

public sealed record NativeConnectionSettings(ulong Revision, string Relay, uint Port, string Local, string Peer, uint Role, uint Seat)
{
    public static bool Code(string? value) => value is { Length: 8 } && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');
    public static bool Host(string? value) => value is { Length: > 0 and <= 127 } && value.Split('.').All(label =>
        label.Length is > 0 and <= 63 && label[0] != '-' && label[^1] != '-' &&
        label.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'));
    public static bool Valid(NativeConnectionSettings? value) => value is not null && value.Revision is > 0 and <= uint.MaxValue && Host(value.Relay) &&
        Code(value.Local) && Code(value.Peer) && value.Local != value.Peer && value.Port is >= 1 and <= 65535 && value.Role is 1 or 2 && value.Seat is 1 or 2;
}

public enum NativeBackendPhase : uint { Stopped, WaitingForSetup, WaitingForJoin, Connecting, Binding, Active, Ended, Error }
public enum NativeBackendCode : uint { None, UnsupportedPackage, LearnControls, ConnectingFailed, SessionEnded, NativeUnavailable, ConfigurationChanged }
public sealed record NativeBackendNotice(ulong Configuration, NativeBackendPhase Phase, NativeBackendCode Code = NativeBackendCode.None);
public interface INativeBackendClient : INativeSessionClient
{
    bool Report(NativeBackendNotice notice);
}
