using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class RewardTests {
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
    static Snapshot Sample() {string c="TEST|0000000000000001|map:1";return new Snapshot {live=true,context=c,mapId=1,rememberNavigation=true,player=new PointData {space=c,units="yards",x=0,y=0,z=0},objects=new[]{new ObjectData {name="Astral Cache",id="F11053EC60004490",entry=5500000,space=c,units="yards",x=3,y=4,z=0}}};}
    static WalletObservation Wallet(long n,string id="test") {return new WalletObservation {valid=true,tokens=n,identity=id};}
    static readonly DateTimeOffset Now=DateTimeOffset.UtcNow;
    static void Scenario(string root,string name,Action<RewardTracker,CacheMemory,Snapshot> test) {var m=new CacheMemory(Path.Combine(root,name+".json"));var s=Sample();m.Update(s,Now);var r=new RewardTracker();r.Update(s,Wallet(100),m,Now);test(r,m,s);}
    static void MemoryLayoutTest() {
        var mem=new Dictionary<uint,byte>();
        Action<uint,byte[]> put=(a,b)=>{for(int i=0;i<b.Length;i++)mem[a+(uint)i]=b[i];};
        Action<uint,uint> u=(a,n)=>put(a,BitConverter.GetBytes(n));
        Func<uint,int,byte[]> read=(a,n)=>Enumerable.Range(0,n).Select(i=>mem[a+(uint)i]).ToArray();
        foreach(uint a in new uint[]{0x100000,0x110000,0x120000,0x130000,0x140000,0x150000,0x160000,0x170000})put(a,new byte[256]);
        u(0xd3f78c,0x100000);mem[0x100008]=8;u(0x100048,0x110000);u(0x100050,5);
        mem[0x110008]=5;u(0x110014,0x130000);mem[0x120008]=5;mem[0x12000b]=1;u(0x120014,0x140000);
        Action<uint,uint,string> key=(slot,str,text)=>{u(slot+16,str);u(slot+24,4);mem[str+8]=4;u(str+16,(uint)text.Length);put(str+20,Encoding.ASCII.GetBytes(text));};
        key(0x130000,0x150000,"ProjectAstral");u(0x130000,0x120000);u(0x130008,5);
        key(0x140000,0x160000,"prestigeTokens");put(0x140000,BitConverter.GetBytes(250.0));u(0x140008,3);
        key(0x140028,0x170000,"_walletPrimed");u(0x140028,1);u(0x140030,1);
        var reader=new WalletMemoryReader(read);var wallet=reader.Read();Check(wallet.valid && wallet.tokens==250,"Typed wallet layout");
        put(0x140000,BitConverter.GetBytes(500.0));Check(reader.Read().tokens==500,"Cached lookup reads fresh values");
        u(0x140028,0);Check(!reader.Read().valid,"Unprimed wallet rejected as baseline");u(0x140028,1);
        u(0x140008,4);bool rejected=false;try{reader.Read();}catch{rejected=true;}Check(rejected,"Wrong value type rejected");u(0x140008,3);
        bool changed=false;var racing=new WalletMemoryReader((a,n)=>{var bytes=read(a,n);if(a==0x140000&&n==40&&!changed){changed=true;put(0x140000,BitConverter.GetBytes(750.0));}return bytes;});
        rejected=false;try{racing.Read();}catch{rejected=true;}Check(rejected,"Concurrent balance change rejected");
    }
    public static int Run(string root) {
        string dir=Path.Combine(Path.GetFullPath(root),"rewards-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reward-test-results.txt");
        try {
            MemoryLayoutTest();
            Scenario(dir,"simultaneous",(r,m,s)=>{s.objects=new ObjectData[0];m.Update(s,Now.AddSeconds(.2));Check(r.Update(s,Wallet(350),m,Now.AddSeconds(.2))!=null && m.Records.Single().looted,"Credit + vanished cache completes");Check(!m.ForNavigation(s).objects.Any(),"Overlay target removed");m.Flush(Now,true);Check(new CacheMemory(Path.Combine(dir,"simultaneous.json")).Records.Single().looted,"Completion persisted");});
            Scenario(dir,"credit-first",(r,m,s)=>{Check(r.Update(s,Wallet(350),m,Now.AddSeconds(.2))==null,"Credit alone not sufficient");s.objects=new ObjectData[0];Check(r.Update(s,Wallet(350),m,Now.AddSeconds(.4))!=null,"Delayed disappearance completes");});
            Scenario(dir,"gone-first",(r,m,s)=>{s.objects=new ObjectData[0];Check(r.Update(s,Wallet(100),m,Now.AddSeconds(.2))==null,"Disappearance alone retained");Check(r.Update(s,Wallet(350),m,Now.AddSeconds(.4))!=null,"Delayed credit completes");});
            Scenario(dir,"flyby",(r,m,s)=>{s.objects=new ObjectData[0];s.player.x=100;Check(r.Update(s,Wallet(350),m,Now.AddSeconds(.2))==null && !m.Records.Single().looted,"Flyby + unrelated credit retained");});
            Scenario(dir,"ambiguous",(r,m,s)=>{var another=new ObjectData {name="Astral Cache",id="F11053EC60004491",entry=5500000,space=s.context,units="yards",x=4,y=4,z=0};s.objects=new[]{s.objects[0],another};m.Update(s,Now);r.Update(s,Wallet(100),m,Now.AddSeconds(.1));s.objects=new ObjectData[0];Check(r.Update(s,Wallet(350),m,Now.AddSeconds(.2))==null && m.Records.All(p=>!p.looted),"Ambiguous nearby caches retained");});
            Scenario(dir,"errors",(r,m,s)=>{s.unreadable=1;r.Update(s,Wallet(350),m,Now.AddSeconds(.2));s.unreadable=0;s.objects=new ObjectData[0];Check(r.Update(s,Wallet(350),m,Now.AddSeconds(.4))==null,"Partial read resets baseline");});
            Scenario(dir,"reload",(r,m,s)=>{s.objects=new ObjectData[0];Check(r.Update(s,Wallet(350,"reloaded"),m,Now.AddSeconds(.2))==null,"UI reload initial balance ignored");});
            Scenario(dir,"reconnect",(r,m,s)=>{s.objects=new ObjectData[0];Check(r.Update(s,Wallet(350),m,Now.AddSeconds(4))==null,"Sampling gap resets baseline");});
            Scenario(dir,"scope",(r,m,s)=>{s.objects=new ObjectData[0];s.context="other";Check(r.Update(s,Wallet(350),m,Now.AddSeconds(.2))==null,"Map/character transition resets baseline");});
            Scenario(dir,"spent",(r,m,s)=>{s.objects=new ObjectData[0];Check(r.Update(s,Wallet(50),m,Now.AddSeconds(.2))==null,"Spending is not reward");});
            Scenario(dir,"stale",(r,m,s)=>{r.Update(s,Wallet(350),m,Now.AddSeconds(.2));for(int i=1;i<7;i++)r.Update(s,Wallet(350),m,Now.AddSeconds(i));s.objects=new ObjectData[0];Check(r.Update(s,Wallet(350),m,Now.AddSeconds(7))==null,"Old credit cannot complete later disappearance");});
            File.WriteAllText(report,"PASS: typed wallet memory fixture, fresh cached values, unprimed/type/race rejection; credit+disappearance in both orders; persistence and overlay removal; flyby, ambiguity, read errors, UI reload, reconnect, map change, spending and stale rewards retained. Synthetic tests, no game input. Real token increase at a cache still requires user playtest.\r\n");return 0;
        }catch(Exception ex){File.WriteAllText(report,ex.ToString());return 1;}
    }
}
