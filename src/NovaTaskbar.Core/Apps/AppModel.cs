using NovaTaskbar.Core.Configuration;

namespace NovaTaskbar.Core.Apps;

/// <summary>Instantâneo de uma janela de topo (dados coletados pela camada Windows).</summary>
public sealed record WindowInfo(
    nint Handle,
    int ProcessId,
    string Title,
    string ClassName,
    string? ExePath,
    string? Aumid,
    long Style,
    long ExStyle,
    nint Owner,
    bool IsVisible,
    bool IsCloaked,
    bool IsMinimized);

/// <summary>Identidade de um app fixado: executável de destino e/ou AppUserModelID (apps empacotados, atalhos com AUMID).</summary>
public sealed record PinIdentity(string? ExePath, string? Aumid);

/// <summary>Nome e ícone de um app em execução que não está fixado.</summary>
public sealed record AppPresentation(string DisplayName, string IconPath);

/// <summary>Entrada do Dock: app fixado e/ou em execução, com suas janelas em ordem Z (mais recente primeiro).</summary>
public sealed class DockEntry
{
    public required string Key { get; init; }
    public PinnedApp? Pin { get; init; }
    public required string DisplayName { get; init; }
    /// <summary>Caminho usado para extrair o ícone (exe, lnk ou shell:AppsFolder\AUMID).</summary>
    public required string IconPath { get; init; }
    /// <summary>Caminho para abrir/nova janela (o do pin, ou o executável/AUMID do app em execução).</summary>
    public required string LaunchPath { get; init; }
    public List<WindowInfo> Windows { get; } = new();
    public bool IsActive { get; set; }

    public bool IsPinned => Pin is not null;
    public bool IsRunning => Windows.Count > 0;
}

public static class AppIdentity
{
    public const string PackagedPrefix = @"shell:AppsFolder\";

