using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace PulseDeck.Services;
public sealed record BoardSensor(string Id, string Name, string Type, float? Value, string Category);
public sealed class BoardService : IDisposable
{
    readonly object gate = new();
    Computer? computer;
    readonly Dictionary<string, IControl> controls = new();
    readonly HashSet<string> changed = new();
    public string Status { get; private set; } = "Não verificado";
    public List<BoardSensor> Sensors { get; private set; } = new();
    public bool DriverPresent => Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO") is RegistryKey key && CloseKey(key);
    static bool CloseKey(RegistryKey key) { key.Dispose(); return true; }
    public static bool IsAdmin
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    }
    public IReadOnlyList<string> ControlIds => controls.Keys.ToList();
    public void Initialize()
    {
        lock (gate)
        {
        if (computer != null) return;
        try
        {
            // Read-only discovery is useful even where a control driver is unavailable.
            computer = new Computer { IsMotherboardEnabled = true, IsCpuEnabled = true, IsGpuEnabled = true, IsControllerEnabled = true };
            computer.Open(); Read();
            int fanCount=Sensors.Count(s=>s.Type=="Fan");
            int boardFanCount=Sensors.Count(s=>s.Type=="Fan" && s.Category is "Placa-mãe" or "Controlador" or "CPU");
            string fanLabel=fanCount==1?"1 ventoinha detectada":$"{fanCount} ventoinhas detectadas";
            string boardFanLabel=boardFanCount==1?"1 da CPU/placa-mãe":$"{boardFanCount} da CPU/placa-mãe";
            Status = controls.Count > 0 ? $"{fanLabel} e {controls.Count} controles expostos. Confirme a ligação física antes de ajustar." :
                fanCount > 0 ? $"{fanLabel}; {boardFanLabel}. Controle manual da placa-mãe indisponível neste PC." :
                DriverPresent ? "Controlador consultado; nenhum sensor de ventoinha foi exposto." : "Nenhum sensor de ventoinha exposto. PawnIO ausente; controles da placa-mãe indisponíveis.";
        }
        catch (Exception ex) { Status = "Leitura da placa-mãe indisponível: " + ex.Message; computer?.Close(); computer = null; Read(); }
        }
    }
    public void Read()
    {
        lock (gate)
        {
        var rows = new List<BoardSensor>();
        void Visit(IHardware hardware)
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType is SensorType.Fan or SensorType.Control or SensorType.Temperature && (sensor.SensorType!=SensorType.Temperature || sensor.Value is > 0 and < 120))
                {
                    string category=hardware.HardwareType.ToString() switch { "Motherboard" or "SuperIO"=>"Placa-mãe", "Controller" or "EmbeddedController"=>"Controlador", "Cpu"=>"CPU", var type when type.StartsWith("Gpu",StringComparison.OrdinalIgnoreCase)=>"GPU", _=>"Outros" };
                    rows.Add(new(sensor.Identifier.ToString(), hardware.Name + " / " + sensor.Name, sensor.SensorType.ToString(), sensor.Value,category));
                    bool motherboardControl=hardware.HardwareType.ToString() is "Motherboard" or "SuperIO" or "Controller" or "EmbeddedController";
                    if (DriverPresent && IsAdmin && motherboardControl && sensor.SensorType == SensorType.Control && sensor.Control != null) controls[sensor.Identifier.ToString()] = sensor.Control;
                }
            }
            foreach (var sub in hardware.SubHardware) Visit(sub);
        }
        if(computer!=null) foreach (var hardware in computer.Hardware) Visit(hardware);
        try
        {
            foreach(var fan in WindowsMetrics.Query("SELECT Name,DeviceID,DesiredSpeed FROM Win32_Fan"))
            {
                string id=fan.GetValueOrDefault("DeviceID")??"Win32_Fan";
                string name=fan.GetValueOrDefault("Name")??id;
                // Win32_Fan.DesiredSpeed is a requested setpoint, never a measured RPM.
                rows.Add(new("wmi:"+id,"Windows / "+name,"Fan",null,"Windows"));
            }
        }
        catch(Exception ex) when(ex is System.Management.ManagementException or UnauthorizedAccessException) { }
        Sensors = rows.OrderBy(s=>s.Type=="Fan"?0:s.Type=="Control"?1:2).ThenBy(s=>s.Category).ThenBy(s=>s.Name).ToList();
        }
    }
    public void SetDuty(string id, int value)
    {
        lock (gate)
        {
        if (!controls.TryGetValue(id, out var control)) throw new InvalidOperationException("Canal não está disponível.");
        if (value < Math.Max(40, control.MinSoftwareValue) || value > Math.Min(100, control.MaxSoftwareValue)) throw new ArgumentException("Use entre 40 e 100%, dentro dos limites do controlador.");
        Read();
        bool boardSource(BoardSensor s) => s.Category is "Placa-mãe" or "Controlador" or "CPU";
        if (!Sensors.Any(s => boardSource(s) && s.Type == "Fan" && s.Value > 0)) throw new InvalidOperationException("Nenhum RPM da placa-mãe está disponível para acompanhar o ajuste.");
        if (!Sensors.Any(s => boardSource(s) && s.Type == "Temperature" && s.Value is > 0 and < 85) || Sensors.Any(s => boardSource(s) && s.Type == "Temperature" && s.Value >= 85)) throw new InvalidOperationException("Telemetria térmica da CPU/placa-mãe indisponível ou elevada.");
        changed.Add(id); control.SetSoftware(value); Read();
        var observed = Sensors.FirstOrDefault(s => s.Id == id)?.Value;
        if (!observed.HasValue || Math.Abs(observed.Value - value) > 2) { Restore(); throw new InvalidOperationException("O controlador não confirmou o comando. Retorno ao modo automático solicitado."); }
        }
    }
    public void Restore()
    {
        lock (gate)
        {
        var errors = new List<string>();
        foreach (var id in changed.ToArray())
        {
            try { controls[id].SetDefault(); changed.Remove(id); }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        if (errors.Count > 0) throw new InvalidOperationException("Falha ao retornar canais ao automático: " + string.Join("; ", errors));
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
        try { Restore(); } finally { computer?.Close(); computer = null; controls.Clear(); }
        }
    }
}
