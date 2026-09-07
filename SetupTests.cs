using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public static class SetupTests {
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    public static int Run(string testRoot,string clientDirectory) {
        string run=Path.Combine(Path.GetFullPath(testRoot),"setup-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);string file=Path.Combine(run,"settings.json");
        string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"setup-test-results.txt");
        try {
            Check(LiveReader.SupportedHash("02F38EE484B9B6EF055BBA0C749738DC0905EB8BCF4B310349BB0C6B4FD4D865"),"Previous inspected client remains supported");
            Check(LiveReader.SupportedHash("EDDFF880343A477FAA1480626571C1385442EFFF5E9AA706BC57341F17DCB96F"),"Updated inspected client is supported");
            Check(!LiveReader.SupportedHash(new string('0',64)) && !LiveReader.SupportedHash(null),"Unknown client hashes remain rejected");
            Application.EnableVisualStyles();
            using(var form=new ScannerForm(false,file)) {
                form.LoadClient();Check(!form.CanStartScan && !form.IsScanning,"First launch must be locked");
                bool rejected=false;try {form.AcceptClient(run);}catch(InvalidDataException){rejected=true;}
                Check(rejected && !form.CanStartScan && !File.Exists(file),"Wrong folder must not unlock or persist");
                File.WriteAllText(Path.Combine(run,"Wow.exe"),"unsupported executable");
                rejected=false;try {form.AcceptClient(run);}catch(InvalidDataException){rejected=true;}
                Check(rejected && !form.CanStartScan && !File.Exists(file),"Unsupported executable must not unlock");
                form.AcceptClient(clientDirectory);Check(form.CanStartScan && !form.IsScanning,"Valid selected folder enables manual start only");
                Check(ClientSettings.Load(file)==Path.GetFullPath(clientDirectory),"Selected path persists");
                form.Close();
            }
            using(var form=new ScannerForm(false,file)) {form.LoadClient();Check(form.CanStartScan && !form.IsScanning,"Later launch restores validated path without automatic scanning");form.Close();}
            ClientSettings.Save(file,clientDirectory);Check(ClientSettings.Load(file)==Path.GetFullPath(clientDirectory),"Atomic settings replacement");
            File.WriteAllText(file,new JavaScriptSerializer().Serialize(new ClientSettings {schema=1,clientDirectory=Path.Combine(run,"missing")}));
            using(var form=new ScannerForm(false,file)) {form.LoadClient();Check(!form.CanStartScan,"Moved/deleted saved folder must relock");form.Close();}
            File.WriteAllText(file,"{broken");
            using(var form=new ScannerForm(false,file)) {form.LoadClient();Check(!form.CanStartScan,"Corrupt settings must relock");form.Close();}
            File.WriteAllText(report,"PASS: first-launch lock; wrong folder and unsupported EXE rejected without saving; explicit valid selection unlocks; no auto-scan; persistence/reload; atomic replacement; missing saved path and malformed settings relock. Isolated settings only; user's first-run setup not prefilled.\r\n");return 0;
        }catch(Exception ex){File.WriteAllText(report,ex.ToString());return 1;}
    }
}
