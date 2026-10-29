using System;
using System.IO;
using System.Text.Json;

namespace PulseDeck;
public sealed class AppPreferences
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PulseDeck","preferences.json");
    public bool IntroEnabled { get; set; } = true;
    public bool MotionEnabled { get; set; } = true;
    public double OverlayOpacity { get; set; } = .9;
    public double OverlaySize { get; set; } = 1;
    public static AppPreferences Current { get; } = Load();
    static AppPreferences Load() { try { return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath))??new(); } catch { return new(); } }
    public void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath,JsonSerializer.Serialize(this)); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
}
