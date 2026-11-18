namespace FsCopilot.Network;

using Ownership;

public sealed record SessionHandshakePacket(byte[] Payload)
{
    public sealed class Codec : IPacketCodec<SessionHandshakePacket>
    {
        public void Encode(SessionHandshakePacket packet, BinaryWriter writer)
        {
            if (!SessionWire.TryDecode(packet.Payload, out _)) throw new InvalidDataException("Invalid session handshake.");
            writer.Write(packet.Payload.Length); writer.Write(packet.Payload);
        }
        public SessionHandshakePacket Decode(BinaryReader reader)
        {
            var size = reader.ReadInt32();
            if (size is <= 0 or > SessionWire.MaxBytes) throw new InvalidDataException("Invalid handshake size.");
            var bytes = reader.ReadBytes(size);
            if (bytes.Length != size || !SessionWire.TryDecode(bytes, out _)) throw new InvalidDataException("Invalid session handshake.");
            return new(bytes);
        }
    }
}
