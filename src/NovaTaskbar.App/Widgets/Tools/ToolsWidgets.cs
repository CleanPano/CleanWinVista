using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Core.Tools;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

// ============================================================================================================
// Lote 1 — utilitários locais (área de transferência, captura, conta-gotas, emoji, conversor, calculadora,
// cronômetro, timer). Funções inspiradas na lista do CoolDock; código e visual próprios.
// ============================================================================================================

/// <summary>Histórico da área de transferência (só texto, só na memória — some ao fechar o app).</summary>
internal sealed class ClipboardWidget : PanelWidget
{
    private const int MaxItems = 25;
    private static readonly List<string> History = new();
    private static bool s_hooked;

    public ClipboardWidget(double iconSize, double width) : base(WidgetKind.Clipboard, iconSize, width, Glyphs.Clipboard)
    {
        if (!s_hooked)
        {
            s_hooked = true;
            Clipboard.ContentChanged += async (_, _) =>
            {
                try
                {
                    var view = Clipboard.GetContent();
                    if (!view.Contains(StandardDataFormats.Text)) return;
                    string text = (await view.GetTextAsync()).Trim();
                    if (text.Length == 0 || text.Length > 20_000) return;
                    History.Remove(text);
                    History.Insert(0, text);
                    if (History.Count > MaxItems) History.RemoveAt(History.Count - 1);
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
            };
        }
    }

    public override string Tooltip => L.F("Clipboard · {0} recent items (this session only)", History.Count);

    public override void Update(WidgetData d) { }

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Clipboard"), 340);
        if (History.Count == 0) p.Children.Add(Hint(L.T("Copy text (Ctrl+C) and it shows up here. Click an item to copy it again.")));
        var list = new StackPanel { Spacing = 4 };
        foreach (var item in History)
        {
            var b = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            b.Content = new TextBlock { Text = item.Replace('\n', ' ').Replace('\r', ' '), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, TextWrapping = TextWrapping.Wrap };
            string copy = item;
            b.Click += (_, _) =>
            {
                var pkg = new DataPackage();
                pkg.SetText(copy);
                Clipboard.SetContent(pkg);
            };
            list.Children.Add(b);
        }
        p.Children.Add(Scroll(list));
        if (History.Count > 0) p.Children.Add(Btn(L.T("Clear history"), () => History.Clear(), ""));
        return p;
    }
}

/// <summary>Capturas de tela: recorte do Windows e pasta de capturas.</summary>
internal sealed class ScreenshotWidget : PanelWidget
{
    public ScreenshotWidget(double iconSize, double width) : base(WidgetKind.Screenshot, iconSize, width, Glyphs.Camera) { }

    public override string Tooltip => L.T("Screenshot") + "\n" + L.T("Click: snip · Right-click: screenshots folder");

    public override void Update(WidgetData d) { }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => WidgetActions.OpenWindowsUri("ms-screenclip:");

    public override void AddMenuItems(MenuFlyout menu)
    {
        menu.Items.Add(Item(L.T("Snip screen"), "", () => WidgetActions.OpenWindowsUri("ms-screenclip:")));
        menu.Items.Add(Item(L.T("Open screenshots folder"), "", () => WidgetActions.OpenFile(ScreenshotsFolder())));
    }

    protected override FrameworkElement BuildPanel() => new Grid();

    private static string ScreenshotsFolder() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
}

/// <summary>Conta-gotas: clique em qualquer lugar da tela e a cor (hex) vai para a área de transferência.</summary>
internal sealed class ColorPickerWidget : PanelWidget
{
    private static readonly ClickCapture Capture = new();
    private static readonly List<string> Recent = new();
    private Color? _last;

    public ColorPickerWidget(double iconSize, double width) : base(WidgetKind.ColorPicker, iconSize, width, Glyphs.Eyedropper) { }

