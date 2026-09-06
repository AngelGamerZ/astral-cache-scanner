using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// Isolated UI acceptance runner: never clicks controls, connects to WoW or imports user data.
public static class UiAcceptanceTests {
    sealed class PreviewForm : ScannerForm {
        public PreviewForm(string settings,float scale):base(false,settings,scale) {}
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get {var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}
        }
    }
    static IEnumerable<Control> Descendants(Control parent) {
        foreach(Control c in parent.Controls) {yield return c;foreach(var child in Descendants(c))yield return child;}
    }
    static string Label(Control c) {
        string value=String.IsNullOrWhiteSpace(c.Name)?c.Text:c.Name;
        if(value!=null && value.Length>65)value=value.Substring(0,65);
        return c.GetType().Name+" '"+value+"'";
    }
    static void Check(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    static T Field<T>(ScannerForm form,string name) {return (T)typeof(ScannerForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
    static void Call(ScannerForm form,string name) {typeof(ScannerForm).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);}
    static void Capture(ScannerForm form,string path) {Application.DoEvents();using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height));image.Save(path);}}
    static void Populated(string run,float scale) {
        string directory=Path.Combine(run,"populated-"+(int)(scale*100));Directory.CreateDirectory(directory);
        using(var form=new PreviewForm(Path.Combine(directory,"settings.json"),scale)) {
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);
            form.MinimumSize=Size.Empty;form.Size=new Size((int)(1180*scale),(int)(800*scale));form.Show();Application.DoEvents();
            var memory=Field<CacheMemory>(form,"cacheMemory");var now=DateTimeOffset.UtcNow;
            const string scope="UI-Testserver|0000000000000001|map:1";
            var one=new ObjectData {id="F11053EC60000001",name="Astral Cache",entry=5500000,space=scope,units="yards",x=1250.5,y=-4420.5,z=23.5,region="Testregion Nord",subregion="Testklippe"};
            var two=new ObjectData {id="F11053EC60000002",name="Astral Cache",entry=5500000,space=scope,units="yards",x=2250.5,y=-3320.5,z=43.5,region="Testregion Süd",subregion="Testtal"};
            var snapshot=new Snapshot {live=true,context=scope,mapId=1,objects=new[]{one,two}};
            memory.Update(snapshot,now);memory.Mark(CacheMemory.Key(snapshot,two),true,"Isolated UI fixture",now);memory.LostLiveData();
            form.ShowSection("finds");Call(form,"RenderSaved");
            var grid=Field<DataGridView>(form,"savedFinds");var mark=Field<Button>(form,"markLooted");var reopen=Field<Button>(form,"markOpen");
            Check(grid.Rows.Count==1,"Open-only filter must hide completed fixture");
            Check(grid.CurrentRow==null && !mark.Enabled && !reopen.Enabled,"No selection must disable both completion actions");
            grid.CurrentCell=grid.Rows[0].Cells[0];grid.Rows[0].Selected=true;Call(form,"UpdateFindSelection");
            Check(mark.Enabled && !reopen.Enabled,"Open fixture must enable only mark-looted action");
            string key=((SavedCache)grid.CurrentRow.Tag).Key;Call(form,"RenderSaved");
            Check(grid.CurrentRow!=null && ((SavedCache)grid.CurrentRow.Tag).Key==key,"Refresh must preserve selected saved GUID");
            Check(Convert.ToString(grid.CurrentRow.Cells[1].Value).Contains("Testregion Nord") && Convert.ToString(grid.CurrentRow.Cells[1].Value).Contains("Testklippe"),"Region and subregion must be visible in selected row");
            Check(Convert.ToString(grid.CurrentRow.Cells[3].Value)==now.ToLocalTime().ToString("dd.MM. HH:mm:ss"),"Last-seen date must use local display time");
            string detail=Field<Label>(form,"findDetail").Text;
            Check(detail.Contains(one.x.Value.ToString("F1")) && detail.Contains(one.y.Value.ToString("F1")) && detail.Contains(one.z.Value.ToString("F1")),"Saved detail must show XYZ world coordinates");
            Field<CheckBox>(form,"showLooted").Checked=true;Check(grid.Rows.Count==2,"Completed filter must reveal both fixtures");
            var done=grid.Rows.Cast<DataGridViewRow>().Single(r=>((SavedCache)r.Tag).looted);grid.CurrentCell=done.Cells[0];done.Selected=true;Call(form,"UpdateFindSelection");
            Check(!mark.Enabled && reopen.Enabled,"Completed fixture must enable only reopen action");
            Capture(form,Path.Combine(directory,"finds-populated.png"));
            grid.ClearSelection();grid.CurrentCell=null;Call(form,"UpdateFindSelection");Check(!mark.Enabled && !reopen.Enabled,"Clearing selection must disable actions again");
            form.ShowSection("database");var database=Field<DataGridView>(form,"databaseRows");Check(database.Rows.Count==2,"Historical database includes open and completed fixtures");
            var search=Field<TextBox>(form,"databaseSearch");search.Text="Testregion Nord";Check(database.Rows.Count==1,"Region search must filter populated database");
            database.CurrentCell=database.Rows[0].Cells[0];database.Rows[0].Selected=true;Call(form,"UpdateDatabaseSelection");
            detail=Field<Label>(form,"databaseDetail").Text;Check(detail.Contains(one.x.Value.ToString("F1")) && detail.Contains(one.y.Value.ToString("F1")) && detail.Contains(one.z.Value.ToString("F1")),"Database detail must show XYZ world coordinates");
            Capture(form,Path.Combine(directory,"database-populated.png"));
            form.ShowSection("debug");var follow=Field<CheckBox>(form,"debugFollow");var journal=Field<DebugJournal>(form,"debugJournal");var debug=Field<TextBox>(form,"debugText");
            follow.Checked=true;journal.Add("UI fixture first entry");Call(form,"RefreshDebugView");Check(debug.Text.Contains("UI fixture first entry"),"Follow mode must display new debug entries");
            follow.Checked=false;string frozen=debug.Text;journal.Add("UI fixture paused entry");Call(form,"RefreshDebugView");Check(debug.Text==frozen,"Paused debug view must remain stable while entries accumulate");
            follow.Checked=true;Check(debug.Text.Contains("UI fixture paused entry"),"Resuming follow must include entries accumulated while paused");
            Capture(form,Path.Combine(directory,"debug-populated.png"));
            form.ShowSection("settings");var settings=Descendants(form).OfType<Panel>().Single(c=>c.Name=="settingsPage");var advanced=Field<Button>(form,"selectJson");settings.ScrollControlIntoView(advanced);Application.DoEvents();
            Rectangle advancedBounds=settings.RectangleToClient(advanced.RectangleToScreen(advanced.ClientRectangle));Check(settings.ClientRectangle.IntersectsWith(advancedBounds),"Lower settings controls reachable by scrolling");Capture(form,Path.Combine(directory,"settings-scrolled.png"));
            Check(!form.IsScanning,"Populated UI fixtures must not start a scan");form.Close();
        }
    }
    static bool DataControl(Control c) {
        var text=c as TextBox;
        return c is ListBox || c is ListView || c is DataGridView || c is RichTextBox || (text!=null && text.Multiline);
    }
    static bool InsideViewport(Control c,Form form) {
        // Content below an explicitly scrollable viewport is intentionally reachable by scrolling.
        for(Control parent=c.Parent;parent!=null && parent!=form;parent=parent.Parent) {
            var scroll=parent as ScrollableControl;
            if(scroll!=null && scroll.AutoScroll)return true;
            Rectangle mapped=parent.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
            Rectangle viewport=parent.ClientRectangle;viewport.Inflate(2,2);
            if(!viewport.Contains(mapped))return false;
        }
        Rectangle inForm=form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
        Rectangle bounds=form.ClientRectangle;bounds.Inflate(2,2);return bounds.Contains(inForm);
    }
    public static int Run(string testRoot) {
        string run=Path.Combine(Path.GetFullPath(testRoot),"ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        string reportPath=Path.Combine(run,"ui-acceptance-results.txt");
        var report=new StringBuilder("Astral Scanner UI acceptance — isolated settings, no scan, no game input.\r\n");
        int failures=0;
        Application.EnableVisualStyles();
        foreach(float scale in new[]{1f,1.75f})foreach(Size logical in new[]{new Size(1000,700),new Size(1180,800)}) {
            string prefix=(int)(scale*100)+"pct-"+logical.Width+"x"+logical.Height;
            try {
                using(var form=new PreviewForm(Path.Combine(run,prefix+"-settings.json"),scale)) {
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;
                    form.Location=new Point(-20000,-20000);
                    form.MinimumSize=Size.Empty;form.MaximumSize=Size.Empty;
                    form.Size=new Size((int)(logical.Width*scale),(int)(logical.Height*scale));
                    form.Show();form.LoadClient();Application.DoEvents();
                    Check(!form.CanStartScan && !form.IsScanning,"Empty setup must keep scan locked");
                    report.AppendLine(prefix+": requested "+(int)(logical.Width*scale)+"x"+(int)(logical.Height*scale)+", actual "+form.Width+"x"+form.Height);
                    foreach(string section in new[]{"overview","finds","database","debug","settings"}) {
                        string scenario=prefix+"-"+section;
                        try {
                            form.ShowSection(section);form.PerformLayout();Application.DoEvents();
                            Check(!form.IsScanning,"Section change must never start a scan");
                            using(var bitmap=new Bitmap(form.Width,form.Height)) {
                                form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));
                                bitmap.Save(Path.Combine(run,scenario+".png"));
                            }
                            var visible=Descendants(form).Where(c=>c.Visible).ToArray();
                            foreach(var c in visible.Where(c=>c is Button || c is ComboBox || c is CheckBox)) {
                                Check(c.Width>4 && c.Height>4,scenario+": collapsed "+Label(c));
                                Check(InsideViewport(c,form),scenario+": clipped action "+Label(c));
                            }
                            var data=visible.Where(DataControl).ToArray();
                            if(section=="finds" || section=="database" || section=="debug") {
                                Check(data.Length>0,scenario+": missing visible data area");
                                Check(data.Any(c=>c.Width>=250*scale && c.Height>=100*scale),scenario+": data area below 250x100 logical pixels");
                                foreach(var c in data)Check(InsideViewport(c,form),scenario+": clipped "+Label(c));
                            }
                            report.AppendLine("PASS "+scenario+" — actions in bounds, data usable, no auto-scan; screenshot saved.");
                        }catch(Exception ex){failures++;report.AppendLine("FAIL "+scenario+": "+ex.Message);}
                    }
                    Check(!File.Exists(Path.Combine(run,prefix+"-settings.json")),"UI preview must not silently persist a client path");
                    form.Close();
                }
            }catch(Exception ex){failures++;report.AppendLine("FAIL "+prefix+": "+ex);}
        }
        foreach(float scale in new[]{1f,1.75f})try {Populated(run,scale);report.AppendLine("PASS populated "+(int)(scale*100)+"% — selection preservation, action gating, region/XYZ/date, database filter, debug pause/resume.");}catch(Exception ex){failures++;report.AppendLine("FAIL populated "+scale+": "+ex);}
        report.AppendLine("Failures: "+failures+". Screenshots require visual review for typography, contrast and empty states.");
        File.WriteAllText(reportPath,report.ToString(),Encoding.UTF8);
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),report.ToString()+"\r\nArtifacts: "+run,Encoding.UTF8);
        return failures==0?0:1;
    }
}
