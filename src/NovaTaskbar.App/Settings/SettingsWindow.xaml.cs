using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.App.Dock;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Windows.Interop;
using NovaTaskbar.App.Services;
using Windows.Graphics;
using WinRT.Interop;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Settings;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly GlassBackdrop _glass = new();

    internal SettingsWindow(SettingsViewModel vm, SettingsService settings)
    {
        ViewModel = vm;
        _settings = settings;
        InitializeComponent();

        Title = L.T("CleanWinVista · Settings");
        SystemBackdrop = _glass; // mesmo vidro do Dock (material, cor, intensidade e luminosidade)
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets\\CleanWinVista.ico");

        Hwnd = WindowNative.GetWindowHandle(this);
        double scale = NativeMethods.GetDpiForWindow(Hwnd) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min((int)(580 * scale), work.Width - 40);
        int h = Math.Min((int)(760 * scale), work.Height - 40);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
        if (AppWindow.Presenter is OverlappedPresenter p) { p.IsMaximizable = false; }

        // Textos do XAML estão em inglês: traduz para o idioma escolhido quando a árvore estiver pronta.
        if (Content is FrameworkElement rootEl) rootEl.Loaded += (_, _) => Localizer.Apply(rootEl);
        BuildSwatches();
        ApplyTheme();
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) => _settings.Changed -= OnSettingsChanged;
    }

    public SettingsViewModel ViewModel { get; }

    // ---- Apoiar o projeto (Bitcoin)
    internal const string BitcoinAddress = "bc1qnkpm79xsymwn0w6gyq7hthrhd247r9pcysqs2w";

    private void OnCopyBtc(object sender, RoutedEventArgs e)
    {
        var pkg = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(BitcoinAddress);
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        CopyBtcText.Text = L.T("Copied ✓");
    }

    /// <summary>Abre o app de carteira registrado para links bitcoin: (endereço fixo no código, nada vem da configuração).</summary>
    private void OnOpenBtc(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("bitcoin:" + BitcoinAddress) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { OnCopyBtc(sender, e); CopyBtcText.Text = L.T("No wallet — copied ✓"); }
    }

    internal const string EthereumAddress = "0xaaee7cd8a131bca4372ef14823a9dfe12f2108e3";

    private void OnCopyEth(object sender, RoutedEventArgs e)
    {
        var pkg = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(EthereumAddress);
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        CopyEthText.Text = L.T("Copied ✓");
    }

    private void OnOpenEth(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ethereum:" + EthereumAddress) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { OnCopyEth(sender, e); CopyEthText.Text = L.T("No wallet — copied ✓"); }
    }

    private void OnRestart(object sender, RoutedEventArgs e) => App.Restart();

    private void OnStartupYes(object sender, RoutedEventArgs e) => ViewModel.AnswerStartup(true);
    private void OnStartupNo(object sender, RoutedEventArgs e) => ViewModel.AnswerStartup(false);
    internal IntPtr Hwnd { get; }

    /// <summary>Ativa e traz para frente (a 2ª instância concede o direito de foco via AllowSetForegroundWindow).</summary>
    public void BringToFront()
    {
        Activate();
        NativeMethods.SetForegroundWindow(Hwnd);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyTheme();
        UpdateSwatchSelection();
    }

    /// <summary>Tema e vidro iguais aos do Dock; muda ao vivo enquanto o usuário mexe nos controles.</summary>
    private void ApplyTheme()
    {
        var a = _settings.Current.Appearance;
        bool dark = ThemeHelper.IsDark(a.Theme);
        Root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        // "Translúcido" (Tint) é desenhado pelo próprio Dock; numa janela comum o equivalente é o Acrylic.
        var material = a.Material == GlassMaterial.Mica && GlassBackdrop.IsMicaSupported ? GlassMaterial.Mica : GlassMaterial.Acrylic;
        // Janela de leitura: um pouco mais de cor que o Dock para manter o texto legível sobre qualquer fundo.
        _glass.Configure(material, ThemeHelper.ParseTint(a.TintColor), Math.Max(a.TintOpacity, 0.45), Math.Max(a.LuminosityOpacity, 0.3), dark);
    }

    private void BuildSwatches()
    {
        foreach (var hex in ViewModel.Swatches)
        {
            var b = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(15),
                Tag = hex,
                BorderThickness = new Thickness(2),
            };
            if (string.IsNullOrEmpty(hex))
            {
                b.Content = new FontIcon { Glyph = "", FontSize = 13 }; // automático (tema)
                ToolTipService.SetToolTip(b, L.T("Automatic (follows theme)"));
            }
            else
            {
                var c = ThemeHelper.ParseTint(hex)!.Value;
                b.Background = new SolidColorBrush(c);
                ToolTipService.SetToolTip(b, hex);
            }
            b.Click += (s, _) => ViewModel.SetTintCommand.Execute((string)((Button)s).Tag);
            SwatchPanel.Children.Add(b);
        }
        UpdateSwatchSelection();
    }

    private void UpdateSwatchSelection()
    {
        string current = ViewModel.TintColor;
        foreach (var child in SwatchPanel.Children.OfType<Button>())
        {
            bool selected = string.Equals((string)child.Tag, current, StringComparison.OrdinalIgnoreCase);
            child.BorderBrush = selected
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : new SolidColorBrush(Colors.Transparent);
        }
    }

    private void OnWidgetUp(object sender, RoutedEventArgs e) => ViewModel.MoveWidgetUpCommand.Execute((sender as FrameworkElement)?.Tag);
    private void OnWidgetDown(object sender, RoutedEventArgs e) => ViewModel.MoveWidgetDownCommand.Execute((sender as FrameworkElement)?.Tag);
    private void OnMoveUp(object sender, RoutedEventArgs e) => ViewModel.MoveUpCommand.Execute((sender as FrameworkElement)?.Tag);
    private void OnMoveDown(object sender, RoutedEventArgs e) => ViewModel.MoveDownCommand.Execute((sender as FrameworkElement)?.Tag);
    private void OnRemove(object sender, RoutedEventArgs e) => ViewModel.RemoveAppCommand.Execute((sender as FrameworkElement)?.Tag);
}

