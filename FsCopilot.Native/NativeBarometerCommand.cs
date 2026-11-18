namespace FsCopilot.Native;

public enum NativeBarometerAction : uint { Gate = 1, Seed, Apply, Observe, Stop }
public enum NativeBarometerResult : uint { Idle, Pending, Succeeded, Rejected, Failed, Unsupported }
public sealed record NativeBarometerCommand(ulong Epoch, ulong Preparation, ulong Revision, ulong SeatRevision, ulong Sequence,
    NativeBarometerAction Action, uint Instrument, uint Pressure16, bool Standard, uint Mask, uint Seat, ulong ReadSerial)
{
    public static bool Valid(NativeBarometerCommand? value) => value is not null && value.Epoch != 0 && value.Preparation != 0 &&
        value.Revision != 0 && value.SeatRevision is > 0 and <= uint.MaxValue && value.Seat is 1 or 2 && Enum.IsDefined(value.Action) &&
        (value.Action is NativeBarometerAction.Gate or NativeBarometerAction.Stop ? value.Instrument == 0 && value.Pressure16 == 0 &&
            !value.Standard && value.Sequence == 0 && (value.Action == NativeBarometerAction.Gate ? value.Mask is > 0 and <= 3 : value.Mask == 0) :
            value.Instrument is 1 or 2 && value.Pressure16 is >= 8000 and <= 19200 && value.Mask == 0 &&
            (value.Action == NativeBarometerAction.Seed ? value.Sequence == 0 : value.Sequence != 0));
}

// Each gate/value status word packs its command identity and result atomically.
// Never reuse the control-mask acknowledgement for instruments.
public static class NativeBarometerAck
{
    public const uint MaxCommand = uint.MaxValue >> 3;
    public static uint Command(uint value) => value >> 3;
    public static NativeBarometerResult Result(uint value) => (NativeBarometerResult)(value & 7);
    public static bool Valid(uint value) => value == 0 || Command(value) != 0 && Result(value) is >= NativeBarometerResult.Pending and <= NativeBarometerResult.Unsupported;
    public static uint Pack(ulong command, NativeBarometerResult result) => command is > 0 and <= MaxCommand &&
        result is >= NativeBarometerResult.Pending and <= NativeBarometerResult.Unsupported ? (uint)command << 3 | (uint)result :
        throw new ArgumentException("Invalid native barometer acknowledgement.");
}