    public static string NormalizePath(string path) =>
        path.Trim().Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    public static bool TryGetPackagedAumid(string? path, out string aumid)
    {
        aumid = "";
        if (path is null || !path.StartsWith(PackagedPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        aumid = path[PackagedPrefix.Length..];
        return aumid.Length > 0;
    }

    public static string PackagedPath(string aumid) => PackagedPrefix + aumid;

    /// <summary>Chave de agrupamento de janelas: AUMID quando existe (apps empacotados, Chrome, Edge…), senão o executável.</summary>
    public static string KeyForWindow(WindowInfo w)
    {
        if (!string.IsNullOrEmpty(w.Aumid)) return "aumid:" + w.Aumid.ToLowerInvariant();
        if (!string.IsNullOrEmpty(w.ExePath)) return "path:" + NormalizePath(w.ExePath);
        return "pid:" + w.ProcessId;
    }

    public static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool SamePath(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && NormalizePath(a) == NormalizePath(b);
}

/// <summary>
/// Regras de quais janelas aparecem como apps (aproximação documentada das regras da barra de tarefas):
/// visível, não "cloaked" (outra área de trabalho virtual / app suspenso), com título,
/// sem dono ou marcada como APPWINDOW, e não ferramenta/no-activate (a menos que APPWINDOW).
/// </summary>
public static class WindowFilter
{
    public const long WS_EX_TOOLWINDOW = 0x00000080, WS_EX_APPWINDOW = 0x00040000, WS_EX_NOACTIVATE = 0x08000000;

    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",            // janela interna de apps UWP (a visível é a ApplicationFrameWindow)
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow",
    };

    public static bool IsAppWindow(WindowInfo w, int ownProcessId)
    {
        if (!w.IsVisible || w.IsCloaked) return false;
        if (w.ProcessId == ownProcessId) return false;
        if (ExcludedClasses.Contains(w.ClassName)) return false;
        if (string.IsNullOrWhiteSpace(w.Title)) return false;

        bool appWindow = (w.ExStyle & WS_EX_APPWINDOW) != 0;
        if (appWindow) return true;
        if ((w.ExStyle & WS_EX_TOOLWINDOW) != 0) return false;
        if ((w.ExStyle & WS_EX_NOACTIVATE) != 0) return false;
        if (w.Owner != 0) return false;
        return true;
    }
}

public sealed record DockBuildResult(List<DockEntry> Entries, List<string> RunningOrder);

/// <summary>
/// Junta apps fixados e janelas em execução em entradas do Dock. Pura e determinística:
/// fixados na ordem do usuário; apps em execução não fixados depois, em ordem estável de chegada.
/// </summary>
public static class DockItemsBuilder
{
    public static DockBuildResult Build(
        IReadOnlyList<(PinnedApp Pin, PinIdentity Identity)> pins,
        IReadOnlyList<WindowInfo> appWindows,
        nint foreground,
        bool showRunningUnpinned,
        IReadOnlyList<string> previousRunningOrder,
        Func<WindowInfo, AppPresentation> present)
    {
        var entries = new List<DockEntry>(pins.Count + 8);
        foreach (var (pin, _) in pins)
            entries.Add(new DockEntry { Key = "pin:" + pin.Id, Pin = pin, DisplayName = pin.DisplayName, IconPath = pin.Path, LaunchPath = pin.Path });

        var running = new Dictionary<string, DockEntry>(StringComparer.Ordinal);
        foreach (var w in appWindows)
        {
            int pinIndex = MatchPin(pins, w);
            DockEntry entry;
            if (pinIndex >= 0)
            {
                entry = entries[pinIndex];
            }
            else
            {
                string key = AppIdentity.KeyForWindow(w);
                if (!running.TryGetValue(key, out entry!))
                {
                    var pres = present(w);
                    string launch = !string.IsNullOrEmpty(w.Aumid) && IsPackaged(w)
                        ? AppIdentity.PackagedPath(w.Aumid!)
                        : w.ExePath ?? "";
                    entry = new DockEntry { Key = key, DisplayName = pres.DisplayName, IconPath = pres.IconPath, LaunchPath = launch };
                    running[key] = entry;
                }
            }
            entry.Windows.Add(w);
            if (w.Handle == foreground) entry.IsActive = true;
        }

        // Ordem estável: chaves já conhecidas mantêm a posição; novas entram no fim.
        var order = previousRunningOrder.Where(running.ContainsKey).ToList();
        foreach (var key in running.Keys)
            if (!order.Contains(key)) order.Add(key);

        if (showRunningUnpinned)
            foreach (var key in order) entries.Add(running[key]);

        return new DockBuildResult(entries, order);
    }

    /// <summary>Executáveis de apps empacotados ficam em \WindowsApps\; ApplicationFrameHost hospeda apps UWP.</summary>
    public static bool IsPackaged(WindowInfo w) =>
        w.ExePath is { } p &&
        (p.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
         p.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
         p.Contains(@"\SystemApps\", StringComparison.OrdinalIgnoreCase));

    /// <summary>Prioridade: AUMID igual; depois mesmo executável (desde que o AUMID da janela não pertença a outro pin).</summary>
    public static int MatchPin(IReadOnlyList<(PinnedApp Pin, PinIdentity Identity)> pins, WindowInfo w)
    {
        if (!string.IsNullOrEmpty(w.Aumid))
            for (int i = 0; i < pins.Count; i++)
                if (AppIdentity.Same(pins[i].Identity.Aumid, w.Aumid)) return i;

        if (!string.IsNullOrEmpty(w.ExePath))
            for (int i = 0; i < pins.Count; i++)
                if (AppIdentity.SamePath(pins[i].Identity.ExePath, w.ExePath)) return i;

        return -1;
    }
}

public enum ClickActionKind { None, Launch, Activate, Minimize }

public readonly record struct ClickAction(ClickActionKind Kind, nint Window = 0);

/// <summary>
/// O que um clique esquerdo faz numa entrada do Dock:
/// não está aberto → abre; aberto em segundo plano → traz a janela mais recente;
/// já em primeiro plano → minimiza (se configurado) ou, com várias janelas, alterna para a menos recente (ciclo).
/// </summary>
public static class ClickPolicy
{
    public static ClickAction Decide(DockEntry entry, nint foreground, bool minimizeIfActive)
    {
        if (!entry.IsRunning)
            return string.IsNullOrEmpty(entry.LaunchPath) ? new(ClickActionKind.None) : new(ClickActionKind.Launch);

        var wins = entry.Windows; // ordem Z: mais recente primeiro
        bool appIsForeground = wins.Any(w => w.Handle == foreground);
        if (!appIsForeground) return new(ClickActionKind.Activate, wins[0].Handle);

        if (wins.Count == 1)
            return minimizeIfActive ? new(ClickActionKind.Minimize, wins[0].Handle) : new(ClickActionKind.Activate, wins[0].Handle);

        // Várias janelas: trazer a do fundo percorre todas, uma por clique.
        return new(ClickActionKind.Activate, wins[^1].Handle);
    }
}
