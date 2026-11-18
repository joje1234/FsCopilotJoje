namespace FsCopilot.Network;
using System.Text;
using FsCopilot.Native;
public enum CockpitRecoveryKind : byte { Request=1, Start, Report }
public sealed record CockpitRecoveryPacket(Guid Session,string Profile,Guid Id,CockpitRecoveryKind Kind,NativeCockpitProgress? Progress=null,uint[]? Available=null)
{
    public sealed class Codec:IPacketCodec<CockpitRecoveryPacket> {
        public void Encode(CockpitRecoveryPacket p,BinaryWriter w) {
            if(p.Session==Guid.Empty||p.Id==Guid.Empty||p.Profile.Length!=32||!p.Profile.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f')||
                !Enum.IsDefined(p.Kind)||(p.Kind==CockpitRecoveryKind.Report?p.Progress is not {Valid:true}:p.Progress is not null)||
                (p.Kind==CockpitRecoveryKind.Start?p.Available is not {Length:16}:p.Available is not null))throw new InvalidDataException("Invalid cockpit recovery frame.");
            w.Write((byte)1);w.Write(p.Session.ToByteArray());w.Write(Encoding.ASCII.GetBytes(p.Profile));w.Write(p.Id.ToByteArray());w.Write((byte)p.Kind);
            if(p.Available is {} available)foreach(var mask in available)w.Write(mask);
            if(p.Progress is not {} s)return;
            w.Write(s.Phase);w.Write(s.Checked);w.Write(s.Corrected);w.Write(s.Pending);w.Write(s.Failed);w.Write(s.Unavailable);
            w.Write((ushort)s.Issues.Length);w.Write(Encoding.ASCII.GetBytes(s.Issues));
        }
        public CockpitRecoveryPacket Decode(BinaryReader r) {
            if(r.ReadByte()!=1)throw new InvalidDataException("Unsupported cockpit recovery frame.");
            byte[] Read(int count){var bytes=r.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException();return bytes;}
            var session=new Guid(Read(16));var profile=Encoding.ASCII.GetString(Read(32));var id=new Guid(Read(16));var kind=(CockpitRecoveryKind)r.ReadByte();
            NativeCockpitProgress? p=null;
            uint[]? available=null;if(kind==CockpitRecoveryKind.Start)available=Enumerable.Range(0,16).Select(_=>r.ReadUInt32()).ToArray();
            if(kind==CockpitRecoveryKind.Report) {
                var phase=r.ReadUInt32();var check=r.ReadUInt32();var correct=r.ReadUInt32();var pending=r.ReadUInt32();var failed=r.ReadUInt32();var unavailable=r.ReadUInt32();
                var length=r.ReadUInt16();if(length>=1024)throw new InvalidDataException("Recovery issues too long.");
                var text=Read(length);if(text.Any(b=>b<' '||b>'~'))throw new InvalidDataException("Invalid recovery text.");
                p=new(phase,check,correct,pending,failed,unavailable,Encoding.ASCII.GetString(text));
            }
            var value=new CockpitRecoveryPacket(session,profile,id,kind,p,available);
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);Encode(value,writer);return value;
        }
    }
}
