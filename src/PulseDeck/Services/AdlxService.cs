using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PulseDeck.Services;

public sealed class FanPoint
{
    public int Temperature { get; set; }
    public int Speed { get; set; }
    public FanPoint Copy() => new() { Temperature = Temperature, Speed = Speed };
}
public sealed record GpuMetrics(double? Usage, int? Clock, double? Temperature, double? Hotspot, int? FanRpm, int? VramMb);
[StructLayout(LayoutKind.Sequential)]
public struct IntRange
{
    public int Min, Max, Step;
    public readonly bool Contains(int value) => value >= Min && value <= Max && (Step <= 1 || (value - Min) % Step == 0);
    public override readonly string ToString() => $"{Min}–{Max} (passo {Step})";
}

// ABI from official AMD ADLX headers, pinned at vendor/ADLX. No private driver IOCTLs.
// All calls are serialized by the UI/diagnostic thread. System has no Acquire/Release.
public sealed class AdlxService : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int VersionFn(out ulong version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn(ulong version, out IntPtr system);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TerminateFn();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int RefFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OutPtrFn(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GpuPtrFn(IntPtr self, IntPtr gpu, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GpuBoolFn(IntPtr self, IntPtr gpu, out byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int BoolFn(IntPtr self, out byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntFn(IntPtr self, out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DoubleFn(IntPtr self, out double value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint SizeFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AtFn(IntPtr self, uint index, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet=CharSet.Unicode)] delegate int QueryFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string iid, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int RangeFn(IntPtr self, out IntRange value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FanRangesFn(IntPtr self, out IntRange speed, out IntRange temperature);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetIntFn(IntPtr self, int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetBoolFn(IntPtr self, byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ValidateFn(IntPtr self, IntPtr states, out int index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetPtrFn(IntPtr self, IntPtr states);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ResetFn(IntPtr self, IntPtr gpu);

    IntPtr library, system, gpu, tuning, performance, fan, graphics;
    TerminateFn? terminate;
    bool initialized;
    public string Status { get; private set; } = "Não verificado";
    public string Name { get; private set; } = "GPU indisponível";
    public string DriverVersion { get; private set; } = "";
    public int VramMb { get; private set; }
    public bool FanSupported => fan != IntPtr.Zero;
    public bool ClockSupported => graphics != IntPtr.Zero;
    public bool FactoryResetSupported => tuning != IntPtr.Zero && gpu != IntPtr.Zero;
    public bool AutoOverclockSupported { get; private set; }
    public IntRange FanSpeedRange { get; private set; }
    public IntRange FanTemperatureRange { get; private set; }
    public IntRange ClockRange { get; private set; }
    // AMD documents Navi4+ GetGPUMaxFrequency as an offset. A negative minimum
    // is unambiguous for this ABI; positive-only ranges retain absolute semantics.
    public bool ClockIsOffset => ClockSupported && ClockRange.Min < 0;
    public bool? ZeroRpmEnabled => fan != IntPtr.Zero && Success(Method<BoolFn>(fan, 8)(fan, out byte supported)) && supported != 0
        && Success(Method<BoolFn>(fan, 9)(fan, out byte value)) ? value != 0 : null;
    public int? MaxClock => graphics == IntPtr.Zero ? null : ReadInt(graphics, 7);
    public bool? IsFactory => tuning == IntPtr.Zero ? null : GpuFlag(tuning, 4);

    static T Method<T>(IntPtr obj, int slot) where T : Delegate
    {
        if (obj == IntPtr.Zero) throw new InvalidOperationException("Interface indisponível.");
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));
    }
    static bool Success(int result) => result is >= 0 and <= 2;
    static void Check(int result, string operation)
    {
        if (!Success(result)) throw new InvalidOperationException($"{operation}: ADLX retornou {result}. Nenhuma confirmação de sucesso foi recebida.");
    }
    static void Release(ref IntPtr pointer)
    {
        if (pointer != IntPtr.Zero) { Method<RefFn>(pointer, 1)(pointer); pointer = IntPtr.Zero; }
    }
    static int? ReadInt(IntPtr obj, int slot) => Success(Method<IntFn>(obj, slot)(obj, out int value)) ? value : null;
    static double? ReadDouble(IntPtr obj, int slot) => Success(Method<DoubleFn>(obj, slot)(obj, out double value)) && double.IsFinite(value) ? value : null;
    bool GpuFlag(IntPtr obj, int slot) => Success(Method<GpuBoolFn>(obj, slot)(obj, gpu, out byte value)) && value != 0;

    public void Initialize()
    {
        if (initialized) return;
        try
        {
            // Absolute system path prevents DLL lookup from the current directory.
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "amdadlx64.dll");
            library = NativeLibrary.Load(path);
            var version = Marshal.GetDelegateForFunctionPointer<VersionFn>(NativeLibrary.GetExport(library, "ADLXQueryFullVersion"));
            var init = Marshal.GetDelegateForFunctionPointer<InitFn>(NativeLibrary.GetExport(library, "ADLXInitialize"));
            terminate = Marshal.GetDelegateForFunctionPointer<TerminateFn>(NativeLibrary.GetExport(library, "ADLXTerminate"));
            Check(version(out ulong full), "Consultar versão");
            DriverVersion = $"ADLX {full >> 48}.{(full >> 32) & 65535}.{(full >> 16) & 65535}.{full & 65535}";
            Check(init(full, out system), "Inicializar ADLX");
            initialized = true;
            IntPtr list = IntPtr.Zero;
            try
            {
                Check(Method<OutPtrFn>(system, 1)(system, out list), "Enumerar GPUs");
                uint count = Method<SizeFn>(list, 3)(list);
                for (uint i = 0; i < count; i++)
                {
                    IntPtr candidate = IntPtr.Zero;
                    Check(Method<AtFn>(list, 11)(list, i, out candidate), "Ler adaptador");
                    // ADLX_GPU_TYPE: 0 = undefined, 1 = integrated, 2 = discrete.
                    int? type = ReadInt(candidate, 5);
                    if (gpu == IntPtr.Zero || type == 2) { Release(ref gpu); gpu = candidate; }
                    else Release(ref candidate);
                    if (type == 2) break;
                }
            }
            finally { Release(ref list); }
            if (gpu == IntPtr.Zero) throw new InvalidOperationException("Nenhum adaptador AMD acessível.");
            Check(Method<OutPtrFn>(gpu, 7)(gpu, out var name), "Nome da GPU");
            Name = Marshal.PtrToStringUTF8(name) ?? "AMD GPU";
            VramMb = ReadInt(gpu, 11) ?? 0;
            Method<OutPtrFn>(system, 9)(system, out performance);
            if (Success(Method<OutPtrFn>(system, 8)(system, out tuning)) && tuning != IntPtr.Zero)
            {
                if (GpuFlag(tuning, 10))
                {
                    IntPtr raw = IntPtr.Zero;
                    try
                    {
                        if (Success(Method<GpuPtrFn>(tuning, 16)(tuning, gpu, out raw)) && raw != IntPtr.Zero)
                        {
                            Method<QueryFn>(raw, 2)(raw, "IADLXManualFanTuning", out fan);
                            if (fan != IntPtr.Zero)
                            {
                                if (Success(Method<FanRangesFn>(fan, 3)(fan, out var speed, out var temp)))
                                { FanSpeedRange = speed; FanTemperatureRange = temp; }
                                else Release(ref fan);
                            }
                        }
                    }
                    finally { Release(ref raw); }
                }
                if (GpuFlag(tuning, 8))
                {
                    IntPtr raw = IntPtr.Zero;
                    try
                    {
                        if (Success(Method<GpuPtrFn>(tuning, 14)(tuning, gpu, out raw)) && raw != IntPtr.Zero)
                        {
                            Method<QueryFn>(raw, 2)(raw, "IADLXManualGraphicsTuning2", out graphics);
                            if (graphics != IntPtr.Zero)
                            {
                                if (Success(Method<RangeFn>(graphics, 6)(graphics, out var range))) ClockRange = range;
                                else Release(ref graphics);
                            }
                        }
                    }
                    finally { Release(ref raw); }
                }
                IntPtr auto = IntPtr.Zero, rawAuto = IntPtr.Zero;
                try
                {
                    if (GpuFlag(tuning, 6) && Success(Method<GpuPtrFn>(tuning, 12)(tuning, gpu, out rawAuto)) && rawAuto != IntPtr.Zero
                        && Success(Method<QueryFn>(rawAuto, 2)(rawAuto, "IADLXGPUAutoTuning", out auto)) && auto != IntPtr.Zero)
                        AutoOverclockSupported = Success(Method<BoolFn>(auto, 4)(auto, out byte supported)) && supported != 0;
                }
                finally { Release(ref auto); Release(ref rawAuto); }
            }
            Status = "Conectado ao driver AMD • consulta direta";
        }
        catch (Exception ex) { Status = "ADLX indisponível: " + ex.Message; Dispose(); }
    }

    public GpuMetrics ReadMetrics()
    {
        IntPtr value = IntPtr.Zero;
        try
        {
            if (performance == IntPtr.Zero || !Success(Method<GpuPtrFn>(performance, 18)(performance, gpu, out value)) || value == IntPtr.Zero)
                return new(null, null, null, null, null, null);
            return new(ReadDouble(value, 4), ReadInt(value, 5), ReadDouble(value, 7), ReadDouble(value, 8), ReadInt(value, 11), ReadInt(value, 12));
        }
        finally { Release(ref value); }
    }
    public List<FanPoint> ReadFanCurve()
    {
        var points = new List<FanPoint>();
        if (!FanSupported) return points;
        IntPtr list = IntPtr.Zero;
        try
        {
            Check(Method<OutPtrFn>(fan, 4)(fan, out list), "Ler curva");
            uint start = Method<SizeFn>(list, 5)(list), end = Method<SizeFn>(list, 6)(list);
            for (uint i = start; i < end; i++)
            {
                IntPtr point = IntPtr.Zero;
                try
                {
                    Check(Method<AtFn>(list, 11)(list, i, out point), "Ler ponto");
                    int speed = ReadInt(point, 3) ?? throw new InvalidOperationException("Rotação indisponível.");
                    int temp = ReadInt(point, 5) ?? throw new InvalidOperationException("Temperatura indisponível.");
                    points.Add(new() { Speed = speed, Temperature = temp });
                }
                finally { Release(ref point); }
            }
            return points;
        }
        finally { Release(ref list); }
    }

    public static void ValidateCurve(IReadOnlyList<FanPoint> points, IntRange speed, IntRange temperature)
    {
        if (points.Count == 0) throw new ArgumentException("Curva vazia.");
        for (int i = 0; i < points.Count; i++)
        {
            if (!speed.Contains(points[i].Speed) || !temperature.Contains(points[i].Temperature)) throw new ArgumentException("Valor fora dos limites ou do passo aceito pelo driver.");
            if (i > 0 && (points[i].Temperature <= points[i - 1].Temperature || points[i].Speed < points[i - 1].Speed))
                throw new ArgumentException("Temperaturas devem crescer e a ventilação não pode diminuir na curva.");
        }
    }
    public void ApplyFanCurve(IReadOnlyList<FanPoint> points)
    {
        if (!FanSupported) throw new InvalidOperationException("Controle de ventilação não suportado.");
        ValidateCurve(points, FanSpeedRange, FanTemperatureRange);
        IntPtr list = IntPtr.Zero;
        try
        {
            // This is an editable state list; no hardware changes occur until SetFanTuningStates.
            Check(Method<OutPtrFn>(fan, 4)(fan, out list), "Preparar curva");
            uint start = Method<SizeFn>(list, 5)(list), end = Method<SizeFn>(list, 6)(list);
            if (end - start != points.Count) throw new InvalidOperationException("Quantidade de pontos mudou. Releia a curva.");
            for (int i = 0; i < points.Count; i++)
            {
                IntPtr point = IntPtr.Zero;
                try
                {
                    Check(Method<AtFn>(list, 11)(list, start + (uint)i, out point), "Preparar ponto");
                    Check(Method<SetIntFn>(point, 4)(point, points[i].Speed), "Definir rotação");
                    Check(Method<SetIntFn>(point, 6)(point, points[i].Temperature), "Definir temperatura");
                }
                finally { Release(ref point); }
            }
            Check(Method<ValidateFn>(fan, 6)(fan, list, out int index), $"Validar curva (índice {index})");
            Check(Method<SetPtrFn>(fan, 7)(fan, list), "Aplicar curva");
            var actual = ReadFanCurve();
            if (actual.Count != points.Count || actual.Where((p, i) => p.Speed != points[i].Speed || p.Temperature != points[i].Temperature).Any())
                throw new InvalidOperationException("O driver não confirmou a curva solicitada. Releia os valores.");
        }
        finally { Release(ref list); }
    }
    public void SetMaxClock(int value)
    {
        if (!ClockSupported || !ClockRange.Contains(value)) throw new ArgumentException("Frequência fora do intervalo aceito.");
        Check(Method<SetIntFn>(graphics, 8)(graphics, value), "Aplicar ajuste de frequência");
        if (MaxClock != value) throw new InvalidOperationException("O driver não confirmou o limite solicitado.");
    }
    public void SetZeroRpm(bool enabled)
    {
        if (!ZeroRpmEnabled.HasValue) throw new InvalidOperationException("Modo Zero RPM indisponível.");
        Check(Method<SetBoolFn>(fan, 10)(fan, enabled ? (byte)1 : (byte)0), "Alterar modo Zero RPM");
        if (ZeroRpmEnabled != enabled) throw new InvalidOperationException("O driver não confirmou o modo Zero RPM.");
    }
    public void ResetFactory()
    {
        if (!FactoryResetSupported) throw new InvalidOperationException("Restauração indisponível.");
        Check(Method<ResetFn>(tuning, 5)(tuning, gpu), "Restaurar GPU");
        if (IsFactory != true) throw new InvalidOperationException("O driver não confirmou o estado de fábrica.");
    }
    public void Dispose()
    {
        Release(ref graphics); Release(ref fan); Release(ref performance); Release(ref tuning); Release(ref gpu);
        if (initialized) { terminate?.Invoke(); initialized = false; }
        system = IntPtr.Zero;
        if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; }
    }
}
