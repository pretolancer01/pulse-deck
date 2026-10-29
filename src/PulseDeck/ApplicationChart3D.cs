using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using PulseDeck.Services;

namespace PulseDeck;
public sealed class ApplicationChart3D : UserControl
{
    readonly Viewport3D viewport = new() { ClipToBounds=true };
    readonly PerspectiveCamera camera = new() { FieldOfView=42, NearPlaneDistance=.1, FarPlaneDistance=100 };
    readonly Model3DGroup scene = new();
    readonly StackPanel ranking = new() { Margin=new(14,12,0,8) };
    readonly TextBlock axis = new() { FontSize=12, Margin=new(18,12,0,0), IsHitTestVisible=false };
    readonly TextBlock detail = new() { FontSize=13, Margin=new(0,12,0,0), TextWrapping=TextWrapping.Wrap };
    readonly Dictionary<GeometryModel3D,string> hitTargets = new();
    readonly Dictionary<string,double> heights = new(StringComparer.OrdinalIgnoreCase);
    readonly List<ScaleTransform3D> animatedScales = new();
    List<ApplicationUsage> source = new(), rows = new();
    string? selected;
    int metric; bool motion=true, dragging;
    Point pointerStart, previousPointer;
    double yaw=23, pitch=18, distance=14.5;
    public IReadOnlyList<ApplicationUsage> Displayed => rows;
    public string? SelectedName => selected;
    public bool CpuMode => metric==1;
    public int Metric => metric;
    public bool MotionEnabled { get => motion; set { motion=value; if(!value) foreach(var scale in animatedScales) { double h=scale.ScaleY; scale.BeginAnimation(ScaleTransform3D.ScaleYProperty,null); scale.ScaleY=h; } } }
    public ApplicationChart3D()
    {
        Focusable=true;
        AutomationProperties.SetName(this,"Gráfico 3D de consumo. Setas selecionam aplicativos. Shift e setas giram. Home restaura a câmera.");
        var root = new Grid(); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height=GridLength.Auto });
        var chart = new Grid(); chart.ColumnDefinitions.Add(new()); chart.ColumnDefinitions.Add(new() { Width=new(235) });
        var stage = new Grid { Background=new LinearGradientBrush(Color.FromRgb(17,27,38),Color.FromRgb(10,15,23),90), ClipToBounds=true };
        stage.RowDefinitions.Add(new() { Height=GridLength.Auto }); stage.RowDefinitions.Add(new()); stage.RowDefinitions.Add(new() { Height=GridLength.Auto });
        viewport.Camera=camera; viewport.Children.Add(new ModelVisual3D { Content=scene });
        Grid.SetRow(viewport,1); viewport.SizeChanged+=(_,_)=>UpdateCamera();
        stage.Children.Add(viewport); axis.Foreground=VisualTheme.Brush("MutedColor"); stage.Children.Add(axis);
        var hint=new TextBlock { Text="ARRASTE PARA GIRAR  ·  SCROLL PARA ZOOM", FontSize=10, Foreground=VisualTheme.Brush("MutedColor"), Margin=new(18,0,8,12), VerticalAlignment=VerticalAlignment.Bottom, IsHitTestVisible=false };
        Grid.SetRow(hint,2); stage.Children.Add(hint); chart.Children.Add(stage); Grid.SetColumn(ranking,1); chart.Children.Add(ranking); root.Children.Add(chart);
        var footer=new StackPanel();
        var controls=new WrapPanel { Margin=new(0,12,0,0) };
        void Action(string title,string help,Action action) { var b=new Button { Content=title,Padding=new Thickness(12,6,12,6),MinHeight=34,Margin=new(0,0,7,0),ToolTip=help }; AutomationProperties.SetName(b,help); b.Click+=(_,_)=>action(); controls.Children.Add(b); }
        Action("↶","Girar gráfico para a esquerda",()=>Rotate(-12)); Action("↷","Girar gráfico para a direita",()=>Rotate(12));
        Action("−","Afastar câmera",()=>Zoom(1)); Action("+","Aproximar câmera",()=>Zoom(-1)); Action("Redefinir vista","Restaurar câmera",ResetCamera);
        footer.Children.Add(controls); detail.Foreground=VisualTheme.Brush("InkColor"); footer.Children.Add(detail);
        Grid.SetRow(footer,1); root.Children.Add(footer); Content=root;
        stage.MouseLeftButtonDown+=(_,e)=> { Focus(); pointerStart=previousPointer=e.GetPosition(stage); dragging=false; stage.CaptureMouse(); e.Handled=true; };
        stage.MouseMove+=(_,e)=> {
            if(!stage.IsMouseCaptured) return;
            var p=e.GetPosition(stage); if((p-pointerStart).Length>4) dragging=true;
            if(dragging) { yaw+=(p.X-previousPointer.X)*.3; pitch=Math.Clamp(pitch+(p.Y-previousPointer.Y)*.2,7,55); UpdateCamera(); }
            previousPointer=p;
        };
        stage.MouseLeftButtonUp+=(_,e)=> {
            if(!stage.IsMouseCaptured) return;
            stage.ReleaseMouseCapture();
            if(!dragging) {
                string? hit=null;
                VisualTreeHelper.HitTest(viewport,null,result=> { if(result is RayMeshGeometry3DHitTestResult mesh && mesh.ModelHit is GeometryModel3D model && hitTargets.TryGetValue(model,out var name)) { hit=name; return HitTestResultBehavior.Stop; } return HitTestResultBehavior.Continue; },new PointHitTestParameters(e.GetPosition(viewport)));
                if(hit!=null) SelectApplication(hit);
            }
            e.Handled=true;
        };
        stage.MouseWheel+=(_,e)=> { Zoom(e.Delta>0?-1:1); e.Handled=true; };
        KeyDown+=(_,e)=> {
            if(e.Key is Key.Left or Key.Right) {
                if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Rotate(e.Key==Key.Left?-12:12);
                else if(rows.Count>0) SelectApplication(rows[Math.Clamp(rows.FindIndex(x=>x.Name==selected)+(e.Key==Key.Left?-1:1),0,rows.Count-1)].Name);
                e.Handled=true;
            }
            else if(e.Key==Key.Home) { ResetCamera(); e.Handled=true; }
        };
        UpdateCamera();
    }
    public void SetData(IEnumerable<ApplicationUsage> values) { source=values.ToList(); Refresh(); }
    public void SetMetric(int choice) { metric=Math.Clamp(choice,0,2); heights.Clear(); Refresh(); }
    public void SelectApplication(string name) { if(!rows.Any(x=>x.Name==name)) return; selected=name; Refresh(); }
    public void ResetCamera() { yaw=23; pitch=18; distance=14.5; UpdateCamera(); }
    void Rotate(double angle) { yaw+=angle; UpdateCamera(); }
    void Zoom(double amount) { distance=Math.Clamp(distance+amount,10,22); UpdateCamera(); }
    void UpdateCamera()
    {
        double aspect=viewport.ActualWidth/Math.Max(1,viewport.ActualHeight);
        camera.FieldOfView=Math.Clamp(2*Math.Atan(Math.Tan(26*Math.PI/360)*aspect)*180/Math.PI,46,160);
        double a=yaw*Math.PI/180,p=pitch*Math.PI/180;
        camera.Position=new(distance*Math.Sin(a)*Math.Cos(p),1.5+distance*Math.Sin(p),distance*Math.Cos(a)*Math.Cos(p));
        camera.LookDirection=new Point3D(0,1.5,0)-camera.Position; camera.UpDirection=new(0,1,0);
    }
    double Value(ApplicationUsage row)=>metric==1?row.CpuPercent??0:metric==2?row.GpuPercent??0:row.RamBytes/1048576.0;
    string ValueText(ApplicationUsage row)=>metric==1?row.Cpu:metric==2?row.Gpu:row.Ram;
    void Refresh()
    {
        rows=(metric==1?source.Where(x=>x.CpuPercent.HasValue).OrderByDescending(x=>x.CpuPercent).ThenBy(x=>x.Name):metric==2?source.Where(x=>x.GpuPercent.HasValue).OrderByDescending(x=>x.GpuPercent).ThenBy(x=>x.Name):source.OrderByDescending(x=>x.RamBytes).ThenBy(x=>x.Name)).Take(6).ToList();
        if(!rows.Any(x=>x.Name==selected)) selected=rows.FirstOrDefault()?.Name;
        double max=rows.Count>0?Math.Max(1,rows.Max(Value)):1;
        double magnitude=Math.Pow(10,Math.Floor(Math.Log10(max)));
        double ceiling=Math.Ceiling(max/magnitude)*magnitude;
        axis.Text=rows.Count==0?"Sem leitura disponível para esta métrica…":$"TOP {rows.Count}  /  {(metric==1?"CPU":metric==2?"GPU":"RAM")}\nAltura: 0 a {ceiling:N0} {(metric==0?"MB":"%")} · mesma escala para todas as colunas";
        scene.Children.Clear(); hitTargets.Clear(); animatedScales.Clear(); scene.Children.Add(SceneGeometry.Lights());
        scene.Children.Add(SceneGeometry.Box(0,-.18,0,9.2,.18,2.2,SceneGeometry.Material(Color.FromRgb(26,40,54))));
        var gridMaterial=SceneGeometry.Material(Color.FromRgb(52,78,94));
        for(int i=0;i<5;i++) scene.Children.Add(SceneGeometry.Box(0,i*.875,-.65,9.2,.012,.012,gridMaterial));
        var focused=(Keyboard.FocusedElement as Button)?.Tag as string;
        ranking.Children.Clear();
        var head=new TextBlock { Text="APLICATIVOS / POR EXECUTÁVEL", FontSize=10, Foreground=VisualTheme.Brush("MutedColor"), Margin=new(0,0,0,10) }; ranking.Children.Add(head);
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i]; bool active=row.Name==selected;
            var color=active?Color.FromRgb(101,232,205):Color.FromRgb((byte)(89+i*9),(byte)(132+i*6),(byte)(183+i*7));
            double h=3.5*Value(row)/ceiling; // Zero really is zero; no invented minimum bar height.
            var bar=SceneGeometry.Box((i-2.5)*1.4,0,0,.85,1,.85,SceneGeometry.Material(color,active));
            var scale=new ScaleTransform3D(1,h,1); bar.Transform=scale;
            if(motion && SystemParameters.ClientAreaAnimation && IsVisible && heights.TryGetValue(row.Name,out double old) && Math.Abs(old-h)>.005)
                scale.BeginAnimation(ScaleTransform3D.ScaleYProperty,new DoubleAnimation(old,h,TimeSpan.FromMilliseconds(550)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut },FillBehavior=FillBehavior.Stop });
            heights[row.Name]=h; animatedScales.Add(scale); hitTargets[bar]=row.Name; scene.Children.Add(bar);
            // Floor badges remain selectable even for a true zero sample.
            var pad=SceneGeometry.Box((i-2.5)*1.4,0,0,1.06,.025,1.06,SceneGeometry.Material(color,active)); scene.Children.Add(pad); hitTargets[pad]=row.Name;
            var content=new Grid(); content.ColumnDefinitions.Add(new() { Width=new(23) }); content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
            content.Children.Add(new TextBlock { Text=(i+1).ToString("00"),FontSize=10,Foreground=new SolidColorBrush(color),VerticalAlignment=VerticalAlignment.Center });
            var label=new TextBlock { Text=row.Name, TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(label,1); content.Children.Add(label);
            var value=new TextBlock { Text=ValueText(row),FontSize=12,Foreground=VisualTheme.Brush("CyanColor"),VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(value,2); content.Children.Add(value);
            var button=new Button { Content=content,Tag=row.Name,Padding=new(8,9,8,9),Margin=new(0,0,0,5),HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderBrush=active?VisualTheme.Brush("CyanColor"):VisualTheme.Brush("LineColor"),Background=active?VisualTheme.Brush("RaisedColor"):VisualTheme.Brush("SurfaceColor"),ToolTip=$"{row.Name} · {row.ProcessCount} processo(s) · RAM {row.Ram} · CPU {row.Cpu} · GPU {row.Gpu}" };
            AutomationProperties.SetName(button,$"{i+1}. {row.Name}. RAM {row.Ram}. CPU {row.Cpu}. GPU {row.Gpu}");
            button.Click+=(_,_)=>SelectApplication(row.Name); ranking.Children.Add(button); if(focused==row.Name) button.Focus();
        }
        var chosen=rows.FirstOrDefault(x=>x.Name==selected);
        detail.Text=chosen==null?"Sem leitura por processo para esta métrica. A GPU geral continua na telemetria do driver.":$"{chosen.Name}  ·  PID {chosen.Pids}  ·  RAM {chosen.Ram}  ·  CPU {chosen.Cpu}  ·  GPU {chosen.Gpu}";
    }
}
