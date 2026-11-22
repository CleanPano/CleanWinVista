using System.Text;
using NovaTaskbar.Core.Apps;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.Windows.Apps;

/// <summary>
/// Coleta as janelas de topo em ordem Z (EnumWindows) já filtradas como "apps".
/// Dados caros (caminho do processo, AUMID) ficam em cache por PID/HWND e são podados quando somem.
/// Não é thread-safe: use sempre no mesmo thread (UI).
/// </summary>
public sealed class WindowEnumerator
{
    private readonly int _ownPid = Environment.ProcessId;
    private readonly Dictionary<int, (string? Exe, string? Aumid)> _processCache = new();
    private readonly Dictionary<nint, string?> _windowAumidCache = new();
    private readonly StringBuilder _sb = new(512);

    /// <summary>Janelas de app visíveis, da mais recente (topo do Z) para a mais antiga.</summary>
    public List<WindowInfo> Snapshot()
    {
        var handles = new List<nint>(256);
        NativeMethods.EnumWindows((h, _) => { handles.Add(h); return true; }, IntPtr.Zero);

        var result = new List<WindowInfo>(32);
        var aliveHwnds = new HashSet<nint>();
        var alivePids = new HashSet<int>();

        foreach (var h in handles)
        {
            if (!NativeMethods.IsWindowVisible(h)) continue; // descarte barato antes de qualquer chamada cara
            var info = Describe(h);
            if (info is null) continue;
            aliveHwnds.Add(h);
            alivePids.Add(info.ProcessId);
            if (WindowFilter.IsAppWindow(info, _ownPid)) result.Add(info);
        }

        Prune(_windowAumidCache, aliveHwnds);
        Prune(_processCache, alivePids);
        return result;
    }

    /// <summary>Lê os dados de uma janela (sem aplicar o filtro).</summary>
    public WindowInfo? Describe(nint h)
    {
        if (!NativeMethods.IsWindow(h)) return null;
        NativeMethods.GetWindowThreadProcessId(h, out uint pidU);
        int pid = (int)pidU;
        long style = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_STYLE).ToInt64();
        long ex = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE).ToInt64();
        nint owner = NativeMethods.GetWindow(h, NativeMethods.GW_OWNER);
        bool cloaked = NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_CLOAKED, out int c, sizeof(int)) == 0 && c != 0;
        string title = GetText(h);
        string cls = GetClass(h);

        var (exe, processAumid) = GetProcess(pid);
        string? aumid = GetWindowAumid(h) ?? processAumid;

        // Apps UWP: a janela visível pertence ao ApplicationFrameHost; o app real é o processo da CoreWindow filha.
        if (aumid is null && exe != null && exe.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var child = FindCoreWindowProcess(h);
            if (child is { } cp)
            {
                var (cexe, caumid) = GetProcess(cp);
                aumid = caumid;
                exe = cexe ?? exe;
            }
        }

        return new WindowInfo(h, pid, title, cls, exe, aumid, style, ex, owner,
            NativeMethods.IsWindowVisible(h), cloaked, NativeMethods.IsIconic(h));
    }

    private string GetText(nint h)
    {
        int len = NativeMethods.GetWindowTextLength(h);
        if (len <= 0) return "";
        _sb.Clear();
        _sb.EnsureCapacity(Math.Min(len + 1, 1024));
        NativeMethods.GetWindowText(h, _sb, Math.Min(len + 1, 1024));
        return _sb.ToString();
    }

    private string GetClass(nint h)
    {
        _sb.Clear();
        _sb.EnsureCapacity(256);
        NativeMethods.GetClassName(h, _sb, 256);
        return _sb.ToString();
    }

    private (string? Exe, string? Aumid) GetProcess(int pid)
    {
        if (_processCache.TryGetValue(pid, out var cached)) return cached;
        string? exe = null, aumid = null;
        IntPtr hp = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (hp != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(hp, 0, sb, ref size)) exe = sb.ToString();

                int len = 0;
                if (NativeMethods.GetApplicationUserModelId(hp, ref len, null) == NativeMethods.ERROR_INSUFFICIENT_BUFFER && len > 0)
                {
                    var ab = new StringBuilder(len);
                    if (NativeMethods.GetApplicationUserModelId(hp, ref len, ab) == 0) aumid = ab.ToString();
                }
            }
            finally { NativeMethods.CloseHandle(hp); }
        }
        // Processos protegidos/elevados podem recusar a consulta: ficam sem caminho (agrupados por PID).
        var value = (exe, string.IsNullOrWhiteSpace(aumid) ? null : aumid);
        _processCache[pid] = value;
        return value;
    }

    private string? GetWindowAumid(nint h)
    {
        if (_windowAumidCache.TryGetValue(h, out var cached)) return cached;
        string? aumid = null;
        Guid iid = typeof(NativeMethods.IPropertyStore).GUID;
        if (NativeMethods.SHGetPropertyStoreForWindow(h, ref iid, out var store) >= 0 && store != null)
        {
            try { aumid = NativeMethods.ReadString(store, NativeMethods.PKEY_AppUserModel_ID); }
            finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(store); }
        }
        aumid = string.IsNullOrWhiteSpace(aumid) ? null : aumid;
        _windowAumidCache[h] = aumid;
        return aumid;
    }

    private int? FindCoreWindowProcess(nint frame)
    {
        NativeMethods.GetWindowThreadProcessId(frame, out uint framePid);
        int? found = null;
        var sb = new StringBuilder(64);
        NativeMethods.EnumChildWindows(frame, (c, _) =>
        {
            sb.Clear();
            NativeMethods.GetClassName(c, sb, 64);
            if (sb.ToString() == "Windows.UI.Core.CoreWindow")
            {
                NativeMethods.GetWindowThreadProcessId(c, out uint p);
                if (p != framePid) { found = (int)p; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void Prune<TKey, TValue>(Dictionary<TKey, TValue> cache, HashSet<TKey> alive) where TKey : notnull
    {
        // Remove PIDs/HWNDs que sumiram (evita reaproveitar dados de um PID reciclado pelo sistema).
        if (cache.Count == 0) return;
        foreach (var k in cache.Keys.Where(k => !alive.Contains(k)).ToList()) cache.Remove(k);
    }
}
