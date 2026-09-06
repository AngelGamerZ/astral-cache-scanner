using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class OverlayTests {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    static Snapshot Sample(double x,double y,double facing) {
        return new Snapshot {live=true,facing=facing,player=new PointData {space="test",units="yards",x=0,y=0,z=0},objects=new[]{new ObjectData {name="Astral Cache",id="TEST",space="test",units="yards",x=x,y=y,z=0}}};
    }
    public static void CheckDirections() {
        var forward=Navigation.Select(Sample(10,0,0));Check(Math.Abs(forward.RelativeAngle.Value)<1e-6 && forward.Instruction=="Vorwärts","Forward direction");
        var left=Navigation.Select(Sample(0,10,0));Check(Math.Abs(left.RelativeAngle.Value-Math.PI/2)<1e-6 && left.Instruction=="Links","Left direction");
        var right=Navigation.Select(Sample(0,-10,0));Check(Math.Abs(right.RelativeAngle.Value+Math.PI/2)<1e-6 && right.Instruction=="Rechts","Right direction");
        Check(Navigation.Select(Sample(-10,0,0)).Instruction=="Umdrehen","Behind");
        Check(Navigation.Select(Sample(0,10,Math.PI/2)).Instruction=="Vorwärts","Player rotation");
        Check(Math.Abs(Navigation.Select(Sample(10,0,2*Math.PI-0.01)).RelativeAngle.Value-0.01)<1e-6,"Angle wrapping");
        var s=Sample(0,0,0);s.objects[0].z=8;var vertical=Navigation.Select(s);Check(vertical.AtPoint && vertical.Instruction=="Über dir" && !vertical.RelativeAngle.HasValue,"Vertical target must not point forward");
        s=Sample(1,0,0);Check(Navigation.Select(s).AtPoint,"Near target avoids unstable arrow");
        s=Sample(10,0,0);s.facing=null;Check(!Navigation.Select(s).RelativeAngle.HasValue,"Missing facing");
        s=Sample(10,0,0);s.facing=Double.NaN;Check(!Navigation.Select(s).RelativeAngle.HasValue,"Invalid facing");
        s=Sample(10,0,0);s.onTransport=true;Check(!Navigation.Select(s).RelativeAngle.HasValue && !Navigation.Select(s).Distance.HasValue,"Transport");
        s=Sample(10,0,0);s.objects[0].space="other";Check(!Navigation.Select(s).RelativeAngle.HasValue,"Different coordinate space");
        s=Sample(10,0,0);s.demo=true;Check(Navigation.Select(s)==null,"Demo cannot create live overlay");
        s=Sample(10,0,0);s.live=false;Check(Navigation.Select(s)==null,"File cannot create live overlay");
        s=Sample(10,0,0);s.objects[0].name="Astral Table";Check(Navigation.Select(s)==null,"Only exact name");
        s=Sample(10,0,0);s.objects=new[]{s.objects[0],new ObjectData {name="Astral Cache",id="NEAR",space="test",units="yards",x=3,y=4,z=0}};Check(Navigation.Select(s).Target.id=="NEAR" && Navigation.Select(s).Count==2,"Nearest cache");
        // For drawing: an up-pointing shape rotated by -relative yields screen right=-sin(relative).
        Check(-Math.Sin(left.RelativeAngle.Value)<-0.99 && -Math.Sin(right.RelativeAngle.Value)>0.99,"Arrow screen handedness");
    }
    public static int Run() {
        string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"overlay-test-results.txt");
        try {
            CheckDirections();Application.EnableVisualStyles();
            using(var overlay=new CacheOverlay()) {
                var diagnostics=new System.Collections.Generic.List<string>();overlay.Diagnostic+=message=>diagnostics.Add(message);
                Check(overlay.InputTransparent,"Layered/click-through/noactivate/toolwindow flags");
                var sample=Sample(24,-20,0);
                overlay.SavePreview(Navigation.Select(sample),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"overlay-preview.png"));
                sample.objects[0].remembered=true;
                overlay.SavePreview(Navigation.Select(sample),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"overlay-saved-preview.png"));
                var nearSaved=Sample(1,0,0);nearSaved.objects[0].remembered=true;
                Check(Navigation.Select(nearSaved).Instruction=="Letzter Fundort erreicht","Remembered position is not confirmed presence");
                var before=GetForegroundWindow();
                // A test-owned PID must not display over the user's game or other apps.
                overlay.SetSnapshot(sample,Process.GetCurrentProcess().Id);Application.DoEvents();
                Check(!overlay.Visible,"Foreground gating");Check(GetForegroundWindow()==before,"Focus unchanged");
                Check(diagnostics.Last().Contains("nicht im Vordergrund") && diagnostics.Last().Contains("keine Loot-Bestätigung"),"Focus loss logged separately from loot");
                int count=diagnostics.Count;overlay.SetSnapshot(sample,Process.GetCurrentProcess().Id);Check(diagnostics.Count==count,"Stable overlay state does not flood log");
                overlay.SetEnabled(false);Check(diagnostics.Last().Contains("deaktiviert"),"Disabled overlay logged");
                overlay.Clear();Check(!overlay.Visible,"Hidden after lost target");
                Check(diagnostics.Last().Contains("keine aktuellen Scandaten"),"Missing scan logged");
            }
            File.WriteAllText(report,"PASS: compass directions, rotation, wrapping, near/vertical targets, missing data, transport, nearest target, no demo/export, window input styles, foreground gating, focus preservation, clear/hide. Preview uses explicitly labelled synthetic data.\r\n");return 0;
        } catch(Exception ex) {File.WriteAllText(report,ex.ToString());return 1;}
    }
}
