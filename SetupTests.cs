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
