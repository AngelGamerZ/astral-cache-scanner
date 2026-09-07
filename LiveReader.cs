using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// External reads only. No client functions are invoked, no privileges enabled.
// This profile is limited to the inspected executable and verified getter bytes.
public sealed class LiveReader : IDisposable {
    WalletMemoryReader wallet;
    public WalletObservation ReadWallet() {
        RequireBytes(0x817ce0,"a18cf7d3006a006a0150e86170030083c40cc3");
        if(wallet==null)wallet=new WalletMemoryReader(Read);
        return wallet.Read();
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    const string ExpectedHash = "02F38EE484B9B6EF055BBA0C749738DC0905EB8BCF4B310349BB0C6B4FD4D865";
    // Both concrete executables passed the same on-disk and live getter checks.
    const string UpdatedHash = "EDDFF880343A477FAA1480626571C1385442EFFF5E9AA706BC57341F17DCB96F";
    public static bool SupportedHash(string hash) {return hash==ExpectedHash || hash==UpdatedHash;}
    IntPtr handle;
    readonly int pid;
    public int Pid { get { return pid; } }
    public static string ValidateDirectory(string directory) {
        if(String.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory) || Path.GetPathRoot(directory).Length<3) throw new InvalidDataException("Bitte einen vollständigen WoW-Ordner auswählen.");
        string full=Path.GetFullPath(directory),exe=Path.Combine(full,"Wow.exe");
        if(!Directory.Exists(full) || !File.Exists(exe))throw new InvalidDataException("In diesem Ordner wurde keine Wow.exe gefunden. Bitte den Spielordner auswählen.");
        using(var f=File.OpenRead(exe))using(var sha=SHA256.Create())if(!SupportedHash(BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "")))throw new InvalidDataException("Diese Wow.exe passt nicht zum geprüften Astral-Client. Bitte den richtigen Ordner wählen; nach einem Clientupdate muss das Profil geprüft werden.");
        return full;
    }
    public LiveReader(string directory) {
        directory=ValidateDirectory(directory);
        var ps=Process.GetProcessesByName("Wow");
        try {
            if(ps.Length!=1) throw new InvalidOperationException("Genau einen Wow-Prozess öffnen; gefunden: " + ps.Length);
            var p=ps[0];
            string path=p.MainModule.FileName;
            if(!String.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(directory,"Wow.exe")), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Der laufende Client gehört nicht zum gewählten Ordner.");
            if(p.MainModule.BaseAddress.ToInt64()!=0x400000) throw new InvalidOperationException("Nicht unterstützte Ladeadresse des Clients.");
            using(var f=File.OpenRead(path)) using(var sha=SHA256.Create()) if(!SupportedHash(BitConverter.ToString(sha.ComputeHash(f)).Replace("-", ""))) throw new InvalidOperationException("Clientdatei verändert: Dieses Profil muss erneut geprüft werden.");
            pid=p.Id;
            handle=OpenProcess(0x0010,false,pid); // PROCESS_VM_READ, nothing else
            if(handle==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"Lesender Zugriff verweigert. Keine Umgehung versucht.");
            try {
                RequireBytes(0x70cdf0,"8b81a401000085c074078b8090000000c3");
                RequireBytes(0x6e6f10,"558bec8b89d80000008b51108b450889108b51148b49188950048948085dc20400");
                RequireBytes(0x6e6f60,"8b81d8000000d94020c3");
                RequireBytes(0x712d50,"558bec83ec0c83b9a00100000074138b89a00100008b018b40588d55f452ffd0eb1e8b81ec0000008b91e80000008b89f00000008945f88955f4894dfc");
                RequireBytes(0x7126a0,"558bec8b51208b450889108b51248b49288950048948085dc20400");
                RequireBytes(0x70c2e0,"558bec8b49048b91e80000008b450881c1d000000089108b511c8b49208950048948085dc20400");
                RequireBytes(0x51a8ca,"8b1d8c08bd00");
            } catch { Dispose(); throw; }
        } finally { foreach(var p in ps)p.Dispose(); }
    }
    public void Dispose() { if(handle!=IntPtr.Zero) { CloseHandle(handle);handle=IntPtr.Zero; } }
    byte[] Read(uint address,int n) {
        if(handle==IntPtr.Zero) throw new ObjectDisposedException("LiveReader");
        if(address<0x10000 || (ulong)address+(uint)n>0x100000000UL) throw new InvalidDataException("Ungültiger Clientzeiger.");
        var b=new byte[n]; UIntPtr got;
        if(!ReadProcessMemory(handle,new IntPtr((long)address),b,(UIntPtr)(uint)n,out got) || got.ToUInt64()!=(ulong)n) throw new Win32Exception(Marshal.GetLastWin32Error(),"Objektdaten momentan nicht lesbar (Ladewechsel oder Prozessende möglich).");
        return b;
    }
    uint U32(uint a) { return BitConverter.ToUInt32(Read(a,4),0); }
    ulong U64(uint a) { return BitConverter.ToUInt64(Read(a,8),0); }
    void RequireBytes(uint a,string expected) { if(BitConverter.ToString(Read(a,expected.Length/2)).Replace("-", "").ToLowerInvariant()!=expected) throw new InvalidDataException("Clientstruktur verändert: Getter-Prüfung fehlgeschlagen."); }
    string Name(uint pointer) {
        var bytes=new List<byte>();
        for(int i=0;i<192;i++) { byte v=Read(pointer+(uint)i,1)[0]; if(v==0) { string s=new UTF8Encoding(false,true).GetString(bytes.ToArray()); if(s.Length==0 || s.Any(Char.IsControl)) throw new InvalidDataException("Ungültiger Objektname."); return s; } bytes.Add(v); }
        throw new InvalidDataException("Objektname überschreitet das Leselimit.");
    }
    PointData Position(uint address,string space) {
        byte[] b=Read(address,12); float x=BitConverter.ToSingle(b,0),y=BitConverter.ToSingle(b,4),z=BitConverter.ToSingle(b,8);
        if(new[]{x,y,z}.Any(v=>Single.IsNaN(v)||Single.IsInfinity(v)||Math.Abs(v)>100000)) return null;
        return new PointData {space=space,units="yards",x=x,y=y,z=z};
    }
    PointData ObjectPosition(uint ptr,string space) {
        if(U32(U32(ptr)+48)!=0x712d50) return null;
        uint transform=U32(ptr+0x1a0);
        if(transform==0)return Position(ptr+0xe8,space);
        uint getter=U32(U32(transform)+0x58);
        if(getter==0x7126a0)return Position(transform+0x20,space);
        if(getter==0x70c2e0 && U32(transform+4)==ptr)return Position(ptr+0xe8,space);
        return null;
    }
    public Snapshot Scan() {
        var clock=Stopwatch.StartNew();
        string region=null,subregion=null;
        try {RequireBytes(0x5155a3,"a18007bd00");RequireBytes(0x5155d3,"a18407bd00");uint r=U32(0xbd0780),sub=U32(0xbd0784);if(r!=0)region=Name(r);if(sub!=0 && Read(sub,1)[0]!=0)subregion=Name(sub);if(U32(0xbd0780)!=r || U32(0xbd0784)!=sub){region=null;subregion=null;}}catch(InvalidDataException){}catch(Win32Exception){}
        uint connection=U32(0xc79ce0),manager=U32(connection+0x2ed0),first=U32(manager+0xac),ptr=first;
        ulong local=U64(manager+0xc0);
        if(local==0)throw new InvalidOperationException("Nicht in der Spielwelt: bitte einloggen.");
        uint map=U32(manager+0xcc),apiMap=U32(0xbd088c);string realm=null;
        try {realm=Name(0xc79b9e);}catch(InvalidDataException){}catch(Win32Exception){}
        int? mapId=map==apiMap && map<10000?(int?)map:null;
        string context=mapId.HasValue && !String.IsNullOrWhiteSpace(realm)?realm+"|"+local.ToString("X16")+"|map:"+mapId:null;
        bool rememberNavigation=context!=null && (map==0 || map==1 || map==530 || map==571);
        string space=rememberNavigation?context:"process-"+pid+"-manager-"+manager+"-map-"+map;
        var seen=new HashSet<uint>();var objects=new List<ObjectData>();int unreadable=0,gameobjects=0;
        PointData player=null;double? facing=null;bool foundPlayer=false,onTransport=false;
        while(ptr!=0 && (ptr&1)==0) {
            if(seen.Count>=5000 || clock.ElapsedMilliseconds>400)throw new InvalidDataException("Scanlimit erreicht; kein vollständiger Durchlauf.");
            if(!seen.Add(ptr))throw new InvalidDataException("Objektliste während des Lesens verändert (Zyklus).");
            byte[] head=Read(ptr,64);uint type=BitConverter.ToUInt32(head,0x14),desc=BitConverter.ToUInt32(head,8),next=BitConverter.ToUInt32(head,0x3c);ulong guid=BitConverter.ToUInt64(head,0x30);
            if(type>7 || guid==0)throw new InvalidDataException("Objektstruktur nicht plausibel.");
            if(guid==local) {
                if(type!=4 || U64(desc)!=guid)throw new InvalidDataException("Spielerzuordnung nicht konsistent.");
                foundPlayer=true;onTransport=U64(ptr+0x790)!=0;
                // On transports the movement coordinates may be local; don't mix spaces.
                if(!onTransport && U32(U32(ptr)+48)==0x6e6f10) {
                    uint movement=U32(ptr+0xd8);player=Position(movement+0x10,space);
                    if(U32(U32(ptr)+56)==0x6e6f60) { float orientation=BitConverter.ToSingle(Read(movement+0x20,4),0); if(!Single.IsNaN(orientation) && !Single.IsInfinity(orientation) && Math.Abs(orientation)<100)facing=orientation; }
                }
            }
            if(type==5) {
                gameobjects++;
                try {
                    if(U64(desc)!=guid || (U32(desc+8)&0x20)==0)throw new InvalidDataException("GameObject-Deskriptor nicht konsistent.");
                    uint vt=U32(ptr);if(U32(vt+216)!=0x70cdf0)throw new InvalidDataException("Nicht unterstützter Namensgetter.");
                    uint info=U32(ptr+0x1a4);string name=Name(U32(info+0x90));
                    uint entry=U32(desc+12);
                    PointData position=null;try { position=ObjectPosition(ptr,space); } catch(Win32Exception) {} catch(InvalidDataException) {}
                    if(U64(ptr+0x30)!=guid || U32(ptr+8)!=desc || U32(ptr+0x1a4)!=info || U32(ptr+0x14)!=5)throw new InvalidDataException("Objekt während des Lesens ausgetauscht.");
                    objects.Add(new ObjectData { region=region,subregion=subregion,id=guid.ToString("X16"),entry=entry,name=name,space=space,units="yards",x=position==null?null:position.x,y=position==null?null:position.y,z=position==null?null:position.z });
                } catch(Win32Exception) { unreadable++; } catch(InvalidDataException) { unreadable++; } catch(DecoderFallbackException) { unreadable++; }
            }
            ptr=next;
        }
        if(!foundPlayer || U32(0xc79ce0)!=connection || U32(connection+0x2ed0)!=manager || U64(manager+0xc0)!=local || U32(manager+0xac)!=first || U32(manager+0xcc)!=map || U32(0xbd088c)!=apiMap)throw new InvalidDataException("Spielwelt während des Lesens verändert. Nächster Scan folgt.");
        return new Snapshot {region=region,subregion=subregion,schema=1,observedAt=DateTimeOffset.UtcNow.ToString("o"),source="Wow.exe PID "+pid,live=true,player=player,facing=facing,objects=objects.ToArray(),totalObjects=seen.Count,gameObjects=gameobjects,unreadable=unreadable,onTransport=onTransport,scanMilliseconds=clock.ElapsedMilliseconds,context=context,mapId=mapId,rememberNavigation=rememberNavigation};
    }
    public LootObservation ReadLoot() {
        // GetNumLootItems checks this GUID, 18 slot fields (stride 0x20), then money.
        RequireBytes(0x588190,"a1d8a8bf000b05dca8bf007501c333c0568d4802bab4a6bf008d70038d642400837ae0007e038d41ff833a007e028bc1837a20007e038d4101837a40007e038d4102837a60007e038d410383ba80000000007e038d410483c10681c2c000000083ee0175bb833dd0a8bf00005e740383c001c3");
        ulong before=U64(0xbfa8d8);
        if(before==0)return new LootObservation {valid=true};
        byte[] slots=Read(0xbfa694,0x24c);ulong after=U64(0xbfa8d8);
        if(before!=after || BitConverter.ToUInt64(slots,0x244)!=before)return new LootObservation();
        int occupied=0;for(int i=0;i<18;i++)if(BitConverter.ToInt32(slots,i*0x20)>0)occupied++;
        if(BitConverter.ToUInt32(slots,0x23c)>0)occupied++;
        return new LootObservation {valid=true,guid=before.ToString("X16"),remaining=occupied};
    }
}
