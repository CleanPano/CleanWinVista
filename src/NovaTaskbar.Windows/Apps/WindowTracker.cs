using Microsoft.Extensions.Logging;
using NovaTaskbar.Core.Apps;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.Windows.Apps;

/// <summary>
/// Acompanha janelas por eventos do sistema (SetWinEventHook, fora do processo) — sem polling.
/// Rajadas de eventos são agrupadas (debounce) e geram um único <see cref="Changed"/> no thread que chamou <see cref="Start"/>.
/// Mudança só de foco atualiza apenas o primeiro plano (barato).
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(60);

    private readonly WindowEnumerator _enumerator = new();
    private readonly ILogger<WindowTracker> _log;
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly List<IntPtr> _hooks = new();
    private SynchronizationContext? _context;
    private Timer? _timer;
    private int _pending; // 0 nada, 1 só foco, 2 estrutura

    public WindowTracker(ILogger<WindowTracker> log)
    {
        _log = log;
        _callback = OnWinEvent; // mantido vivo enquanto os hooks existirem
    }

    public IReadOnlyList<WindowInfo> Windows { get; private set; } = Array.Empty<WindowInfo>();
    public nint Foreground { get; private set; }

    /// <summary>Lista de janelas ou primeiro plano mudou. Disparado no thread de <see cref="Start"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Instala os hooks. Precisa ser chamado num thread com loop de mensagens (UI).</summary>
    public void Start()
    {
        _context = SynchronizationContext.Current ?? throw new InvalidOperationException("WindowTracker.Start requer um SynchronizationContext (UI thread).");
        _timer = new Timer(_ => _context.Post(_ => Flush(), null), null, Timeout.Infinite, Timeout.Infinite);

        uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
        Hook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, flags);
        Hook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND, flags);
        Hook(NativeMethods.EVENT_OBJECT_CREATE, NativeMethods.EVENT_OBJECT_HIDE, flags);       // create, destroy, show, hide
        Hook(NativeMethods.EVENT_OBJECT_NAMECHANGE, NativeMethods.EVENT_OBJECT_NAMECHANGE, flags);
        Hook(NativeMethods.EVENT_OBJECT_CLOAKED, NativeMethods.EVENT_OBJECT_UNCLOAKED, flags);
        _log.LogInformation("WindowTracker: {Count} hooks instalados", _hooks.Count);
        Refresh();
    }

    /// <summary>Reenumera imediatamente (usado na inicialização e por ações do próprio Dock).</summary>
    public void Refresh()
    {
        Windows = _enumerator.Snapshot();
        Foreground = NativeMethods.GetForegroundWindow();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Hook(uint min, uint max, uint flags)
    {
        IntPtr h = NativeMethods.SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0, flags);
        if (h == IntPtr.Zero) _log.LogWarning("SetWinEventHook falhou para 0x{Min:X}-0x{Max:X}", min, max);
        else _hooks.Add(h);
    }

    private void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Só a própria janela (não controles filhos, cursor, cursor de texto…).
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF || hwnd == IntPtr.Zero) return;

        int level;
        if (ev == NativeMethods.EVENT_SYSTEM_FOREGROUND) level = 1;
        else if (ev == NativeMethods.EVENT_OBJECT_NAMECHANGE)
        {
            // Títulos mudam o tempo todo (navegadores, players): só interessam para janelas já listadas
            // ou janelas de topo visíveis que podem virar app ao ganhar título.
            if (!IsKnown(hwnd) && (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd || !NativeMethods.IsWindowVisible(hwnd))) return;
            level = 2;
        }
        else
        {
            // Create/destroy/show/hide disparam para muitas janelas internas; só janelas de topo importam.
            if (ev != NativeMethods.EVENT_OBJECT_DESTROY && NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return;
            if (ev == NativeMethods.EVENT_OBJECT_DESTROY && !IsKnown(hwnd)) return;
            level = 2;
        }

        if (level > _pending) _pending = level;
        _timer?.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private bool IsKnown(IntPtr hwnd)
    {
        foreach (var w in Windows) if (w.Handle == hwnd) return true;
        return false;
    }

    private void Flush()
    {
        int level = _pending;
        _pending = 0;
        if (level == 0) return;
        if (level >= 2)
        {
            Refresh();
            return;
        }
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == Foreground) return;
        Foreground = fg;
        // Uma janela que acabou de ganhar foco pode ser nova e ainda não estar na lista.
        if (!IsKnown(fg)) { Refresh(); return; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        foreach (var h in _hooks) NativeMethods.UnhookWinEvent(h);
        _hooks.Clear();
        _timer?.Dispose();
        _timer = null;
    }
}
