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
        IntPtr fg = NativeMethods.GetForegroundWindow();
        uint tid = fg == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(fg, out _);
        return NativeMethods.GetKeyboardLayout(tid);
    }

    public bool Switch(KeyboardLanguage target)
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        return fg != IntPtr.Zero && NativeMethods.PostMessage(fg, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target.Handle);
    }
}
