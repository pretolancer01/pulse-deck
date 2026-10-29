using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PulseDeck.Services;

namespace PulseDeck;
internal static class VisualTheme
{
    public static Color Color(string key) => System.Windows.Application.Current.TryFindResource(key) is Color value ? value : Colors.Gray;
    public static Brush Brush(string key, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color(key)) { Opacity = opacity }; brush.Freeze(); return brush;
    }
    public static FormattedText Text(string text, double size, Brush brush, Visual visual, bool display = false)
    {
        var family = System.Windows.Application.Current.TryFindResource(display ? "DisplayFont" : "BodyFont") as FontFamily ?? new FontFamily("Segoe UI");
        return new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
    }
}

public sealed class ConstellationField : AnimatedVisual
{
    static readonly Brush baseDots = BuildBaseDots();
    static Brush BuildBaseDots()
    {
        var drawing=new GeometryDrawing(VisualTheme.Brush("MutedColor",.10),null,new EllipseGeometry(new Point(16,16),.65,.65));
        var brush=new DrawingBrush(drawing) { TileMode=TileMode.Tile,Viewport=new Rect(0,0,28,28),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.None };
        brush.Freeze(); return brush;
    }
    Point pointer;
    DateTime interaction = DateTime.MinValue, previous = DateTime.UtcNow;
    double phase, pulse;
    public int InteractionCount { get; private set; }
    protected override bool NeedsAnimation => (DateTime.UtcNow - interaction).TotalSeconds < 5;
    public void SetPointer(Point point, bool burst = false)
    {
        if (!MotionEnabled || Paused || !SystemParameters.ClientAreaAnimation) return;
        pointer = point; interaction = DateTime.UtcNow; InteractionCount++;
        if (burst) pulse = 1;
        InvalidateVisual();
    }
    public void ClearPointer() { interaction = DateTime.MinValue; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var now = DateTime.UtcNow;
        double dt = Math.Min(.1, (now - previous).TotalSeconds); previous = now;
        bool active = MotionEnabled && !Paused && SystemParameters.ClientAreaAnimation && NeedsAnimation;
        if (active) { phase += dt; pulse = Math.Max(0, pulse - dt * 1.6); }
        dc.DrawRectangle(baseDots,null,new Rect(RenderSize));
        var cyan = VisualTheme.Color("CyanColor");
        double fade = active ? Math.Clamp(5 - (now - interaction).TotalSeconds, 0, 1) : 0;
        if(fade<=0) return;
        for (double y = Math.Max(16,16+Math.Floor((pointer.Y-196-16)/28)*28); y < Math.Min(ActualHeight,pointer.Y+196); y += 28)
        for (double x = Math.Max(16,16+Math.Floor((pointer.X-196-16)/28)*28); x < Math.Min(ActualWidth,pointer.X+196); x += 28)
        {
            double dx = x - pointer.X, dy = y - pointer.Y, distance = Math.Sqrt(dx * dx + dy * dy);
            double influence = fade * Math.Pow(Math.Max(0, 1 - distance / 180), 2);
            if (influence > .008)
            {
                double lift = Math.Sin(distance / 26 - phase * 2.2) * (8 + pulse * 18) * influence;
                var point = new Point(x + (distance > 0 ? dx / distance : 0) * lift, y + (distance > 0 ? dy / distance : 0) * lift);
                var brush = new SolidColorBrush(cyan) { Opacity = .16 + influence * .75 };
                dc.DrawEllipse(brush, null, point, 1 + influence * 1.6, 1 + influence * 1.6);
                if (influence > .22 && x + 28 < ActualWidth) dc.DrawLine(new Pen(VisualTheme.Brush("CyanColor", influence * .15), 1), point, new(x + 28, y));
            }
        }
    }
}

public sealed class ReactivePanel : Border
{
    Point pointer;
    public ReactivePanel()
    {
        MouseMove += (_, e) => { pointer = MotionAllowed ? e.GetPosition(this) : new(ActualWidth / 2, ActualHeight / 2); InvalidateVisual(); };
        MouseLeave += (_, _) => InvalidateVisual();
        GotKeyboardFocus += (_, _) => { pointer = new(ActualWidth / 2, ActualHeight / 2); InvalidateVisual(); };
        LostKeyboardFocus += (_, _) => InvalidateVisual();
    }
    static bool MotionAllowed => SystemParameters.ClientAreaAnimation && System.Windows.Application.Current.Properties["MotionAllowed"] is true;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if ((!IsMouseOver && !IsKeyboardFocusWithin) || ActualWidth <= 0 || ActualHeight <= 0) return;
        var color = VisualTheme.Color("CyanColor");
        var brush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = pointer, GradientOrigin = pointer, RadiusX = 200, RadiusY = 150 };
        brush.GradientStops.Add(new(Color.FromArgb(23, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new(Color.FromArgb(0, color.R, color.G, color.B), 1));
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize), CornerRadius.TopLeft, CornerRadius.TopLeft));
        dc.DrawRectangle(brush, null, new Rect(RenderSize)); dc.Pop();
    }
}

