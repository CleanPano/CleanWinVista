using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using NovaTaskbar.Windows.Interop;
using NovaTaskbar.Windows.SystemInfo;

namespace NovaTaskbar.App.Setup;

/// <summary>
/// Instalação por usuário (sem administrador): copia os arquivos para %LOCALAPPDATA%\Programs\CleanWinVista,
/// cria atalhos, registra em "Aplicativos instalados" (HKCU) e grava o idioma escolhido. Nada fora do perfil do usuário.
/// </summary>
internal static class Installer
{
    public const string ProductName = "CleanWinVista";
    public const string ExeName = "NovaTaskbar.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CleanWinVista";

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);
    public static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName + ".lnk");
    private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk");
    private static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaTaskbar");

    public sealed record Options(string Folder, string Language, bool StartWithWindows, bool StartMenu, bool Desktop, bool CleanPano = false);

    /// <summary>Lista dos arquivos instalados (relativos). A desinstalação apaga só o que está aqui.</summary>
    private const string ManifestName = "install-manifest.txt";
    /// <summary>Arquivos extras do pacote (ex.: instalador do CleanPano) — nunca copiados para a pasta do app.</summary>
    private const string ExtrasDir = "extras";

    /// <summary>
    /// Pasta real de instalação: sempre uma subpasta dedicada "CleanWinVista". Escolher "C:\Ferramentas" instala em
    /// "C:\Ferramentas\CleanWinVista" — assim desinstalar nunca alcança arquivos que não são do app.
    /// </summary>
    public static string ResolveTarget(string chosen)
    {
        string full = Path.GetFullPath(chosen.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(full), ProductName, StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, ProductName);
    }

    /// <summary>Pasta instalada atual (do registro), se houver.</summary>
    public static string? InstalledFolder()
    {
        using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return k?.GetValue("InstallLocation") as string;
    }

    /// <summary>Pasta de destino aceitável: absoluta, local, sem caracteres inválidos e fora de pastas do sistema.</summary>
    public static bool IsValidFolder(string folder, out string error)
    {
        error = "";
        // %, ^, ! e " têm significado para o cmd.exe usado na limpeza final — recusados.
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal)
            || folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || folder.IndexOfAny(['%', '^', '!', '"']) >= 0)
        { error = "Choose a valid local folder."; return false; }
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (folder.StartsWith(win, StringComparison.OrdinalIgnoreCase)) { error = "Choose a folder outside the Windows folder."; return false; }
        return true;
    }

    /// <summary>Fecha o Dock se estiver rodando (para poder substituir os arquivos).</summary>
    public static void StopRunning()
    {
        int me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            using (p)
            {
                if (p.Id == me) continue;
                try { p.CloseMainWindow(); if (!p.WaitForExit(1500)) { p.Kill(); p.WaitForExit(3000); } }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
    }

    public static void Install(Options o, IProgress<(double Fraction, string File)> progress)
    {
        string source = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string target = ResolveTarget(o.Folder);
        LogStep("Install: StopRunning");
        StopRunning();
        LogStep("Install: copy");
        Directory.CreateDirectory(target);

        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetRelativePath(source, f).StartsWith(ExtrasDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(Path.GetFileName(f), ManifestName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            File.WriteAllLines(Path.Combine(target, ManifestName), files.Select(f => Path.GetRelativePath(source, f)));
            for (int i = 0; i < files.Count; i++)
            {
                string rel = Path.GetRelativePath(source, files[i]);
                string dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Copy(files[i], dest, overwrite: true); break; }
                    catch (IOException) when (attempt < 5) { Thread.Sleep(400); }
                }
                progress.Report(((i + 1) / (double)files.Count * 0.9, rel));
            }
        }

        string exe = Path.Combine(target, ExeName);
        LogStep("Install: shortcuts/registry");
        progress.Report((0.92, "Shortcuts"));
        if (o.StartMenu) ShortcutWriter.Create(StartMenuLink, exe, null, "macOS-style dock for Windows");
        else TryDelete(StartMenuLink);
        if (o.Desktop) ShortcutWriter.Create(DesktopLink, exe, null, "macOS-style dock for Windows");
        else TryDelete(DesktopLink);

        progress.Report((0.95, "Registry"));
        StartupRegistration.Set(o.StartWithWindows, exe);
        RegisterUninstall(target, exe);
        WriteSettings(o.Language);
        progress.Report((1.0, ""));
    }

    private static void RegisterUninstall(string folder, string exe)
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", ProductName);
        k.SetValue("DisplayVersion", Version);
        k.SetValue("Publisher", ProductName);
        k.SetValue("DisplayIcon", exe);
        k.SetValue("InstallLocation", folder);
        k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        long kb = 0;
        try { kb = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        k.SetValue("EstimatedSize", (int)Math.Min(kb, int.MaxValue), RegistryValueKind.DWord);
    }

    /// <summary>Grava idioma e "já perguntou sobre iniciar com o Windows" no settings.json (cria se não existir).</summary>
    private static void WriteSettings(string language)
    {
        Directory.CreateDirectory(DataFolder);
        string file = Path.Combine(DataFolder, "settings.json");
        JsonObject root;
        try { root = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new() : new(); }
        catch (System.Text.Json.JsonException) { root = new(); }
        if (root["behavior"] is not JsonObject behavior) { behavior = new JsonObject(); root["behavior"] = behavior; }
        behavior["language"] = language;
        behavior["startupAsked"] = true;
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>Desinstala: atalhos, inicialização, registro e arquivos (o .exe em uso é apagado logo após fechar).</summary>
    public static void Uninstall(bool removeData)
    {
        StopRunning();
        string? folder = InstalledFolder() ?? Path.GetDirectoryName(Environment.ProcessPath);
        TryDelete(StartMenuLink);
        TryDelete(DesktopLink);
        StartupRegistration.Disable();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        if (removeData) TryDeleteDir(DataFolder);
        if (folder is null || !IsValidFolder(folder, out _) || !File.Exists(Path.Combine(folder, ExeName))) return;
        folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);

        // Só os arquivos que o instalador colocou (manifesto). Sem manifesto, só se a pasta for a dedicada "CleanWinVista".
        string manifest = Path.Combine(folder, ManifestName);
        IEnumerable<string> files;
        if (File.Exists(manifest))
            files = File.ReadAllLines(manifest).Select(r => r.Trim()).Where(r => r.Length > 0)
                .Select(r => Path.GetFullPath(Path.Combine(folder, r)))
                .Where(f => f.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)); // nada fora da pasta
        else if (string.Equals(Path.GetFileName(folder), ProductName, StringComparison.OrdinalIgnoreCase))
            files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList();
        else return;

        foreach (var f in files.ToList())
            if (!string.Equals(f, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
        TryDelete(manifest);
        // Subpastas que ficaram vazias (de baixo para cima); nada com conteúdo é apagado.
        foreach (var d in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        // O .exe em uso e a pasta (se vazia): o cmd espera este processo fechar. rmdir sem /s nunca apaga conteúdo.
        string exe = Environment.ProcessPath ?? Path.Combine(folder, ExeName);
        var psi = new ProcessStartInfo("cmd.exe", $"/d /c ping 127.0.0.1 -n 3 >nul & del /f /q \"{exe}\" & rmdir \"{folder}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
        };
        try { Process.Start(psi); } catch (System.ComponentModel.Win32Exception) { }
    }

    // ---------------------------------------------------------------- CleanPano (opcional)

    /// <summary>SHA-256 do instalador oficial do CleanPano 1.16.1 empacotado. Arquivo diferente → opção some.</summary>
    private const string CleanPanoSha256 = "e687d3a564673127c52db0d69b120a80768a23058dee8528d3f17d06eee01a4c";

    private static string CleanPanoPath => Path.Combine(AppContext.BaseDirectory, ExtrasDir, "CleanPano-Setup.exe");

    /// <summary>O instalador do CleanPano veio no pacote e é o oficial (hash confere)?</summary>
    public static bool CleanPanoAvailable()
    {
        try
        {
            if (!File.Exists(CleanPanoPath)) return false;
            using var fs = File.OpenRead(CleanPanoPath);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)).Equals(CleanPanoSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Abre o assistente do CleanPano (com a tela dele e o pedido de administrador do Windows) e espera terminar.
    /// Nunca silencioso. Confere o hash de novo logo antes de executar.
    /// </summary>
    public static void RunCleanPanoSetup()
    {
        if (!CleanPanoAvailable()) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(CleanPanoPath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(CleanPanoPath)! });
            p?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception) { } // UAC recusado ou cancelado
    }

    /// <summary>Log do instalador: %TEMP%\CleanWinVista-setup.log (o app ainda não tem logger nesse modo).</summary>
    [ThreadStatic] private static bool _logging;
    public static void LogStep(string step)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "CleanWinVista-setup.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {step}{Environment.NewLine}"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public static void LogError(string where, Exception ex)
    {
        if (_logging) return; // FirstChanceException dentro do próprio log não pode virar recursão
        _logging = true;
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "CleanWinVista-setup.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] v{Version} {where}: {ex}{Environment.NewLine}"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { _logging = false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
