namespace FsCopilot.Native;

// Tuned QNH is represented in the event API's 1/16 mbar units, independently of
// STD mode. Indices are not yet verified as the Baron's left/right instruments.
public sealed record NativeAltimeterObservation(ulong Serial, uint Pressure1, uint Pressure2, uint StandardMask, bool Fresh,
    bool Transport = false, bool Profile = false, bool GateReady = false, uint Blocked = 0,
    bool LocalOnly = false, bool Recovering = false, bool Held = false, bool InputReady = false)
{
    public uint Flags => (Fresh ? 1u : 0u) | (Transport ? 2u : 0u) | (Profile ? 4u : 0u) | (GateReady ? 8u : 0u) | Blocked << 4 |
        (LocalOnly ? 64u : 0u) | (Recovering ? 128u : 0u) | (Held ? 256u : 0u) | (InputReady ? 512u : 0u);
    public static bool Valid(NativeAltimeterObservation? value) => value is not null && value.StandardMask <= 3 && value.Blocked <= 3 &&
        (!value.Profile || value.Transport) && (!value.GateReady || value.Profile && value.Fresh) && (value.Blocked == 0 || value.Transport) &&
        (!(value.LocalOnly || value.Recovering || value.Held || value.InputReady) || value.Transport) &&
        (!value.Recovering || value.LocalOnly && value.Held) && (!value.LocalOnly || !value.GateReady) &&
        (value.Serial == 0 ? value.Pressure1 == 0 && value.Pressure2 == 0 && value.StandardMask == 0 && !value.Fresh :
            value.Pressure1 is >= 8000 and <= 19200 && value.Pressure2 is >= 8000 and <= 19200);
}
