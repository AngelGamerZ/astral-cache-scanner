using System;
using System.IO;
using System.Linq;
public static class DesktopTests {
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    public static int Run(string root) {
        string dir=Path.Combine(root,"desktop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"desktop-test-results.txt");
        try {
            var now=DateTimeOffset.UtcNow;string context="TEST|0000000000000001|map:1";
            var point=new ObjectData {name="Astral Cache",entry=5500000,id="F11053EC60000001",space=context,units="yards",x=3,y=4,z=0,region="Testregion",subregion="Testgebiet"};
            var s=new Snapshot {live=true,context=context,mapId=1,player=new PointData {space=context,units="yards",x=0,y=0,z=0},objects=new[]{point}};
            var m=new CacheMemory(Path.Combine(dir,"finds.json"));var completionLog=new DebugJournal();int completionEvents=0;m.CompletionChanged += r => {completionEvents++;completionLog.Add(DebugJournal.CompletionMessage(r));};m.Update(s,now);var p=new ProximityCompletion();p.Update(s,m,now);s.objects=new ObjectData[0];Check(p.Update(s,m,now.AddSeconds(.2)).Length==0,"Wait for confirmed absence");Check(p.Update(s,m,now.AddSeconds(.8)).Length==0,"Not enough absence time");Check(p.Update(s,m,now.AddSeconds(1.3)).Length==1 && m.Records.Single().looted,"Absent near player completes without tokens");
            Check(completionEvents==1,"Proximity emits one completion event");
            string completionPath=Path.Combine(dir,"completion.txt");completionLog.Export(completionPath);string completionText=File.ReadAllText(completionPath);
            Check(completionText.Contains("Kiste als gelootet markiert") && completionText.Contains("Testregion") && completionText.Contains(point.id) && completionText.Contains("X 3 / Y 4 / Z 0") && completionText.Contains("1 Sekunde"),"Export includes completion, location, object and reason");
            p.Update(s,m,now.AddSeconds(1.5));Check(completionEvents==1,"Completed scan does not repeat event");
            m.Mark(CacheMemory.Key(s,point),true,"Repeated",now);Check(completionEvents==1,"Repeated mark does not repeat event");
            m.Mark(CacheMemory.Key(s,point),false,null,now);p.Reset();s.player.x=100;p.Update(s,m,now);p.Update(s,m,now.AddSeconds(.6));Check(p.Update(s,m,now.AddSeconds(1.2)).Length==0,"Flyby retains location");
            s.player.x=0;p.Reset();p.Update(s,m,now);s.unreadable=1;p.Update(s,m,now.AddSeconds(.7));s.unreadable=0;Check(p.Update(s,m,now.AddSeconds(1.3)).Length==0,"Read failure breaks absence window");
            p.Reset();p.Update(s,m,now);Check(p.Update(s,m,now.AddSeconds(3)).Length==0,"Loading gap breaks absence window");
            var db=new LocationDatabase(Path.Combine(dir,"locations.json"));db.Collect(m.Records);db.Flush(true);string json=Path.Combine(dir,"export.json");db.ExportJson(json);var loaded=new LocationDatabase(json);Check(loaded.Records.Single().region=="Testregion" && loaded.Records.Single().subregion=="Testgebiet","Region persistence and JSON roundtrip");string lua=Path.Combine(dir,"export.lua");db.ExportLua(lua);Check(File.ReadAllText(lua).Contains("regionSource"),"Lua region metadata");
            Check(ReleaseInfo.Describe("{\"tag_name\":\"v1.6.0\"}").StartsWith("Update verfügbar"),"New release");Check(ReleaseInfo.Describe("{\"tag_name\":\"v1.5.0\"}").StartsWith("Aktuell"),"Same release");Check(ReleaseInfo.Describe("{\"tag_name\":\"v1.0.0\"}").StartsWith("Installiert"),"Do not downgrade");
            bool rejected=false;try{ReleaseInfo.Describe("{\"tag_name\":\"v9.0.0\",\"draft\":true}");}catch{rejected=true;}Check(rejected,"Draft rejected");
            var debug=new DebugJournal();for(int i=0;i<1600;i++)debug.Add("Entry "+i);debug.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+" |0000000000000001|map:1");string log=Path.Combine(dir,"debug.txt");debug.Export(log);string text=File.ReadAllText(log);Check(text.Contains("%USERPROFILE%") && !text.Contains("0000000000000001") && !text.Contains("Entry 0\r\n"),"Debug export bounded and redacted");
            File.WriteAllText(report,"PASS: nearby disappearance without tokens; flyby/errors/loading gaps retained; region JSON/Lua persistence; update version comparisons/draft rejection; bounded redacted debug export. Synthetic isolated tests.\r\n");return 0;
        }catch(Exception ex){File.WriteAllText(report,ex.ToString());return 1;}
    }
}

