namespace FsCopilot.Native;

public sealed record NativeGuidanceBridgeState(bool Connected, bool Live, NativeGuidancePacket? Snapshot, string Error);
public sealed record NativeGuidanceFence(NativeGuidancePacket? Gate, NativeGuidancePacket? Value, NativeGuidancePacket? IntentAck = null, NativeGuidancePacket? Reference = null)
{
    public bool Allows(NativeGuidancePacket packet) => packet == Gate || packet == Value || packet == IntentAck || packet == Reference;
}
public interface INativeGuidanceClient
{
    NativeGuidanceBridgeState GuidanceState { get; }
    event Action<NativeGuidanceBridgeState>? GuidanceChanged;
    event Action<ulong>? GuidanceCommandDropped;
    void SetGuidanceFence(NativeGuidanceFence? fence);
    // Host value writes supply the model's final execution fence. It invokes
    // the SDK write under the model lock; lock order is model -> transport.
    bool TrySendGuidance(NativeGuidancePacket command, Func<Func<bool>, bool>? execute = null);
}
