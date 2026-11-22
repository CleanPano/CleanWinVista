using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.Windows.Interop;
using WinRT.Interop;

namespace NovaTaskbar.App.Dock;

/// <summary>
/// Janela da "pílula" de vidro: só desenha o material (Acrylic/Mica) com cantos e borda do DWM.
/// Fica logo abaixo da janela dos ícones, nunca recebe foco nem entrada (a janela dos ícones a cobre).
/// </summary>
internal sealed class GlassWindow : Window
{
    private NativeMethods.RECT _last;
    private readonly NativeMethods.SubclassProc _subclass;

    public GlassWindow()
    {
        Content = new Grid();
        Backdrop = new GlassBackdrop();
        SystemBackdrop = Backdrop;
        Hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "CleanWinVista Glass";

        NativeMethods.MakePopup(Hwnd);
        NativeMethods.SetExStyle(Hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE, NativeMethods.WS_EX_APPWINDOW);
        int round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(Hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        // Área cliente = janela inteira (sem a moldura WS_DLGFRAME que o presenter recoloca).
        _subclass = (h, msg, w, l, id, data) =>
        {
            if (msg == NativeMethods.WM_NCCALCSIZE && w != IntPtr.Zero) return IntPtr.Zero;
            if (msg == NativeMethods.WM_SYSCOMMAND && (w.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE) return IntPtr.Zero;
            if (msg == NativeMethods.WM_SIZE && w.ToInt64() == NativeMethods.SIZE_MINIMIZED && IsVisible)
                DispatcherQueue.TryEnqueue(() => NativeMethods.ShowNoActivate(Hwnd));
            return NativeMethods.DefSubclassProc(h, msg, w, l);
        };
        NativeMethods.SetWindowSubclass(Hwnd, _subclass, UIntPtr.Zero, UIntPtr.Zero);
        NativeMethods.SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    public IntPtr Hwnd { get; }
    public GlassBackdrop Backdrop { get; }
    public bool IsVisible { get; private set; }

    /// <summary>Cor da borda fina do DWM (Windows 11). null = sem borda.</summary>
    public void SetBorder(bool visible, bool dark)
    {
        uint color = !visible ? NativeMethods.DWMWA_COLOR_NONE : dark ? 0x00585858u : 0x00D8D8D8u; // COLORREF 0x00BBGGRR
        NativeMethods.DwmSetWindowAttribute(Hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref color, sizeof(uint));
    }

    /// <summary>Posiciona a pílula (pixels físicos de tela) imediatamente abaixo da janela dos ícones.</summary>
    public void Place(NativeMethods.RECT r, IntPtr insertAfter)
    {
        if (r.Left == _last.Left && r.Top == _last.Top && r.Right == _last.Right && r.Bottom == _last.Bottom) return;
        _last = r;
        NativeMethods.SetWindowPos(Hwnd, insertAfter, r.Left, r.Top, r.Width, r.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    public void ShowGlass()
    {
        if (IsVisible) return;
        NativeMethods.ShowNoActivate(Hwnd);
        IsVisible = true;
    }

    public void HideGlass()
    {
        if (!IsVisible) return;
        NativeMethods.ShowWindow(Hwnd, NativeMethods.SW_HIDE);
        IsVisible = false;
    }
}