    public override string Tooltip => Capture.Active ? L.T("Click anywhere on screen to pick the color (right-click cancels)") :
        L.T("Color picker") + "\n" + L.T("Click: pick a color on screen · Right-click: recent colors");

    public override void Update(WidgetData d) => SetAccent(_last);

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        var ui = DispatcherQueue.GetForCurrentThread();
        Capture.Start((x, y) =>
        {
            var (r, g, b) = ScreenTools.PixelAt(x, y);
            string hex = $"#{r:X2}{g:X2}{b:X2}";
            ui.TryEnqueue(() =>
            {
                _last = Color.FromArgb(255, r, g, b);
                Recent.Remove(hex);
                Recent.Insert(0, hex);
                if (Recent.Count > 12) Recent.RemoveAt(12);
                Copy(hex);
                SetAccent(_last);
            });
        }, () => { });
    }

    public override void AddMenuItems(MenuFlyout menu)
    {
        foreach (var hex in Recent)
        {
            string h = hex;
            menu.Items.Add(Item(L.F("Copy {0}", h), "", () => Copy(h)));
        }
    }

    protected override FrameworkElement BuildPanel() => new Grid();

    private static void Copy(string text)
    {
        var pkg = new DataPackage();
        pkg.SetText(text);
        Clipboard.SetContent(pkg);
    }
}

/// <summary>Emoji: abre o painel de emoji do Windows no app em uso (Win + .).</summary>
internal sealed class EmojiWidget : PanelWidget
{
    public EmojiWidget(double iconSize, double width) : base(WidgetKind.Emoji, iconSize, width, Glyphs.Emoji) { }
    public override string Tooltip => L.T("Emoji, GIFs and symbols (Windows panel)");
    public override void Update(WidgetData d) { }
    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => ScreenTools.OpenEmojiPanel();
    protected override FrameworkElement BuildPanel() => new Grid();
}

/// <summary>Conversor de unidades (local). Moedas: no widget Serviços/Clima (precisa de internet).</summary>
internal sealed class ConverterWidget : PanelWidget
{
    private static readonly string[] Currencies = ["USD", "BRL", "EUR", "GBP", "JPY", "CAD", "AUD", "CHF", "CNY", "MXN", "ARS"];
    private readonly WidgetContext _ctx;

    public ConverterWidget(double iconSize, double width, WidgetContext ctx) : base(WidgetKind.Converter, iconSize, width, Glyphs.Convert) => _ctx = ctx;
    public override string Tooltip => L.T("Unit and currency converter");
    public override void Update(WidgetData d) { }

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Converter"), 320);
        var cat = new ComboBox { ItemsSource = UnitConverter.Categories.Select(c => L.T(c.Name)).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var value = new NumberBox { Value = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, HorizontalAlignment = HorizontalAlignment.Stretch };
        var from = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var to = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var result = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        UnitConverter.Category Current() => UnitConverter.Categories[Math.Max(0, cat.SelectedIndex)];
        void Fill()
        {
            var names = Current().Units.Select(u => L.T(u.Name)).ToList();
            from.ItemsSource = names; to.ItemsSource = names;
            from.SelectedIndex = 0; to.SelectedIndex = Math.Min(1, names.Count - 1);
        }
        void Calc()
        {
            if (from.SelectedIndex < 0 || to.SelectedIndex < 0 || double.IsNaN(value.Value)) { result.Text = "—"; return; }
            var c = Current();
            double r = UnitConverter.Convert(c.Id, c.Units[from.SelectedIndex].Id, c.Units[to.SelectedIndex].Id, value.Value);
            result.Text = $"{Calculator.Format(r)} {L.T(c.Units[to.SelectedIndex].Name).ToLower(L.Culture)}";
        }
        cat.SelectionChanged += (_, _) => { Fill(); Calc(); };
        from.SelectionChanged += (_, _) => Calc();
        to.SelectionChanged += (_, _) => Calc();
        value.ValueChanged += (_, _) => Calc();
        Fill(); Calc();
        p.Children.Add(cat); p.Children.Add(value); p.Children.Add(from); p.Children.Add(to); p.Children.Add(result);
        p.Children.Add(CurrencySection());
        return p;
    }

