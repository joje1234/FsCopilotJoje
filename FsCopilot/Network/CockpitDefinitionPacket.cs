namespace FsCopilot.Network;
using System.Text;
using FsCopilot.Simulation;
public sealed record CockpitDefinitionPacket(Guid Session, string Profile, CockpitChange? Change)
{
    public sealed class Codec : IPacketCodec<CockpitDefinitionPacket>
    {
        public void Encode(CockpitDefinitionPacket p,BinaryWriter w) {
            if(p.Session==Guid.Empty || p.Profile.Length!=32 || !p.Profile.All(c=>c is >= 'a' and <= 'f' or >= '0' and <= '9') ||
                p.Change is not null && !CockpitReplication.Valid(p.Change,512)) throw new InvalidDataException("Invalid cockpit frame.");
            w.Write((byte)2);w.Write(p.Session.ToByteArray());w.Write(Encoding.ASCII.GetBytes(p.Profile));
            w.Write(p.Change is null?(byte)0:p.Change.Request?(byte)1:(byte)2);
            if(p.Change is not { } change) return;
            w.Write(change.Sequence);w.Write((ushort)change.Values.Length);
            foreach(var value in change.Values) {w.Write((ushort)value.Index);w.Write(value.Revision);w.Write(value.Value);w.Write(value.Acknowledged);}
        }
        public CockpitDefinitionPacket Decode(BinaryReader r) {
            if(r.ReadByte()!=2) throw new InvalidDataException("Unsupported cockpit version.");
            var session=r.ReadBytes(16);var hash=r.ReadBytes(32);var kind=r.ReadByte();
            if(session.Length!=16 || hash.Length!=32 || kind>2) throw new InvalidDataException("Invalid cockpit header.");
            var identity=new Guid(session);var profile=Encoding.ASCII.GetString(hash);
            if(identity==Guid.Empty || !profile.All(c=>c is >= 'a' and <= 'f' or >= '0' and <= '9')) throw new InvalidDataException("Invalid cockpit identity.");
            if(kind==0) return new(identity,profile,null);
            var sequence=r.ReadUInt64();var count=r.ReadUInt16();if(count is <1 or >512) throw new InvalidDataException("Invalid cockpit size.");
            var values=new CockpitValue[count];for(var i=0;i<count;i++) values[i]=new(r.ReadUInt16(),r.ReadUInt64(),r.ReadDouble(),r.ReadUInt64());
            var change=new CockpitChange(kind==1,sequence,values);
            if(!CockpitReplication.Valid(change,512)) throw new InvalidDataException("Invalid cockpit values.");
            return new(identity,profile,change);
        }
    }
}
