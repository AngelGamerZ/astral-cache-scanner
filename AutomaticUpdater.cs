using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public sealed class UpdatePlan {
    public string directory {get;set;}
    public string target {get;set;}
    public string version {get;set;}
    public int pid {get;set;}
    public long started {get;set;}
    public Dictionary<string,string> files {get;set;}
}

public static class AutomaticUpdater {
    const int Limit=32*1024*1024;
    public static bool SkipStartup;
    public static string LogFile {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AstralScanner","Logs","updater.log");}}
    static void Log(string text) {try{Directory.CreateDirectory(Path.GetDirectoryName(LogFile));File.AppendAllText(LogFile,DateTimeOffset.Now.ToString("o")+" "+text+Environment.NewLine);}catch{}}
    public static string Hash(string path) {using(var sha=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
    public static void VerifyDownload(string path,long size,string digest) {
        if(new FileInfo(path).Length!=size || !String.Equals(Hash(path),digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Download-Prüfsumme oder Größe stimmt nicht. Keine Dateien ersetzt.");
    }
    static HttpWebRequest Request(string url) {
        ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;
        var r=(HttpWebRequest)WebRequest.Create(url);r.UserAgent="AstralScanner/"+ReleaseInfo.Version;r.Timeout=30000;r.ReadWriteTimeout=30000;return r;
    }
    public static string Metadata() {
        var request=Request("https://api.github.com/repos/"+ReleaseInfo.Repository+"/releases/latest");request.Accept="application/vnd.github+json";
        using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())) {
            var buffer=new char[262145];int count=0,n;
            while(count<buffer.Length && (n=reader.Read(buffer,count,buffer.Length-count))>0)count+=n;
            if(count==buffer.Length)throw new InvalidDataException("GitHub-Antwort zu groß.");return new string(buffer,0,count);
        }
    }
    public static UpdatePlan Prepare(Action<string> progress) {
        return Prepare(Metadata(),progress);
    }
    public static UpdatePlan Prepare(string json,Action<string> progress) {
        progress(ReleaseInfo.Describe(json));
        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
        string tag=(string)data["tag_name"];var version=new Version(tag.TrimStart('v'));
        if(version<=new Version(ReleaseInfo.Version))return null;
        object raw;if(!data.TryGetValue("assets",out raw) || !(raw is IEnumerable))throw new InvalidDataException("Release enthält keine Dateien.");
        string name="AstralScanner-v"+version+".zip";
        var assets=((IEnumerable)raw).Cast<object>().OfType<Dictionary<string,object>>().Where(a=>a.ContainsKey("name") && Object.Equals(a["name"],name)).ToArray();
        if(assets.Length!=1)throw new InvalidDataException("Kein eindeutiges Scanner-Updatepaket gefunden.");
        var asset=assets[0];object digestValue;
        string digest=asset.TryGetValue("digest",out digestValue)?digestValue as string:null;
        if(digest==null || !System.Text.RegularExpressions.Regex.IsMatch(digest,@"\Asha256:[a-fA-F0-9]{64}\z"))throw new InvalidDataException("GitHub-Prüfsumme fehlt; Update bleibt unangetastet.");
        string url=(string)asset["browser_download_url"];
        string expected="https://github.com/"+ReleaseInfo.Repository+"/releases/download/"+tag+"/"+name;
        if(url!=expected)throw new InvalidDataException("Unerwartete Downloadadresse.");
        long size=Convert.ToInt64(asset["size"]);if(size<=0 || size>Limit)throw new InvalidDataException("Unzulässige Paketgröße.");
        string directory=Path.Combine(Path.GetTempPath(),"AstralScanner-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string zip=Path.Combine(directory,"package.zip");progress("Version "+version+" wird automatisch heruntergeladen …");
        using(var response=Request(url).GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(zip)) {
            byte[] buffer=new byte[65536];long count=0;int n;
            while((n=input.Read(buffer,0,buffer.Length))>0){count+=n;if(count>size || count>Limit)throw new InvalidDataException("Download größer als angekündigt.");output.Write(buffer,0,n);}
            if(count!=size)throw new InvalidDataException("Download unvollständig.");
        }
        VerifyDownload(zip,size,digest.Substring(7));
        progress("Download geprüft. Programmdateien werden vorbereitet …");
        var files=Extract(zip,Path.Combine(directory,"files"));
        Version binary=AssemblyName.GetAssemblyName(Path.Combine(directory,"files","AstralScanner.exe")).Version;
        if(binary.Major!=version.Major || binary.Minor!=version.Minor || binary.Build!=Math.Max(0,version.Build) || binary.Revision!=Math.Max(0,version.Revision))throw new InvalidDataException("Programmversion passt nicht zum Release.");
        using(var current=Process.GetCurrentProcess())return new UpdatePlan {directory=directory,target=Path.GetFullPath(current.MainModule.FileName),version=version.ToString(),pid=current.Id,started=current.StartTime.ToUniversalTime().Ticks,files=files};
    }
    public static Dictionary<string,string> Extract(string zip,string destination) {
        Directory.CreateDirectory(destination);var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string layout=null;
        using(var archive=ZipFile.OpenRead(zip)) {
            if(archive.Entries.Count>20)throw new InvalidDataException("Zu viele Archiveinträge.");
            foreach(var entry in archive.Entries) {
                string path=entry.FullName.Replace('\\','/');
                if(path=="AstralScanner/")continue;
                string prefix=path.StartsWith("AstralScanner/",StringComparison.Ordinal)?"AstralScanner/":"";
                string name=path.Substring(prefix.Length);
                if(name!="AstralScanner.exe" && name!="README.md")throw new InvalidDataException("Unerwarteter Dateipfad im Update: "+path);
                if(layout!=null && layout!=prefix)throw new InvalidDataException("Gemischte ZIP-Ordnerstruktur.");layout=prefix;
                if(files.ContainsKey(name) || entry.Length<=0 || entry.Length>Limit)throw new InvalidDataException("Doppelte oder unzulässige Datei.");
                string target=Path.Combine(destination,name);
                using(var input=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew)) {
                    byte[] buffer=new byte[65536];int n;long total=0;
                    while((n=input.Read(buffer,0,buffer.Length))>0){total+=n;if(total>Limit)throw new InvalidDataException("Entpackte Datei zu groß.");output.Write(buffer,0,n);}
                    if(total!=entry.Length)throw new InvalidDataException("Unvollständiger Archiveintrag.");
                }
                files.Add(name,Hash(target));
            }
        }
        if(!files.ContainsKey("AstralScanner.exe"))throw new InvalidDataException("AstralScanner.exe fehlt im Update.");return files;
    }
    public static void Launch(UpdatePlan plan) {
        string targetDirectory=Path.GetDirectoryName(plan.target);
        string probe=Path.Combine(targetDirectory,".astral-write-test-"+Guid.NewGuid().ToString("N"));
        using(File.Create(probe)){}File.Delete(probe);
        File.Copy(plan.target,Path.Combine(plan.directory,"Updater.exe"));
        string manifest=Path.Combine(plan.directory,"plan.json");File.WriteAllText(manifest,new JavaScriptSerializer().Serialize(plan));
        string argument=Convert.ToBase64String(Encoding.UTF8.GetBytes(manifest));
        Process.Start(new ProcessStartInfo(Path.Combine(plan.directory,"Updater.exe"),"--apply-update "+argument){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        for(int i=0;i<100;i++){if(File.Exists(Path.Combine(plan.directory,"ready")))return;Thread.Sleep(50);}
        throw new IOException("Update-Helfer nicht bereit. Der Scanner bleibt geöffnet.");
    }
    public static string Install(UpdatePlan plan) {
        string targetDirectory=Path.GetDirectoryName(Path.GetFullPath(plan.target));
        if(Path.GetFileName(plan.target)!="AstralScanner.exe" || plan.files==null || !plan.files.ContainsKey("AstralScanner.exe"))throw new InvalidDataException("Ungültiges Updateziel.");
        foreach(var file in plan.files) {
            if(file.Key!="AstralScanner.exe" && file.Key!="README.md")throw new InvalidDataException("Unzulässige Zieldatei.");
            if(Hash(Path.Combine(plan.directory,"files",file.Key))!=file.Value)throw new InvalidDataException("Vorbereitete Datei wurde verändert.");
        }
        string backup=Path.Combine(targetDirectory,".astral-update-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
        var applied=new List<string>();
        try {
            // Copy all candidates onto the destination volume before changing any installed file.
            foreach(var file in plan.files)File.Copy(Path.Combine(plan.directory,"files",file.Key),Path.Combine(backup,file.Key+".new"));
            foreach(var file in plan.files.OrderBy(f=>f.Key=="AstralScanner.exe"?1:0)) {
                string target=Path.Combine(targetDirectory,file.Key),candidate=Path.Combine(backup,file.Key+".new"),previous=Path.Combine(backup,file.Key);
                if(File.Exists(target))File.Replace(candidate,target,previous);else File.Move(candidate,target);
                applied.Add(file.Key);
            }
            return backup;
        }catch {
            foreach(string name in applied.AsEnumerable().Reverse()) {
                string target=Path.Combine(targetDirectory,name),previous=Path.Combine(backup,name);
                if(File.Exists(previous))File.Copy(previous,target,true);else File.Delete(target);
            }
            throw;
        }
    }
    public static int Apply(string encoded) {
        UpdatePlan plan=null;bool parentExited=false;
        try {
            string manifest=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            plan=new JavaScriptSerializer().Deserialize<UpdatePlan>(File.ReadAllText(manifest));
            using(var parent=Process.GetProcessById(plan.pid)) {
                if(parent.StartTime.ToUniversalTime().Ticks!=plan.started || !String.Equals(parent.MainModule.FileName,plan.target,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Scanner-Prozess passt nicht zum Update.");
                File.WriteAllText(Path.Combine(plan.directory,"ready"),"ready");
                if(!parent.WaitForExit(60000))throw new IOException("Scanner wurde nicht geschlossen; Update abgebrochen.");parentExited=true;
            }
            string mutexName;
            using(var sha=SHA256.Create())mutexName="Local\\AstralScannerUpdate-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(plan.target).ToLowerInvariant()))).Replace("-","");
            using(var mutex=new Mutex(false,mutexName)) {
                if(!mutex.WaitOne(0))throw new IOException("Ein anderes Update läuft bereits.");
                try {string backup=Install(plan);Log("Update auf "+plan.version+" installiert. Sicherung: "+backup);}
                finally{mutex.ReleaseMutex();}
            }
            Process.Start(new ProcessStartInfo(plan.target){WorkingDirectory=Path.GetDirectoryName(plan.target),UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal});return 0;
        }catch(Exception ex) {
            Log("Update fehlgeschlagen: "+ex);
            if(parentExited && plan!=null)try{Process.Start(new ProcessStartInfo(plan.target,"--skip-auto-update"){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(plan.target),WindowStyle=ProcessWindowStyle.Normal});}catch(Exception restart){Log("Neustart fehlgeschlagen: "+restart.Message);}
            return 1;
        }
    }
}