    /// <summary>Moedas (cotação do BCE via frankfurter.app) — só com “Permitir widgets com internet”.</summary>
    private FrameworkElement CurrencySection()
    {
        var box = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        box.Children.Add(new TextBlock { Text = L.T("Currencies"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (!_ctx.Net.Allowed) { box.Children.Add(Hint(L.T("Turn on “Allow internet widgets” in settings to convert currencies."))); return box; }
        var amount = new NumberBox { Value = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, HorizontalAlignment = HorizontalAlignment.Stretch };
        var from = new ComboBox { ItemsSource = Currencies, SelectedIndex = 0 };
        var to = new ComboBox { ItemsSource = Currencies, SelectedIndex = 1 };
        var result = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsTextSelectionEnabled = true };
        async void Calc()
        {
            if (from.SelectedIndex < 0 || to.SelectedIndex < 0 || double.IsNaN(amount.Value)) return;
            string a = Currencies[from.SelectedIndex], b = Currencies[to.SelectedIndex];
            if (a == b) { result.Text = $"{Calculator.Format(amount.Value)} {b}"; return; }
            result.Text = "…";
            string? json = await _ctx.Net.GetStringAsync($"https://api.frankfurter.app/latest?from={a}&to={b}");
            double? rate = json is null ? null : JsonPath.Number(json, "rates." + b);
            result.Text = rate is { } r ? $"{Calculator.Format(amount.Value * r)} {b}" : L.T("Rate unavailable");
        }
        amount.ValueChanged += (_, _) => Calc();
        from.SelectionChanged += (_, _) => Calc();
        to.SelectionChanged += (_, _) => Calc();
        box.Children.Add(amount);
        box.Children.Add(Row(from, new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center }, to));
        box.Children.Add(result);
        Calc();
        return box;
    }
}

/// <summary>Cronômetro com voltas. O anel mostra os segundos do minuto atual.</summary>
internal sealed class StopwatchWidget : PanelWidget
{
    private static readonly Stopwatch Clock = new();
    private static readonly List<TimeSpan> Laps = new();
    private TextBlock? _display;
    private StackPanel? _laps;

    public StopwatchWidget(double iconSize, double width) : base(WidgetKind.Stopwatch, iconSize, width, Glyphs.Stopwatch, withRing: true) { }

    public override string Tooltip => Clock.IsRunning || Clock.Elapsed > TimeSpan.Zero ? L.F("Stopwatch: {0}", Format(Clock.Elapsed)) : L.T("Stopwatch");

    public override void Update(WidgetData d)
    {
        double sec = Clock.Elapsed.TotalSeconds % 60 / 60;
        SetRing(Clock.Elapsed > TimeSpan.Zero ? sec : null, Clock.IsRunning ? Palette.Orange : Palette.Neutral);
        if (_display != null) _display.Text = Format(Clock.Elapsed);
    }

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Stopwatch"), 280);
        _display = new TextBlock { FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Text = Format(Clock.Elapsed) };
        _display.FontFeatures();
        _laps = new StackPanel { Spacing = 2 };
        RenderLaps();
        var start = Btn(Clock.IsRunning ? L.T("Pause") : L.T("Start"), () => { }, accent: true);
        start.Click += (_, _) => { if (Clock.IsRunning) Clock.Stop(); else Clock.Start(); start.Content = Clock.IsRunning ? L.T("Pause") : L.T("Start"); };
        p.Children.Add(_display);
        p.Children.Add(Row(start, Btn(L.T("Lap"), () => { if (Clock.IsRunning) { Laps.Insert(0, Clock.Elapsed); RenderLaps(); } }),
            Btn(L.T("Reset"), () => { Clock.Reset(); Laps.Clear(); RenderLaps(); _display.Text = Format(TimeSpan.Zero); start.Content = L.T("Start"); })));
        p.Children.Add(Scroll(_laps, 160));
        // Atualiza a cada 50 ms enquanto o painel está aberto.
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.Tick += (_, _) => { if (_display != null) _display.Text = Format(Clock.Elapsed); };
        p.Loaded += (_, _) => timer.Start();
        p.Unloaded += (_, _) => timer.Stop();
        return p;
    }

