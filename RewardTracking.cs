using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public sealed class WalletObservation {public bool valid;public long tokens;public string identity;}

// Read only the existing ProjectAstral wallet. No Lua execution or addon changes.
public sealed class WalletMemoryReader {
    readonly Func<uint,int,byte[]> read;
    uint cachedState,cachedGlobals,cachedNodes,cachedSlot;byte cachedSize;
    public WalletMemoryReader(Func<uint,int,byte[]> reader) {read=reader;}
    static uint U(byte[] b,int offset=0){return BitConverter.ToUInt32(b,offset);}
    uint U32(uint a){return U(read(a,4));}
    byte[] Table(uint address) {
        var h=read(address,36);if(h[8]!=5 || h[11]>17)throw new InvalidDataException("Lua-Tabelle nicht verifiziert.");return h;
    }
    bool SameTable(uint table,byte[] before) {var after=Table(table);return before[11]==after[11] && U(before,20)==U(after,20);}
    bool KeyMatches(byte[] node,string key) {
        if(U(node,24)!=4)return false;
        uint str=U(node,16);var h=read(str,20);
        return h[8]==4 && U(h,16)==key.Length && Encoding.ASCII.GetString(read(str+20,key.Length))==key;
    }
    uint Find(uint table,byte[] header,string name) {
        uint nodes=U(header,20);int count=1<<header[11];byte[] data=read(nodes,checked(count*40));
        for(int i=0;i<count;i++) {
            int offset=i*40;if(U(data,offset+24)!=4)continue;
            byte[] node=new byte[40];Buffer.BlockCopy(data,offset,node,0,40);
            if(KeyMatches(node,name))return checked(nodes+(uint)offset);
        }
        throw new InvalidDataException("ProjectAstral-Wert noch nicht verfügbar: "+name);
    }
    byte[] Value(uint slot,string key,uint type) {
        byte[] node=read(slot,40);if(!KeyMatches(node,key) || U(node,8)!=type)throw new InvalidDataException("Wallet-Typ oder Tabellenplatz verändert.");return node;
    }
    public WalletObservation Read() {
        uint state=U32(0xd3f78c);var stateHeader=read(state,88);
        if(stateHeader[8]!=8 || U(stateHeader,80)!=5)throw new InvalidDataException("Lua-Zustand nicht verifiziert.");
        uint globals=U(stateHeader,72);byte[] gh=Table(globals);
        if(state!=cachedState || globals!=cachedGlobals || U(gh,20)!=cachedNodes || gh[11]!=cachedSize || cachedSlot==0) {
            cachedSlot=Find(globals,gh,"ProjectAstral");cachedState=state;cachedGlobals=globals;cachedNodes=U(gh,20);cachedSize=gh[11];
        }
        uint pa;
        try {pa=U(Value(cachedSlot,"ProjectAstral",5));}catch {cachedSlot=0;throw;}
        var ph=Table(pa);uint tokenSlot=Find(pa,ph,"prestigeTokens"),primedSlot=Find(pa,ph,"_walletPrimed");
        byte[] token=Value(tokenSlot,"prestigeTokens",3);bool primed=U(Value(primedSlot,"_walletPrimed",1))==1;
        double amount=BitConverter.ToDouble(token,0);
        if(Double.IsNaN(amount) || Double.IsInfinity(amount) || amount<0 || amount>1000000000000 || amount!=Math.Truncate(amount))throw new InvalidDataException("Ungültiger Tokenstand.");
        if(!SameTable(pa,ph) || !SameTable(globals,gh) || U32(0xd3f78c)!=state || U32(state+72)!=globals || U32(state+80)!=5 || U(Value(cachedSlot,"ProjectAstral",5))!=pa || BitConverter.ToDouble(Value(tokenSlot,"prestigeTokens",3),0)!=amount || (U(Value(primedSlot,"_walletPrimed",1))==1)!=primed)throw new InvalidDataException("Wallet während des Lesens verändert.");
        return new WalletObservation {valid=primed,tokens=(long)amount,identity=state.ToString("X8")+":"+pa.ToString("X8")};
    }
}

// A fresh credit is consumed only by one nearby cache that also disappears.
public sealed class RewardTracker {
    sealed class Nearby {public ObjectData point;public DateTimeOffset seen;}
    readonly Dictionary<string,Nearby> nearby=new Dictionary<string,Nearby>();
    string context,identity,pending;long balance,gain;DateTimeOffset previous,pendingAt;bool primed;
    public void Reset(){nearby.Clear();context=null;identity=null;pending=null;primed=false;}
    static double? Distance(PointData player,ObjectData point) {
        if(player==null || point==null || player.space!=point.space || player.units!="yards" || point.units!="yards" || !player.x.HasValue || !player.y.HasValue || !player.z.HasValue || !point.x.HasValue || !point.y.HasValue || !point.z.HasValue)return null;
        double x=player.x.Value-point.x.Value,y=player.y.Value-point.y.Value,z=player.z.Value-point.z.Value,d=Math.Sqrt(x*x+y*y+z*z);
        return Double.IsNaN(d) || Double.IsInfinity(d)?(double?)null:d;
    }
    public string Update(Snapshot s,WalletObservation w,CacheMemory memory,DateTimeOffset now) {
        if(s==null || !s.live || s.demo || s.context==null || s.onTransport || w==null || !w.valid || memory==null || s.unreadable!=0) {Reset();return null;}
        bool baseline=!primed || context!=s.context || identity!=w.identity || (now-previous).TotalSeconds>2 || now<previous;
        if(baseline){Reset();context=s.context;identity=w.identity;}
        foreach(var p in s.objects.Where(o=>Reader.IsCache(o.name) && !memory.Completed(s,o))) {
            double? d=Distance(s.player,p);if(d.HasValue && d<=15)nearby[CacheMemory.Key(s,p)]=new Nearby {point=p,seen=now};
        }
        foreach(string key in nearby.Where(p=>(now-p.Value.seen).TotalSeconds>5 || !Distance(s.player,p.Value.point).HasValue || Distance(s.player,p.Value.point)>20).Select(p=>p.Key).ToArray())nearby.Remove(key);
        if(!baseline && w.tokens>balance) {
            var choices=nearby.Where(p=>(now-p.Value.seen).TotalSeconds<=3).ToArray();
            pending=null;
            if(choices.Length==1) {pending=choices[0].Key;pendingAt=now;gain=w.tokens-balance;}
        }
        balance=w.tokens;previous=now;primed=true;
        if(pending!=null) {
            if((now-pendingAt).TotalSeconds>5 || !nearby.ContainsKey(pending))pending=null;
            else if(!s.objects.Any(o=>CacheMemory.Key(s,o)==pending)) {
                string key=pending;pending=null;nearby.Remove(key);
                if(memory.Mark(key,true,"Token-Gutschrift +"+gain+" und Kiste in der Nähe verschwunden",now,true))return key;
            }
        }
        return null;
    }
}
