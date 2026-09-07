using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

public static class ReleaseInfo {
    public const string Version="1.5.1";
    public const string Repository="AngelGamerZ/astral-cache-scanner";
    public const string ReleasesUrl="https://github.com/"+Repository+"/releases";
    public static string Check() {
        ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;
        var request=(HttpWebRequest)WebRequest.Create("https://api.github.com/repos/"+Repository+"/releases/latest");
        request.UserAgent="AstralScanner/"+Version;request.Accept="application/vnd.github+json";request.Timeout=10000;request.ReadWriteTimeout=10000;
        string token=Environment.GetEnvironmentVariable("ASTRAL_GITHUB_TOKEN");if(!String.IsNullOrWhiteSpace(token))request.Headers["Authorization"]="Bearer "+token;
        try {
            using(var response=request.GetResponse())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream)) {
                var buffer=new char[65537];int count=0,n;while(count<buffer.Length && (n=reader.Read(buffer,count,buffer.Length-count))>0)count+=n;
                if(count>65536)throw new InvalidDataException("GitHub-Antwort zu groß.");
                return Describe(new string(buffer,0,count));
            }
        }catch(WebException ex){var response=ex.Response as HttpWebResponse;if(response!=null && response.StatusCode==HttpStatusCode.NotFound)return "Kein sichtbares Release. Bei privatem Repository fehlt eventuell die Zugriffsberechtigung.";if(response!=null && (response.StatusCode==HttpStatusCode.Forbidden || response.StatusCode==HttpStatusCode.Unauthorized))return "GitHub-Zugriff abgelehnt oder Abfragelimit erreicht. Bitte später erneut prüfen.";return "Update-Prüfung nicht möglich: Netzwerk oder GitHub nicht erreichbar.";}
    }
    public static Version LatestVersion(string json) {
        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);object tag,draft,pre;
        if(data==null || !data.TryGetValue("tag_name",out tag) || !(tag is string) || (data.TryGetValue("draft",out draft)&&Object.Equals(draft,true)) || (data.TryGetValue("prerelease",out pre)&&Object.Equals(pre,true)))throw new InvalidDataException("Kein gültiges stabiles Release.");
        Version latest,current;if(!System.Version.TryParse(((string)tag).TrimStart('v'),out latest) || !System.Version.TryParse(Version,out current))throw new InvalidDataException("Unbekanntes Versionsformat.");
        return latest;
    }
    public static bool IsNewer(string json) {return LatestVersion(json)>new System.Version(Version);}
    public static string Describe(string json) {
        var latest=LatestVersion(json);var current=new System.Version(Version);
        return latest>current?"Update verfügbar: "+latest+" · installiert: "+Version:latest==current?"Aktuell: Version "+Version:"Installiert: "+Version+" · GitHub-Release: "+latest;
    }
}

public sealed class DebugJournal {
    public static string CompletionMessage(SavedCache r) {
        return (r.looted?"Kiste als gelootet markiert":"Kiste als offen markiert")+" | "+r.location.name+" | Objekt "+r.location.id+" | Karte "+r.mapId+" | Region "+(r.location.region??"Nicht erfasst")+" | "+String.Format(System.Globalization.CultureInfo.InvariantCulture,"X {0} / Y {1} / Z {2}",r.location.x,r.location.y,r.location.z)+" | Grund: "+(r.looted?r.completionReason??"Nicht angegeben":"Manuell wieder geöffnet")+" | Status im Speicher geändert; Speicherung wird anschließend versucht.";
    }
    readonly Queue<string> lines=new Queue<string>();
    public void Add(string text) {
        string line=DateTimeOffset.Now.ToString("o")+" "+text.Replace("\r"," ").Replace("\n"," | ");
        string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if(!String.IsNullOrEmpty(user))line=line.Replace(user,"%USERPROFILE%");
        line=System.Text.RegularExpressions.Regex.Replace(line,@"\|[0-9A-F]{16}\|map:","|CHARACTER|map:");
        if(line.Length>2000)line=line.Substring(0,2000);lines.Enqueue(line);while(lines.Count>1500)lines.Dequeue();
    }
    public override string ToString(){return "Astral Scanner "+ReleaseInfo.Version+Environment.NewLine+String.Join(Environment.NewLine,lines);}
    public void Export(string path){File.WriteAllText(path,ToString(),new UTF8Encoding(false));}
}

public sealed class ProximityCompletion {
    readonly Dictionary<string,DateTimeOffset> missing=new Dictionary<string,DateTimeOffset>();
    string context;DateTimeOffset previous;
    public string Status {get;private set;}
    public void Reset(){missing.Clear();context=null;Status="Näheprüfung zurückgesetzt";}
    public static double? Distance(PointData a,PointData b) {
        if(a==null || b==null || a.space!=b.space || a.units!="yards" || b.units!="yards" || !a.x.HasValue || !a.y.HasValue || !a.z.HasValue || !b.x.HasValue || !b.y.HasValue || !b.z.HasValue)return null;
        double x=a.x.Value-b.x.Value,y=a.y.Value-b.y.Value,z=a.z.Value-b.z.Value,d=Math.Sqrt(x*x+y*y+z*z);return Double.IsNaN(d)||Double.IsInfinity(d)?(double?)null:d;
    }
    public string[] Update(Snapshot s,CacheMemory memory,DateTimeOffset now) {
        if(s==null || !s.live || s.demo || s.context==null || s.onTransport || s.unreadable!=0 || s.player==null){Reset();Status="Ausgesetzt: keine vollständigen Spiel-/Positionsdaten";return new string[0];}
        if(context!=s.context || now<previous || (now-previous).TotalSeconds>1)Reset();context=s.context;previous=now;
        var visible=new HashSet<string>(s.objects.Select(o=>CacheMemory.Key(s,o)));var eligible=new HashSet<string>();var completed=new List<string>();double? nearest=null;
        foreach(var r in memory.Records.Where(r=>!r.looted && r.context==s.context)) {
            double? distance=Distance(s.player,r.location);if(distance.HasValue && (!nearest.HasValue || distance<nearest))nearest=distance;
            if(!distance.HasValue || distance>10 || visible.Contains(r.Key))continue;
            eligible.Add(r.Key);DateTimeOffset since;
            if(!missing.TryGetValue(r.Key,out since))missing[r.Key]=now;
            else if((now-since).TotalSeconds>=1) {memory.Mark(r.Key,true,"Am gespeicherten Fundort (max. 10 yd), Kiste seit 1 Sekunde in vollständigen Scans nicht vorhanden",now,true);completed.Add(r.Key);}
        }
        foreach(string key in missing.Keys.Where(k=>!eligible.Contains(k)||completed.Contains(k)).ToArray())missing.Remove(key);
        Status="Nächster offener Ort: "+(nearest.HasValue?nearest.Value.ToString("F1")+" yd":"keiner")+" · fehlend in Nähe: "+missing.Count+" · erledigt: "+completed.Count;
        return completed.ToArray();
    }
}

