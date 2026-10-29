using System;
using System.Collections.Generic;
using System.Linq;

namespace PulseDeck.Services;
public sealed record ApplicationUsage(string Name, int ProcessCount, long RamBytes, double? CpuPercent, double? GpuPercent, string Pids)
{
    public string Ram => TempService.FormatSize(RamBytes);
    public string Cpu => CpuPercent.HasValue ? $"{CpuPercent:N1}%" : "Aguardando leitura";
    public string Gpu => GpuPercent.HasValue ? $"{GpuPercent:N1}%" : "Indisponível";
    // Executable names are the available identity, not a guessed friendly application name.
    public static List<ApplicationUsage> Aggregate(IEnumerable<ProcessRow> processes) => processes
        .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        .Select(g => new ApplicationUsage(g.Key, g.Count(), g.Sum(p => p.RamBytes),
            g.All(p => p.CpuPercent.HasValue) ? Math.Clamp(g.Sum(p => p.CpuPercent!.Value), 0, 100) : null,
            g.Any(p => p.GpuPercent.HasValue) ? Math.Clamp(g.Sum(p => p.GpuPercent ?? 0), 0, 100) : null,
            string.Join(", ",g.Select(p=>p.Id))))
        .OrderByDescending(p => p.RamBytes).ThenBy(p => p.Name).ToList();
}
