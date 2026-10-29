using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace PulseDeck;
public sealed record OptimizationPreset(string Name,string Description,bool Dvr=false,bool Transparency=false,bool Animation=false,bool MenuAnimation=false,bool GameMode=false,bool WindowsDefault=false);
public sealed record SavedSetting(string Path,string Name,int? Value);
public sealed class OptimizationSnapshot
{
    public List<SavedSetting> Registry { get; set; } = new();
    public bool Animation { get; set; }
    public bool? MenuAnimation { get; set; }
}
public static class WindowsOptimization
{
    const string GameDvr=@"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    const string GameStore=@"System\GameConfigStore";
    const string Personalize=@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    static readonly (string Path,string Name)[] keys=[(GameDvr,"AppCaptureEnabled"),(GameStore,"GameDVR_Enabled"),(Personalize,"EnableTransparency"),(GameStore,"AutoGameModeEnabled")];
    static readonly string snapshotFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PulseDeck","windows-optimization-backup.json");
    [StructLayout(LayoutKind.Sequential)] struct AnimationInfo { public uint Size; [MarshalAs(UnmanagedType.Bool)] public bool MinAnimate; }
    [DllImport("user32.dll",SetLastError=true)] static extern bool SystemParametersInfo(uint action,uint param,ref AnimationInfo info,uint flags);
    [DllImport("user32.dll",SetLastError=true,EntryPoint="SystemParametersInfoW")] static extern bool SystemParametersInfoBool(uint action,uint param,[MarshalAs(UnmanagedType.Bool)] ref bool info,uint flags);
    public static OptimizationPreset[] Presets => [
        new("Padrão do Windows","Reativa captura, transparência, animações e Modo de Jogo. É uma opção explícita para sair de qualquer preset.",WindowsDefault:true),
        new("Captura desligada","Desativa a captura integrada do Game DVR nesta conta.",Dvr:true),
        new("Modo de Jogo","Ativa o Modo de Jogo para esta conta, mantendo as demais preferências originais.",GameMode:true),
        new("Interface simples","Desliga a transparência do Windows nesta conta.",Transparency:true),
        new("Movimento reduzido","Desliga a animação de minimizar e restaurar janelas.",Animation:true),
        new("Menus diretos","Desliga a animação dos menus do Windows.",MenuAnimation:true),
        new("Jogo focado","Desliga Game DVR e ativa o Modo de Jogo.",Dvr:true,GameMode:true),
        new("Visual leve","Desliga transparência e animações de janelas e menus.",Transparency:true,Animation:true,MenuAnimation:true),
        new("Máximo local","Combina Game DVR desligado, Modo de Jogo ativo e efeitos visuais reduzidos.",Dvr:true,Transparency:true,Animation:true,MenuAnimation:true,GameMode:true)
    ];
    public static string Changes(OptimizationPreset preset)
    {
        var items=new List<string>();
        if(preset.WindowsDefault) return "Captura do Game DVR: ativada\nModo de Jogo: ativado\nTransparência: ativada\nAnimações de janelas e menus: ativadas";
        if(preset.Dvr) items.Add("Captura do Game DVR: desativada (2 opções de usuário)");
        if(preset.GameMode) items.Add("Modo de Jogo: ativado (conta atual)");
        if(preset.Transparency) items.Add("Transparência do Windows: desativada (conta atual)");
        if(preset.Animation) items.Add("Animação de minimizar/restaurar: desativada (sessão atual)");
        if(preset.MenuAnimation) items.Add("Animação dos menus: desativada (sessão atual)");
        return string.Join("\n",items);
    }
    public static bool HasBackup=>File.Exists(snapshotFile);
    static int? Read(string path,string name)
    { using var key=Registry.CurrentUser.OpenSubKey(path); return key?.GetValue(name) is int value?value:null; }
    static void Write(string path,string name,int? value)
    { using var key=Registry.CurrentUser.CreateSubKey(path,true)??throw new IOException("Sem permissão para alterar a configuração."); if(value.HasValue) key.SetValue(name,value.Value,RegistryValueKind.DWord); else key.DeleteValue(name,false); }
    static bool ReadAnimation()
    { var info=new AnimationInfo { Size=(uint)Marshal.SizeOf<AnimationInfo>() }; if(!SystemParametersInfo(0x48,info.Size,ref info,0)) throw new IOException("Não foi possível ler animações do Windows."); return info.MinAnimate; }
    static void WriteAnimation(bool enabled)
    { var info=new AnimationInfo { Size=(uint)Marshal.SizeOf<AnimationInfo>(),MinAnimate=enabled }; if(!SystemParametersInfo(0x49,info.Size,ref info,0x03)) throw new IOException("Não foi possível alterar animações do Windows."); }
    static bool ReadMenuAnimation()
    { bool enabled=false; if(!SystemParametersInfoBool(0x1002,0,ref enabled,0)) throw new IOException("Não foi possível ler animações de menus."); return enabled; }
    static void WriteMenuAnimation(bool enabled)
    { if(!SystemParametersInfoBool(0x1003,0,ref enabled,0x03)) throw new IOException("Não foi possível alterar animações de menus."); }
    static OptimizationSnapshot Capture()
    { var snapshot=new OptimizationSnapshot { Animation=ReadAnimation(),MenuAnimation=ReadMenuAnimation() }; foreach(var (path,name) in keys) snapshot.Registry.Add(new(path,name,Read(path,name))); return snapshot; }
    static OptimizationSnapshot Load()
    {
        var snapshot=JsonSerializer.Deserialize<OptimizationSnapshot>(File.ReadAllText(snapshotFile))??throw new IOException("Cópia de configurações inválida.");
        if(snapshot.Registry.Count is not 3 and not 4) throw new IOException("Cópia de configurações incompleta.");
        for(int i=0;i<snapshot.Registry.Count;i++) if(snapshot.Registry[i].Path!=keys[i].Path || snapshot.Registry[i].Name!=keys[i].Name) throw new IOException("Cópia de configurações inesperada.");
        // Backups from version 0.5.1 did not include these settings; they were never changed by that version.
        if(snapshot.Registry.Count==3) snapshot.Registry.Add(new(keys[3].Path,keys[3].Name,Read(keys[3].Path,keys[3].Name)));
        snapshot.MenuAnimation ??=ReadMenuAnimation();
        return snapshot;
    }
    static void Restore(OptimizationSnapshot snapshot)
    { foreach(var entry in snapshot.Registry) Write(entry.Path,entry.Name,entry.Value); WriteAnimation(snapshot.Animation); WriteMenuAnimation(snapshot.MenuAnimation??true); }
    public static void Apply(OptimizationPreset preset)
    {
        bool previous=HasBackup; var baseline=previous?Load():Capture();
        if(!previous) { Directory.CreateDirectory(Path.GetDirectoryName(snapshotFile)!); File.WriteAllText(snapshotFile,JsonSerializer.Serialize(baseline)); }
        try
        {
            if(preset.WindowsDefault)
            {
                Write(GameDvr,"AppCaptureEnabled",1); Write(GameStore,"GameDVR_Enabled",1);
                Write(Personalize,"EnableTransparency",1); Write(GameStore,"AutoGameModeEnabled",1);
                WriteAnimation(true); WriteMenuAnimation(true);
            }
            else
            {
                Restore(baseline);
                if(preset.Dvr) { Write(GameDvr,"AppCaptureEnabled",0); Write(GameStore,"GameDVR_Enabled",0); }
                if(preset.GameMode) Write(GameStore,"AutoGameModeEnabled",1);
                if(preset.Transparency) Write(Personalize,"EnableTransparency",0);
                if(preset.Animation) WriteAnimation(false);
                if(preset.MenuAnimation) WriteMenuAnimation(false);
            }
        }
        catch { try { Restore(baseline); } catch { } throw; }
    }
    public static void RestoreOriginal()
    { if(!HasBackup) return; var original=Load(); Restore(original); File.Delete(snapshotFile); }
}
