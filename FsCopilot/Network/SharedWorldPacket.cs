namespace FsCopilot.Network;

using FsCopilot.Native;

// One compact packet per world sample. It is the modern backend's sole
// sequenced/unreliable stream, so separate control groups cannot starve it.
public sealed record SharedWorldPacket(NativeWorldFrame Frame)
{
    public sealed class Codec : IPacketCodec<SharedWorldPacket>
    {
        public void Encode(SharedWorldPacket packet, BinaryWriter writer)
        {
            var frame = packet.Frame.Copy();
            if (!NativeWorldFrame.Valid(frame)) throw new InvalidDataException("Invalid world frame.");
            writer.Write((byte)1); writer.Write(frame.Session.ToByteArray()); writer.Write(frame.Revision); writer.Write(frame.Sequence);
            foreach (var generation in frame.Generation) writer.Write(generation);
            writer.Write(frame.Axes);
            foreach (var value in frame.World) writer.Write(value);
            foreach (var value in frame.Targets) writer.Write(value);
        }
        public SharedWorldPacket Decode(BinaryReader reader)
        {
            if (reader.ReadByte() != 1) throw new InvalidDataException("Unknown world frame version.");
            var bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new InvalidDataException("Truncated world identity.");
            var session = new Guid(bytes); var revision = reader.ReadUInt64(); var sequence = reader.ReadUInt64();
            var generation = new ulong[4]; for (var index = 0; index < 4; ++index) generation[index] = reader.ReadUInt64();
            var axes = reader.ReadUInt32(); var world = new double[12]; var targets = new double[21];
            for (var index = 0; index < 12; ++index) world[index] = reader.ReadDouble();
            for (var index = 0; index < 21; ++index) targets[index] = reader.ReadDouble();
            var frame = new NativeWorldFrame(session, revision, sequence, generation, axes, world, targets);
            if (!NativeWorldFrame.Valid(frame)) throw new InvalidDataException("Invalid world frame.");
            return new(frame);
        }
    }
}
