using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace PulseDeck.Services;
// Read-only CPU sensors. Does not install or require motherboard control drivers.
public sealed class CpuTemperatureService : IDisposable
{
    readonly object gate=new();
    Computer? computer;
    IntPtr bridge;
    RyzenRead? amdRead;
    RyzenShutdown? amdShutdown;
    DateTime lastAmdRead=DateTime.MinValue;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    delegate int RyzenInit([MarshalAs(UnmanagedType.LPWStr)] string sdkRoot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int RyzenRead(out double temperature);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void RyzenShutdown();
    public string Status { get; private set; }="Não verificado";
    public string AmdStatus { get; private set; }="SDK não encontrado";
    public string? SensorName { get; private set; }
    public double? LastValue { get; private set; }
    public void Initialize()
    {
        lock(gate)
        {
            if(computer!=null || amdRead!=null) return;
            if(TryInitializeAmd()) return;
            try
            {
                computer=new Computer { IsCpuEnabled=true, IsMotherboardEnabled=false,IsGpuEnabled=false,IsControllerEnabled=false };
                computer.Open(); Read();
                if(LastValue==null)
                {
                    if(AmdStatus!="SDK não encontrado") Status+=" AMD: "+AmdStatus+".";
                    else if((Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")??"").Contains("AuthenticAMD",StringComparison.OrdinalIgnoreCase))
                        Status+=" Para Ryzen, instale o AMD Ryzen Master Monitoring SDK.";
                }
            }
            catch(Exception ex) { Status="Sensor de CPU indisponível: "+ex.Message; try { computer?.Close(); } catch { } computer=null; }
        }
    }
    bool TryInitializeAmd()
    {
        string? sdkRoot=Environment.GetEnvironmentVariable("AMDRMMONITORSDKPATH");
        if(string.IsNullOrWhiteSpace(sdkRoot) || !File.Exists(Path.Combine(sdkRoot,"bin","Platform.dll")))
        {
            try { using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\RyzenMasterMonitoringSDK"); sdkRoot=key?.GetValue("InstallationPath") as string; }
            catch { return false; }
        }
        if(string.IsNullOrWhiteSpace(sdkRoot) || !File.Exists(Path.Combine(sdkRoot,"bin","Platform.dll"))) return false;
        try
        {
            const string resourceName="PulseDeck.Native.PulseDeckRyzenBridge.dll";
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)??throw new FileNotFoundException("Ponte AMD não incorporada.");
            using var memory=new MemoryStream(); stream.CopyTo(memory); byte[] bytes=memory.ToArray();
            string version=Convert.ToHexString(SHA256.HashData(bytes))[..12];
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PulseDeck","native",version);
            Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"PulseDeckRyzenBridge.dll");
            if(!File.Exists(path)) File.WriteAllBytes(path,bytes);
            bridge=NativeLibrary.Load(path);
            var init=Marshal.GetDelegateForFunctionPointer<RyzenInit>(NativeLibrary.GetExport(bridge,"PulseDeckRyzenInit"));
            amdRead=Marshal.GetDelegateForFunctionPointer<RyzenRead>(NativeLibrary.GetExport(bridge,"PulseDeckRyzenRead"));
            amdShutdown=Marshal.GetDelegateForFunctionPointer<RyzenShutdown>(NativeLibrary.GetExport(bridge,"PulseDeckRyzenShutdown"));
            int code=init(sdkRoot);
            if(code!=0) throw new InvalidOperationException("Inicialização AMD retornou "+code);
            AmdStatus="Conectado";
            if(Read()!=null) return true;
            throw new InvalidOperationException("SDK AMD não retornou temperatura válida.");
        }
        catch(Exception ex)
        {
            AmdStatus=ex.Message;
            Status="AMD Ryzen Monitoring SDK indisponível: "+ex.Message;
            try { amdShutdown?.Invoke(); } catch { }
            amdRead=null; amdShutdown=null;
            if(bridge!=IntPtr.Zero) { NativeLibrary.Free(bridge); bridge=IntPtr.Zero; }
            return false;
        }
    }
    public double? Read()
    {
        lock(gate)
        {
            if(amdRead!=null)
            {
                if(DateTime.UtcNow-lastAmdRead<TimeSpan.FromSeconds(1)) return LastValue;
                lastAmdRead=DateTime.UtcNow;
                int code=amdRead(out double value);
                LastValue=code==0 && value is > 0 and < 120?value:null;
                SensorName=LastValue.HasValue?"AMD Ryzen Master Monitoring SDK":null;
                Status=LastValue.HasValue?"Temperatura real da CPU via AMD Ryzen Master Monitoring SDK.":"SDK AMD não retornou temperatura (código "+code+").";
                return LastValue;
            }
            if(computer==null) return null;
            try
            {
                var values=computer.Hardware.Where(h=>h.HardwareType==HardwareType.Cpu).SelectMany(h=> { h.Update(); return h.Sensors; })
                    .Where(s=>s.SensorType==SensorType.Temperature && s.Value is > 0 and < 120).ToList();
                var preferred=values.OrderBy(s=>s.Name.Contains("Package",StringComparison.OrdinalIgnoreCase)?0:s.Name.Contains("Tctl",StringComparison.OrdinalIgnoreCase)?1:s.Name.Contains("Tdie",StringComparison.OrdinalIgnoreCase)?2:3).FirstOrDefault();
                LastValue=preferred?.Value; SensorName=preferred?.Name;
                Status=preferred==null?"Nenhum sensor de temperatura da CPU foi exposto.":"Sensor real da CPU: "+preferred.Name;
                return LastValue;
            }
            catch(Exception ex) { LastValue=null; Status="Leitura da CPU indisponível: "+ex.Message; return null; }
        }
    }
    public void Dispose() { lock(gate) { try { computer?.Close(); } catch { } computer=null; try { amdShutdown?.Invoke(); } catch { } amdRead=null; amdShutdown=null; if(bridge!=IntPtr.Zero) { NativeLibrary.Free(bridge); bridge=IntPtr.Zero; } } }
}
