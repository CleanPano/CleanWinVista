using NovaTaskbar.Core.Apps;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Windows.Apps;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Services;

/// <summary>
/// Sugestão inicial de apps fixados: somente apps realmente instalados neste PC (verificados no disco).
/// Executado uma única vez (DefaultsSeeded); se o usuário remover todos, não são recriados.
/// </summary>
internal static class DefaultApps
{
    public static List<PinnedApp> Discover()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        (string Name, string[] Candidates)[] known =
        [
            (L.T("File Explorer"), [Path.Combine(win, "explorer.exe")]),
            ("Microsoft Edge", [Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe")]),
            ("Google Chrome", [Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(local, @"Google\Chrome\Application\chrome.exe")]),
            ("Firefox", [Path.Combine(pf, @"Mozilla Firefox\firefox.exe")]),
            ("Visual Studio Code", [Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe"), Path.Combine(pf, @"Microsoft VS Code\Code.exe")]),
            ("Spotify", [Path.Combine(roaming, @"Spotify\Spotify.exe")]),
            ("Discord", [Path.Combine(roaming, @"Microsoft\Windows\Start Menu\Programs\Discord Inc\Discord.lnk")]),
            ("Steam", [Path.Combine(pf86, @"Steam\steam.exe")]),
            ("OBS Studio", [Path.Combine(pf, @"obs-studio\bin\64bit\obs64.exe")]),
            (L.T("Task Manager"), [Path.Combine(win, @"System32\Taskmgr.exe")]),
            // Calculadora/Bloco de Notas do Windows 11 são apps empacotados: o .exe em System32 é só um atalho
            // com ícone legado. Suporte a apps empacotados (AUMID) entra na Fase 2.
        ];

        var list = new List<PinnedApp>();
        foreach (var (name, candidates) in known)
        {
            string? path = candidates.FirstOrDefault(c => c.Length > 0 && File.Exists(c));
            if (path != null) list.Add(new PinnedApp { Path = path, DisplayName = name });
        }

        // Bloco de Notas moderno (app empacotado) logo após o Explorador — padrão escolhido pelo dono do app.
        string notepad = AppIdentity.PackagedPath("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
        if (AppInfoProvider.ShellItemName(notepad) is { } notepadName)
            list.Insert(list.Count > 0 && list[0].Path.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                new PinnedApp { Path = notepad, DisplayName = notepadName });

        // Claude (app empacotado), se instalado — no fim, como no Dock do dono do app.
        string claude = AppIdentity.PackagedPath("Claude_pzs8sxrjxfjjc!Claude");
        if (AppInfoProvider.ShellItemName(claude) is { } claudeName)
            list.Add(new PinnedApp { Path = claude, DisplayName = claudeName });
        return list;
    }
}
