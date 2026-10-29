using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.IO;
using PulseDeck.Services;

namespace PulseDeck;
public sealed class OverlayWindow : Window
{
    readonly StackPanel stats=new() { Margin=new(10,1,10,9) };
    readonly Border panel;
    public double BackgroundOpacity { get; private set; } = .9;
    public double SizeFactor { get; private set; } = 1;
    public bool ShowFps { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowGpuTemperature { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowCpuTemperature { get; set; }
    public OverlayWindow()
    {
        Title="PulseDeck Overlay"; Width=188; SizeToContent=SizeToContent.Height;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true;
        Background=Brushes.Transparent; Topmost=true; ShowInTaskbar=false;
        Left=SystemParameters.WorkArea.Right-208; Top=SystemParameters.WorkArea.Top+48;
        panel=new Border { BorderBrush=new SolidColorBrush(Color.FromRgb(48,90,100)),BorderThickness=new(1),CornerRadius=new(9),Child=new StackPanel() };
        SetTransparency(.9);
        var body=(StackPanel)panel.Child;
        var head=new Grid { Margin=new(10,5,6,0),Height=28 };
        head.ColumnDefinitions.Add(new()); head.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var title=new TextBlock { Text="≡  MOVER",FontFamily=new("Bahnschrift"),FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(101,232,205)),VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.SizeAll };
        title.MouseLeftButtonDown+=(_,_)=> { try { DragMove(); } catch(InvalidOperationException) { } }; head.Children.Add(title);
        var closePath=new System.Windows.Shapes.Path { Data=Geometry.Parse("M 2,2 L 12,12 M 12,2 L 2,12"),Stroke=new SolidColorBrush(Color.FromRgb(240,243,248)),StrokeThickness=2,Width=14,Height=14,Stretch=Stretch.None,IsHitTestVisible=false };
        var close=new Button { Content=closePath,Width=28,Height=25,Padding=new(6),MinHeight=25,ToolTip="Fechar overlay" };
        AutomationProperties.SetName(close,"Fechar overlay"); close.Click+=(_,_)=>Close(); Grid.SetColumn(close,1); head.Children.Add(close);
        body.Children.Add(head); body.Children.Add(stats); Content=panel;
    }
    public void SetTransparency(double opacity)
    {
        BackgroundOpacity=Math.Clamp(opacity,.55,1);
        panel.Background=new SolidColorBrush(Color.FromArgb((byte)Math.Round(255*BackgroundOpacity),9,12,18));
    }
    public void SetSize(double factor)
    {
        SizeFactor=Math.Clamp(factor,.65,1.2);
        Width=188*SizeFactor;
        panel.LayoutTransform=new ScaleTransform(SizeFactor,SizeFactor);
    }
    public void SavePreview(string path)
    {
        UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path); encoder.Save(file);
    }
    void Line(string name,string value,Brush brush)
    {
        var line=new Grid { Margin=new(0,2,0,2) }; line.ColumnDefinitions.Add(new()); line.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        line.Children.Add(new TextBlock { Text=name,Foreground=new SolidColorBrush(Color.FromRgb(176,190,207)),FontSize=11 });
        var metric=new TextBlock { Text=value,Foreground=brush,FontSize=12,FontFamily=new("Bahnschrift") }; Grid.SetColumn(metric,1); line.Children.Add(metric); stats.Children.Add(line);
    }
    public void Update(Metrics? metrics,GpuMetrics gpu,double? cpuTemperature,FpsReading fps)
    {
        stats.Children.Clear();
        var cyan=new SolidColorBrush(Color.FromRgb(101,232,205)); var amber=new SolidColorBrush(Color.FromRgb(242,191,123));
        if(ShowFps) Line("FPS",fps.FramesPerSecond.HasValue?$"{fps.FramesPerSecond} FPS":"—",amber);
        if(ShowGpu) Line("GPU",gpu.Usage.HasValue?$"{gpu.Usage:N0}%":"—",cyan);
        if(ShowGpuTemperature) Line("GPU °C",gpu.Temperature.HasValue?$"{gpu.Temperature:N0} °C":"—",amber);
        if(ShowCpu) Line("CPU",metrics?.Cpu is double c?$"{c:N0}%":"—",cyan);
        if(ShowCpuTemperature) Line("CPU °C",cpuTemperature.HasValue?$"{cpuTemperature:N0} °C":"sem sensor",amber);
        if(ShowRam) Line("RAM",metrics is null?"—":$"{metrics.RamPercent:N0}%",cyan);
    }
}
