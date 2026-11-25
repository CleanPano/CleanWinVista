using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using Windows.Devices.Radios;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

// ============================================================================================================
// Lote 4 — sistema: atalhos rápidos (Wi-Fi, Bluetooth, tema, microfone…) e gravador de voz.
// ============================================================================================================

/// <summary>Atalhos rápidos: liga/desliga rádios, tema escuro e microfone; atalhos para configurações do Windows.</summary>
internal sealed class QuickTogglesWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.QuickToggles, iconSize, width, Glyphs.Toggles)
{
    public override string Tooltip => L.T("Quick toggles (Wi-Fi, Bluetooth, theme, microphone)");
    public override void Update(WidgetData d) => SetAccent(ctx.Audio.MicMuted() == true ? Palette.Red : null);

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Quick toggles"), 300);
        var radios = new StackPanel { Spacing = 4 };
        p.Children.Add(radios);
        _ = FillRadiosAsync(radios);

        var dark = new ToggleSwitch { Header = L.T("Windows dark theme"), IsOn = ScreenTools.IsDarkMode() };
        dark.Toggled += (_, _) => ScreenTools.SetDarkMode(dark.IsOn);
        p.Children.Add(dark);

        bool? mic = ctx.Audio.MicMuted();
        var micSwitch = new ToggleSwitch { Header = L.T("Microphone"), IsOn = mic == false, IsEnabled = mic is not null, OnContent = L.T("On"), OffContent = L.T("Muted") };
        micSwitch.Toggled += (_, _) => { ctx.Audio.SetMicMuted(!micSwitch.IsOn); ctx.Refresh(); };
        p.Children.Add(micSwitch);

        p.Children.Add(Row(
            Btn(L.T("Do not disturb"), () => WidgetActions.OpenWindowsUri("ms-settings:notifications"), ""),
            Btn(L.T("Night light"), () => WidgetActions.OpenWindowsUri("ms-settings:nightlight"), "")));
        p.Children.Add(Row(
            Btn(L.T("Display"), () => WidgetActions.OpenWindowsUri("ms-settings:display"), ""),
            Btn(L.T("Sound"), () => WidgetActions.OpenWindowsUri("ms-settings:sound"), "")));
        return p;
    }

    private static async Task FillRadiosAsync(StackPanel host)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) { host.Children.Add(Hint(L.T("Windows did not allow controlling Wi-Fi/Bluetooth."))); return; }
            foreach (var radio in await Radio.GetRadiosAsync())
            {
                if (radio.Kind is not (RadioKind.WiFi or RadioKind.Bluetooth)) continue;
                var r = radio;
                var sw = new ToggleSwitch { Header = r.Kind == RadioKind.WiFi ? "Wi-Fi" : "Bluetooth", IsOn = r.State == RadioState.On };
                sw.Toggled += async (_, _) =>
                {
                    var result = await r.SetStateAsync(sw.IsOn ? RadioState.On : RadioState.Off);
                    if (result != RadioAccessStatus.Allowed) sw.IsOn = r.State == RadioState.On;
                };
                host.Children.Add(sw);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            host.Children.Add(Hint(L.T("Wi-Fi/Bluetooth unavailable on this PC.")));
        }
    }
}

/// <summary>
/// Energia: bloquear, suspender, sair, reiniciar e desligar — para quem usa só o Dock, sem a barra do Windows.
/// Sair, reiniciar e desligar pedem confirmação no próprio painel.
/// </summary>
internal sealed class PowerWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.Power, iconSize, width, Glyphs.Power)
{
    private StackPanel? _host;

    public override string Tooltip => L.T("Power (lock, sleep, restart, shut down)");
    public override void Update(WidgetData d) { }

    protected override FrameworkElement BuildPanel()
    {
        _host = new StackPanel { Width = 230, Padding = new Thickness(4), Spacing = 2 };
        ShowMenu();
        return _host;
    }

    private void ShowMenu()
    {
        if (_host is null) return;
        _host.Children.Clear();
        _host.Children.Add(Entry(L.T("Lock"), Glyphs.Lock, () => Run(PowerActions.Lock)));
        _host.Children.Add(Entry(L.T("Sleep"), Glyphs.Sleep, () => Run(PowerActions.Sleep)));
        _host.Children.Add(Entry(L.T("Sign out"), Glyphs.SignOut, () => Confirm(L.T("Sign out of Windows?"), L.T("Sign out"), PowerActions.SignOut)));
        _host.Children.Add(Entry(L.T("Restart"), Glyphs.Restart, () => Confirm(L.T("Restart this PC?"), L.T("Restart"), PowerActions.Restart)));
        _host.Children.Add(Entry(L.T("Shut down"), Glyphs.Power, () => Confirm(L.T("Shut down this PC?"), L.T("Shut down"), PowerActions.ShutDown)));
    }

