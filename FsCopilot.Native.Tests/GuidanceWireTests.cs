using FsCopilot.Native;
using FsCopilot.Ownership;

internal static class GuidanceWireTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool value) { if (!value) throw new InvalidOperationException("Native guidance contract assertion failed."); }
        void Test(string name, Action run) { run(); ++count; Console.WriteLine("PASS native guidance: " + name); }
        NativeGuidancePacket Command() => new() { Native = "abc", Backend = new string('a', 32), Session = new string('b', 32),
            Consent = new string('c', 32), Epoch = 7, ControlRevision = 4, Command = 99, CommandEpoch = 2, NavigationEpoch = 3,
            Preparation = 4, Revision = 5, Sequence = 6, Action = NativeGuidanceAction.Apply, Setting = GuidanceSetting.Heading,
            Value = 359.995, Host = true, ReadSerial = 12 };
        NativeGuidancePacket Status() => Command() with { Kind = NativeGuidanceKind.Status, Action = NativeGuidanceAction.Gate,
            Setting = GuidanceSetting.Master, Value = 0, Sequence = 0, Serial = 18, Capture = true,
            Flags = NativeGuidanceFlags.Fresh | NativeGuidanceFlags.Transport | NativeGuidanceFlags.Profile | NativeGuidanceFlags.GateReady |
                NativeGuidanceFlags.Capture | NativeGuidanceFlags.SingleHostEngine | NativeGuidanceFlags.NavigationReady,
            NavigationFingerprint = new string('a', 64), Adapter = "test-absolute-ap-v1", Supported = 4083, Blocked = 8191,
            Slots = NativeGuidanceWire.Slots(1, 2, 4), TargetSlots = NativeGuidanceWire.Slots(1, 1, 1), Switches = 8191,
            Heading = 359.995, Altitude = 5000, VerticalSpeed = -1500, Gps = true, Nav = 2,
            GateAck = NativeBarometerAck.Pack(98, NativeBarometerResult.Succeeded), ValueAck = NativeBarometerAck.Pack(99, NativeBarometerResult.Pending) };
        Test("production C++ status is accepted and re-encoded byte for byte", () => {
            var bytes = File.ReadAllBytes("artifacts/guidance-channel/tests/guidance-status-from-cpp.bin");
            Check(NativeGuidanceWire.TryDecode(bytes, out var copy) && copy!.Kind == NativeGuidanceKind.Status &&
                copy.Flags.HasFlag(NativeGuidanceFlags.GateReady | NativeGuidanceFlags.Capture | NativeGuidanceFlags.SingleHostEngine) &&
                copy.Heading == 120 && copy.Blocked == 8191 && copy.Supported == 4095 && copy.Host);
            Check(bytes.SequenceEqual(NativeGuidanceWire.Encode(copy!)));
        });
        Test("absolute commands carry binding and independent guidance scopes", () => {
            var command = Command(); var bytes = NativeGuidanceWire.Encode(command);
            Check(bytes.Length == 512 && NativeGuidanceWire.TryDecode(bytes, out var copy) && copy == command);
            Check(!NativeGuidanceWire.Valid(command with { Consent = "" }));
            Check(!NativeGuidanceWire.Valid(command with { Preparation = 0 }));
            Check(!NativeGuidanceWire.Valid(command with { Value = double.NaN }));
            Check(!NativeGuidanceWire.Valid(command with { Sequence = 0 }));
            Check(!NativeGuidanceWire.Valid(command with { Flags = NativeGuidanceFlags.GateReady }));
        });
        Test("noncanonical strings, booleans, lengths and reserved bytes are rejected", () => {
            var original = NativeGuidanceWire.Encode(Command());
            foreach (var offset in new[] { 240, 244, 320, 409, 511, 39 }) {
                var bytes = original.ToArray(); bytes[offset] = 2;
                Check(!NativeGuidanceWire.TryDecode(bytes, out _));
            }
            Check(!NativeGuidanceWire.TryDecode(original.AsSpan(1), out _));
            Check(!NativeGuidanceWire.TryDecode(original.Concat(new byte[] { 0 }).ToArray(), out _));
        });
        Test("native gate/source proof flags cannot be inferred from a backend claim", () => {
            var state = Status(); Check(NativeGuidanceWire.Valid(state));
            foreach (var bad in new[] { state with { Blocked = 0 }, state with { ReadSerial = 0 },
                state with { Flags = state.Flags & ~NativeGuidanceFlags.Profile }, state with { Flags = state.Flags | NativeGuidanceFlags.Held },
                state with { Capture = false }, state with { NavigationFingerprint = "" }, state with { NavigationFingerprint = new string('A', 64) } })
                Check(!NativeGuidanceWire.Valid(bad));
            var bytes = NativeGuidanceWire.Encode(state); Check(NativeGuidanceWire.TryDecode(bytes, out var copy) && copy == state);
            File.WriteAllBytes("artifacts/guidance-channel/tests/guidance-status-from-csharp.bin", bytes);
        });
        Test("steering disconnect carries the final owner/generation fence", () => {
            var command = Command() with { Setting = GuidanceSetting.Master, Value = 0, SteeringGeneration = 7, SteeringOwner = NativeOwner.Remote };
            var bytes = NativeGuidanceWire.Encode(command);
            Check(NativeGuidanceWire.TryDecode(bytes, out var copy) && copy == command);
            Check(!NativeGuidanceWire.Valid(command with { SteeringOwner = NativeOwner.None }));
            Check(!NativeGuidanceWire.Valid(command with { Value = 1 }));
            Check(!NativeGuidanceWire.Valid(command with { SteeringGeneration = 0 }));
            File.WriteAllBytes("artifacts/guidance-channel/tests/guidance-command-from-csharp.bin", bytes);
        });
        Test("unbound AP observation is local telemetry, never a shared gate proof", () => {
            var state = new NativeGuidancePacket { Kind = NativeGuidanceKind.Status, Native = "abc", Serial = 1,
                Flags = NativeGuidanceFlags.Transport | NativeGuidanceFlags.Fresh, ReadSerial = 1 };
            Check(NativeGuidanceWire.Valid(state));
            Check(!NativeGuidanceWire.Valid(state with { Backend = new string('a', 32) }));
            Check(!NativeGuidanceWire.Valid(state with { Flags = state.Flags | NativeGuidanceFlags.GateReady }));
        });
        return count;
    }
}
