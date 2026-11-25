using System.Runtime.InteropServices;

namespace NovaTaskbar.Windows.Interop;

public static partial class NativeMethods
{
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int caretLeft, caretTop, caretRight, caretBottom;
    }

    /// <summary>idThread = 0: a thread em primeiro plano (a janela com o foco pode ser de outro processo que a janela principal).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
}

public static partial class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
}

public static partial class NativeMethods
{
    public const int DWMWA_CLOAK = 13;

    /// <summary>
    /// Esconde/mostra a janela só na composição (DWM). Diferente de ShowWindow(SW_HIDE), não muda o estado de
    /// visibilidade que o WinUI observa — esconder uma janela WinUI com SW_HIDE derruba o XAML (0x80040111).
    /// </summary>
    public static void Cloak(IntPtr hwnd, bool cloaked)
    {
        int v = cloaked ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref v, sizeof(int));
    }
}

public static partial class NativeMethods
{
    /// <summary>Janela maximizada.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd);
}

public static partial class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}
