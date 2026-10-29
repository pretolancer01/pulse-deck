using System;
using System.Collections.Generic;
using System.Linq;

namespace PulseDeck.Services;
public enum FanProfile { Normal, Medio, Avancado, ExtraRapido }
public static class FanProfiles
{
    public static string Name(FanProfile profile) => profile switch { FanProfile.Medio => "Médio", FanProfile.Avancado => "Avançado", FanProfile.ExtraRapido => "Extra rápido", _ => "Normal" };
    // Normal is the actual curve captured on opening/rechecking, not a guessed factory curve.
    // Other presets keep those temperature points and never reduce their reference fan duty.
    public static List<FanPoint> Build(FanProfile profile, IReadOnlyList<FanPoint> reference, IntRange speed, IntRange temperature)
    {
        AdlxService.ValidateCurve(reference, speed, temperature);
        if (profile == FanProfile.Normal) return reference.Select(p => p.Copy()).ToList();
        int floor = profile switch { FanProfile.Medio => 30, FanProfile.Avancado => 45, FanProfile.ExtraRapido => 70, _ => throw new ArgumentException("Perfil desconhecido.") };
        int maxAligned = speed.Min + (speed.Max - speed.Min) / Math.Max(1, speed.Step) * Math.Max(1, speed.Step);
        var result = new List<FanPoint>();
        for (int i = 0; i < reference.Count; i++)
        {
            double fraction = reference.Count == 1 ? 1 : (double)i / (reference.Count - 1);
            double target = Math.Max(reference[i].Speed, floor + (maxAligned - floor) * fraction);
            int aligned = speed.Min + (int)Math.Ceiling((target - speed.Min) / Math.Max(1, speed.Step)) * Math.Max(1, speed.Step);
            result.Add(new() { Temperature = reference[i].Temperature, Speed = Math.Clamp(aligned, speed.Min, maxAligned) });
        }
        AdlxService.ValidateCurve(result, speed, temperature);
        return result;
    }
}
