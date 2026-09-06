using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

public sealed class LootObservation {public bool valid;public string guid;public int remaining;}
public sealed class SavedCache {
    public string context {get;set;}
    public int? mapId {get;set;}
    public ObjectData location {get;set;}
    public string firstSeen {get;set;}
    public string lastSeen {get;set;}
    public bool looted {get;set;}
    public string completedAt {get;set;}
    public string completionReason {get;set;}
    public bool absentAfterLoot {get;set;}
    public string Key {get {return context+"|"+location.id;}}
}
public sealed class CacheArchive {public int schema {get;set;} public List<SavedCache> finds {get;set;}}
public sealed class CacheMemory {
    readonly string file;
    readonly Dictionary<string,SavedCache> records=new Dictionary<string,SavedCache>();
    readonly HashSet<string> present=new HashSet<string>();
    readonly Dictionary<string,DateTimeOffset> absentSince=new Dictionary<string,DateTimeOffset>();
    bool dirty;DateTimeOffset lastSave;
    string lootKey;bool sawContents;int emptySamples;
    public IEnumerable<SavedCache> Records {get {return records.Values;}}
    public string SaveError {get;private set;}
    public static string DefaultFile {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AstralScanner","finds.json");}}
    public CacheMemory(string path) {
        file=path;
        if(!File.Exists(file))return;
        if(new FileInfo(file).Length>10000000)throw new InvalidDataException("Funddatei größer als 10 MB. Original bleibt erhalten.");
        var archive=new JavaScriptSerializer {MaxJsonLength=10000000}.Deserialize<CacheArchive>(File.ReadAllText(file));
        if(archive==null || archive.schema!=1 || archive.finds==null || archive.finds.Count>10000)throw new InvalidDataException("Ungültige Funddatei. Original bleibt erhalten.");
        foreach(var r in archive.finds) {
            DateTimeOffset t;
            if(r==null || String.IsNullOrWhiteSpace(r.context) || r.context.Length>512 || r.location==null || !Reader.IsCache(r.location.name) || String.IsNullOrWhiteSpace(r.location.id) || !System.Text.RegularExpressions.Regex.IsMatch(r.location.id,@"\A[0-9A-F]{16}\z") || !DateTimeOffset.TryParse(r.lastSeen,out t) || records.ContainsKey(r.Key))throw new InvalidDataException("Ungültiger gespeicherter Fund. Original bleibt erhalten.");
            records.Add(r.Key,r);
        }
    }
    public static string Key(Snapshot s,ObjectData o) {return s.context+"|"+o.id;}
    public bool Completed(Snapshot s,ObjectData o) {SavedCache r;return s.context!=null && records.TryGetValue(Key(s,o),out r) && r.looted;}
    public bool IsPresent(SavedCache r) {return present.Contains(r.Key);}
    public void LostLiveData() {present.Clear();absentSince.Clear();ResetLoot();}
    public void Update(Snapshot s,DateTimeOffset now) {
        present.Clear();
        if(!s.live || s.demo || s.context==null) {ResetLoot();return;}
        foreach(var o in s.objects.Where(o=>Reader.IsCache(o.name))) {
            string key=Key(s,o);present.Add(key);absentSince.Remove(key);
            SavedCache r;
            if(!records.TryGetValue(key,out r)) {
                if(records.Count>=10000)throw new InvalidDataException("Fundlimit erreicht. Bitte Funddatei archivieren.");
                r=new SavedCache {context=s.context,mapId=s.mapId,location=Copy(o),firstSeen=now.ToString("o"),lastSeen=now.ToString("o")};records.Add(key,r);dirty=true;
            }
            if(r.looted) {
                if(!r.absentAfterLoot)continue;
                r.looted=false;r.absentAfterLoot=false;r.completedAt=null;r.completionReason=null;r.firstSeen=now.ToString("o");dirty=true;
            }
            if((now-DateTimeOffset.Parse(r.lastSeen)).TotalSeconds>=1) {r.lastSeen=now.ToString("o");dirty=true;}
            // Preserve known coordinates if this one scan cannot read them.
            if(o.region!=null && (r.location.region!=o.region || r.location.subregion!=o.subregion)){r.location.region=o.region;r.location.subregion=o.subregion;dirty=true;}
            if(o.x.HasValue && o.y.HasValue && (r.location.x!=o.x || r.location.y!=o.y || r.location.z!=o.z || r.location.space!=o.space)) {r.location=Copy(o);dirty=true;}
        }
        if(s.unreadable==0)foreach(var r in records.Values.Where(r=>r.context==s.context && r.looted && !r.absentAfterLoot && !present.Contains(r.Key))) {
            DateTimeOffset since;if(!absentSince.TryGetValue(r.Key,out since))absentSince[r.Key]=now;
            else if((now-since).TotalSeconds>=2) {r.absentAfterLoot=true;dirty=true;}
        }
    }
    public bool Mark(string key,bool looted,string reason,DateTimeOffset now) {
        SavedCache r;if(!records.TryGetValue(key,out r))return false;
        r.looted=looted;r.completedAt=looted?now.ToString("o"):null;r.completionReason=looted?reason:null;r.absentAfterLoot=false;absentSince.Remove(key);dirty=true;ResetLoot();return true;
    }
    public void ResetLoot() {lootKey=null;sawContents=false;emptySamples=0;}
    public string ObserveLoot(Snapshot s,LootObservation loot,DateTimeOffset now) {
        if(s==null || !s.live || s.demo || s.context==null || loot==null || !loot.valid || loot.remaining<0 || loot.remaining>19 || String.IsNullOrEmpty(loot.guid)) {ResetLoot();return null;}
        string key=s.context+"|"+loot.guid;SavedCache record;
        if(!records.TryGetValue(key,out record) || record.looted) {ResetLoot();return null;}
        if(key!=lootKey) {ResetLoot();lootKey=key;}
        if(loot.remaining>0) {sawContents=true;emptySamples=0;}
        else if(sawContents && ++emptySamples>=2) {
            Mark(key,true,"Beuteplätze und Geld leer bei weiterhin aktiver Beute-GUID",now);return key;
        }
        return null;
    }
    public Snapshot ForNavigation(Snapshot s) {
        if(s==null || !s.live || s.demo)return s;
        var objects=s.objects.Where(o=>!Reader.IsCache(o.name) || !Completed(s,o)).ToList();
        if(s.rememberNavigation && s.context!=null) {
            var ids=new HashSet<string>(objects.Select(o=>o.id));
            foreach(var r in records.Values.Where(r=>!r.looted && r.context==s.context && !ids.Contains(r.location.id))) {
                // Only a verified persistent coordinate space may be reused for directions.
                if(r.location.space!=s.context)continue;
                var remembered=Copy(r.location);remembered.remembered=true;remembered.lastSeen=r.lastSeen;objects.Add(remembered);
            }
        }
        return new Snapshot {live=true,source=s.source,observedAt=s.observedAt,player=s.player,facing=s.facing,onTransport=s.onTransport,objects=objects.ToArray(),context=s.context,mapId=s.mapId,rememberNavigation=s.rememberNavigation};
    }
    static ObjectData Copy(ObjectData o) {return new ObjectData {region=o.region,subregion=o.subregion,name=o.name,id=o.id,entry=o.entry,space=o.space,units=o.units,x=o.x,y=o.y,z=o.z};}
    public bool Flush(DateTimeOffset now,bool force=false) {
        if(!dirty)return true;
        if(!force && (now-lastSave).TotalSeconds<2)return SaveError==null;
        lastSave=now;string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            string json=new JavaScriptSerializer {MaxJsonLength=10000000}.Serialize(new CacheArchive {schema=1,finds=records.Values.ToList()});
            File.WriteAllText(temp,json);
            if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);
            dirty=false;SaveError=null;return true;
        }catch(Exception ex){SaveError=ex.Message;return false;}
        finally {try {if(File.Exists(temp))File.Delete(temp);}catch {}}
    }
}