public sealed class TelemetryDial : AnimatedVisual
{
    double? target;
    double rendered;
    public void SetUsage(double? usage) { target = usage; if (!MotionEnabled) rendered = usage ?? 0; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 12);
        if (radius <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        rendered = MotionEnabled && !Paused && SystemParameters.ClientAreaAnimation ? rendered + ((target ?? 0) - rendered) * .12 : target ?? 0;
        dc.DrawEllipse(null, new Pen(VisualTheme.Brush("LineColor"), 7), center, radius, radius);
        for (int i = 0; i < 40; i++)
        {
            double a = i * Math.PI * 2 / 40;
            dc.DrawLine(new Pen(VisualTheme.Brush("LineColor"), 1), new(center.X + Math.Cos(a) * (radius + 8), center.Y + Math.Sin(a) * (radius + 8)), new(center.X + Math.Cos(a) * (radius + 11), center.Y + Math.Sin(a) * (radius + 11)));
        }
        double fraction = Math.Clamp(rendered / 100, 0, .99999);
        if (fraction > 0)
        {
            double a = fraction * Math.PI * 2 - Math.PI / 2;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open()) { ctx.BeginFigure(new(center.X, center.Y - radius), false, false); ctx.ArcTo(new(center.X + Math.Cos(a) * radius, center.Y + Math.Sin(a) * radius), new(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false); }
            dc.DrawGeometry(null, new Pen(VisualTheme.Brush("CyanColor"), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        }
        var value = VisualTheme.Text(target.HasValue ? $"{target:N0}%" : "—", 34, VisualTheme.Brush("InkColor"), this, true);
        dc.DrawText(value, new(center.X - value.Width / 2, center.Y - 27));
        var caption = VisualTheme.Text("Uso da GPU", 11, VisualTheme.Brush("MutedColor"), this);
        dc.DrawText(caption, new(center.X - caption.Width / 2, center.Y + 16));
    }
}

public sealed class FanCurveVisual : FrameworkElement
{
    IReadOnlyList<FanPoint> points = Array.Empty<FanPoint>();
    public void SetPoints(IReadOnlyList<FanPoint> values) { points = values.Select(p => p.Copy()).ToList(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double left = 32, top = 12, width = ActualWidth - 54, height = ActualHeight - 36;
        if (width <= 0 || height <= 0) return;
        for (int i = 0; i <= 2; i++)
        {
            double y = top + height * i / 2;
            dc.DrawLine(new Pen(VisualTheme.Brush("LineColor"), 1), new(left, y), new(left + width, y));
            dc.DrawText(VisualTheme.Text((100 - i * 50).ToString(), 10, VisualTheme.Brush("MutedColor"), this), new(0, y - 6));
        }
        if (points.Count == 0)
        {
            dc.DrawText(VisualTheme.Text("Curva indisponível", 12, VisualTheme.Brush("MutedColor"), this), new(left + 12, top + height / 2)); return;
        }
        double min = Math.Min(25, points[0].Temperature), max = Math.Max(100, points[^1].Temperature);
        Point Position(FanPoint p) => new(left + width * (p.Temperature - min) / Math.Max(1, max - min), top + height * (1 - p.Speed / 100d));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open()) { ctx.BeginFigure(Position(points[0]), false, false); foreach (var point in points.Skip(1)) ctx.LineTo(Position(point), true, false); }
        dc.DrawGeometry(null, new Pen(VisualTheme.Brush("CyanColor", .10), 9), geometry);
        dc.DrawGeometry(null, new Pen(VisualTheme.Brush("CyanColor"), 2), geometry);
        foreach (var p in points) { var xy = Position(p); dc.DrawEllipse(VisualTheme.Brush("CyanColor"), null, xy, 4, 4); var label = VisualTheme.Text($"{p.Temperature}°", 10, VisualTheme.Brush("MutedColor"), this); dc.DrawText(label, new(xy.X - label.Width / 2, top + height + 8)); }
    }
}
