using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class UpdaterTests {
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Zip(string path,params string[] names) {
        using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))foreach(string name in names)using(var writer=new StreamWriter(archive.CreateEntry(name).Open()))writer.Write("new "+name);
    }
    public static int Parent(string encoded) {
        string manifest=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var plan=new JavaScriptSerializer().Deserialize<UpdatePlan>(File.ReadAllText(manifest));
        using(var p=Process.GetCurrentProcess()){plan.pid=p.Id;plan.started=p.StartTime.ToUniversalTime().Ticks;plan.target=p.MainModule.FileName;}
        AutomaticUpdater.Launch(plan);return 0;
    }
    public static int Run(string root) {
        string dir=Path.Combine(root,"updater-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"updater-test-results.txt");
        try {
            foreach(string prefix in new[]{"","AstralScanner/"}) {
                string folder=Path.Combine(dir,prefix==""?"root":"wrapped");Directory.CreateDirectory(folder);
                string zip=Path.Combine(folder,"package.zip");Zip(zip,prefix+"AstralScanner.exe",prefix+"README.md");
                AutomaticUpdater.VerifyDownload(zip,new FileInfo(zip).Length,AutomaticUpdater.Hash(zip));
                bool mismatch=false;try{AutomaticUpdater.VerifyDownload(zip,new FileInfo(zip).Length,new string('0',64));}catch(InvalidDataException){mismatch=true;}Check(mismatch,"Corrupt download checksum rejected");
                mismatch=false;try{AutomaticUpdater.VerifyDownload(zip,new FileInfo(zip).Length+1,AutomaticUpdater.Hash(zip));}catch(InvalidDataException){mismatch=true;}Check(mismatch,"Partial download size rejected");
                var files=AutomaticUpdater.Extract(zip,Path.Combine(folder,"files"));
                Check(files.Count==2 && File.Exists(Path.Combine(folder,"files","AstralScanner.exe")) && !Directory.Exists(Path.Combine(folder,"files","AstralScanner")),"Root/wrapped archives flatten into files");
            }
            int i=0;
            foreach(var names in new[]{new[]{"../AstralScanner.exe"},new[]{"AstralScanner.exe","AstralScanner/README.md"},new[]{"AstralScanner.exe","AstralScanner.exe"},new[]{"AstralScanner.exe","finds.json"},new[]{"README.md"},new[]{"/AstralScanner.exe"}}) {
                string zip=Path.Combine(dir,"bad"+(i++)+".zip");Zip(zip,names);bool rejected=false;
                try{AutomaticUpdater.Extract(zip,Path.Combine(dir,"bad-files"+i));}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid archive rejected: "+String.Join(",",names));
            }
            string target=Path.Combine(dir,"installed");Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target,"AstralScanner.exe"),"old exe");File.WriteAllText(Path.Combine(target,"README.md"),"old readme");File.WriteAllText(Path.Combine(target,"finds.json"),"personal data");
            var plan=new UpdatePlan {directory=Path.Combine(dir,"wrapped"),target=Path.Combine(target,"AstralScanner.exe"),files=new Dictionary<string,string>()};
            foreach(string name in new[]{"AstralScanner.exe","README.md"})plan.files[name]=AutomaticUpdater.Hash(Path.Combine(plan.directory,"files",name));
            bool failed=false;
            using(var locked=new FileStream(plan.target,FileMode.Open,FileAccess.Read,FileShare.Read))try{AutomaticUpdater.Install(plan);}catch(IOException){failed=true;}
            Check(failed && File.ReadAllText(Path.Combine(target,"README.md"))=="old readme" && File.ReadAllText(plan.target)=="old exe","Locked executable rolls back earlier README replacement");
            string backup=AutomaticUpdater.Install(plan);
            Check(AutomaticUpdater.Hash(plan.target)==plan.files["AstralScanner.exe"] && File.ReadAllText(Path.Combine(backup,"AstralScanner.exe"))=="old exe" && File.ReadAllText(Path.Combine(target,"finds.json"))=="personal data","Install keeps backup and personal files");
            File.AppendAllText(Path.Combine(plan.directory,"files","AstralScanner.exe"),"tampered");failed=false;
            try{AutomaticUpdater.Install(plan);}catch(InvalidDataException){failed=true;}Check(failed,"Changed staging rejected before install");
            string fixture=Path.Combine(root,"UpdateFixture.exe");Check(File.Exists(fixture),"Restart fixture must exist");
            string live=Path.Combine(dir,"real-handoff");Directory.CreateDirectory(live);string staged=Path.Combine(dir,"handoff-stage");Directory.CreateDirectory(Path.Combine(staged,"files"));
            File.Copy(typeof(UpdaterTests).Assembly.Location,Path.Combine(live,"AstralScanner.exe"));File.Copy(fixture,Path.Combine(staged,"files","AstralScanner.exe"));
            var handoff=new UpdatePlan {directory=staged,version="test",files=new Dictionary<string,string>{{"AstralScanner.exe",AutomaticUpdater.Hash(fixture)}}};
            string manifest=Path.Combine(staged,"test-plan.json");File.WriteAllText(manifest,new JavaScriptSerializer().Serialize(handoff));
            using(var parent=Process.Start(new ProcessStartInfo(Path.Combine(live,"AstralScanner.exe"),"--updater-parent "+Convert.ToBase64String(Encoding.UTF8.GetBytes(manifest))){UseShellExecute=false,CreateNoWindow=true}))Check(parent.WaitForExit(15000) && parent.ExitCode==0,"Parent hands off and exits normally");
            for(int n=0;n<100 && !File.Exists(Path.Combine(live,"restarted.txt"));n++)Thread.Sleep(100);
            Check(File.Exists(Path.Combine(live,"restarted.txt")) && AutomaticUpdater.Hash(Path.Combine(live,"AstralScanner.exe"))==AutomaticUpdater.Hash(fixture),"Real helper waits, replaces running executable and restarts new executable");
            File.WriteAllText(report,"PASS: root/wrapped ZIP flattening; traversal/mixed/duplicate/data-file/missing-exe rejection; changed staging rejection; locked-file rollback; backup and personal files retained; real parent/helper exit, executable replacement and restart. Isolated temporary fixtures.\r\n");return 0;
        }catch(Exception ex){File.WriteAllText(report,ex.ToString());return 1;}
    }
}
