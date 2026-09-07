using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

public sealed class UpdateNotice : Panel {
    readonly Timer timer=new Timer {Interval=16};
    readonly Stopwatch clock=new Stopwatch();
    readonly Button message=new Button(),close=new Button();
    readonly float scale;
    double exitStart=-1;
    int exitFrom;
    public event Action Requested;
    public UpdateNotice(float scale) {
        this.scale=scale;Height=(int)(106*scale);Visible=false;BackColor=Color.FromArgb(29,39,49);
        message.FlatStyle=close.FlatStyle=FlatStyle.Flat;message.FlatAppearance.BorderSize=close.FlatAppearance.BorderSize=0;
        message.ForeColor=Color.FromArgb(242,222,174);message.BackColor=close.BackColor=BackColor;close.ForeColor=Color.White;
        message.Font=new Font("Segoe UI",13*scale,FontStyle.Bold,GraphicsUnit.Pixel);close.Font=new Font("Segoe UI",18*scale,FontStyle.Regular,GraphicsUnit.Pixel);
        message.TextAlign=ContentAlignment.MiddleLeft;message.Padding=new Padding((int)(20*scale),0,0,0);message.Cursor=close.Cursor=Cursors.Hand;
        message.AccessibleName="Verfügbares Update prüfen und bestätigen";close.Text="×";close.AccessibleName="Updatehinweis schließen";
        Controls.Add(message);Controls.Add(close);
        message.Click+=(s,e)=>{Dismiss();if(Requested!=null)Requested();};close.Click+=(s,e)=>Dismiss();
        timer.Tick+=(s,e)=>Advance(clock.Elapsed.TotalMilliseconds);
        Resize+=(s,e)=>{message.SetBounds(4,4,Math.Max(0,Width-(int)(50*scale)),Height-8);close.SetBounds(Width-(int)(42*scale),(int)(10*scale),(int)(32*scale),(int)(32*scale));};
    }
    public void ShowNotice(string version) {
        message.Text="↑  UPDATE VERFÜGBAR\nVersion "+version+" · Zum Installieren hier klicken";
        exitStart=-1;Top=-Height;Visible=true;BringToFront();clock.Restart();timer.Start();
    }
    public void Dismiss() {if(exitStart<0){exitStart=clock.Elapsed.TotalMilliseconds;exitFrom=Top;}}
    internal void Advance(double milliseconds) {
        const double duration=350;
        int resting=(int)(12*scale);
        if(exitStart<0 && milliseconds>=10350){exitStart=10350;exitFrom=resting;}
        if(exitStart>=0) {
            double t=Math.Min(1,Math.Max(0,(milliseconds-exitStart)/duration));
            Top=exitFrom+(int)((-Height-exitFrom)*t*t);
            if(t>=1){Visible=false;timer.Stop();clock.Stop();}
        }else {
            double t=Math.Min(1,Math.Max(0,milliseconds/duration));
            Top=-Height+(int)((Height+resting)*(1-Math.Pow(1-t,3)));
        }
    }
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Color.FromArgb(212,175,102),2))e.Graphics.DrawRectangle(pen,1,1,Width-2,Height-2);}
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();message.Font.Dispose();close.Font.Dispose();}base.Dispose(disposing);}
}
