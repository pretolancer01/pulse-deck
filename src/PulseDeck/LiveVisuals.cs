using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace PulseDeck;
// Repaint only visible, active visualizations, at most 30 fps. Data collection stays at 0.5 Hz.
public abstract class AnimatedVisual : FrameworkElement
{
    DateTime lastFrame;
    public bool MotionEnabled { get; set; } = SystemParameters.ClientAreaAnimation;
    public bool Paused { get; set; }
    protected virtual bool NeedsAnimation => true;
    protected AnimatedVisual()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += Frame;
        Unloaded += (_, _) => CompositionTarget.Rendering -= Frame;
    }
    void Frame(object? sender, EventArgs e)
    {
        if (!IsVisible || Paused || !MotionEnabled || !NeedsAnimation || !SystemParameters.ClientAreaAnimation || Window.GetWindow(this)?.WindowState == WindowState.Minimized || (DateTime.UtcNow - lastFrame).TotalMilliseconds < 33) return;
        lastFrame = DateTime.UtcNow; InvalidateVisual();
    }
}
public sealed class LiveChart : AnimatedVisual
{
    sealed record Sample(DateTime Time, double? Cpu, double Ram, double? Gpu);
    readonly List<Sample> samples = new();
    DateTime displayTime = DateTime.UtcNow;
    double? mouseX;
    int inspectIndex = -1;
    public LiveChart()
    {
        Focusable = true; Cursor = Cursors.Cross;
        ToolTip = "Passe o mouse para inspecionar amostras reais. Com foco, use as setas esquerda e direita.";
        MouseMove += (_, e) => { mouseX = e.GetPosition(this).X; InvalidateVisual(); };
        MouseLeave += (_, _) => { mouseX = null; if (!IsKeyboardFocused) inspectIndex = -1; InvalidateVisual(); };
        MouseLeftButtonDown += (_, _) => Focus();
        LostKeyboardFocus += (_, _) => { if (!IsMouseOver) inspectIndex = -1; InvalidateVisual(); };
        KeyDown += (_, e) => {
            if (samples.Count == 0) return;
            if (e.Key is Key.Left or Key.Right)
            {
                mouseX = null;
                inspectIndex = Math.Clamp((inspectIndex < 0 ? samples.Count - 1 : inspectIndex) + (e.Key == Key.Left ? -1 : 1), 0, samples.Count - 1);
                InvalidateVisual(); e.Handled = true;
            }
            else if (e.Key == Key.Escape) { inspectIndex = -1; InvalidateVisual(); e.Handled = true; }
        };
    }
    public void Push(double? cpu, double ram, double? gpu)
    {
        var now = DateTime.UtcNow;
        samples.Add(new(now, cpu, ram, gpu));
        samples.RemoveAll(x => x.Time < now.AddSeconds(-125));
        displayTime = now; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth - 44, h = ActualHeight - 24;
        if (w <= 0 || h <= 0) return;
        var grid = new Pen(VisualTheme.Brush("LineColor"), 1);
        var type = new Typeface("Segoe UI");
        for (int i = 0; i <= 4; i++)
        {
            double y = i * h / 4;
            dc.DrawLine(grid, new(0, y), new(w, y));
            var label = new FormattedText((100 - i * 25).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, type, 10, Brushes.LightSlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new(w + 9, y - 7));
        }
        for (int i = 0; i <= 8; i++) dc.DrawLine(grid, new(i * w / 8, 0), new(i * w / 8, h));
        if (samples.Count == 0) return;
        DateTime now = MotionEnabled && !Paused && SystemParameters.ClientAreaAnimation ? DateTime.UtcNow : displayTime;
        void Series(Func<Sample, double?> selector, Color color)
        {
            var geometry = new StreamGeometry();
            bool started = false;
            Point? last = null;
            using (var ctx = geometry.Open())
            {
                foreach (var sample in samples)
                {
                    double? value = selector(sample);
                    // Pauses and unsupported readings are shown as gaps, never as fabricated zeroes.
                    if (value == null || (last != null && (sample.Time - samples[Math.Max(0, samples.IndexOf(sample) - 1)].Time).TotalSeconds > 5)) started = false;
                    if (value == null) continue;
                    var point = new Point(w * (1 - (now - sample.Time).TotalSeconds / 120), h * (1 - Math.Clamp(value.Value, 0, 100) / 100));
                    if (!started) { ctx.BeginFigure(point, false, false); started = true; } else ctx.LineTo(point, true, false);
                    last = point;
                }
            }
            geometry.Freeze();
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w + 2, h + 2)));
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(25, color.R, color.G, color.B)), 8), geometry);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2), geometry);
            if (last.HasValue) dc.DrawEllipse(new SolidColorBrush(color), null, last.Value, 3.5, 3.5);
            dc.Pop();
        }
        Series(s => s.Ram, VisualTheme.Color("PurpleColor"));
        Series(s => s.Gpu, VisualTheme.Color("AmberColor"));
        Series(s => s.Cpu, VisualTheme.Color("CyanColor"));
        if (mouseX.HasValue && mouseX >= 0 && mouseX <= w)
        {
            var target = now.AddSeconds(-120 * (1 - mouseX.Value / w));
            double nearest = double.MaxValue;
            for (int i = 0; i < samples.Count; i++)
            {
                double distance = Math.Abs((samples[i].Time - target).TotalSeconds);
                if (distance < nearest) { nearest = distance; inspectIndex = i; }
            }
        }
        if (inspectIndex >= 0 && inspectIndex < samples.Count)
        {
            var sample = samples[inspectIndex];
            double x = w * (1 - (now - sample.Time).TotalSeconds / 120);
            if (x >= 0 && x <= w)
            {
                dc.DrawLine(new Pen(VisualTheme.Brush("InkColor", .5), 1) { DashStyle = DashStyles.Dash }, new(x, 0), new(x, h));
                string Value(double? value) => value.HasValue ? $"{value:N1}%" : "—";
                string text = $"{sample.Time.ToLocalTime():HH:mm:ss}\nCPU  {Value(sample.Cpu)}\nRAM  {Value(sample.Ram)}\nGPU  {Value(sample.Gpu)}";
                var label = VisualTheme.Text(text, 12, VisualTheme.Brush("InkColor"), this);
                double tooltipX = Math.Clamp(x - 160, 8, Math.Max(8, w - 168));
                dc.DrawRoundedRectangle(VisualTheme.Brush("RaisedColor"), new Pen(VisualTheme.Brush("LineColor"), 1), new(tooltipX, 8, 154, 91), 7, 7);
                dc.DrawText(label, new(tooltipX + 12, 18));
            }
        }
        if (IsKeyboardFocused) dc.DrawRoundedRectangle(null, new Pen(VisualTheme.Brush("CyanColor"), 1), new(0, 0, w, h), 2, 2);
    }
}
public sealed class FanVisual : AnimatedVisual
{
    double angle;
    DateTime last = DateTime.UtcNow;
    public int? Rpm { get; set; }
    public double PreviewFactor { get; set; } = 1;
    public FanVisual() { ToolTip = "Prévia visual do perfil selecionado. O RPM real aparece ao lado; escolher o perfil não altera o hardware."; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var now = DateTime.UtcNow;
        if (!Paused && MotionEnabled && SystemParameters.ClientAreaAnimation) angle = (angle + Math.Min(0.1, (now - last).TotalSeconds) * 78 * PreviewFactor) % 360;
        last = now;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 5;
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(45, 82, 100)), 2), center, radius, radius);
        dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
        var brush = new SolidColorBrush(Rpm.HasValue ? Color.FromRgb(112, 243, 223) : Color.FromRgb(100, 120, 140));
        for (int i = 0; i < 5; i++)
        {
            dc.PushTransform(new RotateTransform(i * 72, center.X, center.Y));
            dc.DrawEllipse(brush, null, new(center.X + radius * .38, center.Y - radius * .35), radius * .19, radius * .42);
            dc.Pop();
        }
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(16, 28, 43)), new Pen(brush, 2), center, 8, 8);
        dc.Pop();
    }
}
