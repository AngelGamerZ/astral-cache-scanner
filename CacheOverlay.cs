using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.IO;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class CacheDirection {
    public ObjectData Target;
    public int Count;
    public double? Distance, Horizontal, Height, RelativeAngle;
    public bool AtPoint;
    public string Instruction;
}

public static class Navigation {
    static bool Finite(double? v) { return v.HasValue && !Double.IsNaN(v.Value) && !Double.IsInfinity(v.Value) && Math.Abs(v.Value)<100000; }
    static bool Comparable(PointData p,PointData o) {
        return p!=null && o!=null && !String.IsNullOrWhiteSpace(p.space) && p.space==o.space && p.units==o.units && p.units=="yards" && Finite(p.x) && Finite(p.y) && Finite(o.x) && Finite(o.y);
    }
    public static double Normalize(double angle) { return Math.Atan2(Math.Sin(angle),Math.Cos(angle)); }
    public static double Distance(PointData p,PointData o) {
        if(!Comparable(p,o))return Double.PositiveInfinity;
        double dx=o.x.Value-p.x.Value,dy=o.y.Value-p.y.Value,dz=Finite(p.z)&&Finite(o.z)?o.z.Value-p.z.Value:0;
        return Math.Sqrt(dx*dx+dy*dy+dz*dz);
    }
    public static CacheDirection Select(Snapshot s) {
        if(s==null || !s.live || s.demo || s.objects==null)return null;
        var matches=s.objects.Where(o=>o!=null && Reader.IsCache(o.name)).ToArray();
        if(matches.Length==0)return null;
        var target=matches.OrderBy(o=>Distance(s.player,o)).ThenBy(o=>o.id,StringComparer.Ordinal).First();
        var result=new CacheDirection {Target=target,Count=matches.Length,Instruction="Richtung nicht verfügbar"};
        if(s.onTransport) { result.Instruction="Auf Transport: Richtung ausgesetzt";return result; }
        if(!Comparable(s.player,target))return result;
        double dx=target.x.Value-s.player.x.Value,dy=target.y.Value-s.player.y.Value;
        result.Horizontal=Math.Sqrt(dx*dx+dy*dy);result.Distance=Distance(s.player,target);
        if(Finite(s.player.z)&&Finite(target.z))result.Height=target.z.Value-s.player.z.Value;
        if(result.Horizontal.Value<2) {
            result.AtPoint=true;
            result.Instruction=result.Height.HasValue && Math.Abs(result.Height.Value)>3 ? (result.Height.Value>0 ? "Über dir" : "Unter dir") : "Direkt bei dir";
            if(target.remembered)result.Instruction="Letzter Fundort erreicht";
            return result;
        }
        if(!Finite(s.facing))return result;
        result.RelativeAngle=Normalize(Math.Atan2(dy,dx)-s.facing.Value);
        string[] sectors={"Vorwärts","Vorne links","Links","Hinten links","Umdrehen","Hinten rechts","Rechts","Vorne rechts"};
        double positive=(result.RelativeAngle.Value+2*Math.PI)%(2*Math.PI);
        int sector=(int)Math.Floor(positive/(Math.PI/4)+0.5)%8;
        result.Instruction=sectors[sector];return result;
    }
}

public sealed class OverlayPlacement {
    public int schema {get;set;}
    public double x {get;set;}
    public double y {get;set;}
    static double Clamp(double value) {return Math.Max(0,Math.Min(1,value));}
    public static OverlayPlacement FromPoint(Point point,Size client,Size card) {
        return new OverlayPlacement {schema=1,x=client.Width>card.Width?Clamp((double)point.X/(client.Width-card.Width)):0,y=client.Height>card.Height?Clamp((double)point.Y/(client.Height-card.Height)):0};
    }
    public Point Resolve(Size client,Size card) {return new Point((int)Math.Round(Clamp(x)*Math.Max(0,client.Width-card.Width)),(int)Math.Round(Clamp(y)*Math.Max(0,client.Height-card.Height)));}
    public static OverlayPlacement Load(string path) {
        if(!File.Exists(path))return null;
        if(new FileInfo(path).Length>4096)throw new InvalidDataException("Ungültige Overlayposition.");
        var value=new JavaScriptSerializer().Deserialize<OverlayPlacement>(File.ReadAllText(path));
        if(value==null)return null;
        if(value.schema!=1 || Double.IsNaN(value.x)||Double.IsInfinity(value.x)||Double.IsNaN(value.y)||Double.IsInfinity(value.y)||value.x<0||value.x>1||value.y<0||value.y>1)throw new InvalidDataException("Ungültige Overlayposition.");
        return value;
    }
    public static void Save(string path,OverlayPlacement value) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,new JavaScriptSerializer().Serialize(value));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}

