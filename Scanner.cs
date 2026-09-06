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
    public string name { get; set; }
    public string id { get; set; }
    public uint entry { get; set; }
    public bool remembered {get;set;}
    public string lastSeen {get;set;}
}
public class Snapshot {
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
public class ScannerForm : Form {
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
    ListBox results = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    ListBox savedFinds=new ListBox {Dock=DockStyle.Fill,HorizontalScrollbar=true};
    CheckBox showLooted=new CheckBox {Text="Gelootete anzeigen",AutoSize=true};
    Label memoryStatus=new Label {AutoSize=true,Text="Fundspeicher wird geladen …"};
    CacheMemory cacheMemory;
    LocationDatabase locationDatabase;
    Label databaseStatus=new Label {AutoSize=true,Text="Fundortdatenbank wird geladen …"};
    ListBox databaseRows=new ListBox {Dock=DockStyle.Fill,HorizontalScrollbar=true};
    string databaseError;
    Timer lootTimer=new Timer {Interval=50};
    DateTimeOffset lastLiveAt;
    string lootError;
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
    CacheOverlay overlay=new CacheOverlay();
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
    public ScannerForm(bool promptForPath=true,string customSettingsFile=null) {
        settingsFile=customSettingsFile??ClientSettings.DefaultFile;
        using(var screen=Graphics.FromHwnd(IntPtr.Zero))uiScale=Math.Max(1,screen.DpiX/96f);
        AutoScaleMode=AutoScaleMode.None;
        var available=Screen.PrimaryScreen.WorkingArea;
        Text = "Astral Cache Scanner · Live · Nur lesend"; Width = Math.Min((int)(1100*uiScale),available.Width-40); Height = Math.Min((int)(800*uiScale),available.Height-40); MinimumSize = new Size(Math.Min((int)(1000*uiScale),available.Width-40),Math.Min((int)(760*uiScale),available.Height-40));
        details.MaximumSize=new Size((int)(1000*uiScale),0);
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding((int)(20*uiScale)), ColumnCount = 1, RowCount = 10 };
        foreach (int h in new[] {40,38,38,42,60,45,70}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h*uiScale));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,50)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30*uiScale)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        layout.Controls.Add(new Label { Text = "ASTRAL CACHE  /  DESKTOP SCANNER", Font = new Font("Segoe UI",18,FontStyle.Bold), AutoSize = true });
        var a = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; a.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); a.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180*uiScale)); a.Controls.Add(client); a.Controls.Add(Button("WoW-Ordner wählen", ChooseClient)); layout.Controls.Add(a);
        var b = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; b.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); b.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180*uiScale)); b.Controls.Add(source); selectJson=Button("JSON-Quelle wählen", SelectSource);selectJson.Enabled=false;b.Controls.Add(selectJson); layout.Controls.Add(b);
        layout.Controls.Add(state); layout.Controls.Add(details); layout.Controls.Add(process);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        startScan=Button("Scan starten", ConnectLive);startScan.Enabled=false;actions.Controls.Add(startScan); actions.Controls.Add(Button("Stoppen", Disconnect)); actions.Controls.Add(sound);
        var top = new CheckBox { Text = "Immer im Vordergrund", AutoSize = true }; top.CheckedChanged += (s,e) => TopMost = top.Checked; actions.Controls.Add(top);
        actions.Controls.Add(Button("Logs öffnen", () => { Directory.CreateDirectory(logDir); Process.Start("explorer.exe", logDir); }));
        actions.Controls.Add(Button("Scan speichern", () => { if(lastSnapshot==null) throw new InvalidOperationException("Noch kein aktueller Scan vorhanden."); Directory.CreateDirectory(logDir); string path=Path.Combine(logDir,"snapshot-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json"); File.WriteAllText(path,new JavaScriptSerializer().Serialize(lastSnapshot));Write("Aktueller Scan gespeichert: "+path); }));
        allObjects.CheckedChanged += (s,e) => { if(lastSnapshot!=null) RenderObjects(lastSnapshot); }; actions.Controls.Add(allObjects);
        overlayEnabled.CheckedChanged+=(s,e)=>overlay.SetEnabled(overlayEnabled.Checked);actions.Controls.Add(overlayEnabled);
        var anchor=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=(int)(130*uiScale) };anchor.Items.AddRange(new object[]{"Oben Mitte","Oben links","Oben rechts","Unten Mitte"});anchor.SelectedIndex=0;anchor.SelectedIndexChanged+=(s,e)=>overlay.OverlayAnchor=anchor.SelectedItem.ToString();actions.Controls.Add(anchor);
        var tabs=new TabControl {Dock=DockStyle.Fill};var livePage=new TabPage("Live-Objekte");livePage.Controls.Add(results);tabs.TabPages.Add(livePage);
        var savedPage=new TabPage("Gespeicherte Kisten");var savedLayout=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};savedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,40*uiScale));savedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,35*uiScale));savedLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var savedActions=new FlowLayoutPanel {Dock=DockStyle.Fill};savedActions.Controls.Add(Button("Als gelootet markieren",()=>MarkSaved(true)));savedActions.Controls.Add(Button("Wieder öffnen",()=>MarkSaved(false)));showLooted.CheckedChanged+=(s,e)=>RenderSaved();savedActions.Controls.Add(showLooted);savedLayout.Controls.Add(savedActions);savedLayout.Controls.Add(memoryStatus);savedLayout.Controls.Add(savedFinds);savedPage.Controls.Add(savedLayout);tabs.TabPages.Add(savedPage);
        var databasePage=new TabPage("Fundort-Datenbank");databasePage.Name="databasePage";
        var databaseLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};databaseLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,40*uiScale));databaseLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,40*uiScale));databaseLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var databaseActions=new FlowLayoutPanel {Dock=DockStyle.Fill};databaseActions.Controls.Add(Button("Importieren / zusammenführen",ImportLocations));databaseActions.Controls.Add(Button("JSON exportieren",()=>ExportLocations(false)));databaseActions.Controls.Add(Button("Lua fürs Addon exportieren",()=>ExportLocations(true)));databaseLayout.Controls.Add(databaseActions);databaseLayout.Controls.Add(databaseStatus);databaseLayout.Controls.Add(databaseRows);databasePage.Controls.Add(databaseLayout);tabs.TabPages.Add(databasePage);
        tabs.SelectedIndexChanged+=(s,e)=>{if(tabs.SelectedTab==databasePage){SyncLocations(true);if(databaseError==null)RenderLocations();}};
        layout.Controls.Add(actions); layout.Controls.Add(tabs); layout.Controls.Add(new Label { Text = "Statusprotokoll · Treffer stammen aus geladenen Objekten, kein Mouseover nötig", AutoSize = true }); layout.Controls.Add(log); Controls.Add(layout);
        try {cacheMemory=new CacheMemory(CacheMemory.DefaultFile);RenderSaved();}catch(Exception ex){memoryStatus.Text="Funddatei nicht lesbar: "+ex.Message;Write(memoryStatus.Text);}
        if(customSettingsFile==null)try {locationDatabase=new LocationDatabase(LocationDatabase.DefaultFile);SyncLocations(true);}catch(Exception ex){databaseStatus.Text="Datenbank nicht verfügbar: "+ex.Message;Write(databaseStatus.Text);}
        timer.Tick += (s,e) => Poll(); timer.Start(); Shown += (s,e) => { hotkeyRegistered=RegisterHotKey(Handle,LootHotkey,0x4003,0x4c);if(!hotkeyRegistered)Write("Strg+Alt+L ist nicht verfügbar; bitte die Fundliste zum Markieren verwenden.");LoadClient();if(!clientApproved && promptForPath)BeginInvoke(new Action(ChooseClient)); };
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
        int top=databaseRows.TopIndex;databaseRows.BeginUpdate();databaseRows.Items.Clear();
        foreach(var p in locationDatabase.Records.OrderBy(p=>p.realm).ThenBy(p=>p.mapId).ThenBy(p=>p.x).ThenBy(p=>p.y))databaseRows.Items.Add(String.Format("{0} | Karte {1} | Astral Cache ({2}) | X {3:F1} / Y {4:F1} / Z {5} | zuletzt {6}",p.realm,p.mapId,p.entry,p.x,p.y,p.z.HasValue?p.z.Value.ToString("F1"):"?",DateTimeOffset.Parse(p.lastSeen).ToLocalTime().ToString("dd.MM.yyyy HH:mm")));
        if(databaseRows.Items.Count>0)databaseRows.TopIndex=Math.Min(top,databaseRows.Items.Count-1);databaseRows.EndUpdate();
        databaseStatus.Text=locationDatabase.Records.Count()+" historische Fundorte · inklusive gelooteter Kisten · keine aktuellen Spawnmeldungen";
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
        var page=Controls.Find("databasePage",true).FirstOrDefault() as TabPage;
        if(page==null)return;((TabControl)page.Parent).SelectedTab=page;
        Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path);}
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
    sealed class SavedRow {public SavedCache Record;public string Text;public override string ToString(){return Text;}}
    void RenderSaved() {
        if(cacheMemory==null)return;
        string selected=savedFinds.SelectedItem is SavedRow?((SavedRow)savedFinds.SelectedItem).Record.Key:null;
        savedFinds.BeginUpdate();savedFinds.Items.Clear();
        foreach(var r in cacheMemory.Records.Where(r=>showLooted.Checked || !r.looted).OrderBy(r=>r.looted).ThenByDescending(r=>r.lastSeen)) {
            string map=r.mapId==0?"Östliche Königreiche":r.mapId==1?"Kalimdor":r.mapId==530?"Scherbenwelt":r.mapId==571?"Nordend":"Karte "+r.mapId;
            string status=r.looted?"Gelootet":cacheMemory.IsPresent(r)?"Aktuell geladen":"Gespeichert · nicht aktuell bestätigt";
            string coordinates=r.location.x.HasValue?String.Format("X {0:F1} / Y {1:F1} / Z {2:F1}",r.location.x,r.location.y,r.location.z):"Position fehlt";
            var row=new SavedRow {Record=r,Text=status+" | "+map+" | "+coordinates+" | zuletzt "+DateTimeOffset.Parse(r.lastSeen).ToLocalTime().ToString("dd.MM. HH:mm:ss")+" | "+r.context};
            int index=savedFinds.Items.Add(row);if(r.Key==selected)savedFinds.SelectedIndex=index;
        }
        savedFinds.EndUpdate();
        memoryStatus.Text=cacheMemory.SaveError!=null?"Speichern fehlgeschlagen: "+cacheMemory.SaveError:cacheMemory.Records.Count(r=>!r.looted)+" offene Fundorte · verschwundene Objekte werden nicht automatisch gelöscht";
    }
    void MarkSaved(bool looted) {
        var row=savedFinds.SelectedItem as SavedRow;
        if(row==null || cacheMemory==null)throw new InvalidOperationException("Bitte zuerst eine Kiste in der Fundliste auswählen.");
        cacheMemory.Mark(row.Record.Key,looted,looted?"Manuell als gelootet bestätigt":null,DateTimeOffset.UtcNow);cacheMemory.Flush(DateTimeOffset.UtcNow,true);
        Write((looted?"Fund als gelootet markiert: ":"Fund wieder geöffnet: ")+row.Record.Key);RenderSaved();
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
    void SetApproved(bool approved) {clientApproved=approved;startScan.Enabled=approved;selectJson.Enabled=approved;}
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
    Button Button(string title, Action action) { var b = new Button { Text = title, AutoSize = true, Height = (int)(30*uiScale) }; b.Click += (s,e) => { try { action(); } catch(Exception ex) { SetState("Fehler", ex.Message, Color.Firebrick); } }; return b; }
    void Write(string message) {
        string line = DateTimeOffset.Now.ToString("o") + " " + message.Replace("\r"," ").Replace("\n"," | ");
        if (log.TextLength > 50000) log.Clear(); log.AppendText(line + Environment.NewLine);
        try { Directory.CreateDirectory(logDir); string path = Path.Combine(logDir, "scanner-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"); if (File.Exists(path) && new FileInfo(path).Length > 2000000) File.Move(path, path + "." + DateTime.UtcNow.Ticks); File.AppendAllText(path, line + Environment.NewLine); } catch { log.AppendText("Logdatei nicht beschreibbar.\r\n"); }
    }
    void SetState(string title, string explanation, Color color) {
        state.Text = title; state.ForeColor = color; details.Text = explanation;
        string key = title + " | " + explanation; if (key != lastState) { lastState = key; Write(key); }
    }
    void Inspect() {
        string exe = Path.Combine(client.Text, "Wow.exe");
        if (!File.Exists(exe)) { Write("Clientprüfung: Wow.exe nicht gefunden."); return; }
        var version = FileVersionInfo.GetVersionInfo(exe).FileVersion;
        string hash; using(var f = File.OpenRead(exe)) using(var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
        Write("Clientprüfung: " + exe + "; Version " + version + "; SHA256 " + hash);
        string addon = Path.Combine(client.Text, "Interface", "AddOns", "ProjectAstral");
        Write("ProjectAstral-Addon: " + (Directory.Exists(addon) ? "vorhanden" : "nicht gefunden") + ". Live-Reader prüft das konkrete Clientprofil separat.");
    }
    void Disconnect() { rewards.Reset();overlay.Clear();if(cacheMemory!=null){cacheMemory.LostLiveData();cacheMemory.Flush(DateTimeOffset.UtcNow,true);RenderSaved();}if(liveReader!=null) {liveReader.Dispose();liveReader=null;} lastSnapshot=null; source.Clear(); demoMode = false; previous = ""; known.Clear(); results.Items.Clear(); if(!clientApproved)RequireClient();else SetState("Scanner gestoppt", "Mit „Scan starten“ die geladenen Spielobjekte wieder lesend prüfen.", Color.DarkOrange); }
    void ConnectLive() {
        Disconnect();
        if(!clientApproved) {RequireClient();return;}
        try {LiveReader.ValidateDirectory(client.Text);}catch(Exception ex) {Write(ex.Message);client.Clear();RequireClient();return;}
        try { liveReader=new LiveReader(client.Text); source.Text="LIVE: Wow.exe · PID " + liveReader.Pid + " · PROCESS_VM_READ"; Write("Live verbunden. Nur Leserechte; Client-Prüfsumme und Getter-Struktur bestätigt. Betreiberfreigabe nicht geprüft."); }
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
        if(s.live && !s.demo)CheckRewards(s);else rewards.Reset();
        var matches = s.objects.Where(o => Reader.IsCache(o.name) && (cacheMemory==null || !cacheMemory.Completed(s,o))).ToArray();
        Snapshot navigation=cacheMemory==null?s:cacheMemory.ForNavigation(s);int remembered=navigation.objects.Count(o=>o.remembered);
        lastSnapshot=s;if(ticks%5==0 || matches.Length>0 || results.Items.Count==0)RenderObjects(s); var current = new HashSet<string>();
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
        var list=s.objects.Where(o=>allObjects.Checked || Reader.IsCache(o.name)).OrderByDescending(o=>Reader.IsCache(o.name)).ThenBy(o=>o.name);
        int topIndex=results.Items.Count>0 ? results.TopIndex : 0;
        results.BeginUpdate(); results.Items.Clear();
        foreach(var o in list)results.Items.Add((Reader.IsCache(o.name)?"*** ":"")+o.name+" | ID "+o.entry+" | "+Reader.Position(s.player,o));
        if(results.Items.Count>0)results.TopIndex=s.objects.Any(o=>Reader.IsCache(o.name))?0:Math.Min(topIndex,results.Items.Count-1);
        results.EndUpdate();
    }
    void Poll() {
        if(!clientApproved)return;
        ticks++;
        if(liveReader!=null) {
            try { var snapshot=liveReader.Scan(); Display(snapshot); if(ticks%150==0)Write(String.Format("Live-Status: {0} Objekte, {1}/{2} GameObject-Namen, {3} ms",snapshot.totalObjects,snapshot.objects.Length,snapshot.gameObjects,snapshot.scanMilliseconds)); }
            catch(Exception ex) { rewards.Reset();overlay.Clear();if(cacheMemory!=null){cacheMemory.LostLiveData();RenderSaved();}lastSnapshot=null;results.Items.Clear();known.Clear();SetState("Live-Scan nicht verfügbar",ex.Message,Color.Firebrick);process.Text="Kein aktueller vollständiger Scan · wird erneut geprüft"; }
            return;
        }
        if (ticks % 5 == 0) { var ps = Process.GetProcessesByName("Wow"); process.Text = ps.Length > 0 ? "WoW läuft · Live-Reader gestoppt" : "WoW-Prozess nicht gefunden"; foreach(var p in ps) p.Dispose(); }
        if (demoMode || String.IsNullOrWhiteSpace(source.Text)) return;
        try { var s = Reader.Read(source.Text); string signature = new JavaScriptSerializer().Serialize(s); if (signature != previous) { Display(s); previous = signature; } }
        catch(Exception ex) { previous = ""; known.Clear(); results.Items.Clear(); SetState("Quelle nicht auswertbar", ex.Message, Color.Firebrick); }
    }
}
public static class Program {
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] public static int Main(string[] args) {
        SetProcessDPIAware();
        if (args.Contains("--self-test")) return Tests.Run();
        if (args.Contains("--overlay-test")) return OverlayTests.Run();
        if (args.Length==3 && args[0]=="--setup-test") return SetupTests.Run(args[1],args[2]);
        if (args.Length==2 && args[0]=="--memory-test") return MemoryTests.Run(args[1]);
        if (args.Length==2 && args[0]=="--database-test") return DatabaseTests.Run(args[1]);
        if (args.Length==2 && args[0]=="--reward-test") return RewardTests.Run(args[1]);
        if (args.Contains("--live-once")) {
            try { string directory=ClientSettings.Load(ClientSettings.DefaultFile);if(directory==null)throw new InvalidOperationException("Zuerst die App öffnen und den WoW-Ordner auswählen.");using(var reader=new LiveReader(directory)) { var s=reader.Scan(); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-check.json"),new JavaScriptSerializer().Serialize(s)); } return 0; }
            catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-check.json"),new JavaScriptSerializer().Serialize(new {error=ex.Message}));return 1; }
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using(var form = new ScannerForm(!args.Contains("--smoke-test"))) {
            if (args.Contains("--smoke-test")) { var t = new Timer { Interval=1500 }; t.Tick += (s,e) => { t.Stop();using(var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app-preview.png")); } form.SaveDatabasePreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"database-preview.png"));form.Close(); }; t.Start(); Application.Run(form); t.Dispose(); }
            else Application.Run(form);
        }
        return 0;
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
