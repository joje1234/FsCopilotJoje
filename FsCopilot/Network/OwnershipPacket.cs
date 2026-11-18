namespace FsCopilot.Network;

using Ownership;

public sealed record OwnershipPacket(byte[] Payload)
{
    public sealed class Codec : IPacketCodec<OwnershipPacket>
    {
        public void Encode(OwnershipPacket packet, BinaryWriter writer)
        {
            if (!OwnershipWire.TryDecode(packet.Payload, out _)) throw new InvalidDataException("Invalid ownership packet.");
            writer.Write(packet.Payload.Length);
            writer.Write(packet.Payload);
        }

        public OwnershipPacket Decode(BinaryReader reader)
        {
            var size = reader.ReadInt32();
            if (size is <= 0 or > OwnershipWire.MaxBytes) throw new InvalidDataException("Invalid ownership packet size.");
            var payload = reader.ReadBytes(size);
            if (payload.Length != size || !OwnershipWire.TryDecode(payload, out _)) throw new InvalidDataException("Invalid ownership packet.");
            return new(payload);
        }
    }
}