public sealed class CacheOverlay : Form {
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X,Y; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref POINT point);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr window,int index,int value);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    const int Transparent=0x20,Layered=0x80000,ToolWindow=0x80,NoActivate=0x8000000;
    const int CardWidth=400,CardHeight=230;
    readonly Timer placementTimer=new Timer { Interval=100 };
    readonly Stopwatch age=Stopwatch.StartNew();
    readonly Font titleFont=new Font("Segoe UI",24,FontStyle.Bold,GraphicsUnit.Pixel);
    readonly Font numberFont=new Font("Segoe UI",32,FontStyle.Bold,GraphicsUnit.Pixel);
    readonly Font bodyFont=new Font("Segoe UI",15,FontStyle.Bold,GraphicsUnit.Pixel);
    readonly Font smallFont=new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly float renderScale;
    CacheDirection direction;
    int gamePid;
    bool active=true;
    string diagnosticState;
    public event Action<string> Diagnostic;
    void Report(string state) {
        if(diagnosticState==state)return;
        diagnosticState=state;
        if(Diagnostic!=null)Diagnostic("Overlay: "+state);
    }
    string overlayAnchor="Oben Mitte";
    readonly string placementFile;
    OverlayPlacement placement;
    bool dragging;
    Point dragOffset;
    Rectangle gameBounds;
    public string OverlayAnchor { get {return overlayAnchor;} set { if(value==overlayAnchor)return;overlayAnchor=value;ResetPosition(); } }
    public void ResetPosition() { placement=null;SavePlacement();PositionOverGame(); }
    public CacheOverlay(string preferencesFile=null) {
        placementFile=preferencesFile??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AstralScanner","overlay-position.json");
        try {placement=OverlayPlacement.Load(placementFile);}catch {placement=null;}
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
        using(var screen=Graphics.FromHwnd(IntPtr.Zero))renderScale=Math.Max(1,screen.DpiX/96f);
        AutoScaleMode=AutoScaleMode.None;ClientSize=new Size((int)(CardWidth*renderScale),(int)(CardHeight*renderScale));BackColor=Color.FromArgb(17,22,33);Opacity=0.94;
        DoubleBuffered=true;Text="Astral Cache Overlay";
        using(var path=Rounded(new RectangleF(0,0,Width,Height),18*renderScale))Region=new Region(path);
        placementTimer.Tick+=(s,e)=>PositionOverGame();placementTimer.Start();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var cp=base.CreateParams;cp.ExStyle|=Transparent|Layered|ToolWindow|NoActivate;return cp; } }
    public bool InputTransparent { get { int flags=GetWindowLong(Handle,-20);return (flags&(Transparent|Layered|ToolWindow|NoActivate))==(Transparent|Layered|ToolWindow|NoActivate); } }
    public CacheDirection CurrentDirection {get {return direction;}}
    void SetInteractive(bool value) {
        int flags=GetWindowLong(Handle,-20),updated=value?flags&~Transparent:flags|Transparent;
        if(flags!=updated)SetWindowLong(Handle,-20,updated);
        Cursor=value?Cursors.SizeAll:Cursors.Default;
    }
    void SavePlacement() {
        try {OverlayPlacement.Save(placementFile,placement);}
        catch(Exception ex) {if(Diagnostic!=null)Diagnostic("Overlayposition konnte nicht gespeichert werden: "+ex.Message);}
    }
    void EndDrag(bool save) {
        if(!dragging)return;
        dragging=false;Capture=false;if(save)SavePlacement();SetInteractive(false);
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left || (GetAsyncKeyState(0x10)&0x8000)==0)return;
        dragging=true;dragOffset=e.Location;Capture=true;
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);if(!dragging)return;
        var desired=new Point(MousePosition.X-dragOffset.X-gameBounds.Left,MousePosition.Y-dragOffset.Y-gameBounds.Top);
        placement=OverlayPlacement.FromPoint(desired,gameBounds.Size,Size);
        Point offset=placement.Resolve(gameBounds.Size,Size);Location=new Point(gameBounds.Left+offset.X,gameBounds.Top+offset.Y);
    }
    protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);if(e.Button==MouseButtons.Left)EndDrag(true);}
    protected override void OnMouseCaptureChanged(EventArgs e) {base.OnMouseCaptureChanged(e);if(dragging&&!Capture)EndDrag(true);}
    protected override void OnVisibleChanged(EventArgs e) {base.OnVisibleChanged(e);if(!Visible){EndDrag(true);if(IsHandleCreated)SetInteractive(false);}}
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x21) {m.Result=new IntPtr(3);return;} // MA_NOACTIVATE: dragging never steals WoW focus.
        base.WndProc(ref m);
    }
    public void SetEnabled(bool value) { active=value;PositionOverGame(); }
    public void SetSnapshot(Snapshot snapshot,int pid) {
        direction=Navigation.Select(snapshot);gamePid=pid;age.Restart();Invalidate();PositionOverGame();
    }
    public void Clear() { direction=null;Hide();Report("ausgeblendet – keine aktuellen Scandaten; keine Loot-Bestätigung."); }
    void PositionOverGame() {
        if(!active || direction==null || gamePid==0 || age.ElapsedMilliseconds>2000) {
            if(Visible)Hide();
            Report(!active?"ausgeblendet – in Einstellungen deaktiviert.":direction==null?"ausgeblendet – kein offenes Navigationsziel; Loot-Abschluss siehe separate Kistenmeldung.":gamePid==0?"ausgeblendet – kein Spielprozess.":"ausgeblendet – Scandaten älter als 2 Sekunden; keine Loot-Bestätigung.");return;
        }
        IntPtr foreground=GetForegroundWindow();uint process;
        GetWindowThreadProcessId(foreground,out process);
        if(process!=(uint)gamePid || IsIconic(foreground)) { if(Visible)Hide();Report("ausgeblendet – WoW nicht im Vordergrund; keine Loot-Bestätigung.");return; }
        RECT rect;var origin=new POINT();
        if(!GetClientRect(foreground,out rect) || !ClientToScreen(foreground,ref origin) || rect.Right-rect.Left<Width || rect.Bottom-rect.Top<Height) { if(Visible)Hide();Report("ausgeblendet – Spielfenster nicht verfügbar oder zu klein; keine Loot-Bestätigung.");return; }
        int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        gameBounds=new Rectangle(origin.X,origin.Y,width,height);
        if(dragging && (GetAsyncKeyState(1)&0x8000)==0)EndDrag(true);
        SetInteractive(dragging || (GetAsyncKeyState(0x10)&0x8000)!=0);
        int margin=(int)(20*renderScale);
        int x=OverlayAnchor=="Oben links"?margin:OverlayAnchor=="Oben rechts"?width-Width-margin:(width-Width)/2;
        int y=OverlayAnchor=="Unten Mitte"?height-Height-2*margin:3*margin;
        x=Math.Max(0,Math.Min(x,width-Width));y=Math.Max(0,Math.Min(y,height-Height));
        if(placement!=null) {Point offset=placement.Resolve(gameBounds.Size,Size);x=offset.X;y=offset.Y;}
        if(!dragging)Location=new Point(origin.X+x,origin.Y+y);
        if(!Visible)Show();
        Report("sichtbar | Objekt "+direction.Target.id+" | "+(direction.Target.remembered?"gespeicherter Fund":"geladene Kiste")+" | "+(direction.AtPoint?"Zielpunkt erreicht: Zielmarkierung ersetzt Pfeil; noch keine Loot-Bestätigung.":direction.RelativeAngle.HasValue?"Richtungspfeil aktiv.":direction.Instruction));
        SetWindowPos(Handle,new IntPtr(-1),Left,Top,Width,Height,0x0010); // SWP_NOACTIVATE
    }
    static GraphicsPath Rounded(RectangleF r,float radius) {
        var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e);DrawCard(e.Graphics,direction,false); }
    void DrawCard(Graphics g,CacheDirection d,bool preview) {
        var original=g.Save();g.ScaleTransform(renderScale,renderScale);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(BackColor);
        using(var border=new Pen(Color.FromArgb(82,102,135),1))using(var outline=Rounded(new RectangleF(0.5f,0.5f,CardWidth-1,CardHeight-1),18))g.DrawPath(border,outline);
        using(var accent=new SolidBrush(Color.FromArgb(248,201,105)))g.FillRectangle(accent,24,0,84,3);
        bool remembered=d!=null && d.Target.remembered;
        using(var gold=new SolidBrush(Color.FromArgb(248,201,105)))g.DrawString(remembered?"Astral Cache Saved":"Astral Cache Found",titleFont,gold,21,17);
        using(var muted=new SolidBrush(Color.FromArgb(166,182,205)))g.DrawString(remembered?"Gespeicherter Fundort · aktuell nicht bestätigt":d!=null && d.Count>1 ? "Nächste Kiste · "+d.Count+" Fundorte" : "Geladenes Spielobjekt",smallFont,muted,23,49);
        var center=new PointF(83,123);
        using(var ring=new Pen(Color.FromArgb(47,64,85),2))g.DrawEllipse(ring,39,79,88,88);
        if(d!=null && d.RelativeAngle.HasValue && !d.AtPoint) {
            var saved=g.Save();g.TranslateTransform(center.X,center.Y);g.RotateTransform((float)(-d.RelativeAngle.Value*180/Math.PI));
            PointF[] arrow={new PointF(0,-34),new PointF(25,5),new PointF(9,1),new PointF(9,29),new PointF(-9,29),new PointF(-9,1),new PointF(-25,5)};
            using(var brush=new SolidBrush(Color.FromArgb(105,231,213)))g.FillPolygon(brush,arrow);g.Restore(saved);
        } else if(d!=null && d.AtPoint) {
            using(var pen=new Pen(Color.FromArgb(105,231,213),5)) { g.DrawEllipse(pen,65,105,36,36);g.DrawLine(pen,83,96,83,104);g.DrawLine(pen,83,142,83,150);g.DrawLine(pen,56,123,64,123);g.DrawLine(pen,102,123,110,123); }
        } else using(var muted=new SolidBrush(Color.FromArgb(166,182,205)))g.DrawString("?",numberFont,muted,69,100);
        using(var white=new SolidBrush(Color.FromArgb(242,247,252))) {
            string distance=d!=null && d.Distance.HasValue ? d.Distance.Value.ToString("F1")+" yd" : "—";
            g.DrawString(distance,numberFont,white,149,80);
            g.DrawString(d==null ? "Kein aktueller Fund" : d.Instruction,bodyFont,white,new RectangleF(151,123,235,45));
        }
        using(var muted=new SolidBrush(Color.FromArgb(166,182,205))) {
            string elevation=d!=null && d.Height.HasValue && Math.Abs(d.Height.Value)>3 ? Math.Abs(d.Height.Value).ToString("F1")+" yd "+(d.Height.Value>0?"höher":"tiefer") : "Luftlinie · keine Wegführung";
            g.DrawString(elevation,smallFont,muted,151,169);
            using(var line=new Pen(Color.FromArgb(47,64,85)))g.DrawLine(line,23,197,377,197);
            g.DrawString(preview?"VORSCHAU · synthetische Testdaten":"Loot: Kiste direkt am Fundort verschwunden",smallFont,muted,23,206);
        }
        g.Restore(original);
    }
    public void SavePreview(CacheDirection data,string path) { using(var bitmap=new Bitmap(Width,Height))using(var g=Graphics.FromImage(bitmap)) { DrawCard(g,data,true);bitmap.Save(path); } }
    protected override void Dispose(bool disposing) {
        if(disposing) { placementTimer.Dispose();titleFont.Dispose();numberFont.Dispose();bodyFont.Dispose();smallFont.Dispose(); }
        base.Dispose(disposing);
    }
}
