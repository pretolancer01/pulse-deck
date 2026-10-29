using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PulseDeck;
internal static class NativeChrome
{
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MinMax { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    public static void Attach(Window window)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
    }
    static IntPtr Hook(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (message != 0x24) return IntPtr.Zero;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info))
        {
            var bounds = Marshal.PtrToStructure<MinMax>(lp);
            bounds.MaxPosition.X = info.Work.Left - info.Monitor.Left; bounds.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            bounds.MaxSize.X = info.Work.Right - info.Work.Left; bounds.MaxSize.Y = info.Work.Bottom - info.Work.Top;
            Marshal.StructureToPtr(bounds, lp, false); handled = true;
        }
        return IntPtr.Zero;
    }
}
