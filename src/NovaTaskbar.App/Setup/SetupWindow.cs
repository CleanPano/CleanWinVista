using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NovaTaskbar.App.Dock;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Core.Localization;
using NovaTaskbar.Windows.Interop;
using Windows.Graphics;
using WinRT.Interop;

namespace NovaTaskbar.App.Setup;

/// <summary>
/// Instalador e desinstalador no visual do Dock: janela de vidro (Acrylic escuro), barra lateral com as etapas,
/// cartão de conteúdo e botões no rodapé. Etapas: idioma → opções → instalando → pronto. Todos os textos em inglês
/// passam por L.T, e trocar o idioma na 1ª etapa traduz o instalador na hora.
/// </summary>
internal sealed class SetupWindow : Window
{
    private enum Step { Language, Options, Installing, Done, Uninstall, Uninstalling, Removed }

    private readonly bool _uninstall;
    private readonly string _i18n = Path.Combine(AppContext.BaseDirectory, "i18n");
    private readonly GlassBackdrop _glass = new();
    private readonly Grid _root = new() { RequestedTheme = ElementTheme.Dark };
    private readonly StackPanel _steps = new() { Spacing = 14, Margin = new Thickness(0, 30, 0, 0) };
    private readonly Grid _page = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _titleText = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private Step _step;

    // escolhas
    private string _language = L.SystemDefault();
    private string _folder = Installer.InstalledFolder() ?? Installer.DefaultFolder;
    private bool _startWithWindows = true, _startMenu = true, _desktop, _launch = true, _removeData;
    private bool _cleanPano;
    private readonly bool _cleanPanoAvailable = Installer.CleanPanoAvailable();

    public static void Run(bool uninstall)
    {
        var w = new SetupWindow(uninstall);
        w.Closed += (_, _) => Application.Current.Exit();
        w.Activate();
    }

