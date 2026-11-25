using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.App.Widgets;

/// <summary>Ações fixas dos widgets (alvos codificados — nada vem da configuração).</summary>
internal static class WidgetActions
{
    public static void Open(string path) => Execute(path);

    /// <summary>Abre um arquivo/pasta local com o app padrão (só caminhos absolutos que existem; nada de rede/URI).</summary>
    public static bool OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        Execute(path);
        return true;
    }

    /// <summary>Mostra o arquivo selecionado no Explorador.</summary>
    public static void Reveal(string path)
    {
        if (File.Exists(path)) Execute("explorer.exe", $"/select,\"{path}\"");
    }

    /// <summary>Abre um link no navegador padrão (só http/https, sem credenciais embutidas).</summary>
    public static bool OpenUrl(string url)
    {
        if (!NovaTaskbar.Core.Tools.UrlRules.IsSafeWebUrl(url)) return false;
        Execute(url);
        return true;
    }

    /// <summary>Abre um URI de app do Windows conhecido (lista fechada).</summary>
    public static void OpenWindowsUri(string uri)
    {
        if (uri is "ms-screenclip:" or "ms-clock:" or "ms-photos:" or "outlookcal:" || uri.StartsWith("ms-settings:", StringComparison.Ordinal)) Execute(uri);
    }

    public static void OpenSettings(string uri)
    {
        if (uri.StartsWith("ms-settings:", StringComparison.Ordinal)) Execute(uri);
    }

    private static void Execute(string file, string? args = null)
    {
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        var info = new NativeMethods.SHELLEXECUTEINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFO>(),
            fMask = NativeMethods.SEE_MASK_NOASYNC,
            lpVerb = "open",
            lpFile = file,
            lpParameters = args,
            nShow = NativeMethods.SW_SHOWNORMAL,
        };
        NativeMethods.ShellExecuteEx(ref info);
    }

    /// <summary>Janela do Dock (definida pelo DockWindow). Usada para receber o teclado enquanto um painel está aberto.</summary>
    public static IntPtr HostWindow { get; set; }

    /// <summary>Lado em que os painéis abrem (para longe da borda da tela). Definido pelo DockWindow antes do clique.</summary>
    public static FlyoutPlacementMode PanelPlacement { get; set; } = FlyoutPlacementMode.Top;

    public static Flyout ShowFlyout(UIElement content, FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        var f = new Flyout { Content = content, ShouldConstrainToRootBounds = false, Placement = PanelPlacement };
        PanelStyle.Apply(f, wide: true);
        OutsideClickDismiss.Attach(f);
        TakeKeyboard(f);
        f.ShowAt(anchor, new FlyoutShowOptions { Position = at, Placement = PanelPlacement });
        return f;
    }

    /// <summary>
    /// O Dock não ativa ao ser clicado (para não roubar o foco), então o teclado continuava indo para o app de trás
    /// e não dava para digitar nos painéis (notas, calculadora, busca…). Com o painel aberto, o Dock vira a janela
    /// ativa; ao fechar, o foco volta para a janela que estava ativa antes.
    /// </summary>
    private static void TakeKeyboard(Flyout f)
    {
        IntPtr previous = IntPtr.Zero;
        f.Opened += (_, _) =>
        {
            if (HostWindow == IntPtr.Zero) return;
            previous = NativeMethods.GetForegroundWindow();
            if (previous != HostWindow) NativeMethods.SetForegroundWindow(HostWindow);
        };
        f.Closed += (_, _) =>
        {
            if (previous == IntPtr.Zero || previous == HostWindow || !NativeMethods.IsWindow(previous)) return;
            if (NativeMethods.GetForegroundWindow() == HostWindow) NativeMethods.SetForegroundWindow(previous);
            previous = IntPtr.Zero;
        };
    }
}
