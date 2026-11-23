using NovaTaskbar.Core.Configuration;

namespace NovaTaskbar.Core.Layout;

/// <summary>O que está em primeiro plano, reduzido ao que importa para decidir esconder o Dock.</summary>
public readonly record struct ForegroundApp(string? ExeName, string? ClassName, bool IsFullscreen, bool IsShell, bool IsOwn, bool IsMaximized = false);

/// <summary>Regras puras de auto-ocultar (testáveis).</summary>
public static class DockVisibilityRules
{
    /// <summary>Navegadores conhecidos (nome do executável, sem diferenciar maiúsculas).</summary>
    public static readonly IReadOnlySet<string> Browsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "opera_gx.exe", "vivaldi.exe",
        "arc.exe", "zen.exe", "librewolf.exe", "waterfox.exe", "floorp.exe", "thorium.exe", "chromium.exe", "iexplore.exe",
    };

    /// <summary>Janela do Explorador de Arquivos (não a área de trabalho nem a barra de tarefas, que também são explorer.exe).</summary>
    public const string ExplorerWindowClass = "CabinetWClass";

    /// <summary>Esta janela em primeiro plano pede que o Dock saia do caminho?</summary>
    public static bool ShouldDodge(ForegroundApp fg) =>
        !fg.IsShell && !fg.IsOwn &&
        (fg.IsFullscreen || fg.IsMaximized
         || string.Equals(fg.ClassName, ExplorerWindowClass, StringComparison.Ordinal)
         || (fg.ExeName is { } exe && Browsers.Contains(exe)));

    /// <summary>
    /// O cursor pode trazer o Dock de volta? Não quando ele já está escondido e há um app em tela cheia
    /// (vídeo, jogo, F11): passar ou rolar o mouse na parte de baixo da tela não deve fazê-lo aparecer.
    /// </summary>
    public static bool PointerMayReveal(ForegroundApp fg, bool currentlyHidden) =>
        !(currentlyHidden && fg.IsFullscreen && !fg.IsShell && !fg.IsOwn);

    /// <summary>
    /// Decide se o Dock deve estar escondido. <paramref name="pointerNear"/> = cursor no Dock ou na faixa da borda;
    /// <paramref name="busy"/> = menu aberto, arrastando, soltando arquivo.
    /// </summary>
    public static bool ShouldHide(DockVisibility mode, ForegroundApp fg, bool pointerNear, bool busy) => mode switch
    {
        _ when busy || pointerNear => false,
        DockVisibility.AutoHide => true,
        DockVisibility.Smart => ShouldDodge(fg),
        _ => false,
    };
}
