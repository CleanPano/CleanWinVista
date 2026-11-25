using Microsoft.UI.Dispatching;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Core.Layout;
using NovaTaskbar.Core.Magnification;
using NovaTaskbar.Windows.Apps;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.App.Dock;

/// <summary>
/// Auto-ocultar: no modo Inteligente o Dock desliza para fora da tela quando um navegador, o Explorador de Arquivos
/// ou um app em tela cheia está em primeiro plano; no modo Auto-ocultar, sempre. Encostar o mouse na borda o traz de volta.
/// Um timer de 100 ms só existe com o recurso ligado (GetForegroundWindow + GetCursorPos — custo desprezível).
/// </summary>
public sealed partial class DockWindow
{
    private const int EdgeBandPx = 3;                                    // faixa da borda que revela o Dock
    private const int RevealSlackPx = 24;                                // folga ao redor da pílula antes de contar "saiu"
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(700);
    private readonly ForegroundWindow _foreground = new();
    private DispatcherQueueTimer? _visibilityTimer;
    private Spring _hide = new(0, 0.30, 1.0, 0.002);                     // 0 = visível, 1 = escondido
    private DateTime _pointerLeftUtc = DateTime.MinValue;
    private int _hideDx, _hideDy;                                        // deslocamento aplicado (px físicos)
    private bool _windowsHidden;
    private NativeMethods.RECT _work, _monitorRect;

    private void ApplyVisibilityMode(DockSettings s)
    {
        if (s.Behavior.Visibility == DockVisibility.AlwaysVisible)
        {
            _visibilityTimer?.Stop();
            SetHidden(false);
            return;
        }
        if (_visibilityTimer is null)
        {
            _visibilityTimer = DispatcherQueue.CreateTimer();
            _visibilityTimer.Interval = TimeSpan.FromMilliseconds(100);
            _visibilityTimer.Tick += (_, _) => VisibilityTick();
        }
        if (!_visibilityTimer.IsRunning) _visibilityTimer.Start();
        VisibilityTick();
    }

    private void VisibilityTick()
    {
        if (_closing || _geo is null) return;
        var fg = _foreground.Read();
        NativeMethods.GetCursorPos(out var pt);
        bool near = PointerNearDock(pt) && DockVisibilityRules.PointerMayReveal(fg, _hide.Target >= 0.5);
        var now = DateTime.UtcNow;

        // Dock trazido pelo mouse por cima de um app que o faria sumir (tela cheia, maximizado, navegador…):
        // um clique fora do Dock e dos seus painéis o esconde na hora, sem espera nem animação.
        bool clicked = NewClick();
        if (clicked && _hide.Target < 0.5 && !_dragging && !_dropHover
            && DockVisibilityRules.ShouldHide(_settings.Current.Behavior.Visibility, fg, false, false)
            && !NovaTaskbar.App.Widgets.OutsideClickDismiss.OverOwnWindow())
        {
            _pointerLeftUtc = DateTime.MinValue;
            SetHidden(true);
            _hide.Snap(1);
            EnsureRendering();
            return;
        }

        if (near) _pointerLeftUtc = DateTime.MaxValue;
        else if (_pointerLeftUtc == DateTime.MaxValue) _pointerLeftUtc = now;

        bool busy = _menuOpen || _dragging || _dropHover;
        bool hide = DockVisibilityRules.ShouldHide(_settings.Current.Behavior.Visibility, fg, near, busy);
        if (hide && _hide.Target < 0.5 && now - _pointerLeftUtc < HideDelay) return; // acabou de sair do Dock: espera um pouco
        SetHidden(hide);
    }

    private bool _mouseWasDown;

    /// <summary>Um botão do mouse foi pressionado desde o último tick (inclui cliques mais curtos que os 100 ms do timer).</summary>
    private bool NewClick()
    {
        bool down = false, since = false;
        foreach (int vk in (ReadOnlySpan<int>)[0x01, 0x02, 0x04]) // esquerdo, direito, meio
        {
            short s = NativeMethods.GetAsyncKeyState(vk);
            down |= (s & 0x8000) != 0;
            since |= (s & 1) != 0;
        }
        bool click = !_mouseWasDown && (down || since);
        _mouseWasDown = down;
        return click;
    }

