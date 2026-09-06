using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public sealed class UiActionButton : Button {
    protected override void OnPaint(PaintEventArgs e) {
        if(Enabled){base.OnPaint(e);return;}
        e.Graphics.Clear(Color.FromArgb(23,32,44));using(var pen=new Pen(Color.FromArgb(44,61,77)))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Color.FromArgb(117,140,159),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
}
public partial class ScannerForm {
    static readonly Color Canvas=Color.FromArgb(14,20,29),Surface=Color.FromArgb(23,32,44),Raised=Color.FromArgb(31,43,57),Ink=Color.FromArgb(231,239,246),Muted=Color.FromArgb(153,174,191),Mint=Color.FromArgb(146,227,197),Line=Color.FromArgb(44,61,77);
    readonly Dictionary<string,Panel> sections=new Dictionary<string,Panel>();
    readonly Dictionary<string,Button> navigation=new Dictionary<string,Button>();
    Panel pageHost;
    Label pageTitle,liveCount,openCount,locationCount,overviewHint,findDetail,databaseDetail,footer;
    TextBox databaseSearch;
    Button stopScan,setupButton,markLooted,markOpen;
    bool rebuildingRows;
    CheckBox debugFollow=new CheckBox {Text="Live folgen",Checked=true,AutoSize=true};
    void RefreshDebugView(){if(!debugFollow.Checked)return;debugText.Text=debugJournal.ToString();debugText.SelectionStart=debugText.TextLength;debugText.ScrollToCaret();}
    int Px(float n){return (int)Math.Round(n*uiScale);}
    Font UiFont(float size,FontStyle style=FontStyle.Regular){return new Font("Segoe UI",size*uiScale,style,GraphicsUnit.Pixel);}
    Label Caption(string text,float size=13,Color? color=null,bool bold=false) {return new Label {Text=text,UseMnemonic=false,AutoSize=true,ForeColor=color??Ink,Font=UiFont(size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,0,0,Px(8))};}
    TableLayoutPanel Stack() {var p=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,Margin=Padding.Empty,Padding=Padding.Empty};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return p;}
    void Row(TableLayoutPanel panel,Control child,float height=0) {panel.RowStyles.Add(height>0?new RowStyle(SizeType.Absolute,Px(height)):new RowStyle(SizeType.Percent,100));panel.Controls.Add(child,0,panel.RowCount++);}
    FlowLayoutPanel Actions(params Control[] controls) {var f=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=Padding.Empty,WrapContents=true,AutoScroll=false};foreach(var c in controls)f.Controls.Add(c);return f;}
    Button ActionButton(string text,Action click,bool primary=false) {
        var b=Button(text,click);b.Name=text;b.Font=UiFont(13,FontStyle.Bold);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=primary?Mint:Line;b.BackColor=primary?Mint:Raised;b.ForeColor=primary?Canvas:Ink;b.Padding=new Padding(Px(13),Px(6),Px(13),Px(6));b.Margin=new Padding(0,0,Px(8),Px(8));b.MinimumSize=new Size(0,Px(36));b.AutoSizeMode=AutoSizeMode.GrowAndShrink;b.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(176,242,216):Color.FromArgb(45,62,80);return b;
    }
    void ThemeText(TextBox text,bool mono=false) {text.BackColor=Surface;text.ForeColor=Ink;text.BorderStyle=BorderStyle.FixedSingle;text.Font=mono?new Font("Consolas",12*uiScale,FontStyle.Regular,GraphicsUnit.Pixel):UiFont(13);text.Margin=new Padding(0,Px(4),0,Px(10));}
    void ThemeCheck(CheckBox c) {c.ForeColor=Ink;c.Font=UiFont(13);c.Margin=new Padding(0,Px(8),Px(20),Px(8));c.BackColor=Color.Transparent;}
    void ConfigureGrid(DataGridView grid,params string[] headings) {
        grid.Dock=DockStyle.Fill;grid.Margin=Padding.Empty;grid.BackgroundColor=Surface;grid.BorderStyle=BorderStyle.None;grid.GridColor=Line;grid.EnableHeadersVisualStyles=false;grid.RowHeadersVisible=false;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.ColumnHeadersHeight=Px(40);grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.RowTemplate.Height=Px(42);grid.Font=UiFont(13);
        grid.DefaultCellStyle=new DataGridViewCellStyle {BackColor=Surface,ForeColor=Ink,SelectionBackColor=Color.FromArgb(42,73,76),SelectionForeColor=Color.White,Padding=new Padding(Px(10),0,Px(8),0)};
        grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(25,35,48);grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle {BackColor=Raised,ForeColor=Muted,Font=UiFont(12,FontStyle.Bold),Padding=new Padding(Px(10),0,0,0)};
        grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
        foreach(var heading in headings){int i=grid.Columns.Add("c"+grid.Columns.Count,heading);grid.Columns[i].MinimumWidth=Px(65);grid.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;}
    }
    Panel Section(string key,string title,string description) {
        var p=new Panel {Name=key+"Page",Dock=DockStyle.Fill,Padding=new Padding(Px(24)),BackColor=Canvas,Visible=false};sections[key]=p;pageHost.Controls.Add(p);return p;
    }
    TableLayoutPanel PageContent(Panel panel,string title,string description) {
        var stack=Stack();stack.RowCount=0;Row(stack,Caption(title,24,Ink,true),40);Row(stack,Caption(description,13,Muted),38);panel.Controls.Add(stack);return stack;
    }
    internal void ShowSection(string key) {
        if(!sections.ContainsKey(key))return;foreach(var p in sections)p.Value.Visible=p.Key==key;sections[key].BringToFront();
        foreach(var n in navigation){n.Value.BackColor=n.Key==key?Raised:Canvas;n.Value.ForeColor=n.Key==key?Mint:Muted;n.Value.FlatAppearance.BorderColor=n.Key==key?Line:Canvas;}
        if(key=="database"){SyncLocations(true);if(databaseError==null)RenderLocations();}
    }
    internal IEnumerable<Control> AllUiControls(){return Descendants(this);}
    static IEnumerable<Control> Descendants(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var child in Descendants(c))yield return child;}}
    void BuildUi() {
        BackColor=Canvas;ForeColor=Ink;Font=UiFont(13);MinimumSize=new Size(Px(1000),Px(700));ClientSize=new Size(Px(1180),Px(800));Text="Astral Scanner · "+ReleaseInfo.Version;StartPosition=FormStartPosition.CenterScreen;
        var viewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true,AutoScrollMinSize=new Size(Px(960),Px(620))};Controls.Add(viewport);
        var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Px(174)));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));viewport.Controls.Add(shell);
        var sidebar=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(Px(16),Px(24),Px(12),Px(20)),BackColor=Canvas};sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(100)));sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(62)));shell.Controls.Add(sidebar);
        var brand=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown};brand.Controls.Add(Caption("A S T R A L",19,Mint,true));brand.Controls.Add(Caption("CACHE SCANNER",10,Muted));sidebar.Controls.Add(brand);
        var menu=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        foreach(var item in new[]{new[]{"overview","Übersicht"},new[]{"finds","Meine Kisten"},new[]{"database","Fundorte"},new[]{"debug","Diagnose"},new[]{"settings","Einstellungen"}}) {string key=item[0];var button=ActionButton(item[1],()=>ShowSection(key));button.Name="nav-"+key;button.AutoSize=false;button.Size=new Size(Px(132),Px(44));button.TextAlign=ContentAlignment.MiddleLeft;button.Margin=new Padding(0,0,0,Px(7));button.FlatAppearance.BorderSize=1;navigation[key]=button;menu.Controls.Add(button);}sidebar.Controls.Add(menu);
        sidebar.Controls.Add(Caption("VERSION "+ReleaseInfo.Version+"\nNur lesender Zugriff",11,Muted));
        var main=Stack();main.RowCount=0;shell.Controls.Add(main);var masthead=new Panel {Dock=DockStyle.Fill,Padding=new Padding(Px(24),Px(20),Px(24),0)};pageTitle=Caption("Kisten finden. Fundorte behalten.",19,Ink,true);pageTitle.Location=new Point(Px(24),Px(16));masthead.Controls.Add(pageTitle);Row(main,masthead,62);
        var statusPanel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Surface,Padding=new Padding(Px(20),Px(16),Px(16),Px(12)),Margin=new Padding(Px(24),0,Px(24),0)};statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Px(250)));
        var statusText=Stack();statusText.RowCount=0;state.Font=UiFont(19,FontStyle.Bold);state.ForeColor=Mint;state.Margin=Padding.Empty;state.AutoSize=false;state.Dock=DockStyle.Fill;details.Font=UiFont(12);details.ForeColor=Muted;details.AutoSize=false;details.Dock=DockStyle.Fill;details.MaximumSize=Size.Empty;Row(statusText,state,32);Row(statusText,details);statusPanel.Controls.Add(statusText);
        startScan=ActionButton("Scan starten",ConnectLive,true);startScan.Name="startScan";startScan.Enabled=false;setupButton=ActionButton("Spielordner wählen",ChooseClient,true);setupButton.Name="setupButton";stopScan=ActionButton("Stoppen",Disconnect);stopScan.Name="stopScan";stopScan.Enabled=false;statusPanel.Controls.Add(Actions(setupButton,startScan,stopScan));Row(main,statusPanel,112);
        pageHost=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};Row(main,pageHost);footer=Caption("Bereit, sobald dein Spielordner bestätigt ist.",11,Muted);footer.Dock=DockStyle.Fill;footer.Margin=new Padding(Px(24),0,0,0);Row(main,footer,26);

        var overview=PageContent(Section("overview","",""),"Deine Umgebung","Aktuelle Kisten und der letzte bekannte Weg zurück.");
        var metrics=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};for(int i=0;i<3;i++)metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        liveCount=Metric(metrics,"JETZT SICHTBAR");openCount=Metric(metrics,"OFFENE KISTEN");locationCount=Metric(metrics,"FUNDORTE GESAMT");Row(overview,metrics,86);
        overviewHint=Caption("Starte den Scan, um Kisten in deiner Umgebung zu erkennen.",13,Muted);overviewHint.Dock=DockStyle.Fill;Row(overview,overviewHint,44);
        ConfigureGrid(results,"Objekt","Sichtungsregion","Entfernung","Zustand");results.Name="liveGrid";results.Columns[0].FillWeight=125;Row(overview,results);

        var finds=PageContent(Section("finds","",""),"Meine Kisten","Deine Funde bleiben auch außerhalb der Sichtweite gespeichert.");
        markLooted=ActionButton("Als gelootet markieren",()=>MarkSaved(true));markOpen=ActionButton("Als offen markieren",()=>MarkSaved(false));ThemeCheck(showLooted);showLooted.CheckedChanged+=(s,e)=>RenderSaved();Row(finds,Actions(markLooted,markOpen,showLooted),48);memoryStatus.Font=UiFont(12);memoryStatus.ForeColor=Muted;memoryStatus.Dock=DockStyle.Fill;Row(finds,memoryStatus,40);
        ConfigureGrid(savedFinds,"Zustand","Sichtungsregion","Karte","Zuletzt gesehen");savedFinds.Name="savedGrid";savedFinds.SelectionChanged+=(s,e)=>UpdateFindSelection();Row(finds,savedFinds);findDetail=Caption("Wähle eine Kiste, um ihren Fundort zu sehen.",12,Muted);findDetail.Dock=DockStyle.Fill;Row(finds,findDetail,72);

        var database=PageContent(Section("database","",""),"Fundort-Datenbank","Alle bekannten Fundstellen – einschließlich gelooteter Kisten.");
        Row(database,Actions(ActionButton("Importieren",ImportLocations),ActionButton("JSON exportieren",()=>ExportLocations(false)),ActionButton("Für Addon exportieren",()=>ExportLocations(true))),48);
        databaseSearch=new TextBox {Dock=DockStyle.Fill,Name="databaseSearch",AccessibleName="Fundorte nach Region, Karte oder Server durchsuchen"};ThemeText(databaseSearch);var filter=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Px(64)));filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));filter.Controls.Add(Caption("Suchen",12,Muted));filter.Controls.Add(databaseSearch);databaseSearch.TextChanged+=(s,e)=>RenderLocations();Row(database,filter,40);
        databaseStatus.Font=UiFont(12);databaseStatus.ForeColor=Muted;databaseStatus.Dock=DockStyle.Fill;Row(database,databaseStatus,34);ConfigureGrid(databaseRows,"Sichtungsregion","Karte","Server","Zuletzt gesehen");databaseRows.Name="databaseGrid";databaseRows.SelectionChanged+=(s,e)=>UpdateDatabaseSelection();Row(database,databaseRows);databaseDetail=Caption("Wähle einen Fundort für die vollständigen Koordinaten.",12,Muted);databaseDetail.Dock=DockStyle.Fill;Row(database,databaseDetail,72);

        var debug=PageContent(Section("debug","",""),"Diagnose & Debug-Log","Bei Problemen: Scan laufen lassen, Problem nachstellen und Protokoll exportieren.");
        Row(debug,Actions(ActionButton("Debug-Log exportieren",ExportDebug,true),ActionButton("Protokollordner öffnen",()=>{Directory.CreateDirectory(logDir);Process.Start("explorer.exe",logDir);}),ActionButton("Diagnoseaufnahme speichern",SaveSnapshot)),50);
        process.Font=UiFont(12);process.ForeColor=Muted;process.Dock=DockStyle.Fill;process.AutoSize=false;Row(debug,process,72);ThemeCheck(debugFollow);debugFollow.CheckedChanged+=(s,e)=>RefreshDebugView();Row(debug,debugFollow,34);ThemeText(debugText,true);Row(debug,debugText);

        var settings=Section("settings","","");settings.AutoScroll=true;var settingsFlow=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};settings.Controls.Add(settingsFlow);settingsFlow.Controls.Add(Caption("Einstellungen & Updates",24,Ink,true));settingsFlow.Controls.Add(Caption("Anzeige anpassen und deine Installation verwalten.",13,Muted));
        var pathGroup=SettingsGroup(settingsFlow,"Spielordner");ThemeText(client);client.Dock=DockStyle.None;client.Name="clientPath";client.Width=Px(650);pathGroup.Controls.Add(client);pathGroup.Controls.Add(ActionButton("WoW-Ordner wählen",ChooseClient));
        var displayGroup=SettingsGroup(settingsFlow,"Anzeige & Ton");ThemeCheck(overlayEnabled);ThemeCheck(sound);overlayEnabled.CheckedChanged+=(s,e)=>overlay.SetEnabled(overlayEnabled.Checked);displayGroup.Controls.Add(overlayEnabled);displayGroup.Controls.Add(sound);var top=new CheckBox {Text="Scannerfenster im Vordergrund",AutoSize=true};ThemeCheck(top);top.CheckedChanged+=(s,e)=>TopMost=top.Checked;displayGroup.Controls.Add(top);
        displayGroup.Controls.Add(Caption("Overlayposition",12,Muted));var anchor=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=Px(210),Font=UiFont(13),BackColor=Raised,ForeColor=Ink,Name="overlayPosition"};anchor.Items.AddRange(new object[]{"Oben Mitte","Oben links","Oben rechts","Unten Mitte"});anchor.SelectedIndex=0;anchor.SelectedIndexChanged+=(s,e)=>overlay.OverlayAnchor=anchor.SelectedItem.ToString();displayGroup.Controls.Add(anchor);
        var updates=SettingsGroup(settingsFlow,"Updates · Version "+ReleaseInfo.Version);updateStatus.Font=UiFont(13);updateStatus.ForeColor=Muted;updates.Controls.Add(updateStatus);updates.Controls.Add(ActionButton("Auf Updates prüfen",CheckUpdates));updates.Controls.Add(ActionButton("Downloads öffnen",()=>Process.Start(ReleaseInfo.ReleasesUrl)));
        var advanced=SettingsGroup(settingsFlow,"Erweiterte Diagnose");ThemeCheck(allObjects);allObjects.Checked=false;allObjects.Text="Alle Spielobjekte in der Übersicht anzeigen";allObjects.CheckedChanged+=(s,e)=>{if(lastSnapshot!=null)RenderObjects(lastSnapshot);};advanced.Controls.Add(allObjects);ThemeText(source);source.Width=Px(650);source.Dock=DockStyle.None;advanced.Controls.Add(source);selectJson=ActionButton("JSON-Diagnosequelle öffnen",SelectSource);selectJson.Enabled=false;advanced.Controls.Add(selectJson);
        Action resizeSettings=()=>{int width=Math.Max(Px(500),settings.ClientSize.Width-settings.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-Px(8));foreach(Control group in settingsFlow.Controls){group.MaximumSize=new Size(width,0);if(group is FlowLayoutPanel)group.MinimumSize=new Size(width,0);}client.Width=source.Width=width-Px(32);};
        settings.SizeChanged+=(s,e)=>resizeSettings();settings.VisibleChanged+=(s,e)=>{if(settings.Visible)resizeSettings();};
        UpdateFindSelection();ShowSection("overview");
    }
    Label Metric(TableLayoutPanel parent,string title){var panel=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Surface,Padding=new Padding(Px(14),Px(9),Px(10),0),Margin=new Padding(0,0,Px(10),Px(10))};panel.Controls.Add(Caption(title,10,Muted,true));var value=Caption("0",25,Mint,true);value.Margin=Padding.Empty;panel.Controls.Add(value);parent.Controls.Add(panel);return value;}
    FlowLayoutPanel SettingsGroup(FlowLayoutPanel parent,string title){var panel=new FlowLayoutPanel {FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Padding=new Padding(Px(16)),BackColor=Surface,Margin=new Padding(0,Px(14),0,0),MinimumSize=new Size(Px(500),0)};panel.Controls.Add(Caption(title,15,Ink,true));parent.Controls.Add(panel);return panel;}
    void SaveSnapshot(){if(lastSnapshot==null)throw new InvalidOperationException("Noch kein aktueller Scan vorhanden.");Directory.CreateDirectory(logDir);string path=Path.Combine(logDir,"snapshot-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");File.WriteAllText(path,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(lastSnapshot));Write("Diagnoseaufnahme gespeichert: "+path);}
    static string MapName(int? id){return id==0?"Östliche Königreiche":id==1?"Kalimdor":id==530?"Scherbenwelt":id==571?"Nordend":"Karte "+id;}
    static string RegionName(string region,string sub){return String.IsNullOrWhiteSpace(region)?"Nicht erfasst":region+(String.IsNullOrWhiteSpace(sub)?"":" · "+sub);}
    void UpdateFindSelection(){if(rebuildingRows)return;var row=savedFinds.CurrentRow==null?null:savedFinds.CurrentRow.Tag as SavedCache;markLooted.Enabled=row!=null&&!row.looted;markOpen.Enabled=row!=null&&row.looted;if(findDetail!=null)findDetail.Text=row==null?"Wähle eine Kiste, um ihren Fundort zu sehen.":String.Format("Weltposition: X {0:F1}   Y {1:F1}   Z {2:F1}\n{3}",row.location.x,row.location.y,row.location.z,row.looted?"Als gelootet markiert. Der Fundort bleibt in der Datenbank.":"Gespeichert: letzter bekannter Fundort. Außer Sicht ist die Kiste nicht bestätigt.");}
    void UpdateDatabaseSelection(){if(rebuildingRows || databaseDetail==null)return;var row=databaseRows.CurrentRow==null?null:databaseRows.CurrentRow.Tag as CacheLocation;databaseDetail.Text=row==null?"Wähle einen Fundort für die vollständigen Koordinaten.":String.Format("Weltposition: X {0:F1}   Y {1:F1}   Z {2}   ·   Objekt-ID {3}\nHistorische Fundstelle. Die Sichtungsregion beschreibt deinen Standort beim Erkennen.",row.x,row.y,row.z.HasValue?row.z.Value.ToString("F1"):"?",row.entry);}
    void RefreshSummary(){if(liveCount==null)return;liveCount.Text=lastSnapshot!=null&&lastSnapshot.live?lastSnapshot.objects.Count(o=>Reader.IsCache(o.name)&& (cacheMemory==null||!cacheMemory.Completed(lastSnapshot,o))).ToString():"0";openCount.Text=cacheMemory==null?"—":cacheMemory.Records.Count(r=>!r.looted).ToString();locationCount.Text=locationDatabase==null?"—":locationDatabase.Records.Count().ToString();stopScan.Enabled=liveReader!=null||!String.IsNullOrWhiteSpace(source.Text);if(footer!=null)footer.Text=liveReader!=null?(cacheMemory==null||cacheMemory.SaveError!=null?"Achtung: Fundspeicher nicht verfügbar":lastSnapshot==null||lastSnapshot.context==null?"Scan aktiv · Kartenzuordnung wird geprüft":"Scan aktiv · Fundorte werden automatisch gespeichert"):"Scan gestoppt · Fundorte bleiben gespeichert";if(overviewHint!=null)overviewHint.Text=liveReader==null?"Starte den Scan, um Kisten in deiner Umgebung zu erkennen.":results.Rows.Count==0?"Gerade keine Kiste sichtbar. Gespeicherte Funde bleiben unter „Meine Kisten“.":"Sichtbare Objekte aus dem aktuellen Scan. Der Pfeil richtet sich nach deiner Figur.";}
}

