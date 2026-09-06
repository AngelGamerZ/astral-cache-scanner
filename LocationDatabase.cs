using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

// Portable, character-independent historical locations. Never an active-cache source.
public sealed class CacheLocation {
    public string realm {get;set;}
    public int mapId {get;set;}
    public uint entry {get;set;}
    public double x {get;set;}
    public double y {get;set;}
    public double? z {get;set;}
    public string firstSeen {get;set;}
    public string lastSeen {get;set;}
}
public sealed class LocationArchive {
    public string format {get;set;}
    public int schema {get;set;}
    public string coordinates {get;set;}
    public string units {get;set;}
    public string exportedAt {get;set;}
    public List<CacheLocation> locations {get;set;}
}
public sealed class LocationDatabase {
    const int Limit=50000, MaxBytes=25000000;
    readonly string file;
    Dictionary<string,CacheLocation> records=new Dictionary<string,CacheLocation>(StringComparer.Ordinal);
    bool dirty;DateTimeOffset lastSave;
    public string SaveError {get;private set;}
    public IEnumerable<CacheLocation> Records {get {return records.Values;}}
    public static string DefaultFile {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AstralScanner","locations.json");}}
    public LocationDatabase(string path) {file=Path.GetFullPath(path);if(File.Exists(file))records=Read(file);else dirty=true;}
    static JavaScriptSerializer Serializer() {return new JavaScriptSerializer {MaxJsonLength=MaxBytes,RecursionLimit=32};}
    static string Number(double x) {return x.ToString("R",CultureInfo.InvariantCulture);}
    static bool Finite(double n) {return !Double.IsNaN(n) && !Double.IsInfinity(n) && Math.Abs(n)<=1000000;}
    static string Timestamp(string value) {
        DateTimeOffset t;
        if(value==null || value.Length>40 || !(value.EndsWith("Z",StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(value,@"[+-]\d\d:\d\d$")) || !DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out t) || t.Year<2000 || t>DateTimeOffset.UtcNow.AddMinutes(5))throw new InvalidDataException("Ungültiger Sichtungszeitpunkt.");
        return t.ToUniversalTime().ToString("o");
    }
    static CacheLocation Normalize(CacheLocation p) {
        if(p==null || String.IsNullOrWhiteSpace(p.realm) || p.realm.Length>128 || p.realm.Any(Char.IsControl) || p.mapId<0 || p.mapId>=10000 || p.entry==0 || !Finite(p.x) || !Finite(p.y) || (p.z.HasValue && !Finite(p.z.Value)))throw new InvalidDataException("Ungültiger Fundort (Server, Karte, Objekt-ID oder Koordinaten).");
        var result=new CacheLocation {realm=p.realm.Trim(),mapId=p.mapId,entry=p.entry,x=Math.Round(p.x,1,MidpointRounding.AwayFromZero),y=Math.Round(p.y,1,MidpointRounding.AwayFromZero),z=p.z.HasValue?(double?)Math.Round(p.z.Value,1,MidpointRounding.AwayFromZero):null,firstSeen=Timestamp(p.firstSeen),lastSeen=Timestamp(p.lastSeen)};
        if(String.CompareOrdinal(result.firstSeen,result.lastSeen)>0)throw new InvalidDataException("Erste Sichtung liegt nach letzter Sichtung.");
        return result;
    }
    static string Key(CacheLocation p) {return Serializer().Serialize(new object[]{p.realm,p.mapId,p.entry,Number(p.x),Number(p.y),p.z.HasValue?Number(p.z.Value):"unknown"});}
    static bool MergeOne(Dictionary<string,CacheLocation> target,CacheLocation p) {
        string key=Key(p);CacheLocation existing;
        if(!target.TryGetValue(key,out existing)) {if(target.Count>=Limit)throw new InvalidDataException("Datenbanklimit von 50.000 Fundorten erreicht.");target.Add(key,p);return true;}
        string first=String.CompareOrdinal(existing.firstSeen,p.firstSeen)<0?existing.firstSeen:p.firstSeen;
        string last=String.CompareOrdinal(existing.lastSeen,p.lastSeen)>0?existing.lastSeen:p.lastSeen;
        if(first==existing.firstSeen && last==existing.lastSeen)return false;
        target[key]=new CacheLocation {realm=existing.realm,mapId=existing.mapId,entry=existing.entry,x=existing.x,y=existing.y,z=existing.z,firstSeen=first,lastSeen=last};return true;
    }
    static Dictionary<string,CacheLocation> Read(string path) {
        if(new FileInfo(path).Length>MaxBytes)throw new InvalidDataException("Datei größer als 25 MB.");
        string json=File.ReadAllText(path);
        var raw=Serializer().DeserializeObject(json) as Dictionary<string,object>;
        object rawRows;
        if(raw==null || !raw.TryGetValue("locations",out rawRows) || !(rawRows is object[]))throw new InvalidDataException("Fundortliste fehlt.");
        foreach(var row in (object[])rawRows) {
            var values=row as Dictionary<string,object>;
            if(values==null || new[]{"realm","mapId","entry","x","y","firstSeen","lastSeen"}.Any(k=>!values.ContainsKey(k) || values[k]==null))throw new InvalidDataException("Ein Fundort enthält nicht alle Pflichtfelder.");
            foreach(string k in new[]{"mapId","entry","x","y","z"}) {
                object v;if(!values.TryGetValue(k,out v) || v==null)continue;
                if(!(v is int || v is long || v is decimal || v is double))throw new InvalidDataException("Koordinaten und IDs müssen JSON-Zahlen sein.");
                double number=Convert.ToDouble(v,CultureInfo.InvariantCulture);
                if(!Finite(number) && k!="entry")throw new InvalidDataException("Ungültige Zahl.");
                if((k=="mapId" || k=="entry") && (Double.IsNaN(number) || Double.IsInfinity(number) || number!=Math.Truncate(number)))throw new InvalidDataException("Karten- und Objekt-IDs müssen ganze Zahlen sein.");
            }
        }
        var archive=Serializer().Deserialize<LocationArchive>(json);
        if(archive==null || archive.format!="astral-cache-locations" || archive.schema!=1 || archive.coordinates!="world" || archive.units!="yards" || archive.locations==null || archive.locations.Count>Limit)throw new InvalidDataException("Keine unterstützte Astral-Fundortdatenbank (JSON, Schema 1, Weltkoordinaten in Yards).");
        var result=new Dictionary<string,CacheLocation>(StringComparer.Ordinal);
        foreach(var p in archive.locations)MergeOne(result,Normalize(p));
        return result;
    }
    static LocationArchive Archive(IEnumerable<CacheLocation> points) {return new LocationArchive {format="astral-cache-locations",schema=1,coordinates="world",units="yards",exportedAt=DateTimeOffset.UtcNow.ToString("o"),locations=points.OrderBy(p=>p.realm,StringComparer.Ordinal).ThenBy(p=>p.mapId).ThenBy(p=>p.entry).ThenBy(p=>p.x).ThenBy(p=>p.y).ThenBy(p=>p.z).ToList()};}
    static void AtomicWrite(string path,string content,bool backup) {
        path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,content,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,backup?path+".bak":null);else File.Move(temp,path);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public int Collect(IEnumerable<SavedCache> finds) {
        int added=0;
        foreach(var r in finds) {
            if(r==null || !r.mapId.HasValue || r.location==null || r.context==null || !Reader.IsCache(r.location.name) || !r.location.x.HasValue || !r.location.y.HasValue || r.location.units!="yards")continue;
            // Exclude session/instance-relative positions from the portable atlas.
            string suffix="|map:"+r.mapId.Value.ToString(CultureInfo.InvariantCulture);
            if(r.location.space!=r.context || !r.context.EndsWith(suffix,StringComparison.Ordinal))continue;
            string prefix=r.context.Substring(0,r.context.Length-suffix.Length);int split=prefix.LastIndexOf('|');
            if(split<1 || !System.Text.RegularExpressions.Regex.IsMatch(prefix.Substring(split+1),@"\A[0-9A-F]{16}\z"))continue;
            var point=Normalize(new CacheLocation {realm=prefix.Substring(0,split),mapId=r.mapId.Value,entry=r.location.entry,x=r.location.x.Value,y=r.location.y.Value,z=r.location.z,firstSeen=r.firstSeen,lastSeen=r.lastSeen});
            int before=records.Count;if(MergeOne(records,point))dirty=true;added+=records.Count-before;
        }
        return added;
    }
    // Validate the complete batch, then persist it before replacing in-memory state.
    public int Import(IEnumerable<string> paths) {
        var next=new Dictionary<string,CacheLocation>(records,StringComparer.Ordinal);int files=0;
        foreach(string path in paths) {if(++files>100)throw new InvalidDataException("Höchstens 100 Dateien pro Import.");foreach(var p in Read(path).Values)MergeOne(next,p);}
        int added=next.Count-records.Count;
        AtomicWrite(file,Serializer().Serialize(Archive(next.Values)),true);
        records=next;dirty=false;SaveError=null;lastSave=DateTimeOffset.UtcNow;return added;
    }
    public bool Flush(bool force=false) {
        if(!dirty)return true;
        if(!force && (DateTimeOffset.UtcNow-lastSave).TotalSeconds<2)return SaveError==null;
        lastSave=DateTimeOffset.UtcNow;
        try {AtomicWrite(file,Serializer().Serialize(Archive(records.Values)),true);dirty=false;SaveError=null;return true;}
        catch(Exception ex){SaveError=ex.Message;return false;}
    }
    void CheckExport(string path) {
        string full=Path.GetFullPath(path);
        foreach(string reserved in new[]{file,file+".bak",CacheMemory.DefaultFile,CacheMemory.DefaultFile+".bak",ClientSettings.DefaultFile})if(String.Equals(full,Path.GetFullPath(reserved),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Bitte für den Export eine separate Datei auswählen.");
    }
    public void ExportJson(string path) {CheckExport(path);AtomicWrite(path,Serializer().Serialize(Archive(records.Values)),false);}
    // UTF-8 text is quoted as Lua 5.1 decimal byte escapes, avoiding executable input.
    static string LuaText(string text) {return "\""+String.Concat(Encoding.UTF8.GetBytes(text).Select(b=>"\\"+b.ToString("D3",CultureInfo.InvariantCulture)))+"\"";}
    public void ExportLua(string path) {
        CheckExport(path);var b=new StringBuilder("-- Astral Cache historical locations, schema 1. NOT live spawn status.\n-- World coordinates in yards; not normalized zone/map pin coordinates.\n-- Include this data file in a future addon's TOC before its consumer.\nAstralCacheLocations = { schema = 1, coordinates = \"world\", units = \"yards\", locations = {\n");
        foreach(var p in Archive(records.Values).locations)b.Append("  { realm = ").Append(LuaText(p.realm)).Append(", mapId = ").Append(p.mapId).Append(", entry = ").Append(p.entry).Append(", x = ").Append(Number(p.x)).Append(", y = ").Append(Number(p.y)).Append(", z = ").Append(p.z.HasValue?Number(p.z.Value):"nil").Append(", firstSeen = ").Append(LuaText(p.firstSeen)).Append(", lastSeen = ").Append(LuaText(p.lastSeen)).Append(" },\n");
        b.Append("} }\n");AtomicWrite(path,b.ToString(),false);
    }
}
