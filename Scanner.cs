using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class PointData {
    public string space { get; set; }
    public string units { get; set; }
    public double? x { get; set; }
    public double? y { get; set; }
    public double? z { get; set; }
}
public class ObjectData : PointData {
    public string region {get;set;}
    public string subregion {get;set;}
    public string name { get; set; }
    public string id { get; set; }
    public uint entry { get; set; }
    public bool remembered {get;set;}
    public string lastSeen {get;set;}
}
public class Snapshot {
    public string region {get;set;}
    public string subregion {get;set;}
    public int schema { get; set; }
    public string observedAt { get; set; }
    public string source { get; set; }
    public bool demo { get; set; }
    public PointData player { get; set; }
    public ObjectData[] objects { get; set; }
    public bool live { get; set; }
    public int totalObjects { get; set; }
    public int gameObjects { get; set; }
    public int unreadable { get; set; }
    public bool onTransport { get; set; }
    public long scanMilliseconds { get; set; }
    public double? facing { get; set; }
    public string context {get;set;}
    public int? mapId {get;set;}
    public bool rememberNavigation {get;set;}
}
public static class Reader {
    public static bool IsCache(string name) { return name!=null && String.Equals(name.Trim(),"Astral Cache",StringComparison.OrdinalIgnoreCase); }
    public static Snapshot Parse(string json, DateTimeOffset now) {
        var s = new JavaScriptSerializer { MaxJsonLength = 1048576 }.Deserialize<Snapshot>(json);
        DateTimeOffset date;
        if (s == null || s.schema != 1 || s.objects == null || s.objects.Length > 5000 || String.IsNullOrWhiteSpace(s.source))
            throw new InvalidDataException("Unvollständiges oder nicht unterstütztes Datenformat.");
        if (s.observedAt == null || !(s.observedAt.EndsWith("Z") || System.Text.RegularExpressions.Regex.IsMatch(s.observedAt, @"[+-]\d\d:\d\d$")) || !DateTimeOffset.TryParse(s.observedAt, out date))
            throw new InvalidDataException("Zeitstempel mit Zeitzone fehlt.");
        if ((now - date).TotalSeconds > 10) throw new InvalidDataException("Daten veraltet (> 10 Sekunden). Keine aktuelle Aussage möglich.");
        if ((date - now).TotalSeconds > 2) throw new InvalidDataException("Zeitstempel liegt in der Zukunft.");
        if (s.objects.Any(o => o == null || String.IsNullOrWhiteSpace(o.name))) throw new InvalidDataException("Objekt ohne Namen.");
        s.live = false; // A file cannot declare itself a direct client connection.
        return s;
    }
    static bool Valid(double? v) { return v.HasValue && !Double.IsNaN(v.Value) && !Double.IsInfinity(v.Value) && Math.Abs(v.Value) < 1e8; }
    public static string Position(PointData p, PointData o) {
        if (p == null || o == null || String.IsNullOrWhiteSpace(p.space) || p.space != o.space || p.units != o.units || (p.units != "yards" && p.units != "meters") || !Valid(p.x) || !Valid(p.y) || !Valid(o.x) || !Valid(o.y))
            return "Entfernung / Richtung nicht ableitbar";
        double dx = o.x.Value - p.x.Value, dy = o.y.Value - p.y.Value;
        double dz = Valid(p.z) && Valid(o.z) ? o.z.Value - p.z.Value : 0;
        string dim = Valid(p.z) && Valid(o.z) ? "3D" : "2D";
        return String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1} {1} ({2}) | ΔX {3:+0.0;-0.0;0}, ΔY {4:+0.0;-0.0;0}{5}", Math.Sqrt(dx*dx + dy*dy + dz*dz), p.units == "yards" ? "Yards" : "m", dim, dx, dy, dim == "3D" ? ", ΔZ " + dz.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "");
    }
    public static Snapshot Read(string path) {
        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
            if (f.Length > 1048576) throw new InvalidDataException("Quelldatei größer als 1 MiB.");
            using (var r = new StreamReader(f)) return Parse(r.ReadToEnd(), DateTimeOffset.UtcNow);
        }
    }
}
public partial class ScannerForm : Form {
    [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr handle,int id,uint modifiers,uint key);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr handle,int id);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr handle,out uint pid);
    bool hotkeyRegistered;
    const int LootHotkey=901;
    TextBox client = new TextBox { ReadOnly=true,Dock = DockStyle.Fill };
    TextBox source = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
    Label state = new Label { AutoSize = true, Text = "WoW-Ordner auswählen", ForeColor = Color.DarkOrange, Font = new Font("Segoe UI", 15, FontStyle.Bold) };
    Label details = new Label { AutoSize = true, MaximumSize = new Size(900,0), Text = "Direkte, ausschließlich lesende Prüfung der geladenen GameObjects." };
    Label process = new Label { AutoSize = true };
    DataGridView results = new DataGridView();
    DataGridView savedFinds=new DataGridView();
    CheckBox showLooted=new CheckBox {Text="Gelootete anzeigen",AutoSize=true};
    Label memoryStatus=new Label {AutoSize=true,Text="Fundspeicher wird geladen …"};
    CacheMemory cacheMemory;
    LocationDatabase locationDatabase;
    Label databaseStatus=new Label {AutoSize=true,Text="Fundortdatenbank wird geladen …"};
    DataGridView databaseRows=new DataGridView();
    string databaseError;
    Timer lootTimer=new Timer {Interval=50};
    DateTimeOffset lastLiveAt;
    string lootError;
    ProximityCompletion proximity=new ProximityCompletion();
    DebugJournal debugJournal=new DebugJournal();
    TextBox debugText=new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both,WordWrap=false};
    Label updateStatus=new Label {Text="Installiert: "+ReleaseInfo.Version,AutoSize=true};
    bool updateBusy;
    string pendingRelease;
    UpdateNotice updateNotice;
    Button installUpdate,checkUpdate;
    internal bool HasPendingUpdate {get{return pendingRelease!=null;}}
    internal bool InstallUpdateEnabled {get{return installUpdate.Enabled;}}
    void SetUpdateBusy(bool busy) {updateBusy=busy;if(!IsDisposed){installUpdate.Enabled=!busy;checkUpdate.Enabled=!busy;}}
    internal void ApplyUpdateResult(string json) {
        bool newer=ReleaseInfo.IsNewer(json);
        string description=ReleaseInfo.Describe(json);
        pendingRelease=newer?json:null;installUpdate.Enabled=!updateBusy;
        updateStatus.Text=description;
        if(newer)updateNotice.ShowNotice(ReleaseInfo.LatestVersion(json).ToString());else updateNotice.Dismiss();
        Write(description);
    }
    async void CheckUpdates() {await CheckUpdatesCore();}
    async System.Threading.Tasks.Task CheckUpdatesCore() {
        if(updateBusy)return;SetUpdateBusy(true);updateStatus.Text="GitHub wird geprüft …";
        try {
            string json=await System.Threading.Tasks.Task.Run(()=>AutomaticUpdater.Metadata());
            if(!IsDisposed)ApplyUpdateResult(json);
        }catch(Exception ex){if(!IsDisposed){pendingRelease=null;updateStatus.Text="Update-Prüfung fehlgeschlagen – erneut prüfen oder Downloads öffnen.";Write("Update: "+ex.Message);}}
        finally{SetUpdateBusy(false);}
    }
    async void ConfirmUpdate() {
        if(updateBusy)return;
        if(pendingRelease==null)await CheckUpdatesCore();
        if(IsDisposed || pendingRelease==null || updateBusy)return;
        string approvedRelease=pendingRelease;
        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(approvedRelease);
        updateNotice.Dismiss();
        SetUpdateBusy(true);
        if(MessageBox.Show(this,"Version "+data["tag_name"]+" jetzt herunterladen und installieren?\n\nDer Scanner wird gespeichert, geschlossen und danach neu gestartet.","Update installieren?",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){SetUpdateBusy(false);return;}
        try {
            var progress=new Progress<string>(message=>{if(!IsDisposed){updateStatus.Text=message;Write(message);}});
            var plan=await System.Threading.Tasks.Task.Run(()=>AutomaticUpdater.Prepare(approvedRelease,message=>((IProgress<string>)progress).Report(message)));
            if(IsDisposed || plan==null)return;
            updateStatus.Text="Update geprüft. Scanner wird gespeichert und neu gestartet …";Write(updateStatus.Text);
            timer.Stop();lootTimer.Stop();
            try {
                SyncLocations(true);
                if(cacheMemory!=null && !cacheMemory.Flush(DateTimeOffset.UtcNow,true))throw new IOException("Kisten konnten nicht gespeichert werden: "+cacheMemory.SaveError);
                if(locationDatabase!=null && !locationDatabase.Flush(true))throw new IOException("Fundorte konnten nicht gespeichert werden: "+locationDatabase.SaveError);
                await System.Threading.Tasks.Task.Run(()=>AutomaticUpdater.Launch(plan));
                if(!IsDisposed)Close();
            }catch{if(!IsDisposed){timer.Start();lootTimer.Start();}throw;}
        }
        catch(Exception ex){if(!IsDisposed){updateStatus.Text="Automatisches Update nicht möglich – bisherige Version bleibt verfügbar.";Write("Update: "+ex.Message);}}
        finally {SetUpdateBusy(false);}
    }
    void ExportDebug() {using(var d=new SaveFileDialog {Filter="Debug-Protokoll (*.txt)|*.txt",FileName="AstralScanner-Debug-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"})if(d.ShowDialog()==DialogResult.OK){debugJournal.Export(d.FileName);Write("Debug-Protokoll exportiert.");}}
    RewardTracker rewards=new RewardTracker();
    string rewardError,rewardStatus="Token-Erkennung noch nicht verbunden";
    void CheckRewards(Snapshot snapshot) {
        if(liveReader==null || cacheMemory==null){rewards.Reset();return;}
        try {
            var wallet=liveReader.ReadWallet();rewardStatus=wallet.valid?"Token-Erkennung aktiv · Stand "+wallet.tokens:"Token-Erkennung wartet auf Kontostand";
            string completed=rewards.Update(snapshot,wallet,cacheMemory,DateTimeOffset.UtcNow);
            if(completed!=null){cacheMemory.Flush(DateTimeOffset.UtcNow,true);Write("Automatisch als gelootet markiert: Token-Gutschrift und verschwundene Kiste | "+completed);RenderSaved();SyncLocations(true);}
            rewardError=null;
        }catch(Exception ex){rewards.Reset();rewardStatus="Token-Erkennung nicht verfügbar";if(rewardError!=ex.Message){Write(rewardStatus+": "+ex.Message);rewardError=ex.Message;}}
    }
    TextBox log = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    CheckBox sound = new CheckBox { Text = "Alarmton", Checked = true, AutoSize = true };
    Timer timer = new Timer { Interval = 200 };
    CacheOverlay overlay;
    CheckBox overlayEnabled=new CheckBox { Text="Ingame-Overlay",Checked=true,AutoSize=true };
    string previous = "", lastState = "";
    HashSet<string> known = new HashSet<string>();
    string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AstralScanner", "Logs");
    bool demoMode;
    LiveReader liveReader;
    Snapshot lastSnapshot;
    CheckBox allObjects = new CheckBox { Text = "Alle GameObjects anzeigen", Checked = true, AutoSize = true };
    int ticks;
    float uiScale=1;
    readonly string settingsFile;
    bool clientApproved;
    Button startScan,selectJson;
    public ScannerForm(bool promptForPath=true,string customSettingsFile=null,float? scaleOverride=null) {
        settingsFile=customSettingsFile??ClientSettings.DefaultFile;
        overlay=new CacheOverlay(customSettingsFile==null?null:Path.Combine(Path.GetDirectoryName(Path.GetFullPath(customSettingsFile)),"overlay-position.json"));
        if(customSettingsFile!=null)logDir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(customSettingsFile)),"Logs");
        using(var screen=Graphics.FromHwnd(IntPtr.Zero))uiScale=Math.Max(1,screen.DpiX/96f);
        if(scaleOverride.HasValue)uiScale=scaleOverride.Value;
        AutoScaleMode=AutoScaleMode.None;BuildUi();
        using(var iconStream=typeof(ScannerForm).Assembly.GetManifestResourceStream("AstralScanner.ico"))
            if(iconStream!=null)using(var appIcon=new Icon(iconStream))Icon=(Icon)appIcon.Clone();
        updateNotice=new UpdateNotice(uiScale);Controls.Add(updateNotice);updateNotice.BringToFront();updateNotice.Requested+=ConfirmUpdate;
        Action placeNotice=()=>{updateNotice.Width=Math.Min(Px(540),Math.Max(Px(280),ClientSize.Width-Px(32)));updateNotice.Left=(ClientSize.Width-updateNotice.Width)/2;};
        Resize+=(s,e)=>placeNotice();placeNotice();

        if(!scaleOverride.HasValue){var area=Screen.PrimaryScreen.WorkingArea;MinimumSize=new Size(Math.Min(Px(1000),area.Width-40),Math.Min(Px(700),area.Height-40));Size=new Size(Math.Min(Width,area.Width-40),Math.Min(Height,area.Height-40));}
        try {cacheMemory=new CacheMemory(customSettingsFile==null?CacheMemory.DefaultFile:Path.Combine(Path.GetDirectoryName(Path.GetFullPath(customSettingsFile)),"finds.json"));cacheMemory.CompletionChanged += r => Write(DebugJournal.CompletionMessage(r));RenderSaved();}catch(Exception ex){memoryStatus.Text="Funddatei nicht lesbar: "+ex.Message;Write(memoryStatus.Text);}
        try {locationDatabase=new LocationDatabase(customSettingsFile==null?LocationDatabase.DefaultFile:Path.Combine(Path.GetDirectoryName(Path.GetFullPath(customSettingsFile)),"locations.json"));SyncLocations(true);}catch(Exception ex){databaseStatus.Text="Datenbank nicht verfügbar: "+ex.Message;Write(databaseStatus.Text);}
        overlay.Diagnostic += message => Write(message);
        timer.Tick += (s,e) => Poll(); timer.Start(); Shown += (s,e) => { hotkeyRegistered=customSettingsFile==null && RegisterHotKey(Handle,LootHotkey,0x4003,0x4c);if(customSettingsFile==null && !hotkeyRegistered)Write("Strg+Alt+L ist nicht verfügbar; bitte die Fundliste zum Markieren verwenden.");LoadClient();if(customSettingsFile==null && !AutomaticUpdater.SkipStartup)CheckUpdates();if(AutomaticUpdater.SkipStartup)Write("Letztes Update wurde abgebrochen. Details: "+AutomaticUpdater.LogFile);RefreshSummary();if(!clientApproved && promptForPath)BeginInvoke(new Action(ChooseClient)); };
        lootTimer.Tick+=(s,e)=>PollLoot();lootTimer.Start();
        FormClosed += (s,e) => { if(hotkeyRegistered)UnregisterHotKey(Handle,LootHotkey);timer.Dispose();lootTimer.Dispose();SyncLocations(true);if(cacheMemory!=null)cacheMemory.Flush(DateTimeOffset.UtcNow,true);overlay.Dispose(); if(liveReader!=null)liveReader.Dispose(); };
    }
    void SyncLocations(bool force=false) {
        if(locationDatabase==null)return;
        try {
            int added=cacheMemory==null?0:locationDatabase.Collect(cacheMemory.Records);
            if(!locationDatabase.Flush(force))throw new IOException(locationDatabase.SaveError);
            if(force || added>0 || databaseError!=null)RenderLocations();
            databaseError=null;
        }catch(Exception ex){databaseStatus.Text="Datenbankfehler: "+ex.Message;if(databaseError!=ex.Message)Write(databaseStatus.Text);databaseError=ex.Message;}
    }
    void RenderLocations() {
        if(locationDatabase==null)return;
        int top=databaseRows.FirstDisplayedScrollingRowIndex;
        var selected=databaseRows.CurrentRow==null?null:databaseRows.CurrentRow.Tag as CacheLocation;int restore=-1;
        rebuildingRows=true;databaseRows.Rows.Clear();
        string query=databaseSearch==null?"":databaseSearch.Text.Trim();
        foreach(var p in locationDatabase.Records.OrderBy(p=>p.region).ThenBy(p=>p.mapId).ThenBy(p=>p.x)) {
            string region=RegionName(p.region,p.subregion),map=MapName(p.mapId);
            if(query.Length>0 && (region+" "+map+" "+p.realm).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)<0)continue;
            int i=databaseRows.Rows.Add(region,map,p.realm,DateTimeOffset.Parse(p.lastSeen).ToLocalTime().ToString("dd.MM.yyyy HH:mm"));databaseRows.Rows[i].Tag=p;
            if(selected!=null && selected.realm==p.realm && selected.mapId==p.mapId && selected.entry==p.entry && selected.x==p.x && selected.y==p.y && selected.z==p.z)restore=i;
        }
        databaseRows.ClearSelection();databaseRows.CurrentCell=null;
        if(restore>=0){databaseRows.CurrentCell=databaseRows.Rows[restore].Cells[0];databaseRows.Rows[restore].Selected=true;}
        if(top>=0 && databaseRows.Rows.Count>0)databaseRows.FirstDisplayedScrollingRowIndex=Math.Min(top,databaseRows.Rows.Count-1);
        rebuildingRows=false;UpdateDatabaseSelection();
        databaseStatus.Text=databaseRows.Rows.Count+" von "+locationDatabase.Records.Count()+" Fundorten · historische Sammlung, keine aktuellen Spawns";RefreshSummary();
    }
    void RequireDatabase() {if(locationDatabase==null)throw new InvalidOperationException("Fundortdatenbank nicht verfügbar. Details im Datenbank-Reiter.");}
    void ImportLocations() {
        RequireDatabase();
        using(var dialog=new OpenFileDialog {Title="Astral-Fundortdatenbanken zusammenführen",Filter="Astral-Fundortdatenbank (*.json)|*.json",Multiselect=true}) {
            if(dialog.ShowDialog()!=DialogResult.OK)return;
            SyncLocations(true);int added=locationDatabase.Import(dialog.FileNames);RenderLocations();Write("Datenbanken zusammengeführt: "+dialog.FileNames.Length+" Datei(en), "+added+" neue Fundorte. Persönlicher Lootstatus unverändert.");
            MessageBox.Show(this,added+" neue Fundorte übernommen.\nDoppelte Positionen wurden zusammengeführt.","Import abgeschlossen",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
    }
    void ExportLocations(bool lua) {
        RequireDatabase();SyncLocations(true);
        if(databaseError!=null)throw new InvalidOperationException("Export abgebrochen, weil lokale Funde nicht vollständig gesichert werden konnten: "+databaseError);
        using(var dialog=new SaveFileDialog {Title=lua?"Fundorte als Lua-Daten exportieren":"Fundortdatenbank exportieren",Filter=lua?"Lua-Daten (*.lua)|*.lua":"Astral-Fundortdatenbank (*.json)|*.json",DefaultExt=lua?"lua":"json",FileName=lua?"AstralCacheLocations.lua":"AstralCacheLocations.json",OverwritePrompt=true}) {
            if(dialog.ShowDialog()!=DialogResult.OK)return;
            if(lua)locationDatabase.ExportLua(dialog.FileName);else locationDatabase.ExportJson(dialog.FileName);
            Write("Fundortdatenbank exportiert: "+dialog.FileName+" | "+locationDatabase.Records.Count()+" Orte (Weltkoordinaten in Yards).");
        }
    }
    internal void SaveDatabasePreview(string path) {
        ShowSection("database");Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path);}
    }
    protected override void WndProc(ref Message message) {
        if(message.Msg==0x312 && message.WParam.ToInt32()==LootHotkey) {
            try {ConfirmOverlayLoot();}catch(Exception ex){Write("Fund konnte nicht bestätigt werden: "+ex.Message);}
            return;
        }
        base.WndProc(ref message);
    }
    void ConfirmOverlayLoot() {
        if(cacheMemory==null || liveReader==null || lastSnapshot==null || !overlay.Visible || (DateTimeOffset.UtcNow-lastLiveAt).TotalSeconds>1)return;
        uint foreground;GetWindowThreadProcessId(GetForegroundWindow(),out foreground);if(foreground!=(uint)liveReader.Pid)return;
        var target=overlay.CurrentDirection;
        if(target==null || !target.Distance.HasValue || target.Distance.Value>15) {Write("Strg+Alt+L: Kein verifizierter Overlay-Fund innerhalb von 15 Yards. Bei Bedarf in der Fundliste markieren.");return;}
        string key=CacheMemory.Key(lastSnapshot,target.Target);
        if(cacheMemory.Mark(key,true,"Vom Nutzer per Strg+Alt+L als gelootet bestätigt",DateTimeOffset.UtcNow)) {
            cacheMemory.Flush(DateTimeOffset.UtcNow,true);Write("Fund per Tastenkürzel bestätigt: "+key);RenderSaved();overlay.SetSnapshot(cacheMemory.ForNavigation(lastSnapshot),liveReader.Pid);
        }
    }
    void RenderSaved() {
        if(cacheMemory==null)return;
        var selected=savedFinds.CurrentRow==null?null:savedFinds.CurrentRow.Tag as SavedCache;
        string selectedKey=selected==null?null:selected.Key;int top=savedFinds.FirstDisplayedScrollingRowIndex;
        rebuildingRows=true;savedFinds.Rows.Clear();int restore=-1;
        foreach(var r in cacheMemory.Records.Where(r=>showLooted.Checked || !r.looted).OrderBy(r=>r.looted).ThenByDescending(r=>r.lastSeen)) {
            string status=r.looted?"Gelootet":cacheMemory.IsPresent(r)?"Sichtbar":"Gespeichert";
            int i=savedFinds.Rows.Add(status,RegionName(r.location.region,r.location.subregion),MapName(r.mapId),DateTimeOffset.Parse(r.lastSeen).ToLocalTime().ToString("dd.MM. HH:mm:ss"));savedFinds.Rows[i].Tag=r;
            savedFinds.Rows[i].Cells[0].Style.ForeColor=r.looted?Muted:cacheMemory.IsPresent(r)?Mint:Color.FromArgb(234,202,143);
            if(r.Key==selectedKey)restore=i;
        }
        savedFinds.ClearSelection();savedFinds.CurrentCell=null;if(restore>=0){savedFinds.CurrentCell=savedFinds.Rows[restore].Cells[0];savedFinds.Rows[restore].Selected=true;}
        if(top>=0 && savedFinds.Rows.Count>0)savedFinds.FirstDisplayedScrollingRowIndex=Math.Min(top,savedFinds.Rows.Count-1);
        rebuildingRows=false;UpdateFindSelection();
        memoryStatus.Text=cacheMemory.SaveError!=null?"Speichern fehlgeschlagen: "+cacheMemory.SaveError:cacheMemory.Records.Count(r=>!r.looted)+" offene Kisten · außerhalb der Reichweite bleiben sie gespeichert";RefreshSummary();
    }
    void MarkSaved(bool looted) {
        var row=savedFinds.CurrentRow==null?null:savedFinds.CurrentRow.Tag as SavedCache;
        if(row==null || cacheMemory==null)throw new InvalidOperationException("Bitte zuerst eine Kiste auswählen.");
        cacheMemory.Mark(row.Key,looted,looted?"Manuell als gelootet bestätigt":null,DateTimeOffset.UtcNow);cacheMemory.Flush(DateTimeOffset.UtcNow,true);
        Write((looted?"Kiste als gelootet markiert: ":"Kiste als offen markiert: ")+row.Key);RenderSaved();
        if(lastSnapshot!=null && liveReader!=null)overlay.SetSnapshot(cacheMemory.ForNavigation(lastSnapshot),liveReader.Pid);
    }
    void PollLoot() {
        if(liveReader==null || cacheMemory==null || lastSnapshot==null || !lastSnapshot.live || (DateTimeOffset.UtcNow-lastLiveAt).TotalSeconds>1) {if(cacheMemory!=null)cacheMemory.ResetLoot();return;}
        try {
            string completed=cacheMemory.ObserveLoot(lastSnapshot,liveReader.ReadLoot(),DateTimeOffset.UtcNow);
            if(completed!=null) {cacheMemory.Flush(DateTimeOffset.UtcNow,true);Write("Lootabschluss beobachtet: "+completed);RenderSaved();overlay.SetSnapshot(cacheMemory.ForNavigation(lastSnapshot),liveReader.Pid);}
            lootError=null;
        }catch(Exception ex){cacheMemory.ResetLoot();if(ex.Message!=lootError){lootError=ex.Message;Write("Automatische Loot-Erkennung momentan nicht verfügbar: "+lootError);}}
    }
    void SetApproved(bool approved) {clientApproved=approved;startScan.Enabled=approved;selectJson.Enabled=approved;startScan.Visible=approved;stopScan.Visible=approved;setupButton.Visible=!approved;}
    void RequireClient() { SetApproved(false);overlay.Clear();SetState("WoW-Ordner erforderlich","Wähle zuerst den Spielordner mit der passenden Wow.exe. Ohne gültigen Pfad bleibt der Scan gesperrt.",Color.DarkOrange);process.Text="Scan gesperrt"; }
    internal void LoadClient() {
        SetApproved(false);
        try {string saved=ClientSettings.Load(settingsFile);if(saved!=null) {client.Text=saved;SetApproved(true);SetState("Bereit zum Scannen","Gespeicherter WoW-Ordner geprüft. Mit „Scan starten“ verbinden.",Color.SteelBlue);process.Text="Scan noch nicht gestartet";return;} }
        catch(Exception ex) {Write("Gespeicherter WoW-Ordner nicht verwendbar: "+ex.Message);}
        client.Clear();RequireClient();
    }
    internal void AcceptClient(string directory) {
        string saved=ClientSettings.Save(settingsFile,directory);
        client.Text=saved;SetApproved(true);Inspect();
        SetState("Bereit zum Scannen","WoW-Ordner bestätigt und gespeichert. Klicke auf „Scan starten“.",Color.SteelBlue);process.Text="Scan noch nicht gestartet";
    }
    internal bool CanStartScan { get {return clientApproved && startScan.Enabled && selectJson.Enabled;} }
    internal bool IsScanning {get {return liveReader!=null;}}
    void ChooseClient() {
        Disconnect();
        using(var dialog=new FolderBrowserDialog {Description="Wähle den Project-Astral-Spielordner, in dem Wow.exe liegt. Erst nach einer gültigen Auswahl kann der Scan starten.",ShowNewFolderButton=false}) {
            if(Directory.Exists(client.Text))dialog.SelectedPath=client.Text;
            while(dialog.ShowDialog(this)==DialogResult.OK) {
                try {AcceptClient(dialog.SelectedPath);return;}
                catch(Exception ex) {MessageBox.Show(this,ex.Message,"Ordner nicht bestätigt",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            }
        }
        if(!clientApproved)RequireClient();
    }
    Button Button(string title, Action action) { var b = new UiActionButton { Text = title, AutoSize = true, Height = (int)(30*uiScale) }; b.Click += (s,e) => { try { action(); } catch(Exception ex) { SetState("Fehler", ex.Message, Color.Firebrick); } }; return b; }
    void Write(string message) {
        debugJournal.Add(message);RefreshDebugView();
        string line = DateTimeOffset.Now.ToString("o") + " " + message.Replace("\r"," ").Replace("\n"," | ");
        if (log.TextLength > 50000) log.Clear(); log.AppendText(line + Environment.NewLine);
        try { Directory.CreateDirectory(logDir); string path = Path.Combine(logDir, "scanner-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"); if (File.Exists(path) && new FileInfo(path).Length > 2000000) File.Move(path, path + "." + DateTime.UtcNow.Ticks); File.AppendAllText(path, line + Environment.NewLine); } catch { log.AppendText("Logdatei nicht beschreibbar.\r\n"); }
    }
    void SetState(string title, string explanation, Color color) {
        state.Text = title.StartsWith("LIVE")?(title.Contains("Keine")?"Scanner läuft":"Kiste entdeckt"):title;
        state.ForeColor=color==Color.Firebrick?Color.FromArgb(255,155,155):color==Color.DarkOrange?Color.FromArgb(234,202,143):Mint;
        details.Text=explanation;RefreshSummary();
        string key = title + " | " + explanation; if (key != lastState) { lastState = key; Write(key); }
    }
    void Inspect() {
        string exe = Path.Combine(client.Text, "Wow.exe");
        if (!File.Exists(exe)) { Write("Clientprüfung: Wow.exe nicht gefunden."); return; }
        var version = FileVersionInfo.GetVersionInfo(exe).FileVersion;
        string hash; using(var f = File.OpenRead(exe)) using(var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
        Write("Clientprüfung: " + exe + "; Version " + version + "; SHA256 " + hash);
        try {Write("Kompatibilität: "+ClientCompatibility.ValidateFile(exe));}catch(Exception ex){Write("Kompatibilität: "+ex.Message);}
        string addon = Path.Combine(client.Text, "Interface", "AddOns", "ProjectAstral");
        Write("ProjectAstral-Addon: " + (Directory.Exists(addon) ? "vorhanden" : "nicht gefunden") + ". Live-Reader prüft das konkrete Clientprofil separat.");
    }
    void Disconnect() { proximity.Reset();rewards.Reset();overlay.Clear();if(cacheMemory!=null){cacheMemory.LostLiveData();cacheMemory.Flush(DateTimeOffset.UtcNow,true);RenderSaved();}if(liveReader!=null) {liveReader.Dispose();liveReader=null;} lastSnapshot=null; source.Clear(); demoMode = false; previous = ""; known.Clear(); results.Rows.Clear(); if(!clientApproved)RequireClient();else SetState("Scanner gestoppt", "Mit „Scan starten“ die geladenen Spielobjekte wieder lesend prüfen.", Color.DarkOrange); }
    void ConnectLive() {
        Disconnect();
        if(!clientApproved) {RequireClient();return;}
        try {LiveReader.ValidateDirectory(client.Text);}catch(Exception ex) {Write(ex.Message);client.Clear();RequireClient();return;}
        try { liveReader=new LiveReader(client.Text); source.Text="LIVE: Wow.exe · PID " + liveReader.Pid + " · PROCESS_VM_READ"; Write("Live verbunden. Nur Leserechte; "+liveReader.Compatibility+"; Getter-Struktur bestätigt. Betreiberfreigabe nicht geprüft."); }
        catch(Exception ex) { SetState("Live-Verbindung fehlgeschlagen",ex.Message,Color.Firebrick); }
    }
    void SelectSource() { if(!clientApproved) {RequireClient();return;}using (var d = new OpenFileDialog { Filter = "Objekt-Snapshot (*.json)|*.json", CheckFileExists = true }) if (d.ShowDialog() == DialogResult.OK) { Disconnect(); source.Text = d.FileName; Poll(); } }
    void Demo() {
        Disconnect(); demoMode = true;
        var s = new Snapshot { demo = true, source = "Eingebauter Funktionstest", player = new PointData { space="demo", units="yards", x=0,y=0,z=0 }, objects = new[] { new ObjectData { name="Astral Cache", id="DEMO-1",space="demo",units="yards",x=30,y=40,z=0 } } };
        Display(s); Write("DEMO: synthetischer Fund, kein Zugriff auf Spielobjekte.");
    }
    void Display(Snapshot s) {
        if(s.live && !s.demo && cacheMemory!=null) {cacheMemory.Update(s,DateTimeOffset.UtcNow);cacheMemory.Flush(DateTimeOffset.UtcNow);if(ticks%5==0){RenderSaved();SyncLocations();}}
        if(s.live && cacheMemory!=null) {
            var completed=proximity.Update(s,cacheMemory,DateTimeOffset.UtcNow);
            if(completed.Length>0){cacheMemory.Flush(DateTimeOffset.UtcNow,true);Write("Näheprüfung: "+completed.Length+" Kiste(n) als gelootet markiert. "+proximity.Status);RenderSaved();SyncLocations(true);}
            if(ticks%10==0){debugJournal.Add("Scan · Karte "+s.mapId+" · Region "+s.region+" / "+s.subregion+" · Objekte "+s.gameObjects+" · unlesbar "+s.unreadable+" · "+proximity.Status);RefreshDebugView();}
        }else proximity.Reset();
        if(s.live && !s.demo)CheckRewards(s);else rewards.Reset();
        var matches = s.objects.Where(o => Reader.IsCache(o.name) && (cacheMemory==null || !cacheMemory.Completed(s,o))).ToArray();
        Snapshot navigation=cacheMemory==null?s:cacheMemory.ForNavigation(s);int remembered=navigation.objects.Count(o=>o.remembered);
        lastSnapshot=s;if(ticks%5==0 || matches.Length>0 || results.Rows.Count==0)RenderObjects(s); var current = new HashSet<string>();
        if(s.live && liveReader!=null){lastLiveAt=DateTimeOffset.UtcNow;overlay.SetSnapshot(navigation,liveReader.Pid);}else overlay.Clear();
        foreach(var o in matches) { string key = s.source + "|" + (o.id ?? (o.space + ":" + o.x + ":" + o.y + ":" + o.z)); current.Add(key); }
        string title = s.live ? "LIVE · " : s.demo ? "DEMO · " : "EXPORT · "; title += matches.Length > 0 ? "Astral Cache erkannt (" + matches.Length + ")" : s.live ? "Keine Astral Cache in den gelesenen Objekten" : "Kein Treffer im aktuellen Export";
        if(s.live && matches.Length==0 && remembered>0)title=remembered+" gespeicherte Kiste(n) auf dieser Karte";
        string explanation = s.live ? "Namen werden direkt aus der Objektliste gelesen. " + (s.unreadable>0 ? "Achtung: " + s.unreadable + " GameObjects nicht lesbar; Scan unvollständig. " : "") + (s.onTransport ? "Auf Transport: Entfernung ausgesetzt, da Koordinaten lokal sein können." : "Entfernung als Luftlinie; ΔX/ΔY beziehen sich auf Weltachsen.") : "Quelle: " + s.source + (s.demo ? ". Simulierte Daten – kein echter Spielfund." : ". Externe Angaben; Herkunft und Vollständigkeit nicht verifiziert.");
        if(s.live && remembered>0)explanation="Das Overlay kann zu gespeicherten Fundorten führen. Ob die Kiste dort noch steht, ist außerhalb der geladenen Umgebung nicht bestätigt.";
        if(s.live && (cacheMemory==null || cacheMemory.SaveError!=null))explanation="ACHTUNG: Funde können derzeit nicht dauerhaft gespeichert werden. Details unter Gespeicherte Kisten.";
        else if(s.live && s.context==null)explanation+=" Fundspeicherung ausgesetzt: Karte/Realm nicht sicher zugeordnet.";
        SetState(title, explanation, s.demo ? Color.SlateBlue : matches.Length > 0 ? Color.DarkGreen : s.unreadable>0 ? Color.DarkOrange : Color.SteelBlue);
        if(s.live)process.Text=String.Format("Letzter Scan {0:T} · {1} Objekte · {2}/{3} GameObjects mit Namen · {4} ms\nSpieler: {5}",DateTime.Now,s.totalObjects,s.objects.Length,s.gameObjects,s.scanMilliseconds,s.player==null ? "Position nicht verfügbar" : String.Format("X {0:F1} / Y {1:F1} / Z {2:F1}",s.player.x,s.player.y,s.player.z));
        if (current.Except(known).Any()) { Write("Fund: " + String.Join("; ", matches.Select(o=>o.name+" | ID "+o.entry+" | GUID "+o.id+" | "+Reader.Position(s.player,o)))); if (sound.Checked) System.Media.SystemSounds.Exclamation.Play(); }
        if(s.live)process.Text+=" · "+rewardStatus;
        known = current;
    }
    void RenderObjects(Snapshot s) {
        int top=results.FirstDisplayedScrollingRowIndex;results.Rows.Clear();
        foreach(var o in s.objects.Where(o=>allObjects.Checked || (Reader.IsCache(o.name) && (cacheMemory==null || !cacheMemory.Completed(s,o)))).OrderByDescending(o=>Reader.IsCache(o.name)).ThenBy(o=>o.name)) {
            double distance=Navigation.Distance(s.player,o);
            int i=results.Rows.Add(o.name,RegionName(o.region,o.subregion),Double.IsInfinity(distance)?"Nicht verfügbar":distance.ToString("F1")+" yd",s.live?"Sichtbar":s.demo?"Testdaten":"Datei-Import");results.Rows[i].Tag=o;
        }
        results.ClearSelection();results.CurrentCell=null;if(top>=0 && results.Rows.Count>0)results.FirstDisplayedScrollingRowIndex=Math.Min(top,results.Rows.Count-1);RefreshSummary();
    }
    void Poll() {
        if(!clientApproved)return;
        ticks++;
        if(liveReader!=null) {
            try { var snapshot=liveReader.Scan(); Display(snapshot); if(ticks%150==0)Write(String.Format("Live-Status: {0} Objekte, {1}/{2} GameObject-Namen, {3} ms",snapshot.totalObjects,snapshot.objects.Length,snapshot.gameObjects,snapshot.scanMilliseconds)); }
            catch(Exception ex) { proximity.Reset();rewards.Reset();overlay.Clear();if(cacheMemory!=null){cacheMemory.LostLiveData();RenderSaved();}lastSnapshot=null;results.Rows.Clear();known.Clear();SetState("Live-Scan nicht verfügbar",ex.Message,Color.Firebrick);process.Text="Kein aktueller vollständiger Scan · wird erneut geprüft"; }
            return;
        }
        if (ticks % 5 == 0) { var ps = Process.GetProcessesByName("Wow"); process.Text = ps.Length > 0 ? "WoW läuft · Live-Reader gestoppt" : "WoW-Prozess nicht gefunden"; foreach(var p in ps) p.Dispose(); }
        if (demoMode || String.IsNullOrWhiteSpace(source.Text)) return;
        try { var s = Reader.Read(source.Text); string signature = new JavaScriptSerializer().Serialize(s); if (signature != previous) { Display(s); previous = signature; } }
        catch(Exception ex) { previous = ""; known.Clear(); results.Rows.Clear(); SetState("Quelle nicht auswertbar", ex.Message, Color.Firebrick); }
    }
}
public static class Program {
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] public static int Main(string[] args) {
        SetProcessDPIAware();
        if(args.Length==2 && args[0]=="--apply-update")return AutomaticUpdater.Apply(args[1]);
        if(args.Length==2 && args[0]=="--updater-test")return UpdaterTests.Run(args[1]);
        if(args.Length==2 && args[0]=="--updater-parent")return UpdaterTests.Parent(args[1]);
        AutomaticUpdater.SkipStartup=args.Contains("--skip-auto-update");
        if (args.Contains("--self-test")) return Tests.Run();
        if (args.Contains("--overlay-test")) return OverlayTests.Run();
        if (args.Length==3 && args[0]=="--setup-test") return SetupTests.Run(args[1],args[2]);
        if (args.Length==2 && args[0]=="--memory-test") return MemoryTests.Run(args[1]);
        if (args.Length==2 && args[0]=="--database-test") return DatabaseTests.Run(args[1]);
        if (args.Length==2 && args[0]=="--reward-test") return RewardTests.Run(args[1]);
        if (args.Length==2 && args[0]=="--desktop-test") return DesktopTests.Run(args[1]);
        if (args.Length==2 && args[0]=="--ui-test") return UiAcceptanceTests.Run(args[1]);
        if(args.Contains("--update-check")){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-check-results.txt"),ReleaseInfo.Check());return 0;}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-check-results.txt"),ex.ToString());return 1;}}
        if (args.Contains("--live-once")) {
            try { string directory=ClientSettings.Load(ClientSettings.DefaultFile);if(directory==null)throw new InvalidOperationException("Zuerst die App öffnen und den WoW-Ordner auswählen.");using(var reader=new LiveReader(directory)) { var s=reader.Scan(); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-check.json"),new JavaScriptSerializer().Serialize(s)); } return 0; }
            catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-check.json"),new JavaScriptSerializer().Serialize(new {error=ex.Message}));return 1; }
        }
        bool firstInstance;
        string instanceName="Local\\AstralScanner-"+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
        using(var instance=new System.Threading.Mutex(true,instanceName,out firstInstance)) {
        if(!firstInstance){MessageBox.Show("Astral Scanner läuft bereits. Bitte das vorhandene Fenster verwenden.","Astral Scanner",MessageBoxButtons.OK,MessageBoxIcon.Information);return 0;}
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using(var form = new ScannerForm(!args.Contains("--smoke-test"))) {
            if (args.Contains("--smoke-test")) { var t = new Timer { Interval=1500 }; t.Tick += (s,e) => { t.Stop();using(var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app-preview.png")); } form.SaveDatabasePreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"database-preview.png"));form.Close(); }; t.Start(); Application.Run(form); t.Dispose(); }
            else Application.Run(form);
        }
        return 0;
        }
    }
}
public static class Tests {
    static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    public static int Run() {
        try {
            var now = DateTimeOffset.UtcNow;
            var s = new Snapshot { schema=1, source="test", observedAt=now.ToString("o"), objects=new[] { new ObjectData { name="Astral Cache" } } };
            var json = new JavaScriptSerializer(); Check(Reader.Parse(json.Serialize(s), now).objects.Length == 1);
            Check(Reader.IsCache("Astral Cache")); Check(Reader.IsCache(" astral cache ")); Check(!Reader.IsCache("Astral Table")); Check(!Reader.IsCache("Astral Cache Fragment")); Check(!Reader.IsCache(null));
            s.live=true; Check(!Reader.Parse(json.Serialize(s),now).live); s.live=false;
            Check(Reader.Position(new PointData { space="1",units="yards",x=0,y=0 }, new PointData { space="1",units="yards",x=3,y=4 }).StartsWith("5.0 Yards"));
            Check(Reader.Position(new PointData { space="1",units="yards",x=0,y=0,z=0 }, new PointData { space="1",units="yards",x=0,y=0,z=7 }).StartsWith("7.0 Yards (3D)"));
            Check(Reader.Position(new PointData { space="1",units="yards",x=0,y=0 }, new PointData { space="2",units="yards",x=3,y=4 }).Contains("nicht ableitbar"));
            Check(Reader.Position(null, new PointData()).Contains("nicht ableitbar"));
            Check(Reader.Position(new PointData { space="1",units="yards",x=Double.NaN,y=0 }, new PointData { space="1",units="yards",x=3,y=4 }).Contains("nicht ableitbar"));
            foreach(var time in new[] { now.AddSeconds(-11), now.AddSeconds(5) }) { s.observedAt=time.ToString("o"); bool rejected=false; try { Reader.Parse(json.Serialize(s),now); } catch(InvalidDataException) { rejected=true; } Check(rejected); }
            s.observedAt=now.ToString("o"); s.objects=new ObjectData[] {null}; bool invalid=false; try { Reader.Parse(json.Serialize(s),now); } catch(InvalidDataException) { invalid=true; } Check(invalid);
            OverlayTests.CheckDirections();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),"PASS: exact name match and false-positive rejection; file cannot impersonate live source; parsing; 2D/3D distance; map mismatch; missing/nonfinite coordinates; stale/future snapshots; null object rejection; overlay directions/rotation/angle wrapping/nearest cache/height/transport/no-demo.\r\n"); return 0;
        } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),ex.ToString()); return 1; }
    }
}



