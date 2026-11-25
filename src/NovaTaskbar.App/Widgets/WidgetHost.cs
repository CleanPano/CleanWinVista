using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NovaTaskbar.App.Services;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Coleta os dados dos widgets e os entrega às views. Um único timer de 1 s, ligado só quando há widgets
/// visíveis (modo Estendido); CPU/RAM/rede a cada 2 s, volume a cada 2 s, bateria a cada 30 s; mídia por eventos.
/// Widgets contextuais (música) têm um segundo timer de 5 s que decide se aparecem no Dock.
/// </summary>
internal sealed class WidgetHost : IDisposable
{
    public static WidgetHost? Instance { get; private set; }

    private readonly SettingsService _settings;
    private readonly ILogger<WidgetHost> _log;
    private readonly SystemMetrics _metrics = new();
    private readonly WidgetData _data = new();
    private readonly List<WidgetView> _views = new();
    private DispatcherQueueTimer? _timer;
    private DispatcherQueue? _ui;
    private long _ticks;
    private bool _mediaStarted;
    private string _lastArtKey = "";

    // ---- contexto (widgets que aparecem só quando há o que mostrar)
    private static readonly TimeSpan PauseGrace = TimeSpan.FromSeconds(8);       // música pausada: tempo para retomar pelo Dock
    private DispatcherQueueTimer? _contextTimer;
    private readonly HashSet<WidgetKind> _shown = new();
    private DateTime _lastPlayingUtc = DateTime.MinValue;
    private const int BatteryCheckTicks = 6;   // bateria do sistema: 6 × 5 s = 30 s
    private const int DevicesCheckTicks = 12;  // periféricos: 12 × 5 s = 1 min (consulta Bluetooth é mais cara)
    private readonly DeviceBatteries _devices;
    private long _contextTicks;
    private bool _readingDevices;

    public WidgetHost(SettingsService settings, AudioVolume audio, MediaWatcher media, WidgetStore store, SecretStore secrets, NetGate net,
        NovaTaskbar.Windows.Apps.AppLauncher launcher, ILogger<WidgetHost> log)
    {
        _settings = settings;
        Audio = audio;
        Media = media;
        _log = log;
        _devices = new DeviceBatteries(log);
        Context = new WidgetContext
        {
            Store = store, Secrets = secrets, Net = net, Audio = audio, Launcher = launcher, Settings = settings, Log = log,
            UI = DispatcherQueue.GetForCurrentThread()!, Refresh = Refresh,
        };
        Instance = this;
        Focus = new FocusTimer(TimeSpan.FromMinutes(settings.Current.Widgets.FocusMinutes));
        settings.Changed += (_, _) =>
        {
            var d = TimeSpan.FromMinutes(_settings.Current.Widgets.FocusMinutes);
            if (Focus.Duration != d) { Focus.SetDuration(d); Refresh(); }
        };
    }

    public AudioVolume Audio { get; }
    public MediaWatcher Media { get; }
    public FocusTimer Focus { get; }
    public KeyboardLayouts Keyboards { get; } = new();

    /// <summary>Serviços dos widgets de ferramenta (o Dock preenche Windows/Activate).</summary>
    public WidgetContext Context { get; }

    /// <summary>Largura do widget em vagas (periféricos crescem com o número de aparelhos).</summary>
    public double Slots(WidgetKind kind) => kind == WidgetKind.Devices
        ? WidgetSettings.DevicesWidth(_data.Devices.Count)
        : _settings.Current.Widgets.WidthInSlots(kind);

    /// <summary>Parte da chave do item no Dock que, ao mudar, recria o widget (ex.: nº de periféricos).</summary>
    public string Variant(WidgetKind kind) => kind == WidgetKind.Devices ? Math.Min(_data.Devices.Count, WidgetSettings.MaxDevicesShown).ToString() : "";

    /// <summary>Algum widget mudou de valor e precisa de quadros de animação.</summary>
    public event EventHandler? NeedsFrame;

    /// <summary>Um widget contextual apareceu ou sumiu (o Dock deve sincronizar os itens).</summary>
    public event EventHandler? ContextChanged;

    /// <summary>Widgets comuns sempre aparecem; contextuais só quando há o que mostrar.</summary>
    public bool IsShown(WidgetKind kind) => !WidgetSettings.IsContextual(kind) || _shown.Contains(kind);

