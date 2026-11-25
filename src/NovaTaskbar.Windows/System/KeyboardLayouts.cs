using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.Windows.SystemInfo;

/// <summary>
/// Idiomas de teclado instalados e o idioma da janela em primeiro plano. Trocar = pedir à janela ativa
/// (WM_INPUTLANGCHANGEREQUEST), como o seletor do Windows faz; o Dock nunca rouba o foco.
/// </summary>
public sealed class KeyboardLayouts
{
    public IReadOnlyList<KeyboardLanguage> Installed()
    {
        int n = NativeMethods.GetKeyboardLayoutList(0, null);
        if (n <= 0) return [];
        var list = new IntPtr[n];
        n = NativeMethods.GetKeyboardLayoutList(n, list);
        return KeyboardLanguages.Describe(list.Take(Math.Max(0, n)));
    }

    /// <summary>Layout da janela em primeiro plano (o que o usuário está digitando).</summary>
    public IntPtr Current()
    {
        IntPtr h = FocusWindow();
        uint tid = h == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(h, out _);
        return NativeMethods.GetKeyboardLayout(tid);
    }

    /// <summary>
    /// Janela que realmente recebe o teclado. Em muitos apps (Store/UWP, navegadores, Office) ela não é a janela
    /// principal nem está na mesma thread — pedir a troca só à janela principal às vezes não fazia nada.
    /// </summary>
    private static IntPtr FocusWindow()
    {
        var info = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        if (NativeMethods.GetGUIThreadInfo(0, ref info) && info.hwndFocus != IntPtr.Zero) return info.hwndFocus;
        return NativeMethods.GetForegroundWindow();
    }

    public bool Switch(KeyboardLanguage target)
    {
        IntPtr focus = FocusWindow(), fg = NativeMethods.GetForegroundWindow();
        bool posted = focus != IntPtr.Zero && NativeMethods.PostMessage(focus, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target.Handle);
        if (fg != IntPtr.Zero && fg != focus) posted |= NativeMethods.PostMessage(fg, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target.Handle);
        return posted;
    }

    /// <summary>
    /// Confere, um instante depois de <see cref="Switch"/>, se o idioma mudou. Se a janela ignorou o pedido
    /// (app que não repassa a mensagem, janela de administrador), troca pelo atalho do próprio Windows (Win + Espaço).
    /// </summary>
    public void EnsureSwitched(KeyboardLanguage target, IReadOnlyList<KeyboardLanguage> all)
    {
        IntPtr now = Current();
        if (now == IntPtr.Zero || now == target.Handle) return;
        int from = -1, to = -1;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Handle == now) from = i;
            if (all[i].Handle == target.Handle) to = i;
        }
        if (from < 0 || to < 0) return;
        int steps = ((to - from) % all.Count + all.Count) % all.Count;
        if (steps > 0) ScreenTools.CycleInputLanguage(steps);
    }
}
