namespace FsCopilot.Native;

public sealed record NativeWorldFrame(Guid Session, ulong Revision, ulong Sequence, ulong[] Generation, uint Axes, double[] World, double[] Targets)
{
    public NativeWorldFrame Copy() => this with { Generation = Generation.ToArray(), World = World.ToArray(), Targets = Targets.ToArray() };
    public static bool ValidWorld(double[]? values) => values is { Length: 12 } && values.All(double.IsFinite) &&
        Math.Abs(values[0]) <= Math.PI / 2 + 0.000001 && Math.Abs(values[1]) <= Math.PI + 0.000001 &&
        Math.Abs(values[3]) <= Math.PI + 0.000001 && Math.Abs(values[4]) <= Math.PI * 2 + 0.000001 && Math.Abs(values[5]) <= Math.PI * 2 + 0.000001;
    public static bool Valid(NativeWorldFrame? frame) => frame is not null && frame.Session != Guid.Empty && frame.Revision != 0 && frame.Sequence != 0 &&
        frame.Generation is { Length: 4 } && frame.Generation.All(value => value != 0) && (frame.Axes & 3u) == 3 && (frame.Axes >> 21) == 0 &&
        ValidWorld(frame.World) && frame.Targets is { Length: 21 } && frame.Targets.Select((value, axis) =>
            double.IsFinite(value) && (value is >= 0 and <= 1 || value == -1 && (frame.Axes & (1u << axis)) == 0)).All(value => value);
}
