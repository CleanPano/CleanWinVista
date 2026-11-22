using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using NovaTaskbar.Core.Widgets;

namespace NovaTaskbar.Windows.SystemInfo;

public readonly record struct MemoryInfo(double UsedPercent, ulong TotalBytes, ulong AvailableBytes);

public readonly record struct BatteryInfo(bool HasBattery, int? Percent, bool Charging, bool OnAc, TimeSpan? Remaining);

/// <summary>
/// Leituras reais do sistema (sem valores inventados). Cada método retorna null/HasBattery=false quando a
/// métrica não está disponível, para a UI mostrar "indisponível".
/// </summary>
public sealed class SystemMetrics
{
    private CpuUsage.Sample? _lastCpu;
    private (long Rx, long Tx, DateTime At)? _lastNet;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>% de CPU desde a leitura anterior (primeira chamada só inicializa → null).</summary>
    public double? CpuPercent()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return null;
        var now = new CpuUsage.Sample(i.Value, k.Value, u.Value);
        var prev = _lastCpu;
        _lastCpu = now;
        return prev is { } p ? CpuUsage.Percent(p, now) : null;
    }

    public MemoryInfo? Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0) return null;
        double used = (m.ullTotalPhys - m.ullAvailPhys) * 100.0 / m.ullTotalPhys;
        return new MemoryInfo(used, m.ullTotalPhys, m.ullAvailPhys);
    }

    /// <summary>Download/upload (bytes/s) somando adaptadores ativos não-loopback/túnel. Primeira chamada → null.</summary>
    public (double Down, double Up)? Network()
    {
        long rx = 0, tx = 0;
        bool any = false;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var st = nic.GetIPStatistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
                any = true;
            }
        }
        catch (NetworkInformationException) { return null; }
        if (!any) return null;

        var now = DateTime.UtcNow;
        var prev = _lastNet;
        _lastNet = (rx, tx, now);
        if (prev is not { } p) return null;
        var dt = now - p.At;
        return (Rate.PerSecond(p.Rx, rx, dt), Rate.PerSecond(p.Tx, tx, dt));
    }

    public static BatteryInfo Battery()
    {
        if (!GetSystemPowerStatus(out var s)) return new BatteryInfo(false, null, false, true, null);
        bool noBattery = (s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255;
        if (noBattery) return new BatteryInfo(false, null, false, s.ACLineStatus == 1, null);
        int? pct = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
        bool charging = (s.BatteryFlag & 8) != 0;
        TimeSpan? remaining = s.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime) : null;
        return new BatteryInfo(true, pct, charging, s.ACLineStatus == 1, remaining);
    }
}
