using System.Runtime.InteropServices;

namespace NovaTaskbar.Windows.Interop;

/// <summary>P/Invoke Win32/DWM. Apenas APIs documentadas. Ver docs/WINDOWS-INTEGRATION.md.</summary>
public static partial class NativeMethods
{
    // ---------- Estilos de janela ----------
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_TOPMOST = 0x00000008;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004,
        SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040,
        SWP_NOOWNERZORDER = 0x0200, SWP_ASYNCWINDOWPOS = 0x4000;

    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    public const int ASFW_ANY = -1;

    // ---------- Monitores ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    public const int MDT_EFFECTIVE_DPI = 0;

    // ---------- Subclasse de janela (mensagens de sistema) ----------
    public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    public const uint WM_DISPLAYCHANGE = 0x007E, WM_SETTINGCHANGE = 0x001A, WM_DPICHANGED = 0x02E0,
        WM_MOUSEACTIVATE = 0x0021, WM_NCDESTROY = 0x0082, WM_NCCALCSIZE = 0x0083, WM_SIZE = 0x0005, WM_SYSCOMMAND = 0x0112;
    public const int SIZE_MINIMIZED = 1, SC_MINIMIZE = 0xF020;
    public const int MA_NOACTIVATE = 3;
    public const int SPI_SETWORKAREA = 0x002F;

    // ---------- DWM ----------
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_NCRENDERING_POLICY = 2;
    public const int DWMWCP_DEFAULT = 0, DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    public const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    // ---------- Shell ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    public const uint SEE_MASK_NOASYNC = 0x00000100, SEE_MASK_FLAG_NO_UI = 0x00000400, SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    public const int SW_SHOWNORMAL = 1;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr h);

    // ---------- DispatcherQueue do sistema (necessário para Windows.UI.Composition) ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct DispatcherQueueOptions { public int dwSize; public int threadType; public int apartmentType; }

    [DllImport("CoreMessaging.dll")]
    public static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr dispatcherQueueController);

    public const long WS_POPUP = 0x80000000L, WS_CAPTION = 0x00C00000L, WS_THICKFRAME = 0x00040000L,
        WS_SYSMENU = 0x00080000L, WS_MINIMIZEBOX = 0x00020000L, WS_MAXIMIZEBOX = 0x00010000L, WS_MINIMIZE = 0x20000000L,
        WS_CLIPCHILDREN = 0x02000000L, WS_CLIPSIBLINGS = 0x04000000L;

    /// <summary>Transforma a janela em popup puro (sem moldura, legenda ou caixas de sistema).</summary>
    public static void MakePopup(IntPtr hwnd)
    {
        long st = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        st &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_MINIMIZE);
        st |= WS_POPUP | WS_CLIPCHILDREN | WS_CLIPSIBLINGS;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(st));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>Mostra sem ativar (AppWindow.Show(false) mostra minimizado).</summary>
    public static void ShowNoActivate(IntPtr hwnd) => ShowWindow(hwnd, SW_SHOWNOACTIVATE);

    public static void SetExStyle(IntPtr hwnd, long add, long remove)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | add) & ~remove;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}