    private void RenderLaps()
    {
        if (_laps is null) return;
        _laps.Children.Clear();
        for (int i = 0; i < Laps.Count; i++)
            _laps.Children.Add(new TextBlock { Text = L.F("Lap {0}", Laps.Count - i) + $"   {Format(Laps[i])}", Opacity = 0.8 });
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.f") : t.ToString(@"mm\:ss\.ff");
}

/// <summary>Timer regressivo: escolha os minutos; o anel esvazia; ao terminar, som e destaque.</summary>
internal sealed class TimerWidget : PanelWidget
{
    private readonly WidgetContext _ctx;
    private static DateTime? s_end;
    private static TimeSpan s_total;
    private static bool s_rang;
    private TextBlock? _display;

    public TimerWidget(double iconSize, double width, WidgetContext ctx) : base(WidgetKind.Timer, iconSize, width, Glyphs.Timer, withRing: true) => _ctx = ctx;

    private static TimeSpan Left => s_end is { } e && e > DateTime.Now ? e - DateTime.Now : TimeSpan.Zero;

    public override string Tooltip => s_end is null ? L.F("Timer · {0} min", _ctx.Store.Data.TimerMinutes) + "\n" + L.T("Click: set up and start") : L.F("Timer: {0} left", Format(Left));

    public override void Update(WidgetData d)
    {
        if (s_end is { } && Left == TimeSpan.Zero && !s_rang)
        {
            s_rang = true;
            ScreenTools.Chime();
        }
        bool done = s_end is { } && Left == TimeSpan.Zero;
        SetAccent(done ? Palette.Green : null);
        SetRing(s_end is null ? null : s_total.TotalSeconds > 0 ? Left.TotalSeconds / s_total.TotalSeconds : 0, Palette.Blue);
        SetBadge(s_end is null || done ? null : Left.TotalMinutes >= 1 ? $"{Math.Ceiling(Left.TotalMinutes)}" : $"{Math.Ceiling(Left.TotalSeconds)}s");
        if (_display != null) _display.Text = s_end is null ? Format(TimeSpan.FromMinutes(_ctx.Store.Data.TimerMinutes)) : Format(Left);
    }

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Timer"), 280);
        _display = new TextBlock { FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _display.FontFeatures();
        var minutes = new NumberBox { Header = L.T("Minutes"), Value = _ctx.Store.Data.TimerMinutes, Minimum = 1, Maximum = 600, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var presets = Row(Btn("1", () => minutes.Value = 1), Btn("5", () => minutes.Value = 5), Btn("10", () => minutes.Value = 10), Btn("25", () => minutes.Value = 25), Btn("60", () => minutes.Value = 60));
        p.Children.Add(_display);
        p.Children.Add(minutes);
        p.Children.Add(presets);
        p.Children.Add(Row(
            Btn(L.T("Start"), () =>
            {
                int m = (int)Math.Clamp(double.IsNaN(minutes.Value) ? 5 : minutes.Value, 1, 600);
                _ctx.Store.Update(x => x.TimerMinutes = m);
                s_total = TimeSpan.FromMinutes(m);
                s_end = DateTime.Now + s_total;
                s_rang = false;
                _ctx.Refresh();
            }, accent: true),
            Btn(L.T("Stop"), () => { s_end = null; _ctx.Refresh(); })));
        Update(new WidgetData());
        return p;
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
}
