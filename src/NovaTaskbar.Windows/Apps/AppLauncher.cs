using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NovaTaskbar.Core.Apps;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.Windows.Apps;

/// <summary>
/// Abre apps com ShellExecuteEx (verbo "open", sem argumentos vindos da configuração).
/// Apps empacotados abrem por shell:AppsFolder\&lt;AUMID&gt;, como o menu Iniciar faz.
/// </summary>
public sealed class AppLauncher(ILogger<AppLauncher> log)
{
    public bool Launch(PinnedApp app) => Launch(app.Path, app.DisplayName);

    public bool Launch(string path, string displayName)
    {
        if (!SettingsValidator.IsAcceptablePath(path, out var reason))
        {
            log.LogWarning("Lançamento recusado ({Reason}): {Path}", reason, path);
            return false;
        }
        bool packaged = AppIdentity.TryGetPackagedAumid(path, out _);
        if (!packaged && !File.Exists(path) && !Directory.Exists(path))
        {
            log.LogWarning("Arquivo não encontrado: {Path}", path);
            return false;
        }

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        var info = new NativeMethods.SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFO>(),
            fMask = NativeMethods.SEE_MASK_NOASYNC,
            lpVerb = "open",
            lpFile = path,
            lpDirectory = packaged || Directory.Exists(path) ? null : Path.GetDirectoryName(path),
            nShow = NativeMethods.SW_SHOWNORMAL,
        };
        var sw = Stopwatch.StartNew();
        if (!NativeMethods.ShellExecuteEx(ref info))
        {
            int err = Marshal.GetLastWin32Error();
            // 1223 = ERROR_CANCELLED (usuário cancelou UAC, por exemplo): não é falha do Dock.
            if (err != 1223) log.LogWarning("ShellExecuteEx falhou para {Path}: {Error}", path, new Win32Exception(err).Message);
            return false;
        }
        log.LogInformation("Aberto {Name} em {Ms} ms", displayName, sw.ElapsedMilliseconds);
        return true;
    }
}
