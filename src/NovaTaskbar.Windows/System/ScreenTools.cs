using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NovaTaskbar.Windows.SystemInfo;

/// <summary>Utilidades de tela/teclado/sistema para os widgets de ferramentas (todas locais, sem administrador).</summary>
public static class ScreenTools
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hdc, int x, int y);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>Cor do pixel da tela em (x, y), coordenadas físicas.</summary>
    public static (byte R, byte G, byte B) PixelAt(int x, int y)
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            uint c = GetPixel(dc, x, y);
            return ((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    /// <summary>Som de aviso do Windows (lembretes, fim do timer).</summary>
    public static void Chime() => MessageBeep(0x40 /* MB_ICONASTERISK */);

    /// <summary>Abre o painel de emoji do Windows (Win + .) no app que está em primeiro plano.</summary>
    public static void OpenEmojiPanel()
    {
        const ushort VK_LWIN = 0x5B, VK_OEM_PERIOD = 0xBE;
        var inputs = new[] { Key(VK_LWIN, false), Key(VK_OEM_PERIOD, false), Key(VK_OEM_PERIOD, true), Key(VK_LWIN, true) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Avança o idioma do teclado pelo atalho do Windows (Win + Espaço), <paramref name="steps"/> vezes.</summary>
    public static void CycleInputLanguage(int steps)
    {
        const ushort VK_LWIN = 0x5B, VK_SPACE = 0x20;
        var inputs = new List<INPUT> { Key(VK_LWIN, false) };
        for (int i = 0; i < Math.Clamp(steps, 1, 20); i++) { inputs.Add(Key(VK_SPACE, false)); inputs.Add(Key(VK_SPACE, true)); }
        inputs.Add(Key(VK_LWIN, true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <summary>Pasta de Downloads real do usuário (pode ter sido movida).</summary>
    public static string DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p) == 0)
        {
            try { return Marshal.PtrToStringUni(p) ?? ""; }
            finally { Marshal.FreeCoTaskMem(p); }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    // ---------------- Modo escuro do Windows (preferência do usuário em HKCU)
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDarkMode()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Personalize);
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    public static void SetDarkMode(bool dark)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Personalize, writable: true);
        k.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        k.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        // Avisa os apps abertos (como o seletor de tema do Windows faz).
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "ImmersiveColorSet", 0x0002 /* SMTO_ABORTIFHUNG */, 200, out _);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = 1, // INPUT_KEYBOARD
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 0x0002u : 0u } },
    };
}

/// <summary>
/// Conta-gotas: captura o próximo clique em qualquer lugar da tela (gancho de mouse de baixo nível, só enquanto ativo)
/// e devolve a cor sob o cursor. Clique direito cancela. O clique capturado não chega ao app embaixo.
/// </summary>
public sealed class ClickCapture : IDisposable
{
    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int x; public int y; public uint mouseData; public uint flags; public uint time; public IntPtr extra; }

    private readonly LowLevelProc _proc;
    private IntPtr _hook;
    private Action<int, int>? _onClick;
    private Action? _onCancel;

    public ClickCapture() => _proc = Hook;

    public bool Active => _hook != IntPtr.Zero;

    /// <summary>Instala o gancho (no thread com laço de mensagens — o UI thread).</summary>
    public bool Start(Action<int, int> onClick, Action onCancel)
    {
        Stop();
        _onClick = onClick;
        _onCancel = onCancel;
        _hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _hook != IntPtr.Zero)
        {
            int msg = (int)wParam;
            if (msg is 0x0201 or 0x0204) // WM_LBUTTONDOWN / WM_RBUTTONDOWN
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var click = _onClick; var cancel = _onCancel;
                Stop();
                if (msg == 0x0201) click?.Invoke(info.x, info.y); else cancel?.Invoke();
                return (IntPtr)1; // consome o clique
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
