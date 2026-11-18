namespace FsCopilot.Native;

// Descriptive backend state only. Native input-mask/read-back flags provide
// simulator proof independently; a UI report never grants control or a lease.
public sealed record NativeInstrumentFeedback(ulong Epoch, ulong Preparation, ulong Revision, ulong SeatRevision,
    uint Phase, uint Pending, uint Protocol, uint Code)
{
    public static bool Valid(NativeInstrumentFeedback? value) => value is not null && value.Epoch != 0 && value.Revision != 0 &&
        value.SeatRevision is > 0 and <= uint.MaxValue && value.Phase <= 8 && value.Pending <= 3 && value.Protocol == 3 && value.Code <= 1 &&
        (value.Preparation != 0 || value.Phase is 0 or 4 or 6 or 8);
}
