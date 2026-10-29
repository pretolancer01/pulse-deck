using System;
using System.IO;
using System.Linq;
using System.Threading;
using PulseDeck.Services;

namespace PulseDeck;
static class SelfTests
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "PulseDeck-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture, "nested"));
            File.WriteAllText(Path.Combine(fixture, "nested", "sample.tmp"), "abcdef");
            var scan = TempService.Scan(CancellationToken.None, fixture);
            Require(scan.Rows.Count == 1 && scan.Rows[0].Bytes == 6, "Scan must return all nested regular files and exact sizes.");
            bool canceled = false;
            try { TempService.Scan(new CancellationToken(true), fixture); } catch (OperationCanceledException) { canceled = true; }
            Require(canceled, "Scan must honor cancellation.");
            bool rejected = false;
            try { TempService.ValidateForRecycle(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini")); } catch (IOException) { rejected = true; }
            Require(rejected, "Recycle must reject paths outside temp.");
            var speed = new IntRange { Min = 20, Max = 100, Step = 5 };
            var temp = new IntRange { Min = 20, Max = 100, Step = 1 };
            AdlxService.ValidateCurve([new() { Temperature = 30, Speed = 20 }, new() { Temperature = 80, Speed = 100 }], speed, temp);
            foreach (var points in new FanPoint[][] {
                [new() { Temperature = 30, Speed = 21 }],
                [new() { Temperature = 90, Speed = 80 }, new() { Temperature = 80, Speed = 100 }],
                [new() { Temperature = 30, Speed = 80 }, new() { Temperature = 80, Speed = 20 }]
            })
            {
                rejected = false; try { AdlxService.ValidateCurve(points, speed, temp); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "Invalid fan curve must be rejected before any hardware call.");
            }
            var reference = new FanPoint[] { new() { Temperature = 30, Speed = 20 }, new() { Temperature = 50, Speed = 40 }, new() { Temperature = 80, Speed = 100 } };
            int[] previous = reference.Select(p => p.Speed).ToArray();
            foreach (FanProfile profile in Enum.GetValues<FanProfile>())
            {
                var curve = FanProfiles.Build(profile, reference, speed, temp);
                Require(curve.Count == reference.Length && curve.Select(p => p.Temperature).SequenceEqual(reference.Select(p => p.Temperature)), "Profiles must retain driver point counts and temperatures.");
                Require(curve.Select((p, i) => p.Speed >= previous[i]).All(x => x), "Stronger profiles cannot lower reference/previous fan duty.");
                previous = curve.Select(p => p.Speed).ToArray();
                AdlxService.ValidateCurve(curve, speed, temp);
            }
            Require(reference[0].Speed == 20, "Profile previews must not mutate the session reference.");
            var grouped = ApplicationUsage.Aggregate(new[] {
                new ProcessRow("browser",1,"3%","",100,3), new ProcessRow("Browser",2,"5%","",200,5),
                new ProcessRow("game",3,"20%","",500,20), new ProcessRow("unknown",4,"—","",20,null)
            });
            Require(grouped.Count == 3 && grouped[0].Name == "game", "Application ranking must include all sampled processes and sort by actual RAM.");
            Require(grouped[1].ProcessCount == 2 && grouped[1].RamBytes == 300 && grouped[1].CpuPercent == 8, "Executable grouping must sum RAM/CPU and group case-insensitively.");
            Require(grouped[2].CpuPercent == null, "Unknown CPU samples must not become invented zero values.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), "PASS: recursive scan, sizes, cancellation, recycle boundary, fan curve limits/steps/order, four fan profiles with preserved temperatures and non-decreasing duty. No hardware settings changed; no user files deleted.");
        }
        finally
        {
            // Only files created by this test, in a fixed verified fixture, are removed.
            File.Delete(Path.Combine(fixture, "nested", "sample.tmp"));
            Directory.Delete(Path.Combine(fixture, "nested")); Directory.Delete(fixture);
        }
    }
}
