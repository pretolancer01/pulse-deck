using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PulseDeck.Services;

namespace PulseDeck;
public partial class MainWindow : Window
{
    readonly AdlxService gpu = new();
    readonly BoardService board = new();
    readonly CpuTemperatureService cpuTemperature = new();
    readonly WindowsMetrics metrics = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly TaskCompletionSource ready = new();
    ConstellationField? sidebarField;
    public Task Ready => ready.Task;
    bool paused, ticking, closing, deleting, gpuChanged, checking;
    bool exitRequested;
    System.Windows.Forms.NotifyIcon? trayIcon;
    CancellationTokenSource? storageCancellation;
    StorageResult? storageResult;
    sealed record StorageChartItem(StorageEntry Entry, string Name, string SizeLabel, string LocationLabel, double BarPercent, bool CanOpen);
    List<ProcessRow> processes = new();
    List<TempRow> tempRows = new();
    List<FanPoint>? originalCurve;
    int? originalClock;
    bool? originalFactory;
    bool? originalZeroRpm, referenceZeroRpm;
    List<FanPoint>? fanReference;
    FanProfile selectedFanProfile = FanProfile.Normal;
    IInputElement? focusBeforeCommands;
    sealed record NavigationEntry(string Title, int Tab);
    readonly NavigationEntry[] navigationEntries = [new("Visão geral", 0), new("Ventoinhas da GPU", 1), new("Overclock e ajustes da GPU", 2), new("Arquivos temporários", 3), new("Armazenamento", 4), new("Otimização do Windows", 5), new("Overlay", 6), new("Sistema e diagnóstico", 7)];
    CancellationTokenSource? scanCancellation;
    string diagnostic = "", cpuName = "Processador não identificado", boardName = "Placa-mãe não identificada";
    GpuMetrics lastGpu = new(null, null, null, null, null, null);
    public MainWindow()
    {
        InitializeComponent();
        InitializeTray();
        InitializeStorageDrives();
        SourceInitialized += (_, _) => NativeChrome.Attach(this);
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        ConfigureKeyboard();
        MotionToggle.IsChecked = AppPreferences.Current.MotionEnabled && SystemParameters.ClientAreaAnimation;
        IntroToggle.IsChecked = AppPreferences.Current.IntroEnabled;
        OverlayOpacitySlider.Value = Math.Clamp(AppPreferences.Current.OverlayOpacity,.55,1)*100;
        OverlaySizeSlider.Value = Math.Clamp(AppPreferences.Current.OverlaySize,.65,1.2)*100;
        ProcessesGrid.RequestBringIntoView += (_, e) => { if (!ProcessesGrid.IsKeyboardFocusWithin) e.Handled = true; };
        Loaded += async (_, _) => { sidebarField = Navigation.Template.FindName("SidebarField", Navigation) as ConstellationField; await Initialize(); };
        timer.Tick += async (_, _) => await Tick();
        Closing += OnClosing;
        Closed += (_, _) => { trayIcon?.Dispose(); overlay?.Close(); gpu.Dispose(); cpuTemperature.Dispose(); try { board.Dispose(); } catch { } };
    }
    async Task Initialize()
    {
        try
        {
            var hardware = await Task.Run(() => {
                string cpu = "Processador não identificado", mainboard = "Placa-mãe não identificada", clock = "";
                try { var row = WindowsMetrics.Query("SELECT Name,MaxClockSpeed FROM Win32_Processor").FirstOrDefault(); if (row != null) { cpu = row["Name"]; clock = "Frequência declarada pelo Windows: " + row["MaxClockSpeed"] + " MHz. Não é leitura de clock efetivo."; } } catch { }
                try { var row = WindowsMetrics.Query("SELECT Manufacturer,Product FROM Win32_BaseBoard").FirstOrDefault(); if (row != null) mainboard = row["Manufacturer"] + " " + row["Product"]; } catch { }
                return (cpu, mainboard, clock);
            });
            if (closing) return;
            cpuName = hardware.cpu; boardName = hardware.mainboard;
            HardwareLabel.Text = cpuName; OcCpuName.Text = cpuName; CpuClockLabel.Text = hardware.clock; BoardNameLabel.Text = boardName;
            gpu.Initialize();
            // Board initialization requires an already-installed signed driver and elevation.
            await Task.Run(board.Initialize);
            await Task.Run(cpuTemperature.Initialize);
            if (closing) return;
            UpdateSupport(); ReadCurve(); UpdateOptimizationPreview();
            await Tick();
            timer.Start();
            Log("Diagnóstico inicial concluído. Nenhum ajuste aplicado.");
            UpdateMotion();
        }
        catch (Exception ex) { StatusLabel.Text = "Falha na inicialização: " + ex.Message; Log(ex.Message); }
        finally { ready.TrySetResult(); }
    }
    void UpdateSupport()
    {
        FanGpuName.Text = OcGpuName.Text = HeroGpuName.Text = gpu.Name;
        FanSupportLabel.Text = gpu.FanSupported ? "Controle direto pelo driver AMD. Perfis com revisão antes de aplicar." : "O driver não expôs uma interface de curva compatível.";
        FanLimitsLabel.Text = gpu.FanSupported ? $"Ventilação: {gpu.FanSpeedRange}% • temperatura: {gpu.FanTemperatureRange} °C" : "Sem limites verificados: edição indisponível.";
        ApplyFanButton.IsEnabled = gpu.FanSupported;
        foreach (var preset in FanPresets.Children.OfType<RadioButton>()) preset.IsEnabled = gpu.FanSupported;
        ClockInput.IsEnabled = ApplyClockButton.IsEnabled = gpu.ClockSupported;
        ResetGpuButton.IsEnabled = gpu.FactoryResetSupported;
        if (gpu.MaxClock is int clock) ClockInput.Text = clock.ToString();
        ClockFieldLabel.Text = gpu.ClockIsOffset ? "Deslocamento sobre a frequência base (MHz)" : "Limite máximo de frequência (MHz)";
        ClockRangeLabel.Text = gpu.ClockSupported ? $"Driver: {gpu.ClockRange} MHz • ajuste atual: {gpu.MaxClock} MHz" : "A interface de frequência compatível não foi exposta pelo driver.";
        OcGpuSupportLabel.Text = gpu.ClockSupported ? (gpu.ClockIsOffset ? "Nesta GPU o driver usa offset: 0 MHz mantém a referência; +50 MHz solicita um acréscimo de 50 MHz. Não é o clock efetivo." : "Limite de frequência disponível por interface oficial AMD.") : $"Ajuste manual compatível indisponível. Auto-overclock reportado pelo driver: {(gpu.AutoOverclockSupported ? "sim (ainda não integrado)" : "não")}.";
        BoardStatusLabel.Text = board.Status;
        BoardSensorsGrid.ItemsSource = board.Sensors;
        BoardChannelPicker.ItemsSource = board.ControlIds;
        if (board.ControlIds.Count > 0) BoardChannelPicker.SelectedIndex = 0;
        ApplyBoardButton.IsEnabled = board.ControlIds.Count > 0;
        BoardActions.Visibility = board.ControlIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BoardControls.Visibility = board.Sensors.Any(s => s.Type is "Fan" or "Control") ? Visibility.Visible : Visibility.Collapsed;
        diagnostic = $"PulseDeck 0.5.3 • C# / WPF / .NET 10 x64\nVerificação: {DateTimeOffset.Now:dd/MM/yyyy HH:mm:ss zzz}\n\nCPU: {cpuName}\nPlaca-mãe: {boardName}\n\nGPU: {gpu.Name}\n{gpu.DriverVersion}\n{gpu.Status}\nVRAM total: {(gpu.VramMb > 0 ? gpu.VramMb + " MB (ADLX)" : "indisponível")}\nCurva GPU: {gpu.FanSupported}\nClock manual compatível: {gpu.ClockSupported}\nAuto-overclock reportado: {gpu.AutoOverclockSupported}\n\nCPU/gabinete: {board.Status}\nDriver PawnIO instalado: {board.DriverPresent}\nSessão elevada: {BoardService.IsAdmin}\nCanais expostos: {board.ControlIds.Count}\n\nOverclock CPU: sem backend documentado de escrita integrado. SDK público Ryzen Master Monitoring é de leitura.\n\nO diagnóstico não altera clocks nem ventilação.";
        DiagnosticLabel.Text = diagnostic;
    }
    async Task Tick()
    {
        if (ticking || closing || paused || checking || !IsVisible) return;
        ticking = true;
        try
        {
            var data = await Task.Run(metrics.Read); latestMetrics=data;
            lastCpuTemperature = await Task.Run(cpuTemperature.Read);
            CpuTempStatus.Text = cpuTemperature.Status;
            if (closing) return;
            try { lastGpu = gpu.ReadMetrics(); }
            catch { lastGpu = new(null, null, null, null, null, null); }
            CpuValue.Text = data.Cpu.HasValue ? $"{data.Cpu:N0}%" : "—";
            CpuMetricDial.Set(data.Cpu,data.Cpu.HasValue?$"{data.Cpu:N0}%":"—","uso da CPU",VisualTheme.Color("CyanColor"));
            RamValue.Text = $"{data.RamPercent:N0}%";
            RamMetricDial.Set(data.RamPercent,$"{data.RamPercent:N0}%","uso da RAM",VisualTheme.Color("PurpleColor"));
            RamDetail.Text = $"{(data.TotalRam - data.AvailableRam) / 1073741824.0:N1} / {data.TotalRam / 1073741824.0:N1} GB";
            AnimateBar(CpuBar, data.Cpu ?? 0); AnimateBar(RamBar, data.RamPercent);
            GpuTempValue.Text = lastGpu.Temperature.HasValue ? $"{lastGpu.Temperature:N0}°C" : "—";
            GpuTempMetricDial.Set(lastGpu.Temperature.HasValue?Math.Clamp(lastGpu.Temperature.Value/85*100,0,100):null,lastGpu.Temperature.HasValue?$"{lastGpu.Temperature:N0}°C":"—","de 85 °C",VisualTheme.Color("AmberColor"));
            GpuClockValue.Text = lastGpu.Clock.HasValue ? $"{lastGpu.Clock:N0} MHz • clock atual" : "Clock indisponível";
            GpuLoadValue.Text = lastGpu.Usage.HasValue ? $"{lastGpu.Usage:N0}% de uso" : "Uso indisponível";
            ProcessValue.Text = data.ProcessCount.ToString();
            processes = data.Processes; FilterProcesses(); UpdateApplications(); UpdateFpsTargets();
            HistoryChart.Push(data.Cpu, data.RamPercent, lastGpu.Usage);
            GpuFanVisual.Rpm = lastGpu.FanRpm; GpuFanVisual.InvalidateVisual(); GpuDial.SetUsage(lastGpu.Usage);
            FanRpmLabel.Text = lastGpu.FanRpm.HasValue ? $"{lastGpu.FanRpm:N0} RPM" : "RPM indisponível";
            RefreshOverlay();
            OcTelemetryLabel.Text = $"Clock: {Number(lastGpu.Clock, "MHz")}  /  GPU: {Number(lastGpu.Temperature, "°C")}  /  hotspot: {Number(lastGpu.Hotspot, "°C")}  /  VRAM: {Number(lastGpu.VramMb, "MB")}";
            AdviceLabel.Text = data.RamPercent >= 85 ? "Pressão de memória elevada. Examine os aplicativos de maior consumo." : $"{data.AvailableRam / 1073741824.0:N1} GB disponíveis. Acompanhe tendências antes de alterar o sistema.";
            if (board.Sensors.Count > 0) { await Task.Run(board.Read); BoardSensorsGrid.ItemsSource = null; BoardSensorsGrid.ItemsSource = board.Sensors; }
            // Session guard: if telemetry is lost or temperatures rise, request restoration.
            if (gpuChanged && (!lastGpu.Temperature.HasValue || lastGpu.Temperature >= 85 || lastGpu.Hotspot >= 100))
            { UndoGpu(); Log("Proteção térmica: restauração solicitada por temperatura elevada ou telemetria indisponível."); }
            if (board.ControlIds.Count > 0 && (!board.Sensors.Any(s => s.Category is "Placa-mãe" or "Controlador" or "CPU" && s.Type == "Temperature" && s.Value is > 0 and < 85) || board.Sensors.Any(s => s.Category is "Placa-mãe" or "Controlador" or "CPU" && s.Type == "Temperature" && s.Value >= 85))) { board.Restore(); boardHasChanges = false; }
            StatusLabel.Text = $"Última leitura {DateTime.Now:HH:mm:ss} • atualização a cada 2 s";
            LiveState.Text = "Monitorando";
        }
        catch (Exception ex) { StatusLabel.Text = "Falha na leitura: " + ex.Message; LiveState.Text = "Leitura indisponível"; }
        finally { ticking = false; }
    }
    static string Number(double? value, string unit) => value.HasValue ? $"{value:N0} {unit}" : "indisponível";
    void AnimateBar(System.Windows.Controls.ProgressBar bar, double value)
    {
        if (MotionAllowed) bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null); bar.Value = value; }
    }
    bool MotionAllowed => MotionToggle?.IsChecked == true && SystemParameters.ClientAreaAnimation && !paused;
    void UpdateMotion()
    {
        if (HistoryChart == null || GpuFanVisual == null) return;
        HistoryChart.MotionEnabled = GpuFanVisual.MotionEnabled = MotionAllowed;
        HistoryChart.Paused = GpuFanVisual.Paused = paused;
        if (Backdrop != null) { Backdrop.MotionEnabled = MotionAllowed; Backdrop.Paused = paused; Backdrop.InvalidateVisual(); }
        if (GpuDial != null) { GpuDial.MotionEnabled = MotionAllowed; GpuDial.Paused = paused; GpuDial.InvalidateVisual(); }
        foreach(var dial in new[]{CpuMetricDial,RamMetricDial,GpuTempMetricDial}) { dial.MotionEnabled=MotionAllowed; dial.Paused=paused; dial.InvalidateVisual(); }
        if(sidebarField!=null) { sidebarField.MotionEnabled=MotionAllowed; sidebarField.Paused=paused; sidebarField.InvalidateVisual(); }
        if (AppsChart != null) AppsChart.MotionEnabled = MotionAllowed;
        System.Windows.Application.Current.Properties["MotionAllowed"] = MotionAllowed;
        ActivityDot.BeginAnimation(OpacityProperty, null);
        if (MotionAllowed) ActivityDot.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
    void Motion_Changed(object sender, RoutedEventArgs e)
    {
        UpdateMotion();
        if (IsLoaded && !Environment.GetCommandLineArgs().Contains("--ui-test")) { AppPreferences.Current.MotionEnabled = MotionToggle.IsChecked == true; AppPreferences.Current.Save(); }
    }
    void Navigation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Navigation || !MotionAllowed || Navigation.SelectedContent is not FrameworkElement panel) return;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(.55, 1, TimeSpan.FromMilliseconds(200)));
    }
    void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (gpuChanged || boardHasChanges) { MessageBox.Show(this, "Desfaça os ajustes ativos antes de pausar a supervisão térmica.", "Monitoramento ativo"); return; }
        paused = !paused; PauseButton.Content = paused ? "Retomar" : "Pausar"; LiveState.Text = paused ? "Pausado" : "Monitorando"; UpdateMotion();
        if (!paused) _ = Tick();
    }
    void FilterProcesses()
    {
        if (ProcessesGrid == null) return;
        int? selected = (ProcessesGrid.SelectedItem as ProcessRow)?.Id;
        string filter = ProcessSearch.Text.Trim();
        var rows = processes.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.Id.ToString().Contains(filter)).ToList();
        ProcessesGrid.ItemsSource = rows;
        if (selected.HasValue) ProcessesGrid.SelectedItem = rows.FirstOrDefault(p => p.Id == selected);
    }
    void ProcessSearch_Changed(object sender, TextChangedEventArgs e) => FilterProcesses();
    void Process_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessDetail != null) ProcessDetail.Text = ProcessesGrid.SelectedItem is ProcessRow row ? $"{row.Name} • PID {row.Id} • CPU {row.Cpu} • RAM {row.Ram}" : "Selecione uma linha para inspecionar.";
    }
    void ReadCurve()
    {
        try
        {
            var curve = gpu.ReadFanCurve(); FanCurveGrid.ItemsSource = curve; FanCurvePreview.SetPoints(curve);
            foreach (var button in FanPresets.Children.OfType<RadioButton>()) button.IsChecked = false;
            ApplyFanButton.IsEnabled = false;
            if (fanReference == null && curve.Count > 0)
            {
                fanReference = curve.Select(p => p.Copy()).ToList(); referenceZeroRpm = gpu.ZeroRpmEnabled;
                NormalProfile.IsChecked = true;
            }
            FanProfileSummary.Text = curve.Count > 0 ? "Curva aplicada lida do driver. Selecione um perfil para preparar uma alteração." : "Curva indisponível.";
        }
        catch (Exception ex) { FanSupportLabel.Text = ex.Message; ApplyFanButton.IsEnabled = false; }
    }
    void ReadCurve_Click(object sender, RoutedEventArgs e) => ReadCurve();
    bool Confirm(string message, string title) => ConfirmationDialog.Ask(this, message, title);
    void RequireThermalTelemetry()
    {
        if (paused) throw new InvalidOperationException("Retome as métricas antes de ajustar o hardware.");
        lastGpu = gpu.ReadMetrics();
        if (!lastGpu.Temperature.HasValue || lastGpu.Temperature >= 85 || lastGpu.Hotspot >= 100) throw new InvalidOperationException("É necessária telemetria térmica válida, abaixo do limite de supervisão.");
    }
    void RememberGpu()
    {
        if (gpuChanged) return;
        originalFactory = gpu.IsFactory;
        originalClock = gpu.MaxClock;
        originalZeroRpm = gpu.ZeroRpmEnabled;
        originalCurve = gpu.FanSupported ? gpu.ReadFanCurve().Select(p => p.Copy()).ToList() : null;
    }
    void ApplyFan_Click(object sender, RoutedEventArgs e)
    {
        if (!FanPresets.Children.OfType<RadioButton>().Any(button => button.IsChecked == true)) return;
        try
        {
            if (!FanCurveGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !FanCurveGrid.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("Corrija os campos inválidos antes de aplicar.");
            var points = ((IEnumerable<FanPoint>)FanCurveGrid.ItemsSource).Select(p => p.Copy()).ToList();
            AdlxService.ValidateCurve(points, gpu.FanSpeedRange, gpu.FanTemperatureRange);
            string details = string.Join("\n", points.Select(p => $"{p.Temperature} °C → {p.Speed}%"));
            bool? zeroRpm = selectedFanProfile == FanProfile.Normal ? referenceZeroRpm : gpu.ZeroRpmEnabled.HasValue ? false : null;
            string zeroMessage = zeroRpm.HasValue ? (zeroRpm.Value ? "Modo Zero RPM: habilitado, como na curva inicial." : "Modo Zero RPM: desativado para este perfil.") : "";
            if (!Confirm($"Aplicar o perfil {FanProfiles.Name(selectedFanProfile)} em {gpu.Name}?\n\n{details}\n\n{zeroMessage}\nA ventilação muda imediatamente. O driver será consultado para verificar o resultado.", "Confirmar perfil da GPU")) return;
            RequireThermalTelemetry(); RememberGpu(); gpuChanged = true; UndoGpuButton.IsEnabled = true;
            gpu.ApplyFanCurve(points); if (zeroRpm.HasValue) gpu.SetZeroRpm(zeroRpm.Value);
            UndoClockButton.IsEnabled = true; ReadCurve(); FanProfileSummary.Text = $"{FanProfiles.Name(selectedFanProfile)} aplicado e relido.";
            Log($"Perfil {FanProfiles.Name(selectedFanProfile)} aplicado e confirmado por releitura ADLX.");
        }
        catch (Exception ex) { HardwareFailure(ex); }
    }
    void ApplyClock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(ClockInput.Text, out int value) || !gpu.ClockRange.Contains(value)) throw new ArgumentException("Informe uma frequência dentro do intervalo e passo do driver.");
            string kind = gpu.ClockIsOffset ? "Deslocamento sobre a base" : "Limite máximo";
            if (!Confirm($"GPU: {gpu.Name}\n{kind} atual: {gpu.MaxClock} MHz\nNovo ajuste: {value} MHz\n\nPode haver instabilidade e perda de trabalho. Não haverá teste automático de estabilidade nem garantia de ganho. Feche outros utilitários de ajuste para evitar comandos concorrentes. Aplicar?", "Confirmar ajuste de frequência")) return;
            RequireThermalTelemetry(); RememberGpu(); gpuChanged = true; UndoGpuButton.IsEnabled = true;
            gpu.SetMaxClock(value); Log($"Limite de frequência {value} MHz confirmado por releitura ADLX."); UpdateSupport();
            UndoClockButton.IsEnabled = true;
        }
        catch (Exception ex) { HardwareFailure(ex); }
    }
    void HardwareFailure(Exception ex)
    {
        Log("Falha: " + ex.Message);
        if (gpuChanged) { try { UndoGpu(); } catch (Exception restore) { Log("Restauração falhou: " + restore.Message); } }
        MessageBox.Show(this, ex.Message, "Ajuste não confirmado", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    void UndoGpu()
    {
        if (!gpuChanged) return;
        if (originalFactory == true) gpu.ResetFactory();
        else
        {
            if (originalClock.HasValue && gpu.ClockSupported) gpu.SetMaxClock(originalClock.Value);
            if (originalCurve is { Count: > 0 }) gpu.ApplyFanCurve(originalCurve);
            if (originalZeroRpm.HasValue) gpu.SetZeroRpm(originalZeroRpm.Value);
        }
        gpuChanged = false; UndoGpuButton.IsEnabled = UndoClockButton.IsEnabled = false; ReadCurve(); UpdateSupport(); Log("Ajustes anteriores da sessão restaurados e relidos.");
    }
    void UndoGpu_Click(object sender, RoutedEventArgs e) { try { UndoGpu(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Falha na restauração"); } }
    void ResetGpu_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Restaurar TODOS os ajustes da GPU ao padrão de fábrica? Isso pode substituir curvas, clocks e ajustes feitos por outros aplicativos.", "Restaurar GPU")) return;
        try { gpu.ResetFactory(); gpuChanged = false; UndoGpuButton.IsEnabled = UndoClockButton.IsEnabled = false; UpdateSupport(); ReadCurve(); Log("Restauração de fábrica confirmada pelo driver."); }
        catch (Exception ex) { HardwareFailure(ex); }
    }
    bool boardHasChanges;
    void ApplyBoard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (paused) throw new InvalidOperationException("Retome o monitoramento antes de ajustar.");
            string id = BoardChannelPicker.SelectedItem as string ?? throw new ArgumentException("Selecione um canal verificado.");
            if (!int.TryParse(BoardDutyInput.Text, out int duty) || duty is < 40 or > 100) throw new ArgumentException("Informe entre 40 e 100%.");
            if (!Confirm($"Canal reportado: {id}\nComando: {duty}%\n\nConfirme que identificou a ligação física deste canal. RPM de outros sensores não comprova a ligação. Aplicar e acompanhar?", "Confirmar canal da placa-mãe")) return;
            boardHasChanges = true; board.SetDuty(id, duty); BoardSensorsGrid.ItemsSource = board.Sensors; Log($"Canal {id}: comando de {duty}% relido no controlador. Confirme a resposta física.");
        }
        catch (Exception ex) { try { board.Restore(); boardHasChanges = false; } catch { } MessageBox.Show(this, ex.Message, "Controle da placa-mãe"); }
    }
    void RestoreBoard_Click(object sender, RoutedEventArgs e) { try { board.Restore(); boardHasChanges = false; Log("Retorno dos canais alterados ao modo automático solicitado."); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }
    async void Recheck_Click(object sender, RoutedEventArgs e)
    {
        if (ticking || checking || gpuChanged || boardHasChanges) { MessageBox.Show(this, "Aguarde a leitura e desfaça ajustes ativos antes de reverificar."); return; }
        checking = true;
        try { gpu.Dispose(); gpu.Initialize(); await Task.Run(() => { board.Dispose(); board.Initialize(); }); if (!closing) { fanReference = null; UpdateSupport(); ReadCurve(); Log("Suporte reverificado sem alterar hardware."); } }
        catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        finally { checking = false; }
    }
    void ExportDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "PulseDeck-diagnostico.txt", Filter = "Texto|*.txt" };
        if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, diagnostic, Encoding.UTF8); StatusLabel.Text = "Diagnóstico exportado."; }
    }
    async Task Scan()
    {
        if (scanCancellation != null || deleting) return;
        var cancellation = new CancellationTokenSource(); scanCancellation = cancellation;
        ScanButton.IsEnabled = false; CancelScanButton.IsEnabled = true; RecycleButton.IsEnabled = false;
        TempStatusLabel.Text = "Analisando em segundo plano. Você pode continuar usando o painel.";
        try
        {
            var result = await Task.Run(() => TempService.Scan(cancellation.Token));
            if (closing) return;
            tempRows = result.Rows; FilterTemp();
            TempStatusLabel.Text += $" • {result.Skipped} local(is)/item(ns) sem acesso";
        }
        catch (OperationCanceledException) { if (!closing) TempStatusLabel.Text = "Análise cancelada. A lista anterior foi preservada."; }
        catch (Exception ex) { if (!closing) TempStatusLabel.Text = "Falha: " + ex.Message; }
        finally { scanCancellation = null; cancellation.Dispose(); if (!closing) { ScanButton.IsEnabled = true; CancelScanButton.IsEnabled = false; UpdateTempSelection(); } }
    }
    async void Scan_Click(object sender, RoutedEventArgs e) => await Scan();
    void CancelScan_Click(object sender, RoutedEventArgs e) => scanCancellation?.Cancel();
    void FilterTemp()
    {
        if (TempGrid == null || AgePicker == null) return;
        int days = AgePicker.SelectedIndex switch { 1 => 1, 2 => 7, 3 => 30, _ => 0 };
        var rows = tempRows.Where(p => days == 0 || p.ModifiedAt < DateTime.Now.AddDays(-days)).ToList();
        TempGrid.ItemsSource = rows;
        TempStatusLabel.Text = $"{rows.Count} exibidos de {tempRows.Count} arquivos • {TempService.FormatSize(rows.Sum(x => x.Bytes))}";
        UpdateTempSelection();
    }
    void TempFilter_Changed(object sender, SelectionChangedEventArgs e) => FilterTemp();
    void SelectAll_Click(object sender, RoutedEventArgs e) { if (scanCancellation == null && !deleting) TempGrid.SelectAll(); }
    void ClearSelection_Click(object sender, RoutedEventArgs e) => TempGrid.UnselectAll();
    void TempSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateTempSelection();
    void UpdateTempSelection()
    {
        if (TempSelectionLabel == null || RecycleButton == null) return;
        var selected = TempGrid.SelectedItems.Cast<TempRow>().ToList();
        TempSelectionLabel.Text = selected.Count == 0 ? "Nenhum arquivo selecionado" : $"{selected.Count} selecionados • {TempService.FormatSize(selected.Sum(x => x.Bytes))}";
        RecycleButton.IsEnabled = selected.Count > 0 && scanCancellation == null && !deleting;
    }
    async void Recycle_Click(object sender, RoutedEventArgs e)
    {
        if (scanCancellation != null || deleting) return;
        var selected = TempGrid.SelectedItems.Cast<TempRow>().ToList();
        if (selected.Count == 0 || !Confirm($"Enviar {selected.Count} arquivos, totalizando {TempService.FormatSize(selected.Sum(x => x.Bytes))}, para a Lixeira?\n\nArquivos em uso ou caminhos alterados serão ignorados.", "Confirmar limpeza")) return;
        deleting = true; RecycleButton.IsEnabled = false; ScanButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(() => TempService.Recycle(selected));
            MessageBox.Show(this, $"{result.Removed} enviados à Lixeira. {result.Failed} não removidos.", "Resultado da limpeza");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Falha na limpeza"); }
        finally { deleting = false; ScanButton.IsEnabled = true; }
        await Scan();
    }
    void Log(string message)
    {
        SessionLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}\n"); SessionLog.ScrollToEnd();
        StatusLabel.Text = message;
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseDeck");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, $"session-{DateTime.Now:yyyy-MM-dd}.log"), $"{DateTimeOffset.Now:O} {message}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested)
        {
            e.Cancel = true;
            Hide();
            trayIcon?.ShowBalloonTip(2000, "PulseDeck", "O PulseDeck continua ativo. Clique duas vezes no ícone para reabrir.", System.Windows.Forms.ToolTipIcon.Info);
            return;
        }
        if (deleting) { e.Cancel = true; MessageBox.Show(this, "Aguarde o término do envio à Lixeira antes de fechar."); return; }
        try { UndoGpu(); board.Restore(); }
        catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, "Não foi possível restaurar os ajustes: " + ex.Message + "\nA janela permanecerá aberta para permitir nova tentativa.", "Restauração pendente"); return; }
        closing = true; timer.Stop(); scanCancellation?.Cancel(); storageCancellation?.Cancel();
    }
    void InitializeTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir PulseDeck", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Sair do PulseDeck", null, (_, _) => Dispatcher.Invoke(() => ExitApplication()));
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "");
        trayIcon = new System.Windows.Forms.NotifyIcon { Icon = icon ?? System.Drawing.SystemIcons.Application, Text = "PulseDeck", ContextMenuStrip = menu, Visible = true };
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }
    void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    public void ShowFromTrayForTest() => ShowFromTray();
    public void ExitApplication(int exitCode = 0)
    {
        exitRequested = true;
        Close();
        if (closing) System.Windows.Application.Current.Shutdown(exitCode);
        else exitRequested = false;
    }
    public void SavePreview(string path, int tab)
    {
        Navigation.SelectedIndex = Math.Clamp(tab, 0, Navigation.Items.Count - 1);
        if (Navigation.SelectedContent is FrameworkElement panel) { panel.BeginAnimation(OpacityProperty, null); panel.Opacity = 1; }
        UpdateLayout();
        var visual = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    public void PreparePreview(int tab)
    {
        Navigation.SelectedIndex = Math.Clamp(tab, 0, Navigation.Items.Count - 1);
        if (Navigation.SelectedContent is FrameworkElement panel)
        {
            panel.BeginAnimation(OpacityProperty, null); panel.Opacity = 1;
            if (panel is ScrollViewer scroll) scroll.ScrollToTop();
        }
        UpdateLayout();
    }
    public async Task<string> RunUiSmokeTest()
    {
        var results = new StringBuilder();
        void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); results.AppendLine("PASS " + description); }
        Require(RamValue.Text != "—" && ProcessesGrid.Items.Count > 0, "Native telemetry and process list populated");
        if(cpuTemperature.AmdStatus=="Conectado") Require(lastCpuTemperature is > 0 and < 120 && CpuTempStatus.Text.Contains("AMD"), "AMD SDK temperature reaches the live overlay source");
        Require(ApplyFanButton.IsEnabled == gpu.FanSupported && ApplyClockButton.IsEnabled == gpu.ClockSupported, "Hardware controls reflect actual ADLX support");
        Require(!ApplyBoardButton.IsEnabled || board.ControlIds.Count > 0, "No motherboard control enabled without a detected channel");
        Require(BoardControls.Visibility == (board.Sensors.Any(s => s.Type is "Fan" or "Control") ? Visibility.Visible : Visibility.Collapsed), "CPU/case fan section follows detected sensors");
        Require(BoardActions.Visibility == (board.ControlIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed), "Motherboard controls require an exposed writable channel");
        Require(WindowStyle == WindowStyle.None && System.Windows.Shell.WindowChrome.GetWindowChrome(this) != null, "Window uses native resize chrome without an OS title strip");
        Require(FanPresets.Children.OfType<RadioButton>().Count() == 4, "Exactly four GPU fan profiles are presented");
        if (gpu.FanSupported)
        {
            var before = gpu.ReadFanCurve();
            var zeroBefore = gpu.ZeroRpmEnabled;
            foreach (var button in FanPresets.Children.OfType<RadioButton>()) button.IsChecked = true;
            var after = gpu.ReadFanCurve();
            Require(before.Select(p => (p.Temperature, p.Speed)).SequenceEqual(after.Select(p => (p.Temperature, p.Speed))), "Selecting all profile previews never sends a fan command");
            Require(zeroBefore == gpu.ZeroRpmEnabled, "Profile previews preserve the driver Zero RPM state");
            ReadCurve();
            Require(!ApplyFanButton.IsEnabled && !FanPresets.Children.OfType<RadioButton>().Any(button => button.IsChecked == true), "Rereading the applied curve clears stale profile selection");
            NormalProfile.IsChecked = true;
            Require(ApplyFanButton.IsEnabled, "Selecting a new draft enables its confirmation");
        }
        OpenCommands(); CommandSearch.Text = "temporários";
        Require(CommandList.Items.Count == 1, "Command palette filters available destinations");
        ActivateCommand(); Require(Navigation.SelectedIndex == 3 && CommandOverlay.Visibility == Visibility.Collapsed, "Command palette navigates and dismisses");
        Require(AppsGrid.Items.Count > 0 && AppsChart.Displayed.Count > 0, "3D application chart contains live grouped process data");
        Require(AppsRamMetric.Width >= 140 && AppsCpuMetric.Width >= 140 && AppsGpuMetric.Width >= 140, "RAM, CPU and GPU selectors are prominent beside the chart");
        AppsCpuMetric.IsChecked = true;
        Require(AppsChart.CpuMode && AppsChart.Displayed.All(x => x.CpuPercent.HasValue), "CPU ranking excludes unknown samples rather than inventing zero");
        AppsRamMetric.IsChecked = true;
        Require(AppsChart.Displayed.First().RamBytes == ApplicationUsage.Aggregate(processes).Max(x => x.RamBytes), "3D RAM leader matches the actual largest executable group");
        AppsGpuMetric.IsChecked = true;
        Require(AppsChart.Metric == 2 && AppsChart.Displayed.All(x=>x.GpuPercent.HasValue), "GPU chart uses only exposed Windows engine samples");
        AppsRamMetric.IsChecked = true;
        AppsChart.SelectApplication(AppsChart.Displayed.Last().Name);
        Require(AppsChart.SelectedName == AppsChart.Displayed.Last().Name, "3D chart supports explicit application selection");
        Require(Icon != null, "Native window loads the embedded PulseDeck icon");
        Require(!gpu.ClockIsOffset || ClockFieldLabel.Text.Contains("Deslocamento"), "RDNA4 offset is labeled separately from measured clock");
        double width = Width, height = Height;
        foreach (var size in new[] { (Width: 1060d, Height: 720d), (Width: 1400d, Height: 920d) })
        {
            Width = size.Width; Height = size.Height;
            for (int i = 0; i < Navigation.Items.Count; i++)
            {
                Navigation.SelectedIndex = i; UpdateLayout();
                Require(((TabItem)Navigation.Items[i]).IsSelected && ((FrameworkElement)Content).ActualWidth > 0, $"Tab {i} lays out at {size.Width}x{size.Height}");
            }
        }
        Width = width; Height = height;
        Navigation.SelectedIndex = 3;
        await Scan();
        Require(tempRows.Count == TempGrid.Items.Count, "Asynchronous temp scan returns full unfiltered list");
        TempGrid.SelectAll(); Require(TempGrid.SelectedItems.Count == TempGrid.Items.Count, "Select all includes virtualized temp rows");
        TempGrid.UnselectAll(); Require(!RecycleButton.IsEnabled, "Recycle disabled when selection is empty");
        AgePicker.SelectedIndex = 2;
        Require(TempGrid.Items.Cast<TempRow>().All(x => x.ModifiedAt < DateTime.Now.AddDays(-7)), "Age filter excludes newer files");
        AgePicker.SelectedIndex = 0;
        ProcessSearch.Text = "PulseDeck";
        Require(ProcessesGrid.Items.Cast<ProcessRow>().All(x => x.Name.Contains("PulseDeck", StringComparison.OrdinalIgnoreCase)), "Process search filters by name");
        ProcessSearch.Text = "";
        MotionToggle.IsChecked = false;
        Require(!HistoryChart.MotionEnabled && !GpuFanVisual.MotionEnabled && !Backdrop.MotionEnabled && !GpuDial.MotionEnabled && !AppsChart.MotionEnabled, "Reduced-motion toggle stops all animated visuals");
        Require(!WindowsOptimization.HasBackup || OptRestoreButton.IsEnabled, "Optimization backup is visible when already present");
        Require(OptPresetList.Items.Count == 9 && WindowsOptimization.Presets.Length == 9 && WindowsOptimization.Presets[0].WindowsDefault && OptChanges.Text.Contains("Game DVR"), "Nine Windows profiles include a visible standard preset");
        var overlayCheck = new OverlayWindow();
        overlayCheck.SetSize(.65); overlayCheck.SetTransparency(.8);
        Require(Math.Abs(overlayCheck.Width-122.2)<.1 && Math.Abs(overlayCheck.BackgroundOpacity-.8)<.001, "Overlay size and transparency respond independently");
        var previewSpeeds = new double[4];
        var previewProfiles = new[] { NormalProfile, MediumProfile, AdvancedProfile, ExtraProfile };
        for (int i = 0; i < previewProfiles.Length; i++)
        {
            previewProfiles[i].IsChecked = true;
            previewSpeeds[i] = GpuFanVisual.PreviewFactor;
        }
        Require(previewSpeeds.Zip(previewSpeeds.Skip(1), (a, b) => b > a).All(x => x), "Fan preview speed increases across all four profiles");
        Require(!gpuChanged && !boardHasChanges, "UI test did not apply hardware settings");
        results.AppendLine("No user files deleted; no clock or fan commands sent.");
        return results.ToString();
    }
    public async Task RunPresentationDemo()
    {
        WindowState=WindowState.Normal;
        Width=1920; Height=1080; Left=0; Top=0;
        Topmost=true; Activate();
        MotionToggle.IsChecked=true; UpdateMotion();
        async Task Show(int tab,int milliseconds)
        {
            Navigation.SelectedIndex=tab;
            Activate();
            UpdateLayout();
            await Task.Delay(milliseconds);
        }
        await Show(0,3000);
        AppsChart.BringIntoView();
        await Task.Delay(1500);
        AppsCpuMetric.IsChecked=true; await Task.Delay(1800);
        AppsGpuMetric.IsChecked=true; await Task.Delay(1800);
        AppsRamMetric.IsChecked=true; await Task.Delay(800);
        await Show(1,1300);
        foreach(var profile in new[]{NormalProfile,MediumProfile,AdvancedProfile,ExtraProfile})
        { if(profile.IsEnabled) profile.IsChecked=true; await Task.Delay(1250); }
        if(BoardControls.IsVisible) { BoardControls.BringIntoView(); await Task.Delay(1800); }
        await Show(2,4000);
        await Show(3,3500);
        await Show(4,1200);
        for(int i=0;i<OptPresetList.Items.Count;i+=2)
        { OptPresetList.SelectedIndex=i; await Task.Delay(900); }
        await Show(5,1200);
        ShowOverlay_Click(this,new RoutedEventArgs());
        if(overlay!=null) { overlay.Left=Width-overlay.Width-28; overlay.Top=82; }
        await Task.Delay(4200);
        HideOverlay_Click(this,new RoutedEventArgs());
        await Show(6,3500);
    }
}
