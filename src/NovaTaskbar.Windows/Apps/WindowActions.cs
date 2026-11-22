using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.Windows.Apps;

/// <summary>Ações sobre janelas de outros apps. Fechar usa WM_CLOSE (o app pode perguntar se quer salvar).</summary>
public static class WindowActions
{
    /// <summary>Restaura se minimizada e traz para o primeiro plano.</summary>
    public static bool Activate(nint hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)) return false;
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        // Permitido porque o clique no Dock foi o último evento de entrada do usuário (regra de foreground do Windows).
        return NativeMethods.SetForegroundWindow(hwnd);
    }

    public static bool Minimize(nint hwnd) =>
        NativeMethods.IsWindow(hwnd) && NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);

    public static bool Close(nint hwnd) =>
        NativeMethods.IsWindow(hwnd) && NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
}
