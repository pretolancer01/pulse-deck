using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PulseDeck;
// A short, bounded overlay belonging only to PulseDeck; no hooks into other applications.
public sealed class IntroWindow : Window
{
    readonly Stopwatch watch=new();
    readonly AxisAngleRotation3D yaw=new(new(0,1,0),-24);
    readonly AxisAngleRotation3D orbit=new(new(.4,.8,1),0);
    readonly ScaleTransform3D beat=new(1,1,1), wave=new(1,1,1);
    readonly SolidColorBrush waveBrush=new(Color.FromArgb(80,101,232,205));
    readonly DispatcherTimer end=new() { Interval=TimeSpan.FromSeconds(3.2) };
    readonly TaskCompletionSource completion=new();
    double lastFrame;
    public Task Completion => completion.Task;
    public IntroWindow()
    {
        Title="PulseDeck • abertura"; Width=Math.Min(1400,Math.Max(950,SystemParameters.PrimaryScreenWidth*.72)); Height=Math.Min(900,Math.Max(680,SystemParameters.PrimaryScreenHeight*.72)); WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true; ShowInTaskbar=false; WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new Grid(); Content=root;
        root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height=new(115) });
        var viewport=new Viewport3D { Width=Math.Min(780,Width*.63), HorizontalAlignment=HorizontalAlignment.Center, Camera=new PerspectiveCamera(new(0,.25,8.8),new(0,-.2,-8.8),new(0,1,0),48),IsHitTestVisible=false };
        root.Children.Add(viewport);
        var scene=SceneGeometry.Lights(); var core=new Model3DGroup();
        var body=SceneGeometry.Material(Color.FromRgb(101,232,205),true);
        var pulse=new[] {new Point(-1.9,0),new Point(-1,0),new Point(-.55,1.12),new Point(.07,-1.03),new Point(.66,0),new Point(1.9,0)};
        for(int i=1;i<pulse.Length;i++)
        {
            var a=pulse[i-1]; var b=pulse[i]; var delta=b-a;
            var segment=SceneGeometry.Box(0,-.11,0,delta.Length+.06,.22,.32,body);
            var transform=new Transform3DGroup(); transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new(0,0,1),Math.Atan2(delta.Y,delta.X)*180/Math.PI)));
            transform.Children.Add(new TranslateTransform3D((a.X+b.X)/2,(a.Y+b.Y)/2,0)); segment.Transform=transform; core.Children.Add(segment);
        }
        var outer=SceneGeometry.Torus(2.28,.035,SceneGeometry.Material(Color.FromRgb(101,232,205),true)); core.Children.Add(outer);
        var ring=SceneGeometry.Torus(2.52,.018,SceneGeometry.Material(Color.FromRgb(184,162,245),true)); ring.Transform=new RotateTransform3D(orbit); scene.Children.Add(ring);
        var ripple=SceneGeometry.Torus(2.3,.012,new EmissiveMaterial(waveBrush)); ripple.Transform=wave; scene.Children.Add(ripple);
        var transforms=new Transform3DGroup(); transforms.Children.Add(beat); transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new(1,0,0),-12))); transforms.Children.Add(new RotateTransform3D(yaw)); core.Transform=transforms; scene.Children.Add(core);
        viewport.Children.Add(new ModelVisual3D { Content=scene });
        var brand=new Border { Background=new SolidColorBrush(Color.FromArgb(240,9,12,18)),CornerRadius=new(18),Padding=new(30,13,30,17),HorizontalAlignment=HorizontalAlignment.Center };
        var words=new StackPanel(); words.Children.Add(new TextBlock { Text="PulseDeck",FontFamily=new("Bahnschrift"),FontSize=43,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center });
        words.Children.Add(new TextBlock { Text="SEU SISTEMA. EM PULSO.",FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(101,232,205)),HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,4,0,0) }); brand.Child=words; Grid.SetRow(brand,1); root.Children.Add(brand);
        PreviewKeyDown+=(_,e)=> { if(e.Key is Key.Escape or Key.Enter) { e.Handled=true; Close(); } };
        Loaded+=(_,_)=> { watch.Start(); CompositionTarget.Rendering+=Frame; end.Start(); Focus(); };
        end.Tick+=(_,_)=>Close();
        Closed+=(_,_)=> { end.Stop(); watch.Stop(); CompositionTarget.Rendering-=Frame; completion.TrySetResult(); };
    }
    void Frame(object? sender,EventArgs e)
    {
        double t=watch.Elapsed.TotalSeconds; if(t-lastFrame<1.0/30) return; lastFrame=t;
        double phase=t%1.25;
        double pulse=.09*Math.Exp(-Math.Pow((phase-.20)/.09,2))+.045*Math.Exp(-Math.Pow((phase-.43)/.12,2));
        beat.ScaleX=beat.ScaleY=beat.ScaleZ=1+pulse;
        yaw.Angle=-24+20*Math.Sin(t*1.3); orbit.Angle=t*22;
        double travel=(t%1.25)/1.25;
        wave.ScaleX=wave.ScaleY=wave.ScaleZ=1+travel*.6; waveBrush.Color=Color.FromArgb((byte)(90*(1-travel)),101,232,205);
        Opacity=Math.Clamp(Math.Min(t/.22,(3.2-t)/.3),0,1);
    }
    public void SaveFrame(string path)
    {
        UpdateLayout(); var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file=File.Create(path); encoder.Save(file);
    }
}
