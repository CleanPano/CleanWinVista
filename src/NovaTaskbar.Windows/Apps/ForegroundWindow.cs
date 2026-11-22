using System.Text;
using NovaTaskbar.Core.Layout;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.Windows.Apps;

/// <summary>Lê a janela em primeiro plano (exe, classe, tela cheia). Barato: chamado em timer só quando o auto-ocultar está ligado.</summary>
public sealed class ForegroundWindow
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "ForegroundStaging",
    };
    private readonly Dictionary<uint, string?> _exeByPid = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public IntPtr Handle { get; private set; }

    public ForegroundApp Read()
    {
        IntPtr h = NativeMethods.GetForegroundWindow();
        Handle = h;
        if (h == IntPtr.Zero) return new ForegroundApp(null, null, false, true, false);
        NativeMethods.GetWindowThreadProcessId(h, out uint pid);
        if (pid == _ownPid) return new ForegroundApp(null, null, false, false, true);

        var sb = new StringBuilder(128);
        NativeMethods.GetClassName(h, sb, sb.Capacity);
        string cls = sb.ToString();
        bool shell = ShellClasses.Contains(cls);
        return new ForegroundApp(ExeName(pid), cls, !shell && IsFullscreen(h), shell, false, !shell && NativeMethods.IsZoomed(h));
    }

    private string? ExeName(uint pid)
    {
        if (_exeByPid.TryGetValue(pid, out var cached)) return cached;
        if (_exeByPid.Count > 256) _exeByPid.Clear();
        string? name = null;
        IntPtr p = NativeMethods.OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (p != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(p, 0, sb, ref size)) name = Path.GetFileName(sb.ToString());
            }
            finally { NativeMethods.CloseHandle(p); }
        }
        return _exeByPid[pid] = name;
    }

    /// <summary>A janela cobre o monitor inteiro (vídeo, jogo, F11 no navegador).</summary>
    private static bool IsFullscreen(IntPtr h)
    {
        if (!NativeMethods.GetWindowRect(h, out var r)) return false;
        var mon = NativeMethods.MonitorFromWindow(h, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(mon, ref mi)) return false;
        var m = mi.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