    /// <summary>Liga/desliga a observação de contexto (modo Estendido horizontal).</summary>
    public void EnableContext(bool on)
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        if (!on) { _contextTimer?.Stop(); return; }
        StartMedia();
        if (_contextTimer is null)
        {
            _contextTimer = _ui.CreateTimer();
            _contextTimer.Interval = TimeSpan.FromSeconds(5);
            _contextTimer.Tick += (_, _) => ContextTick();
        }
        if (!_contextTimer.IsRunning) { _contextTimer.Start(); _contextTicks = 0; ContextTick(raise: false); }
    }

    private void ContextTick(bool raise = true)
    {
        long t = _contextTicks++;
        if (t % BatteryCheckTicks == 0) _data.Battery = SystemMetrics.Battery();
        SampleKeyboard();
        if (t % DevicesCheckTicks == 0) _ = RefreshDevicesAsync();
        Evaluate(raise); // também expira a carência da música pausada
    }

    /// <param name="force">Avisa o Dock mesmo sem mudança de visibilidade (ex.: nº de periféricos mudou a largura).</param>
    private void Evaluate(bool raise, bool force = false)
    {
        var now = DateTime.UtcNow;
        bool changed =
            Toggle(WidgetKind.Media, _data.Media.Playing || now - _lastPlayingUtc < PauseGrace)
            | Toggle(WidgetKind.Battery, _data.Battery.HasBattery)                 // só notebooks/tablets
            | Toggle(WidgetKind.Keyboard, _data.Keyboards.Count >= 2)
            | Toggle(WidgetKind.Devices, _data.Devices.Count > 0);
        if ((changed || force) && raise) ContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Toggle(WidgetKind kind, bool on) => on ? _shown.Add(kind) : _shown.Remove(kind);

    private void StartMedia()
    {
        if (_mediaStarted || _ui is null) return;
        _mediaStarted = true;
        var ui = _ui;
        Media.Changed += (_, _) => ui.TryEnqueue(() => _ = RefreshMediaAsync());
        _ = Media.StartAsync();
    }

    public WidgetView Create(WidgetKind kind, double iconSize, double spacing, bool reduceMotion)
    {
        if (iconSize <= 0) throw new ArgumentOutOfRangeException(nameof(iconSize), "Widget criado antes do layout (tamanho do ícone 0).");
        var cfg = _settings.Current.Widgets;
        double width = (iconSize + spacing) * Slots(kind) - spacing;
        WidgetView v = kind switch
        {
            WidgetKind.Clock => new ClockWidget(iconSize, width, cfg, reduceMotion, Context),
            WidgetKind.System => new SystemWidget(iconSize, width, cfg.StyleOf(kind)),
            WidgetKind.Network => new NetworkWidget(iconSize, width, cfg.StyleOf(kind)),
            WidgetKind.Battery => new BatteryWidget(iconSize, width),
            WidgetKind.Volume => new VolumeWidget(iconSize, width, Audio),
            WidgetKind.Media => new MediaWidget(iconSize, width, Media, reduceMotion),
            WidgetKind.Focus => new FocusWidget(iconSize, width, Focus, cfg, Refresh),
            WidgetKind.Keyboard => new KeyboardWidget(iconSize, width, Keyboards, Refresh),
            WidgetKind.Devices => new DevicesWidget(iconSize, width),
            WidgetKind.Clipboard => new ClipboardWidget(iconSize, width),
            WidgetKind.Screenshot => new ScreenshotWidget(iconSize, width),
            WidgetKind.ColorPicker => new ColorPickerWidget(iconSize, width),
            WidgetKind.Emoji => new EmojiWidget(iconSize, width),
            WidgetKind.Converter => new ConverterWidget(iconSize, width, Context),
            WidgetKind.Calculator => new CalculatorWidget(iconSize, width, Context),
            WidgetKind.Stopwatch => new StopwatchWidget(iconSize, width),
            WidgetKind.Timer => new TimerWidget(iconSize, width, Context),
            WidgetKind.Notes => new NotesWidget(iconSize, width, Context),
            WidgetKind.Todos => new TodosWidget(iconSize, width, Context),
            WidgetKind.Water => new WaterWidget(iconSize, width, Context),
            WidgetKind.Posture => new PostureWidget(iconSize, width, Context),
            WidgetKind.Progress => new ProgressWidget(iconSize, width, Context),
            WidgetKind.AnalogClock => new AnalogClockWidget(iconSize, width),
            WidgetKind.WorldClock => new WorldClockWidget(iconSize, width, Context),
            WidgetKind.Downloads => new DownloadsWidget(iconSize, width),
            WidgetKind.FileShelf => new FileShelfWidget(iconSize, width, Context),
            WidgetKind.Bookmarks => new BookmarksWidget(iconSize, width, Context),
            WidgetKind.Search => new SearchWidget(iconSize, width, Context),
            WidgetKind.AppSwitcher => new AppSwitcherWidget(iconSize, width, Context),
            WidgetKind.Photos => new PhotosWidget(iconSize, width),
            WidgetKind.QuickToggles => new QuickTogglesWidget(iconSize, width, Context),
            WidgetKind.VoiceMemo => new VoiceMemoWidget(iconSize, width, Context),
            WidgetKind.Weather => new WeatherWidget(iconSize, width, Context),
            WidgetKind.Calendar => new CalendarWidget(iconSize, width, Context),
            WidgetKind.Services => new ServicesWidget(iconSize, width, Context),
            WidgetKind.Power => new PowerWidget(iconSize, width, Context),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return v;
    }

    /// <summary>Define as views ativas; liga/desliga o timer conforme houver widgets.</summary>
    public void SetViews(IEnumerable<WidgetView> views)
    {
        _views.Clear();
        _views.AddRange(views);
        _ui ??= DispatcherQueue.GetForCurrentThread();
        if (_views.Count == 0) { _timer?.Stop(); return; }

        if (_timer is null)
        {
            _timer = _ui.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) => Tick();
        }
        if (!_timer.IsRunning) _timer.Start();

        if (_views.Any(v => v.Kind == WidgetKind.Media)) StartMedia();
        Sample(all: true);
        Push();
    }

    public void Refresh() { _data.Now = DateTime.Now; Push(); }

    public void SetFocusMinutes(int minutes)
    {
        _settings.Update(s => s.Widgets.FocusMinutes = minutes);
        Focus.SetDuration(TimeSpan.FromMinutes(minutes));
        Refresh();
    }

    private void Tick()
    {
        _ticks++;
        Sample(all: false);
        Push();
    }

    private void Sample(bool all)
    {
        _data.Now = DateTime.Now;
        bool Has(WidgetKind k) => _views.Any(v => v.Kind == k);
        try
        {
            if (Has(WidgetKind.System) && (all || _ticks % 2 == 0)) SampleSystem();
            if (Has(WidgetKind.Network) && (all || _ticks % 2 == 0)) SampleNetwork();
            if (Has(WidgetKind.Volume) && (all || _ticks % 2 == 1)) _data.Volume = Audio.Read();
            if (Has(WidgetKind.Battery) && (all || _ticks % 30 == 0)) _data.Battery = SystemMetrics.Battery();
            if (Has(WidgetKind.Keyboard)) SampleKeyboard(); // a cada segundo: acompanha a troca de janela/idioma
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _log.LogWarning(ex, "Falha ao ler métricas");
        }
    }

    private void SampleKeyboard()
    {
        _data.Keyboards = Keyboards.Installed();
        _data.KeyboardCurrent = Keyboards.Current();
    }

    private async Task RefreshDevicesAsync()
    {
        if (_readingDevices) return;
        _readingDevices = true;
        try
        {
            var list = await Task.Run(_devices.ReadAsync); // Bluetooth/WinRT fora do UI thread
            bool changed = list.Count != _data.Devices.Count || !list.SequenceEqual(_data.Devices);
            _data.Devices = list;
            if (changed) { Evaluate(raise: true, force: true); Push(); }
        }
        finally { _readingDevices = false; }
    }

    private void SampleSystem()
    {
        _data.Cpu = _metrics.CpuPercent() ?? _data.Cpu;
        _data.Memory = _metrics.Memory();
        if (_data.Cpu is { } c) _data.CpuHistory.Add(c);
        if (_data.Memory is { } m) _data.RamHistory.Add(m.UsedPercent);
    }

    private void SampleNetwork()
    {
        _data.Network = _metrics.Network();
        if (_data.Network is not { } n) return;
        _data.DownHistory.Add(n.Down);
        _data.UpHistory.Add(n.Up);
    }

    /// <summary>Muda o estilo de um widget (a chave do item no Dock inclui o estilo → é recriado).</summary>
    public void SetStyle(WidgetKind kind, WidgetStyle style) => _settings.Update(s =>
    {
        var w = s.Widgets.Items.FirstOrDefault(x => x.Kind == kind);
        if (w != null) w.Style = style;
    });

    private void Push()
    {
        if (_views.Count == 0) return;
        foreach (var v in _views) v.Update(_data);
        NeedsFrame?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshMediaAsync()
    {
        var state = await Media.ReadAsync(withThumbnail: true);
        string artKey = state.HasSession ? state.Title + "|" + state.Artist : "";
        if (artKey != _lastArtKey)
        {
            _lastArtKey = artKey;
            _data.MediaArt = await DecodeAsync(state.Thumbnail);
        }
        bool wasPlaying = _data.Media.Playing;
        _data.Media = state;
        if (state.Playing || wasPlaying) _lastPlayingUtc = DateTime.UtcNow; // ao pausar, começa a carência
        Evaluate(raise: true);
        Push();
    }

    private static async Task<ImageSource?> DecodeAsync(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage { DecodePixelWidth = 96 };
            using var ms = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            return bmp;
        }
        catch (Exception) { return null; } // capa em formato inesperado: mostra o ícone padrão
    }

    public void Dispose()
    {
        _timer?.Stop();
        _contextTimer?.Stop();
        Media.Dispose();
    }
}
