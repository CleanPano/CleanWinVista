using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NovaTaskbar.Windows.SystemInfo;

/// <summary>
/// Ações de energia do widget do Dock (sem administrador). Desligar/reiniciar/sair usam o shutdown.exe do próprio
/// Windows (caminho fixo em System32, argumentos fixos): o sistema fecha os apps do jeito normal e avisa se algum
/// tiver trabalho não salvo (sem /f, nada é forçado).
/// </summary>
public static class PowerActions
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    /// <summary>Bloqueia a sessão (Win + L).</summary>
    public static bool Lock() => LockWorkStation();

    /// <summary>Suspende o PC. Só retorna quando ele acorda — chamar fora do UI thread.</summary>
    public static bool Sleep() => SetSuspendState(false, false, false);

    public static bool SignOut() => Shutdown("/l");
    public static bool Restart() => Shutdown("/r /t 0");
    public static bool ShutDown() => Shutdown("/s /t 0");

    private static bool Shutdown(string arguments)
    {
        string exe = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        if (!File.Exists(exe)) return false;
        using var p = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true });
        return p is not null;
    }
}
