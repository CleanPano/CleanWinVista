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