    private void Confirm(string question, string action, Func<bool> run)
    {
        if (_host is null) return;
        _host.Children.Clear();
        _host.Children.Add(new TextBlock { Text = question, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 0) });
        var hint = Hint(L.T("Unsaved work in open apps may be lost."));
        hint.Margin = new Thickness(4, 2, 4, 8);
        _host.Children.Add(hint);
        var buttons = new Grid { ColumnSpacing = 6 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cancel = Btn(L.T("Cancel"), ShowMenu);
        var ok = Btn(action, () => Run(run), accent: true);
        cancel.HorizontalAlignment = ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(ok, 1);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        _host.Children.Add(buttons);
        cancel.Loaded += (_, _) => cancel.Focus(FocusState.Programmatic); // Enter por engano cancela, não desliga
    }

    /// <summary>Fecha o painel e executa fora do UI thread (suspender só retorna quando o PC acorda).</summary>
    private void Run(Func<bool> action)
    {
        OpenPanel?.Hide();
        var log = ctx.Log;
        _ = Task.Run(() =>
        {
            try
            {
                if (!action()) log.LogWarning("Energia: o Windows recusou a ação");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                log.LogWarning("Energia: falha ao executar ({Error})", ex.GetType().Name);
            }
        });
    }

    private static Button Entry(string text, string glyph, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = text, FontSize = 14 });
        var b = new Button
        {
            Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        b.Click += (_, _) => click();
        return b;
    }
}

/// <summary>Gravador de voz: grava o microfone em .m4a na pasta Músicas\CleanWinVista.</summary>
internal sealed class VoiceMemoWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.VoiceMemo, iconSize, width, Glyphs.Mic)
{
    private static MediaCapture? s_capture;
    private static DateTime s_started;
    private static string? s_lastFile;

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CleanWinVista");
    private static bool Recording => s_capture != null;

    public override string Tooltip => Recording ? L.F("Recording… {0:mm\\:ss}", DateTime.Now - s_started) + "\n" + L.T("Click: stop") : L.T("Voice recorder") + "\n" + L.T("Click: record · Right-click: recordings");

    public override void Update(WidgetData d)
    {
        SetAccent(Recording ? Palette.Red : null);
        SetBadge(Recording ? $"{(DateTime.Now - s_started).TotalSeconds:0}" : null);
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => _ = Recording ? StopAsync() : StartAsync();

    public override void AddMenuItems(MenuFlyout menu)
    {
        if (s_lastFile != null && File.Exists(s_lastFile)) menu.Items.Add(Item(L.T("Play last recording"), "", () => WidgetActions.OpenFile(s_lastFile)));
        menu.Items.Add(Item(L.T("Open recordings folder"), "", () => { Directory.CreateDirectory(Folder); WidgetActions.OpenFile(Folder); }));
    }

    protected override FrameworkElement BuildPanel() => new Grid();

    private async Task StartAsync()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings { StreamingCaptureMode = StreamingCaptureMode.Audio });
            var folder = await StorageFolder.GetFolderFromPathAsync(Folder);
            var file = await folder.CreateFileAsync(L.T("Recording") + $" {DateTime.Now:yyyy-MM-dd HH-mm-ss}.m4a", CreationCollisionOption.GenerateUniqueName);
            await capture.StartRecordToStorageFileAsync(MediaEncodingProfile.CreateM4a(AudioEncodingQuality.Auto), file);
            s_capture = capture;
            s_started = DateTime.Now;
            s_lastFile = file.Path;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or IOException)
        {
            ctx.Log.LogWarning("Gravador: sem acesso ao microfone ({Error})", ex.GetType().Name);
            WidgetActions.OpenWindowsUri("ms-settings:privacy-microphone");
        }
        ctx.Refresh();
    }

    private async Task StopAsync()
    {
        var capture = s_capture;
        s_capture = null;
        if (capture != null)
        {
            try { await capture.StopRecordAsync(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException) { }
            capture.Dispose();
            ScreenTools.Chime();
        }
        ctx.Refresh();
    }
}
