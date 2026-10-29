using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseDeck.Services;
public sealed record FpsReading(int? FramesPerSecond,string Status,int FrameCount);
public static class FpsService
{
    public static string? PresentMonPath
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("PULSEDECK_PRESENTMON_PATH");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            string local = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
            if (File.Exists(local)) return local;
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string installed = Path.Combine(programFiles, "PresentMon", "PresentMon.exe");
            return File.Exists(installed) ? installed : null;
        }
    }
    public static bool Available=>PresentMonPath is not null;
    public static async Task<FpsReading> CaptureAsync(int pid,CancellationToken cancellation)
    {
        if(!Available) return new(null,"PresentMon não encontrado neste PC.",0);
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PulseDeck","fps");
        Directory.CreateDirectory(folder);
        string csv=Path.Combine(folder,$"capture-{Guid.NewGuid():N}.csv");
        var start=new ProcessStartInfo(PresentMonPath!) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in new[]{"-process_id",pid.ToString(),"-timed","3","-terminate_after_timed","-session_name","PulseDeckFPS-"+Guid.NewGuid().ToString("N"),"-no_top","-output_file",csv}) start.ArgumentList.Add(arg);
        try
        {
            using var process=Process.Start(start)??throw new IOException("PresentMon não iniciou.");
            using var cancel=cancellation.Register(()=> { try { if(!process.HasExited) process.Kill(); } catch { } });
            var output=process.StandardOutput.ReadToEndAsync(cancellation); var error=process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation); await output; string message=await error;
            if(process.ExitCode!=0) return new(null,string.IsNullOrWhiteSpace(message)?"Captura de quadros indisponível.":message.Trim(),0);
            if(!File.Exists(csv)) return new(null,"O jogo não apresentou quadros durante a captura.",0);
            var lines=await File.ReadAllLinesAsync(csv,cancellation);
            if(lines.Length<4) return new(null,"Aguardando quadros do processo selecionado.",Math.Max(0,lines.Length-1));
            var headers=lines[0].Split(','); int frameIndex=Array.IndexOf(headers,"msBetweenPresents"),chainIndex=Array.IndexOf(headers,"SwapChainAddress");
            if(frameIndex<0 || chainIndex<0) return new(null,"Formato de FPS não reconhecido.",0);
            var frames=lines.Skip(1).Select(line=>line.Split(','))
                .Where(columns=>columns.Length>Math.Max(frameIndex,chainIndex) && double.TryParse(columns[frameIndex],NumberStyles.Float,CultureInfo.InvariantCulture,out double ms) && ms is > 0 and < 1000)
                .GroupBy(columns=>columns[chainIndex]).OrderByDescending(group=>group.Count()).FirstOrDefault();
            if(frames==null || frames.Count()<8) return new(null,"Poucos quadros para calcular FPS.",frames?.Count()??0);
            var last=frames.TakeLast(120).Select(columns=>double.Parse(columns[frameIndex],CultureInfo.InvariantCulture)).ToArray();
            double mean=last.Average();
            int fps=(int)Math.Round(1000/mean);
            return new(fps,$"{fps} FPS de apresentação · {last.Length} quadros · PresentMon",last.Length);
        }
        catch(OperationCanceledException) { return new(null,"Captura interrompida.",0); }
        catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return new(null,"FPS indisponível: "+ex.Message,0); }
        finally { try { if(File.Exists(csv)) File.Delete(csv); } catch(IOException) { } }
    }
}