    private void SetHidden(bool hidden)
    {
        double target = hidden ? 1 : 0;
        if (_hide.Target == target) return;
        _hide.Target = target;
        if (!hidden) ShowDockWindows();
        if (_reduceMotion) _hide.Snap(target);
        EnsureRendering();
    }

    /// <summary>Visível: cursor sobre a pílula (com folga) ou magnificando. Escondido: cursor na faixa da borda, no trecho do Dock.</summary>
    private bool PointerNearDock(NativeMethods.POINT pt)
    {
        var pill = _lastPillWindow;
        int l = _window.Left + _hideDx + (int)(pill.X * _scale), t = _window.Top + _hideDy + (int)(pill.Y * _scale);
        int r = _window.Left + _hideDx + (int)(pill.Right * _scale), b = _window.Top + _hideDy + (int)(pill.Bottom * _scale);
        if (_hide.Target < 0.5)
            return _anim.IsHovering || (pt.X >= l - RevealSlackPx && pt.X <= r + RevealSlackPx && pt.Y >= t - RevealSlackPx && pt.Y <= b + RevealSlackPx);

        // Escondido: o trecho ao longo da borda é o da janela (pílula na posição de repouso).
        int wl = _window.Left, wt = _window.Top, wr = _window.Right, wb = _window.Bottom;
        return _orient.Edge switch
        {
            DockEdge.Top => pt.Y <= _work.Top + EdgeBandPx && pt.Y >= _monitorRect.Top && pt.X >= wl && pt.X <= wr,
            DockEdge.Left => pt.X <= _work.Left + EdgeBandPx && pt.X >= _monitorRect.Left && pt.Y >= wt && pt.Y <= wb,
            DockEdge.Right => pt.X >= _work.Right - 1 - EdgeBandPx && pt.X <= _monitorRect.Right && pt.Y >= wt && pt.Y <= wb,
            _ => pt.Y >= _work.Bottom - 1 - EdgeBandPx && pt.Y <= _monitorRect.Bottom && pt.X >= wl && pt.X <= wr,
        };
    }

    /// <summary>Avança a animação de esconder; move as duas janelas (Dock + vidro). Retorna true enquanto anima.</summary>
    private bool StepHide(double dt)
    {
        if (_geo is null) return false;
        _hide.Step(dt);
        double f = Math.Clamp(_hide.Value, 0, 1);
        // Distância: da pílula até sair da área de trabalho (altura da pílula + margem da borda + folga).
        double dist = (_geo.PillHeight + _settings.Current.Layout.ScreenMargin + 6) * _scale * f;
        int d = (int)Math.Round(dist);
        var (dx, dy) = _orient.Edge switch
        {
            DockEdge.Top => (0, -d),
            DockEdge.Left => (-d, 0),
            DockEdge.Right => (d, 0),
            _ => (0, d),
        };
        if (dx != _hideDx || dy != _hideDy)
        {
            _hideDx = dx;
            _hideDy = dy;
            NativeMethods.SetWindowPos(Hwnd, IntPtr.Zero, _window.Left + dx, _window.Top + dy, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
        bool settled = _hide.IsSettled;
        if (settled && _hide.Target >= 1 && !_windowsHidden) HideDockWindows(); // fora da tela: some de vez (sem fresta)
        return !settled;
    }

    /// <summary>Fora da tela: "camufla" as duas janelas no DWM (sem SW_HIDE, que o WinUI não tolera).</summary>
    private void HideDockWindows()
    {
        _windowsHidden = true;
        NativeMethods.Cloak(Hwnd, true);
        NativeMethods.Cloak(_glass.Hwnd, true);
    }

    private void ShowDockWindows()
    {
        if (!_windowsHidden) return;
        _windowsHidden = false;
        NativeMethods.Cloak(Hwnd, false);
        NativeMethods.Cloak(_glass.Hwnd, false);
    }
}
