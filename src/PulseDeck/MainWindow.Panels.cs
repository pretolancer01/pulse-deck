using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PulseDeck.Services;

namespace PulseDeck;
public partial class MainWindow
{
    sealed record FpsTargetOption(string Name,int Id,double? GpuPercent)
    { public override string ToString()=>$"{Name}  ·  PID {Id}  ·  GPU {(GpuPercent.HasValue?$"{GpuPercent:N0}%":"—")}"; }
    OverlayWindow? overlay;
    Metrics? latestMetrics;
    double? lastCpuTemperature;
    CancellationTokenSource? fpsCancellation;
    FpsReading fpsReading=new(null,"Selecione um jogo para medir FPS.",0);
    int fpsTargetPid;
    void OptPreset_Changed(object sender,SelectionChangedEventArgs e) => UpdateOptimizationPreview();
    void UpdateOptimizationPreview()
    {
        if(OptTitle==null || OptPresetList.SelectedIndex<0) return;
        var preset=WindowsOptimization.Presets[OptPresetList.SelectedIndex];
        OptTitle.Text=preset.Name; OptDescription.Text=preset.Description; OptChanges.Text=WindowsOptimization.Changes(preset);
        OptHardware.Text=$"Identificação local: {cpuName}  /  {gpu.Name}. Estas opções do Windows são por conta e não alteram clocks nem frequências de hardware.";
        OptApplyButton.Content=preset.WindowsDefault?"Revisar e aplicar padrão":"Revisar e aplicar";
        OptRestoreButton.IsEnabled=WindowsOptimization.HasBackup;
    }
    void OptApply_Click(object sender,RoutedEventArgs e)
    {
        if(OptPresetList.SelectedIndex<0) return;
        var preset=WindowsOptimization.Presets[OptPresetList.SelectedIndex];
        if(!Confirm($"Aplicar {preset.Name} nesta conta do Windows?\n\n{WindowsOptimization.Changes(preset)}\n\nAs configurações anteriores serão guardadas para restauração.","Confirmar otimização")) return;
        try { WindowsOptimization.Apply(preset); OptStatus.Text=$"{preset.Name} aplicado. Você pode escolher Padrão do Windows ou restaurar seus ajustes guardados."; OptRestoreButton.IsEnabled=true; }
        catch(Exception ex) { OptStatus.Text="Não foi possível aplicar: "+ex.Message; }
    }
    void OptDefault_Click(object sender,RoutedEventArgs e)
    {
        OptPresetList.SelectedIndex=0;
        OptApply_Click(sender,e);
    }
    void OptRestore_Click(object sender,RoutedEventArgs e)
    {
        if(!WindowsOptimization.HasBackup || !Confirm("Restaurar as configurações de Windows guardadas antes do primeiro preset?","Restaurar preferências")) return;
        try { WindowsOptimization.RestoreOriginal(); OptStatus.Text="Preferências anteriores restauradas."; OptRestoreButton.IsEnabled=false; }
        catch(Exception ex) { OptStatus.Text="Restauração incompleta: "+ex.Message; }
    }
    void ShowOverlay_Click(object sender,RoutedEventArgs e)
    {
        if(overlay is { IsVisible:true }) { overlay.Activate(); return; }
        overlay=new OverlayWindow(); overlay.Closed+=(_,_)=> { fpsCancellation?.Cancel(); overlay=null; OverlayStatus.Text="Overlay fechado."; };
        SyncOverlayOptions(); overlay.Show(); OverlayStatus.Text="Overlay ativo. Arraste o título do HUD para movê-lo.";
        fpsCancellation=new CancellationTokenSource(); _=MonitorFps(fpsCancellation.Token);
    }
    void HideOverlay_Click(object sender,RoutedEventArgs e) { fpsCancellation?.Cancel(); overlay?.Close(); overlay=null; OverlayStatus.Text="Overlay oculto."; }
    void OverlayOptions_Changed(object sender,RoutedEventArgs e) => SyncOverlayOptions();
    void SyncOverlayOptions()
    {
        if(overlay==null || OverlayFps==null) return;
        overlay.ShowFps=OverlayFps.IsChecked==true; overlay.ShowGpu=OverlayGpu.IsChecked==true;
        overlay.ShowGpuTemperature=OverlayGpuTemp.IsChecked==true; overlay.ShowCpu=OverlayCpu.IsChecked==true;
        overlay.ShowCpuTemperature=OverlayCpuTemp.IsChecked==true; overlay.ShowRam=OverlayRam.IsChecked==true;
        overlay.SetTransparency(OverlayOpacitySlider.Value/100);
        overlay.SetSize(OverlaySizeSlider.Value/100);
        RefreshOverlay();
    }
    void OverlayOpacity_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(OverlayOpacityValue==null) return;
        OverlayOpacityValue.Text=$"{OverlayOpacitySlider.Value:N0}%";
        overlay?.SetTransparency(OverlayOpacitySlider.Value/100);
        if(IsLoaded && !Environment.GetCommandLineArgs().Contains("--ui-test")) { AppPreferences.Current.OverlayOpacity=OverlayOpacitySlider.Value/100; AppPreferences.Current.Save(); }
    }
    void OverlaySize_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(OverlaySizeValue==null) return;
        OverlaySizeValue.Text=$"{OverlaySizeSlider.Value:N0}%";
        overlay?.SetSize(OverlaySizeSlider.Value/100);
        if(IsLoaded && !Environment.GetCommandLineArgs().Contains("--ui-test")) { AppPreferences.Current.OverlaySize=OverlaySizeSlider.Value/100; AppPreferences.Current.Save(); }
    }
    void FpsTarget_Changed(object sender,SelectionChangedEventArgs e)
    {
        fpsTargetPid=(FpsTargetPicker?.SelectedItem as FpsTargetOption)?.Id??0;
        fpsCancellation?.Cancel(); fpsCancellation?.Dispose(); fpsCancellation=null;
        fpsReading=new(null,fpsTargetPid>0?"Aguardando quadros…":"Selecione um processo.",0); FpsStatus.Text=fpsReading.Status; RefreshOverlay();
        if(overlay is { IsVisible:true }) { fpsCancellation=new CancellationTokenSource(); _=MonitorFps(fpsCancellation.Token); }
    }
    void UpdateFpsTargets()
    {
        if(FpsTargetPicker==null) return;
        int old=fpsTargetPid;
        var targets=processes.Where(p=>p.GpuPercent.HasValue && p.Name is not "PulseDeck" and not "dwm" && p.Name is not "System")
            .OrderByDescending(p=>p.GpuPercent).Take(30).Select(p=>new FpsTargetOption(p.Name,p.Id,p.GpuPercent)).ToList();
        if(targets.Count==0) return;
        if(old>0 && targets.Any(p=>p.Id==old)) return;
        if(FpsTargetPicker.ItemsSource is System.Collections.Generic.IEnumerable<FpsTargetOption> prior && prior.Select(p=>p.Id).SequenceEqual(targets.Select(p=>p.Id))) return;
        FpsTargetPicker.ItemsSource=targets;
        FpsTargetPicker.SelectedItem=targets.FirstOrDefault(p=>p.Id==old)??targets.First();
    }
    async Task MonitorFps(CancellationToken token)
    {
        while(!token.IsCancellationRequested && overlay is { IsVisible:true })
        {
            int pid=fpsTargetPid;
            if(OverlayFps.IsChecked==true && pid>0)
            {
                fpsReading=await FpsService.CaptureAsync(pid,token);
                if(token.IsCancellationRequested) break;
                RefreshOverlay(); FpsStatus.Text=fpsReading.Status;
            }
            try { await Task.Delay(1200,token); } catch(OperationCanceledException) { break; }
        }
    }
    void RefreshOverlay()
    {
        double? temp=lastCpuTemperature ?? board.Sensors.FirstOrDefault(s=>s.Type=="Temperature" && s.Name.Contains("CPU",StringComparison.OrdinalIgnoreCase))?.Value;
        overlay?.Update(latestMetrics,lastGpu,temp,fpsReading);
    }
}
