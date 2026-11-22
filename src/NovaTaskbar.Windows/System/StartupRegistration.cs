using Microsoft.Win32;

namespace NovaTaskbar.Windows.SystemInfo;

/// <summary>
/// "Iniciar com o Windows" pela chave Run do próprio usuário (HKCU — sem administrador). O valor guarda o caminho
/// do executável entre aspas; se o app foi movido, <see cref="IsEnabled"/> continua verdadeiro e
/// <see cref="Enable"/> corrige o caminho.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "CleanWinVista";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }

    public static bool Enable(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return false;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
        return true;
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static bool Set(bool enabled, string exePath)
    {
        try
        {
            if (enabled) return Enable(exePath);
            Disable();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }
}
