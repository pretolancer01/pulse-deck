using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace PulseDeck.Services;
public sealed record ProcessRow(string Name, int Id, string Cpu, string Ram, long RamBytes, double? CpuPercent = null, double? GpuPercent = null);
public sealed record Metrics(double? Cpu, double RamPercent, ulong TotalRam, ulong AvailableRam, int ProcessCount, List<ProcessRow> Processes);
public sealed class WindowsMetrics
{
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus value);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    long previousIdle, previousTotal;
    DateTime previousTime = DateTime.UtcNow;
    Dictionary<int, (long Start, double Cpu)> previousProcesses = new();
    public Metrics Read()
    {
        double? cpu = null;
        if (GetSystemTimes(out long idle, out long kernel, out long user))
        {
            long total = kernel + user, delta = total - previousTotal;
            if (previousTotal > 0 && delta > 0) cpu = Math.Clamp(100.0 * (1 - (double)(idle - previousIdle) / delta), 0, 100);
            previousIdle = idle; previousTotal = total;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new InvalidOperationException("Não foi possível ler a memória do Windows.");
        var now = DateTime.UtcNow;
        double elapsed = Math.Max(0.1, (now - previousTime).TotalSeconds);
        var next = new Dictionary<int, (long, double)>();
        var rows = new List<ProcessRow>();
        using var current = Process.GetCurrentProcess();
        var processes = Process.GetProcesses();
        int count = processes.Length;
        foreach (var p in processes)
        {
            using (p)
            {
                try
                {
                    if (p.Id == 0) continue;
                    string name = p.ProcessName;
                    long ram = p.WorkingSet64;
                    double? percent = null;
                    try
                    {
                        double seconds = p.TotalProcessorTime.TotalSeconds;
                        long start = p.StartTime.ToUniversalTime().Ticks;
                        if (previousProcesses.TryGetValue(p.Id, out var prior) && prior.Start == start)
                            percent = Math.Clamp(100 * (seconds - prior.Cpu) / elapsed / Environment.ProcessorCount, 0, 100);
                        next[p.Id] = (start, seconds);
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
                    rows.Add(new(name, p.Id, percent.HasValue ? $"{percent:N1}%" : "—", $"{ram / 1048576.0:N0} MB", ram, percent));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        previousProcesses = next; previousTime = now;
        var gpuByPid = ReadGpuEngines();
        rows = rows.Select(p => p with { GpuPercent = gpuByPid.TryGetValue(p.Id, out var value) ? value : null }).ToList();
        return new(cpu, memory.Load, memory.TotalPhysical, memory.AvailablePhysical, count, rows.OrderByDescending(x => x.RamBytes).ToList());
    }
    static Dictionary<int,double> ReadGpuEngines()
    {
        var values=new Dictionary<int,double>();
        try
        {
            using var search=new ManagementObjectSearcher(@"root\cimv2","SELECT Name,UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            search.Options.Timeout=TimeSpan.FromSeconds(2);
            using var results=search.Get();
            foreach(ManagementObject item in results) using(item)
            {
                string name=item["Name"]?.ToString()??"";
                if(!name.StartsWith("pid_",StringComparison.OrdinalIgnoreCase)) continue;
                int end=name.IndexOf('_',4);
                if(end<0 || !int.TryParse(name.AsSpan(4,end-4),out int pid)) continue;
                if(!double.TryParse(item["UtilizationPercentage"]?.ToString(),out double value)) continue;
                if(!values.TryGetValue(pid,out double prior) || value>prior) values[pid]=Math.Clamp(value,0,100);
            }
        }
        catch(System.Management.ManagementException) { }
        catch(UnauthorizedAccessException) { }
        return values;
    }
    public static List<Dictionary<string, string>> Query(string wql, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, string>>();
        using var search = new ManagementObjectSearcher(scope, wql);
        search.Options.Timeout = TimeSpan.FromSeconds(5);
        using var result = search.Get();
        foreach (ManagementObject item in result)
        {
            using (item)
            {
                var row = new Dictionary<string, string>();
                foreach (PropertyData property in item.Properties) row[property.Name] = property.Value?.ToString() ?? "";
                rows.Add(row);
            }
        }
        return rows;
    }
}
