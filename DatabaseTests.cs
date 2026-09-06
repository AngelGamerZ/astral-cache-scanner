using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Web.Script.Serialization;

public static class DatabaseTests {
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action,string message) {bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message);}
    static readonly DateTimeOffset Now=DateTimeOffset.UtcNow.AddMinutes(-1);
    static CacheLocation Point(double x=12.34) {return new CacheLocation {realm="Astral Test",mapId=1,entry=5500000,x=x,y=-42.56,z=7.89,firstSeen=Now.AddDays(-2).ToString("o"),lastSeen=Now.ToString("o")};}
    static string Fixture(string dir,string name,params CacheLocation[] points) {
        string path=Path.Combine(dir,name+".json");File.WriteAllText(path,new JavaScriptSerializer().Serialize(new LocationArchive {format="astral-cache-locations",schema=1,coordinates="world",units="yards",locations=points.ToList()}));return path;
    }
    public static int Run(string root) {
        string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"database-test-results.txt");
        string dir=Path.Combine(Path.GetFullPath(root),"database-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try {
            string local=Path.Combine(dir,"locations.json");var db=new LocationDatabase(local);
            string first=Fixture(dir,"first",Point(),Point(12.33));Check(db.Import(new[]{first})==1,"Same rounded location deduplicated");
            Check(db.Import(new[]{first,first})==0 && db.Records.Count()==1,"Repeated import is idempotent");
            var later=Point();later.firstSeen=Now.AddDays(-4).ToString("o");later.lastSeen=Now.AddSeconds(20).ToString("o");db.Import(new[]{Fixture(dir,"later",later)});
            Check(DateTimeOffset.Parse(db.Records.Single().firstSeen)==Now.AddDays(-4) && DateTimeOffset.Parse(db.Records.Single().lastSeen)==Now.AddSeconds(20),"Timestamp union");
            var realm=Point();realm.realm="Other realm";var map=Point();map.mapId=0;var entry=Point();entry.entry=5500001;var height=Point();height.z=18;var unknownHeight=Point();unknownHeight.z=null;
            db.Import(new[]{Fixture(dir,"separate",realm,map,entry,height,unknownHeight,Point(13))});Check(db.Records.Count()==7,"Realm/map/entry/height/nearby position separation");
            Check(new LocationDatabase(local).Records.Count()==7,"Persistence after restart");
            string before=File.ReadAllText(local);var bad=Point();bad.x=Double.PositiveInfinity;string invalid=Fixture(dir,"bad",bad);string extra=Fixture(dir,"extra",Point(50));
            Reject(()=>db.Import(new[]{extra,invalid}),"Invalid batch rejected");Check(File.ReadAllText(local)==before && db.Records.Count()==7,"No partial import or disk mutation");
            string missing=Path.Combine(dir,"missing.json");File.WriteAllText(missing,File.ReadAllText(first).Replace("\"x\":12.34,",""));Reject(()=>db.Import(new[]{missing}),"Missing x is not zero");
            string wrong=Path.Combine(dir,"wrong.json");File.WriteAllText(wrong,File.ReadAllText(first).Replace("\"world\"","\"zone-normalized\""));Reject(()=>db.Import(new[]{wrong}),"Wrong coordinate system rejected");
            var time=Point();time.firstSeen=Now.AddDays(1).ToString("o");Reject(()=>db.Import(new[]{Fixture(dir,"future",time)}),"Future/inverted dates rejected");
            string exported=Path.Combine(dir,"export.json");db.ExportJson(exported);Check(new LocationDatabase(exported).Records.Count()==7,"JSON round trip");
            var reverse=new LocationDatabase(Path.Combine(dir,"reverse.json"));reverse.Import(new[]{Fixture(dir,"later-again",later),first});Check(reverse.Records.Single().firstSeen==db.Records.First(p=>p.realm=="Astral Test"&&p.mapId==1&&p.entry==5500000&&p.x==12.3&&p.z==7.9).firstSeen,"Order-independent union");
            var memory=new CacheMemory(Path.Combine(dir,"finds.json"));string context="Astral Test|0000000000001234|map:1";
            var s=new Snapshot {live=true,context=context,mapId=1,rememberNavigation=true,objects=new[]{new ObjectData {name="Astral Cache",id="F11053EC60000001",entry=5500000,space=context,units="yards",x=20,y=30,z=40}}};
            memory.Update(s,Now);memory.Mark(CacheMemory.Key(s,s.objects[0]),true,"test",Now);Check(db.Collect(memory.Records)==1,"Looted local finds collected");
            Check(db.Collect(memory.Records)==0,"Local collection idempotent");db.Flush(true);db.ExportJson(exported);string data=File.ReadAllText(exported);Check(!data.Contains("0000000000001234") && !data.Contains("F11053EC60000001") && !data.Contains("looted"),"No character/object GUID or private loot state exported");
            var isolated=new CacheMemory(Path.Combine(dir,"isolated.json"));Check(!isolated.Records.Any(),"Atlas import does not create active finds");
            var rec=memory.Records.Single();rec.location.space="session";Check(db.Collect(new[]{rec})==0,"Session-relative coordinates excluded");
            var evil=Point(100);evil.realm="Astral \"; error('injected') -- Ä";db.Import(new[]{Fixture(dir,"escape",evil)});
            CultureInfo culture=Thread.CurrentThread.CurrentCulture;
            try {Thread.CurrentThread.CurrentCulture=new CultureInfo("de-DE");string lua=Path.Combine(dir,"AstralCacheLocations.lua");db.ExportLua(lua);string text=File.ReadAllText(lua);Check(text.Contains("x = 12.3") && !text.Contains("error('injected')") && text.Contains("\\034") && text.Contains("\\195\\132") && text.Contains("z = nil"),"Lua decimal culture, quoting, UTF8 and missing height");}
            finally {Thread.CurrentThread.CurrentCulture=culture;}
            Reject(()=>db.ExportLua(local),"Cannot overwrite active DB with export");
            string blocked=Path.Combine(dir,"blocked");File.WriteAllText(blocked,"blocker");var failing=new LocationDatabase(Path.Combine(blocked,"locations.json"));Reject(()=>failing.Import(new[]{first}),"Write failure propagated");Check(!failing.Records.Any(),"Write failure does not commit import");
            string corrupt=Path.Combine(dir,"corrupt.json");File.WriteAllText(corrupt,"broken");Reject(()=>new LocationDatabase(corrupt),"Corrupt local DB rejected");Check(File.ReadAllText(corrupt)=="broken","Corrupt original preserved");
            File.WriteAllText(report,"PASS: local/looted collection; repeated import deduplication; timestamp union; map/realm/entry/height separation; restart; all-or-nothing multi-file validation and persistence; missing coordinates and wrong coordinate systems rejected; JSON round trip; GUID/privacy separation; no active-find import; instance-space exclusion; Lua quoting/UTF8/locale; protected paths; write failure and corrupt data preservation. Isolated synthetic fixtures, no game input.\r\n");return 0;
        }catch(Exception ex){File.WriteAllText(report,ex.ToString());return 1;}
    }
}
