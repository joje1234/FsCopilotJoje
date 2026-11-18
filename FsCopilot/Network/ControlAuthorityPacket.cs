namespace FsCopilot.Network;

using Ownership;

public sealed record ControlAuthorityPacket(byte[] Payload)
{
    public sealed class Codec : IPacketCodec<ControlAuthorityPacket>
    {
        public void Encode(ControlAuthorityPacket packet, BinaryWriter writer)
        {
            if (!AuthorityWire.TryDecode(packet.Payload, out _)) throw new InvalidDataException("Invalid control authority packet.");
            writer.Write(packet.Payload.Length); writer.Write(packet.Payload);
        }
        public ControlAuthorityPacket Decode(BinaryReader reader)
        {
            var size = reader.ReadInt32();
            if (size is <= 0 or > AuthorityWire.MaxBytes) throw new InvalidDataException("Invalid control authority size.");
            var bytes = reader.ReadBytes(size);
            if (bytes.Length != size || !AuthorityWire.TryDecode(bytes, out _)) throw new InvalidDataException("Invalid control authority packet.");
            return new(bytes);
        }
    }
}
