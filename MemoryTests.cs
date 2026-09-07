using System;
using System.IO;
using System.Linq;

public static class MemoryTests {
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    static Snapshot Sample() {
        string context="TEST|0000000000000001|map:1";
        return new Snapshot {live=true,context=context,mapId=1,rememberNavigation=true,facing=0,player=new PointData {space=context,units="yards",x=0,y=0,z=0},objects=new[]{new ObjectData {name="Astral Cache",id="F11053EC60004490",entry=5500000,space=context,units="yards",x=30,y=40,z=0}}};
    }
    public static int Run(string root) {
        string directory=Path.Combine(Path.GetFullPath(root),"memory-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string file=Path.Combine(directory,"finds.json"),report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-test-results.txt");
        try {
            DateTimeOffset now=DateTimeOffset.UtcNow;var s=Sample();var m=new CacheMemory(file);m.Update(s,now);
            string key=CacheMemory.Key(s,s.objects[0]);Check(m.Records.Count()==1,"First discovery stored");Check(m.Flush(now,true) && File.Exists(file),"Persistence");
            m.Update(s,now.AddSeconds(1));Check(m.Records.Count()==1,"No duplicate while visible");
            s.objects=new ObjectData[0];s.player.x=100;m.Update(s,now.AddSeconds(3));
            var navigation=m.ForNavigation(s);Check(navigation.objects.Length==1 && navigation.objects[0].remembered,"Fly past keeps remembered target");
            Check(Navigation.Select(navigation).Distance>80,"Remembered distance follows current player");
            s.unreadable=1;m.Update(s,now.AddSeconds(4));Check(m.Records.Single().looted==false,"Unreadable object does not mean looted");s.unreadable=0;
            m.Flush(now.AddSeconds(5),true);m=new CacheMemory(file);Check(m.ForNavigation(s).objects.Length==1,"Restart retains navigation");
            s.context="OTHER";s.player.space="OTHER";Check(m.ForNavigation(s).objects.Length==0,"Other realm/character/map excluded");s=Sample();s.objects=new ObjectData[0];s.rememberNavigation=false;Check(m.ForNavigation(s).objects.Length==0,"No remembered direction in unverified instance scope");s.rememberNavigation=true;
            m.ObserveLoot(s,new LootObservation {valid=true,guid="F110000000000001",remaining=1},now);Check(m.ObserveLoot(s,new LootObservation {valid=true,guid="F110000000000001",remaining=0},now)==null,"Unrelated loot ignored");
            string guid="F11053EC60004490";
            m.ObserveLoot(s,new LootObservation {valid=true,guid=guid,remaining=2},now);
            m.ObserveLoot(s,new LootObservation {valid=true},now);Check(!m.Records.Single().looted,"Closing unlooted window retains find");
            for(int i=0;i<3;i++)m.ObserveLoot(s,new LootObservation {valid=true,guid=guid,remaining=0},now);Check(!m.Records.Single().looted,"Empty without observed contents is inconclusive");
            m.ObserveLoot(s,new LootObservation {valid=true,guid=guid,remaining=1},now);
            m.ObserveLoot(s,new LootObservation {valid=true,guid=guid,remaining=0},now);Check(!m.Records.Single().looted,"Single empty sample insufficient");
            Check(m.ObserveLoot(s,new LootObservation {valid=true,guid=guid,remaining=0},now)==key,"Observed full loot completion");
            Check(m.Records.Single().looted && m.ForNavigation(s).objects.Length==0,"Completed target removed from navigation");
            s=Sample();m.Update(s,now.AddSeconds(6));Check(m.Records.Single().looted && m.ForNavigation(s).objects.Length==0,"Still-rendered completed object does not reappear");
            m.Flush(now.AddSeconds(6),true);m=new CacheMemory(file);Check(m.Records.Single().looted,"Completion survives restart");
            s.objects=new ObjectData[0];m.Update(s,now.AddSeconds(7));m.Update(s,now.AddSeconds(10));s=Sample();m.Update(s,now.AddSeconds(11));Check(!m.Records.Single().looted,"New appearance after confirmed absence reopens");
            Check(m.Mark(key,true,"manual",now) && m.Records.Single().looted,"Manual completion");Check(m.Mark(key,false,null,now) && !m.Records.Single().looted,"Undo completion");
            // A completion based on actual disappearance must survive an immediate restart.
            m.Mark(key,true,"Confirmed disappearance",now.AddSeconds(12),true);Check(m.Flush(now.AddSeconds(12),true),"Confirmed disappearance saved");
            m=new CacheMemory(file);Check(m.Records.Single().looted && m.Records.Single().absentAfterLoot,"Persisted disappearance evidence survives restart");
            s=Sample();m.Update(s,now.AddSeconds(20));Check(!m.Records.Single().looted && m.ForNavigation(s).objects.Length==1,"Same-GUID respawn after immediate restart reopens");
            m.Mark(key,true,"Manual completion while still visible",now.AddSeconds(21));m.Flush(now.AddSeconds(21),true);m=new CacheMemory(file);m.Update(s,now.AddSeconds(22));
            Check(m.Records.Single().looted && !m.Records.Single().absentAfterLoot && m.ForNavigation(s).objects.Length==0,"No disappearance evidence: still-loaded looted object remains suppressed after restart");
            m.Mark(key,false,null,now.AddSeconds(23),true);Check(!m.Records.Single().absentAfterLoot,"Reopening clears disappearance evidence");
            var empty=new CacheMemory(Path.Combine(directory,"no-export.json"));s.demo=true;empty.Update(s,now);Check(!empty.Records.Any(),"No demo persistence");s.demo=false;s.live=false;empty.Update(s,now);Check(!empty.Records.Any(),"No export persistence");
            string blocked=Path.Combine(directory,"blocked");File.WriteAllText(blocked,"test");var failing=new CacheMemory(Path.Combine(blocked,"finds.json"));failing.Update(Sample(),now);Check(!failing.Flush(now,true) && failing.SaveError!=null && failing.Records.Count()==1,"Disk failure retains unsaved memory and reports error");File.Delete(blocked);Check(failing.Flush(now.AddSeconds(3),true),"Persistence recovers after disk failure");
            File.WriteAllText(file,"{invalid");bool rejected=false;try {new CacheMemory(file);}catch {rejected=true;}Check(rejected && File.ReadAllText(file)=="{invalid","Corrupt original preserved");
            File.WriteAllText(report,"PASS: discovery/deduplication; retained after fly-by/partial scan/restart; updated distance; map/realm/character and instance separation; unrelated loot ignored; early close and unobserved autoloot retain find; two empty observations after contents complete exact GUID; no immediate resurrection; completion persistence; confirmed-disappearance restart/respawn and still-loaded suppression; reappearance; manual completion/undo; no demo/export persistence; disk failure/recovery; corrupt data preserved. Synthetic isolated fixtures, no game input.\r\n");return 0;
        }catch(Exception ex){File.WriteAllText(report,ex.ToString());return 1;}
    }
}
