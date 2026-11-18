using System.Buffers.Binary;
using System.Text;
namespace FsCopilot.Native;
public enum NativeCockpitKind : uint { Status = 1, Apply, Report }
public enum NativeCockpitResult : uint { Idle, Pending, Succeeded, Rejected, Failed }
public sealed record NativeCockpitProgress(uint Phase=0,uint Checked=0,uint Corrected=0,uint Pending=0,uint Failed=0,uint Unavailable=0,string Issues="") {
    public bool Valid => Phase<=4 && Checked<=512 && Corrected<=Checked && Pending<=512 && Failed<=512 && Unavailable<=512 &&
        Checked+Pending+Failed+Unavailable<=512 && Issues is not null && Issues.Length<1024 && Issues.All(c=>c is >= ' ' and <= '~');
}
public sealed record NativeCockpitPacket
{
    public NativeCockpitKind Kind { get; init; } = NativeCockpitKind.Status;
    public string Native { get; init; } = "";
    public string Backend { get; init; } = "";
    public string Session { get; init; } = "";
    public string Profile { get; init; } = "";
    public ulong Serial { get; init; }
    public ulong Command { get; init; }
    public ulong ReadSerial { get; init; }
    public uint Index { get; init; }
    public uint Count { get; init; }
    public NativeCockpitResult Result { get; init; }
    public bool Active { get; init; }
    public bool Linked { get; init; }
    public ulong ResyncRequest {get;init;}
    public ulong ResyncAck {get;init;}
    public NativeCockpitProgress Recovery {get;init;}=new();
    public double[] Values { get; init; } = new double[512];
    public uint[] Valid { get; init; } = new uint[16];
    public string Expression { get; init; } = "";
    public bool Known(int index) => index >= 0 && index < Count && (Valid[index / 32] & (1u << (index % 32))) != 0;
    public NativeCockpitPacket Copy() => this with {Values = Values.ToArray(), Valid = Valid.ToArray()};
}
public sealed record NativeCockpitState(bool Connected, bool Live, NativeCockpitPacket? Packet, string Error);
public interface INativeCockpitClient
{
    NativeCockpitState CockpitState { get; }
    event Action<NativeCockpitState>? CockpitChanged;
    bool TrySendCockpit(NativeCockpitPacket command);
    void SetCockpitFence(NativeCockpitFence? fence);
}
public sealed record NativeCockpitFence(string Native, string Backend, string Session, string Profile) {
    public bool Allows(NativeCockpitPacket p) => p.Kind is NativeCockpitKind.Apply or NativeCockpitKind.Report && p.Native == Native && p.Backend == Backend && p.Session == Session && p.Profile == Profile;
}
public static class NativeCockpitSendGate {
    public static bool Allows(NativeCockpitPacket p, NativeBridgeState main, NativeCockpitState current) => NativeCockpitWire.Valid(p) &&
        p.Kind is NativeCockpitKind.Apply or NativeCockpitKind.Report && main.Live && main.Fresh && main.Snapshot is { } observation && main.Session is {Phase:NativeSessionPhase.Active} session &&
        current.Live && current.Packet is {Active:true} native && p.Native == observation.Native && p.Native == native.Native &&
        p.Backend == main.Backend && p.Backend == session.Backend && p.Session == session.Session && p.Session == native.Session &&
        p.Profile == native.Profile && p.ReadSerial <= native.Serial && native.Serial-p.ReadSerial <= 4 && p.Index < native.Count;
}
public static class NativeCockpitWire
{
    public const int Size = 8192;
    public const string StatusName = "FSC_COCKPIT_STATUS_V2", CommandName = "FSC_COCKPIT_COMMAND_V2";
    private static bool Token(string? text, int capacity) => text is not null && text.Length < capacity && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool Valid(NativeCockpitPacket? p) => p is not null && Enum.IsDefined(p.Kind) && Enum.IsDefined(p.Result) &&
        Token(p.Native,24) && Token(p.Backend,40) && Token(p.Session,40) && Token(p.Profile,64) && p.Count <= 512 && p.Index < 512 &&
        p.Values is {Length:512} && p.Valid is {Length:16} && p.Recovery is {Valid:true} && p.Expression is not null && p.Expression.Length < 2048 && p.Expression.All(c => c is >= ' ' and <= '~') &&
        Enumerable.Range(0,512).All(i => (p.Valid[i/32] & (1u << (i%32))) == 0 || i < p.Count && double.IsFinite(p.Values[i])) &&
        (p.Kind == NativeCockpitKind.Status || p.Command > 0 && p.ReadSerial > 0) &&
        (p.Kind != NativeCockpitKind.Apply || p.Expression.Length > 0 && double.IsFinite(p.Values[0]) && double.IsFinite(p.Values[1]) && Math.Abs(p.Values[0])<=1e12 && Math.Abs(p.Values[1])<=1e12);
    public static byte[] Encode(NativeCockpitPacket p) {
        if (!Valid(p)) throw new InvalidDataException("Invalid cockpit IPC packet.");
        var b = new byte[Size]; void U(int o,uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o),v);
        void L(int o,ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(o),v);
        void T(int o,string v) => Encoding.ASCII.GetBytes(v).CopyTo(b,o);
        U(0,0x31504346); U(4,2); U(8,(uint)p.Kind); U(12,Size); T(16,p.Native); T(40,p.Backend); T(80,p.Session); T(120,p.Profile);
        L(184,p.Serial); L(192,p.Command); L(200,p.ReadSerial); U(208,p.Index); U(212,p.Count); U(216,(uint)p.Result); U(220,p.Active?1u:0u);
        for(var i=0;i<512;i++) L(224+i*8,BitConverter.DoubleToUInt64Bits(p.Values[i]));
        for(var i=0;i<16;i++) U(4320+i*4,p.Valid[i]); T(4384,p.Expression); U(6432,p.Linked?1u:0u);
        L(6436,p.ResyncRequest);L(6444,p.ResyncAck);U(6452,p.Recovery.Phase);U(6456,p.Recovery.Checked);U(6460,p.Recovery.Corrected);
        U(6464,p.Recovery.Pending);U(6468,p.Recovery.Failed);U(6472,p.Recovery.Unavailable);T(6476,p.Recovery.Issues);return b;
    }
    public static bool TryDecode(byte[] b, out NativeCockpitPacket? p) {
        p=null; if(b.Length!=Size) return false;
        uint U(int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)); ulong L(int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o));
        string? T(int o,int n) {
            var end=Array.IndexOf(b,(byte)0,o,n); if(end<0)return null;
            for(var i=o;i<end;i++)if(b[i]>127)return null;
            return b.AsSpan(end,o+n-end).ContainsAnyExcept((byte)0)?null:Encoding.ASCII.GetString(b,o,end-o);
        }
        if(U(0)!=0x31504346 || U(4)!=2 || U(12)!=Size || U(220)>1 || U(6432)>1 || b.AsSpan(7500).ContainsAnyExcept((byte)0)) return false;
        var native=T(16,24);var backend=T(40,40);var session=T(80,40);var profile=T(120,64);var expression=T(4384,2048);
        var issues=T(6476,1024);
        if(native is null || backend is null || session is null || profile is null || expression is null || issues is null) return false;
        var values=new double[512];var valid=new uint[16];
        for(var i=0;i<512;i++) values[i]=BitConverter.UInt64BitsToDouble(L(224+i*8));for(var i=0;i<16;i++) valid[i]=U(4320+i*4);
        var result=new NativeCockpitPacket {Kind=(NativeCockpitKind)U(8),Native=native,Backend=backend,Session=session,Profile=profile,Expression=expression,
            Serial=L(184),Command=L(192),ReadSerial=L(200),Index=U(208),Count=U(212),Result=(NativeCockpitResult)U(216),Active=U(220)==1,Linked=U(6432)==1,Values=values,Valid=valid,
            ResyncRequest=L(6436),ResyncAck=L(6444),Recovery=new(U(6452),U(6456),U(6460),U(6464),U(6468),U(6472),issues)};
        if(!Valid(result)) return false; p=result;return true;
    }
}
