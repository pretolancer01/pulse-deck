using System;
using System.Windows;
using System.Windows.Media;

namespace PulseDeck;
public sealed class MetricDial : AnimatedVisual
{
    double? value; double rendered;
    string text="—", description="";
    Color accent=Color.FromRgb(101,232,205);
    public void Set(double? percent,string label,string caption,Color color)
    { value=percent; text=label; description=caption; accent=color; if(!MotionEnabled) rendered=percent??0; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double r=Math.Max(0,Math.Min(ActualHeight,ActualWidth)/2-14); if(r==0)return;
        var c=new Point(ActualWidth/2,ActualHeight/2);
        rendered=MotionEnabled&&!Paused&&SystemParameters.ClientAreaAnimation?rendered+((value??0)-rendered)*.16:value??0;
        dc.DrawEllipse(null,new Pen(VisualTheme.Brush("LineColor"),7),c,r,r);
        double part=Math.Clamp(rendered/100,0,.99999);
        if(part>0)
        {
            double a=part*Math.PI*2-Math.PI/2;
            var arc=new StreamGeometry(); using(var ctx=arc.Open()) { ctx.BeginFigure(new(c.X,c.Y-r),false,false); ctx.ArcTo(new(c.X+Math.Cos(a)*r,c.Y+Math.Sin(a)*r),new(r,r),0,part>.5,SweepDirection.Clockwise,true,false); }
            dc.DrawGeometry(null,new Pen(new SolidColorBrush(accent),7) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round },arc);
        }
        var primary=VisualTheme.Text(text,27,VisualTheme.Brush("InkColor"),this,true);
        var secondary=VisualTheme.Text(description,10,VisualTheme.Brush("MutedColor"),this);
        dc.DrawText(primary,new(c.X-primary.Width/2,c.Y-24)); dc.DrawText(secondary,new(c.X-secondary.Width/2,c.Y+15));
    }
}