    private SetupWindow(bool uninstall)
    {
        _uninstall = uninstall;
        if (uninstall) _language = InstalledLanguage();
        L.Load(_language, _i18n);
        _step = uninstall ? Step.Uninstall : Step.Language;

        SystemBackdrop = _glass;
        _glass.Configure(GlassMaterial.Acrylic, null, 0.55, 0.35, dark: true);
        ExtendsContentIntoTitleBar = true;
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "CleanWinVista.ico")); } catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.COMException) { }

        // ---- moldura
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Padding = new Thickness(16, 0, 0, 0) };
        titleBar.Children.Add(Logo(18));
        titleBar.Children.Add(_titleText);
        Grid.SetColumnSpan(titleBar, 2);
        _root.Children.Add(titleBar);
        SetTitleBar(titleBar);

        // Barra lateral: marca + etapas
        var side = new StackPanel { Padding = new Thickness(26, 18, 18, 18) };
        side.Children.Add(Logo(56));
        side.Children.Add(new TextBlock { Text = Installer.ProductName, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        side.Children.Add(new TextBlock { Text = "v" + Installer.Version, Opacity = 0.55, FontSize = 12 });
        side.Children.Add(_steps);
        Grid.SetRow(side, 1);
        Grid.SetRowSpan(side, 2);
        _root.Children.Add(side);

        // Conteúdo em cartão de vidro
        var card = new Border
        {
            Background = Res("LayerOnAcrylicFillColorDefaultBrush"), BorderBrush = Res("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(28, 24, 28, 24), Margin = new Thickness(0, 10, 24, 12), Child = _page,
        };
        Grid.SetRow(card, 1);
        Grid.SetColumn(card, 1);
        _root.Children.Add(card);

        _buttons.Margin = new Thickness(0, 0, 24, 22);
        Grid.SetRow(_buttons, 2);
        Grid.SetColumn(_buttons, 1);
        _root.Children.Add(_buttons);

        Content = _root;

        // Tamanho fixo, centralizado, sem maximizar
        var hwnd = WindowNative.GetWindowHandle(this);
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = (int)(780 * scale), h = (int)(590 * scale);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
        if (AppWindow.Presenter is OverlappedPresenter p) { p.IsMaximizable = false; p.IsMinimizable = false; p.IsResizable = false; }

        Render();
    }

    private static string InstalledLanguage()
    {
        try
        {
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaTaskbar", "settings.json");
            if (File.Exists(file) && System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))?["behavior"]?["language"]?.GetValue<string>() is { } c) return L.Normalize(c);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException) { }
        return L.SystemDefault();
    }

    // ================================================================ render

    private void Render()
    {
        Title = L.T(_uninstall ? "Uninstall CleanWinVista" : "CleanWinVista Setup");
        _titleText.Text = Title;
        _root.FlowDirection = L.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        RenderSteps();
        _page.Children.Clear();
        _buttons.Children.Clear();
        _page.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _step switch
        {
            Step.Language => LanguagePage(),
            Step.Options => OptionsPage(),
            Step.Installing => ProgressPage(L.T("Installing…")),
            Step.Done => DonePage(),
            Step.Uninstall => UninstallPage(),
            Step.Uninstalling => ProgressPage(L.T("Removing…")),
            _ => RemovedPage(),
        } });
    }

    private void RenderSteps()
    {
        _steps.Children.Clear();
        if (_uninstall) return;
        var all = new[] { (Step.Language, "Language"), (Step.Options, "Options"), (Step.Installing, "Install"), (Step.Done, "Finish") };
        foreach (var (s, label) in all)
        {
            bool done = s < _step, current = s == _step;
            var dot = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.5),
                BorderBrush = Res(current || done ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"),
                Background = done ? Res("TextFillColorPrimaryBrush") : new SolidColorBrush(Colors.Transparent),
                Child = done ? (UIElement)new FontIcon { Glyph = "", FontSize = 11, Foreground = Res("SolidBackgroundFillColorBaseBrush") }
                    : current ? new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Res("TextFillColorPrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : null,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = L.T(label), VerticalAlignment = VerticalAlignment.Center, Opacity = current ? 1 : done ? 0.8 : 0.45, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal });
            _steps.Children.Add(row);
        }
    }

    // ---------------------------------------------------------------- 1. idioma

    private FrameworkElement LanguagePage()
    {
        var p = Page(L.T("Welcome to CleanWinVista"), L.T("A clean, macOS-style dock for Windows. Choose the language of the app:"));
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < L.Languages.Count; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lang = L.Languages[i];
            bool selected = lang.Code == _language;
            var content = new StackPanel { Spacing = 0 };
            content.Children.Add(new TextBlock { Text = lang.NativeName, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal });
            if (lang.Code != "en") content.Children.Add(new TextBlock { Text = lang.EnglishName, FontSize = 11, Opacity = 0.55 });
            var b = new Button
            {
                Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 6, 12, 6), MinHeight = 44, CornerRadius = new CornerRadius(8),
                Background = selected ? Res("SubtleFillColorSecondaryBrush") : new SolidColorBrush(Colors.Transparent),
                BorderBrush = selected ? Res("TextFillColorPrimaryBrush") : Res("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(selected ? 1.5 : 1),
            };
            string code = lang.Code;
            b.Click += (_, _) => { _language = code; L.Load(code, _i18n); Render(); };
            Grid.SetRow(b, i / 2);
            Grid.SetColumn(b, i % 2);
            grid.Children.Add(b);
        }
        p.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) });
        AddButton(L.T("Cancel"), Close);
        AddButton(L.T("Next"), () => { _step = Step.Options; Render(); }, accent: true);
        return p;
    }

    // ---------------------------------------------------------------- 2. opções

    private FrameworkElement OptionsPage()
    {
        bool update = Installer.InstalledFolder() != null;
        var p = Page(L.T("Installation options"), update ? L.T("CleanWinVista is already installed. It will be updated; your settings are kept.") : L.T("CleanWinVista installs for your user only. No administrator rights needed."));
        var folder = new TextBox { Text = _folder, Header = L.T("Install location"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var browse = new Button { Content = L.T("Browse…"), VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += async (_, _) =>
        {
            var picker = new global::Windows.Storage.Pickers.FolderPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFolderAsync() is { } f) folder.Text = Path.Combine(f.Path, Installer.ProductName);
        };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(folder);
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        p.Children.Add(row);
        p.Children.Add(new TextBlock { Text = L.F("Space required: {0:0} MB", SourceSizeMb()), FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 2, 0, 8) });

        var start = Check(L.T("Start CleanWinVista with Windows"), _startWithWindows, v => _startWithWindows = v);
        var menu = Check(L.T("Add to the Start menu"), _startMenu, v => _startMenu = v);
        var desk = Check(L.T("Create a desktop shortcut"), _desktop, v => _desktop = v);
        var checks = new StackPanel { Spacing = 0 };
        checks.Children.Add(start); checks.Children.Add(menu); checks.Children.Add(desk);
        if (_cleanPanoAvailable)
        {
            // Opcional e desmarcado: o outro app do mesmo autor. Abre o instalador próprio dele (com o pedido de administrador do Windows).
            var cp = Check("", _cleanPano, v => _cleanPano = v);
            var label = new StackPanel { Spacing = 1 };
            label.Children.Add(new TextBlock { Text = L.T("Also install CleanPano, a free PC cleaner by the same developer"), TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = L.T("Its own installer opens after this one; Windows will ask for administrator permission."), FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
            cp.Content = label;
            checks.Children.Add(cp);
        }
        p.Children.Add(checks);
        var error = new TextBlock { Foreground = Res("SystemFillColorCriticalBrush"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        p.Children.Add(error);

        AddButton(L.T("Back"), () => { _folder = folder.Text; _step = Step.Language; Render(); });
        AddButton(update ? L.T("Update") : L.T("Install"), () =>
        {
            if (!Installer.IsValidFolder(folder.Text.Trim(), out var e)) { error.Text = L.T(e); return; }
            _folder = Installer.ResolveTarget(folder.Text); // sempre a subpasta dedicada "CleanWinVista"
            _ = InstallAsync();
        }, accent: true);
        return p;
    }

    private static double SourceSizeMb()
    {
        try
        {
            string extras = Path.Combine(AppContext.BaseDirectory, "extras") + Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(extras, StringComparison.OrdinalIgnoreCase)).Sum(f => new FileInfo(f).Length) / 1048576.0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    // ---------------------------------------------------------------- 3. instalando

    // Barra própria (trilho + preenchimento), no visual do Dock. O ProgressBar do WinUI derrubava o processo
    // (falha nativa 0xc000027b em Microsoft.UI.Xaml.dll) ao montar esta página.
    private Border? _barFill;
    private Grid? _barTrack;
    private double _barFraction;
    private TextBlock? _file;

    private FrameworkElement ProgressPage(string title)
    {
        var p = Page(title, L.T("Please wait a moment."));
        _barFraction = 0;
        _barFill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(3), Background = Res("TextFillColorPrimaryBrush") };
        _barTrack = new Grid { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 24, 0, 8), Background = Res("ControlStrokeColorDefaultBrush") };
        _barTrack.Children.Add(_barFill);
        _barTrack.SizeChanged += (_, _) => SetProgress(_barFraction);
        _file = new TextBlock { FontSize = 11, Opacity = 0.55, TextTrimming = TextTrimming.CharacterEllipsis };
        p.Children.Add(_barTrack);
        p.Children.Add(_file);
        return p;
    }

    private void SetProgress(double fraction)
    {
        _barFraction = Math.Clamp(fraction, 0, 1);
        if (_barFill != null && _barTrack != null) _barFill.Width = _barTrack.ActualWidth * _barFraction;
    }

    private async Task InstallAsync()
    {
        Installer.LogStep("InstallAsync: start");
        _step = Step.Installing;
        Render();
        Installer.LogStep("InstallAsync: page rendered");
        var progress = new Progress<(double Fraction, string File)>(x => { SetProgress(x.Fraction); if (_file != null) _file.Text = x.File; });
        var opts = new Installer.Options(_folder, _language, _startWithWindows, _startMenu, _desktop, _cleanPano && _cleanPanoAvailable);
        string? error = null;
        try { await Task.Run(() => Installer.Install(opts, progress)); }
        catch (Exception ex)
        {
            // Qualquer falha volta para a tela de opções com a mensagem (antes: erro inesperado = tela "Installing…" congelada).
            error = ex.Message;
            Installer.LogError("Install", ex);
        }
        if (error != null)
        {
            _step = Step.Options;
            Render();
            _page.Children.Add(new TextBlock { Text = L.T("Installation failed:") + " " + error, Foreground = Res("SystemFillColorCriticalBrush"), VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap });
            return;
        }
        Installer.LogStep("InstallAsync: done");
        _step = Step.Done;
        Render();
    }

    // ---------------------------------------------------------------- 4. pronto

    private FrameworkElement DonePage()
    {
        var p = Page(L.T("CleanWinVista is ready"), L.T("The Dock was installed. You can change everything later by right-clicking the Dock → Settings."));
        p.Children.Add(Check(L.T("Open CleanWinVista now"), _launch, v => _launch = v));
        bool cleanPano = _cleanPano && _cleanPanoAvailable;
        if (cleanPano)
            p.Children.Add(new TextBlock { Text = L.T("The CleanPano installer opens when you click Finish."), FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        AddButton(L.T("Finish"), async () =>
        {
            if (_launch)
            {
                try { Process.Start(new ProcessStartInfo(Path.Combine(_folder, Installer.ExeName)) { UseShellExecute = true, WorkingDirectory = _folder }); }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (cleanPano)
            {
                // O instalador do CleanPano roda da pasta temporária do pacote: esperamos ele terminar antes de sair.
                AppWindow.Hide();
                await Task.Run(Installer.RunCleanPanoSetup);
            }
            Close();
        }, accent: true);
        return p;
    }

    // ---------------------------------------------------------------- desinstalar

    private FrameworkElement UninstallPage()
    {
        var p = Page(L.T("Uninstall CleanWinVista"), L.T("The Dock, its shortcuts and the Windows startup entry will be removed. The Windows taskbar is not affected."));
        p.Children.Add(Check(L.T("Also delete my settings, notes, events and other data"), _removeData, v => _removeData = v));
        AddButton(L.T("Cancel"), Close);
        AddButton(L.T("Uninstall"), async () =>
        {
            _step = Step.Uninstalling;
            Render();
            SetProgress(0.5);
            await Task.Run(() => Installer.Uninstall(_removeData));
            _step = Step.Removed;
            Render();
        }, accent: true);
        return p;
    }

    private FrameworkElement RemovedPage()
    {
        var p = Page(L.T("CleanWinVista was removed"), L.T("Thank you for trying CleanWinVista."));
        AddButton(L.T("Close"), Close, accent: true);
        return p;
    }

    // ================================================================ peças

    private static StackPanel Page(string title, string subtitle)
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        p.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        return p;
    }

    private static CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = value };
        c.Click += (_, _) => set(c.IsChecked == true);
        return c;
    }

    private void AddButton(string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text, MinWidth = 104, Padding = new Thickness(16, 6, 16, 7) };
        if (accent && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var st) && st is Style style) b.Style = style;
        b.Click += (_, _) => click();
        _buttons.Children.Add(b);
    }

    private static Image Logo(double size) => new()
    {
        Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left,
        Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "CleanWinVista-64.png"))),
    };

    /// <summary>Pincel do tema ESCURO (a janela é sempre escura; o tema do app pode ser claro).</summary>
    private static Brush Res(string key)
    {
        if (FindDark(Application.Current.Resources, key) is Brush dark) return dark;
        return Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Colors.Gray);
    }

    private static object? FindDark(ResourceDictionary d, string key)
    {
        foreach (var name in new[] { "Dark", "Default" }) // no WinUI o tema escuro costuma ser a chave "Default"
            if (d.ThemeDictionaries.TryGetValue(name, out var t) && t is ResourceDictionary td && td.TryGetValue(key, out var v)) return v;
        foreach (var m in d.MergedDictionaries)
            if (FindDark(m, key) is { } found) return found;
        return null;
    }
}
