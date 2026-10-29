using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using PulseDeck.Services;

namespace PulseDeck;
public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "PulseDeck â€¢ falha"); args.Handled = true; };
        if (e.Args.Contains("--diagnose") || e.Args.Contains("--self-test"))
        {
            int exit = 0;
            try
            {
                using var gpu = new AdlxService(); gpu.Initialize();
                using var board = new BoardService(); board.Initialize();
                using var cpuTemperature = new CpuTemperatureService(); cpuTemperature.Initialize();
                var metrics = new WindowsMetrics().Read();
                var report = new {
                    At = DateTimeOffset.Now, Version = "0.5.2", Mode = "Read-only: no clocks/fans changed",
                    Cpu = WindowsMetrics.Query("SELECT Name,MaxClockSpeed,CurrentClockSpeed FROM Win32_Processor"),
                    Board = WindowsMetrics.Query("SELECT Manufacturer,Product,Version FROM Win32_BaseBoard"),
                    WindowsFans = WindowsMetrics.Query("SELECT Name,DeviceID,DesiredSpeed FROM Win32_Fan"),
                    Gpu = new { gpu.Name, gpu.Status, gpu.DriverVersion, gpu.VramMb, gpu.FanSupported, gpu.FanSpeedRange, gpu.FanTemperatureRange, gpu.ClockSupported, gpu.ClockIsOffset, gpu.ZeroRpmEnabled, gpu.ClockRange, gpu.MaxClock, gpu.AutoOverclockSupported, gpu.IsFactory, Metrics = gpu.ReadMetrics(), Curve = gpu.ReadFanCurve() },
                    BoardControl = new { board.Status, board.DriverPresent, BoardService.IsAdmin, board.Sensors, board.ControlIds },
                    CpuTemperature = new { cpuTemperature.Status, cpuTemperature.AmdStatus, cpuTemperature.SensorName, ValueCelsius = cpuTemperature.LastValue },
                    Memory = new { metrics.RamPercent, metrics.TotalRam, metrics.AvailableRam, metrics.ProcessCount },
                    CpuTuning = "Blocked: public Ryzen Master Monitoring SDK is read-only; no documented write backend integrated."
                };
                string path = Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "hardware-report.json");
                File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                if (e.Args.Contains("--self-test")) SelfTests.Run();
            }
            catch (Exception ex) { exit = 1; File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostic-error.txt"), ex.ToString()); }
            Shutdown(exit); return;
        }
        if(e.Args.Contains("--overlay-preview"))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try
            {
                using var gpu=new AdlxService(); gpu.Initialize();
                using var cpu=new CpuTemperatureService(); cpu.Initialize();
                var overlayPreview=new OverlayWindow { ShowCpuTemperature=true };
                overlayPreview.SetTransparency(AppPreferences.Current.OverlayOpacity);
                overlayPreview.SetSize(AppPreferences.Current.OverlaySize);
                overlayPreview.Update(new WindowsMetrics().Read(),gpu.ReadMetrics(),cpu.LastValue,new FpsReading(null,"Prévia sem captura de jogo",0));
                overlayPreview.Show(); await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Render);
                overlayPreview.SavePreview(Argument(e.Args,"--output")??Path.Combine(AppContext.BaseDirectory,"preview-overlay.png"));
                overlayPreview.Close(); Shutdown(0); return;
            }
            catch(Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"overlay-preview-error.txt"),ex.ToString()); Shutdown(1); return; }
        }
        bool testing = e.Args.Contains("--ui-test") || e.Args.Contains("--preview") || e.Args.Contains("--tray-test");
        if (e.Args.Contains("--intro-test") || (!testing && !e.Args.Contains("--no-intro") && AppPreferences.Current.IntroEnabled && AppPreferences.Current.MotionEnabled && SystemParameters.ClientAreaAnimation))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var intro = new IntroWindow(); intro.Show();
            if(e.Args.Contains("--intro-test"))
            {
                await System.Threading.Tasks.Task.Delay(1400);
                if(intro.IsVisible) intro.SaveFrame(Argument(e.Args,"--output") ?? Path.Combine(AppContext.BaseDirectory,"preview-intro.png"));
            }
            await intro.Completion;
            if(e.Args.Contains("--intro-test")) { Shutdown(0); return; }
        }
        var window = new MainWindow(); MainWindow = window; ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Show();
        if (e.Args.Contains("--tray-test"))
        {
            await window.Ready;
            window.Close();
            bool hidden = !window.IsVisible && System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle == IntPtr.Zero;
            window.ShowFromTrayForTest();
            bool reopened = window.IsVisible;
            File.WriteAllText(Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "tray-test.txt"), $"Hidden={hidden}; Reopened={reopened}");
            window.ExitApplication(hidden && reopened ? 0 : 1);
            return;
        }
        if(e.Args.Contains("--demo"))
        {
            try { await window.Ready; await window.RunPresentationDemo(); }
            catch(Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"demo-error.txt"),ex.ToString()); }
            window.ExitApplication(); return;
        }
        if (e.Args.Contains("--ui-test"))
        {
            int exit = 0;
            try
            {
                await window.Ready;
                var result = await window.RunUiSmokeTest();
                File.WriteAllText(Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "ui-test-results.txt"), result);
            }
            catch (Exception ex) { exit = 1; File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-test-error.txt"), ex.ToString()); }
            window.ExitApplication(exit); return;
        }
        if (e.Args.Contains("--preview"))
        {
            await window.Ready;
            if (int.TryParse(Argument(e.Args, "--width"), out var width)) window.Width = Math.Max(window.MinWidth, width);
            if (int.TryParse(Argument(e.Args, "--height"), out var height)) window.Height = Math.Max(window.MinHeight, height);
            int seconds = int.TryParse(Argument(e.Args, "--seconds"), out var delay) ? Math.Clamp(delay, 1, 30) : 1;
            int tab = int.TryParse(Argument(e.Args, "--tab"), out var n) ? n : 0;
            window.PreparePreview(tab);
            await System.Threading.Tasks.Task.Delay(seconds * 1000);
            window.SavePreview(Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "preview.png"), tab);
            window.ExitApplication();
        }
    }
    static string? Argument(string[] args, string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
}
