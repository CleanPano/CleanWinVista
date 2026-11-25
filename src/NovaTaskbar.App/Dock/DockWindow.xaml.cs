using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.App.Services;
using NovaTaskbar.App.Widgets;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Core.Apps;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Core.Layout;
using NovaTaskbar.Core.Magnification;
using NovaTaskbar.Windows.Apps;
using NovaTaskbar.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Dock;

/// <summary>
/// Janela dos ícones: transparente, sempre no topo, sem foco, fora do Alt+Tab.
/// Itens = apps fixados + (separador) + apps abertos não fixados, alimentados pelo <see cref="WindowTracker"/> (eventos, sem polling).
/// Renderiza com transformações de composição (Translation/Scale/Opacity) — sem relayout por quadro — e só
/// assina CompositionTarget.Rendering enquanto alguma mola/animação está em movimento (CPU ociosa ≈ 0).
/// </summary>
/// <remarks>
/// Movimento (referência: Apple "Designing Fluid Interfaces" / skills de Emil Kowalski em designbookmark.com):
/// molas criticamente amortecidas e interrompíveis para tudo que o usuário toca; entrada/saída de itens pela
/// presença (vizinhos abrem/fecham espaço), nunca a partir de escala 0; rótulo ancorado no ícone, 150 ms;
/// feedback no pointer-down; arrastar com histerese e acompanhamento 1:1; respeita "Efeitos de animação" do Windows.
/// </remarks>
public sealed partial class DockWindow : Window
{
    private sealed class ItemView
    {
        public required string Key;
        public DockEntry? Entry;           // null = separador ou widget
        public WidgetView? Widget;         // widget do modo Estendido
        public required Grid Root;
        public Image? Image;
        public FontIcon? Fallback;
        public Border? Line;               // separador
        public required Border Indicator;
        public Spring Presence;
        public Spring Offset;              // FLIP: deslocamento residual ao reordenar
        public Spring Running;             // indicador visível
        public Spring Active;              // indicador alongado (app em primeiro plano)
        public bool Removing;
        public bool Pressed;
        public string? IconPath;
        public bool IsSeparator => Entry is null && Widget is null;
        public bool IsWidget => Widget is not null;
        public double Slots;               // largura em vagas (widgets); ícones = 1
    }

    private const double BounceDuration = 0.7;     // s — duas quicadas amortecidas
    private const double BounceHeight = 14;        // DIP
    private const double DragThreshold = 6;        // DIP de histerese antes de virar arrasto
    private const double PresenceResponse = 0.32;  // s — mola de entrada/saída de itens
    private const double ReorderResponse = 0.28;   // s — mola dos vizinhos ao reordenar
    private const double TooltipResponse = 0.12;   // s — ~150 ms até assentar

    private readonly SettingsService _settings;
    private readonly IconService _icons;
    private readonly AppLauncher _launcher;
    private readonly WindowTracker _tracker;
    private readonly AppInfoProvider _info;
    private readonly WidgetHost _widgets;
    private readonly ILogger _log;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly GlassWindow _glass;
    private readonly MagnificationAnimator _anim = new();
    private readonly List<ItemView> _items = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly NativeMethods.SubclassProc _subclass;
    private readonly global::Windows.UI.ViewManagement.UISettings _ui = new();

    private DockGeometry _geo = null!;
    private MagnificationParameters _p;
    private IconFrame[] _frames = new IconFrame[16];
    private DockItemSpec[] _specs = new DockItemSpec[16];
    private double _maxIcon;
    private double _scale = 1.0;
    private NativeMethods.RECT _window;
    /// <summary>O desenho do ícone ocupa 78 % da vaga (margem óptica, como os ícones do macOS); widgets não mudam.</summary>
    private const double IconInset = 0.78;
    private RectD _lastPill;            // pílula no espaço canônico (Dock inferior)
    private RectD _lastPillWindow;      // pílula em coordenadas da janela
    private DockOrientation _orient = new(DockEdge.Bottom, 0, 0);
    private double _origin;
    private GlassMaterial _effectiveMaterial;
    private readonly GlassWindow _tipGlass;
    private bool _dark;
    private bool _reduceMotion;
    private string _layoutSignature = "";
    private List<string> _runningOrder = new();
    private bool _renderHooked;
    private TimeSpan _lastRenderTime;
    private NativeMethods.RECT _regionApplied;
    private bool _regionExpanded;
    private int _hoverIndex = -1;
    private Spring _tooltip = new(0, TooltipResponse, 1.0, 0.002);
    private bool _menuOpen;
    private bool _closing;

    // clique / arrasto
    private int _pressIndex = -1;
    private global::Windows.Foundation.Point _pressPoint;
    private bool _dragging;
    private ItemView? _dragItem;
    private double _dragGrabOffset;
    private double _dragX;
    private int _dragHome;          // índice (sem o item arrastado) onde o app aberto estava
    private uint _capturedPointer;

    // arrastar arquivo do Explorer
    private bool _dropHover;
    private int _dropIndex = -1;
    private Spring _dropMarker = new(0, 0.12, 1.0, 0.002);

    // salto ao abrir
    private ItemView? _bounceItem;
    private double _bounceStart;

    internal DockWindow(SettingsService settings, IconService icons, AppLauncher launcher, WindowTracker tracker,
        AppInfoProvider info, WidgetHost widgets, ILogger<DockWindow> log, Action openSettings, Action exit)
    {
        _settings = settings;
        _icons = icons;
        _launcher = launcher;
        _tracker = tracker;
        _info = info;
        _widgets = widgets;
        _log = log;
        _openSettings = openSettings;
        _exit = exit;
        _subclass = WndProc;

        InitializeComponent();
        Hwnd = WindowNative.GetWindowHandle(this);
        WidgetActions.HostWindow = Hwnd;
        SystemBackdrop = new TransparentBackdrop();
        ConfigureChrome();
        _glass = new GlassWindow();
        _tipGlass = new GlassWindow(); // fundo do rótulo (hover) com o mesmo vidro do Dock

        _settings.Changed += (_, _) => ApplySettings();
        _tracker.Changed += (_, _) => SyncItems(animate: true);
        _widgets.NeedsFrame += (_, _) => EnsureRendering();
        _widgets.ContextChanged += (_, _) => SyncItems(animate: true); // música começou/parou
        _widgets.Context.Windows = () => _tracker.Windows.Where(w => w.IsVisible && !w.IsCloaked && w.Owner == 0 && w.Title.Length > 0).ToList();
        _widgets.Context.Activate = w => WindowActions.Activate(w.Handle);
        Root.PointerWheelChanged += OnPointerWheel;
        ThemeHelper.SystemThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_settings.Current.Appearance.Theme == ThemePreference.System) ApplySettings();
        });
    }

    internal IntPtr Hwnd { get; }

    public void Start()
    {
        ApplySettings();
        _tracker.Start();           // primeira enumeração dispara SyncItems
        SnapAll();
        if (_effectiveMaterial != GlassMaterial.Tint) _glass.ShowGlass();
        NativeMethods.ShowNoActivate(Hwnd);
        NativeMethods.SetWindowPos(Hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        RenderFrame(0);
        UpdateRegion(false, force: true);
        _log.LogInformation("Dock exibido: {Count} itens ({Running} em execução), material {Material}, escala {Scale:0.##}",
            _items.Count(i => !i.IsSeparator), _items.Count(i => i.Entry?.IsRunning == true), _effectiveMaterial, _scale);
    }

    public void Shutdown()
    {
        if (_closing) return;
        _closing = true;
        UnhookRendering();
        _tracker.Dispose();
        NativeMethods.RemoveWindowSubclass(Hwnd, _subclass, UIntPtr.Zero);
        _glass.Close();
        _tipGlass.Close();
        Close();
    }

    // ================================================================== janela

    private void ConfigureChrome()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "CleanWinVista";

        NativeMethods.MakePopup(Hwnd);
        NativeMethods.SetExStyle(Hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE, NativeMethods.WS_EX_APPWINDOW);

        int noRound = NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(Hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));
        uint none = NativeMethods.DWMWA_COLOR_NONE;
        NativeMethods.DwmSetWindowAttribute(Hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref none, sizeof(uint));
        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(Hwnd, ref margins);

        NativeMethods.SetWindowSubclass(Hwnd, _subclass, UIntPtr.Zero, UIntPtr.Zero);
        NativeMethods.SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        switch (msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                return new IntPtr(NativeMethods.MA_NOACTIVATE); // clicar no Dock não rouba o foco
            case NativeMethods.WM_SYSCOMMAND when (wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE:
                return IntPtr.Zero; // o Dock não minimiza (Win+M, "minimizar tudo", ferramentas externas)
            case NativeMethods.WM_SIZE when wParam.ToInt64() == NativeMethods.SIZE_MINIMIZED:
                DispatcherQueue.TryEnqueue(() => { NativeMethods.ShowNoActivate(Hwnd); if (_glass.IsVisible) NativeMethods.ShowNoActivate(_glass.Hwnd); });
                break;
            case NativeMethods.WM_NCCALCSIZE when wParam != IntPtr.Zero:
                // Sem área não-cliente: o OverlappedPresenter recoloca WS_DLGFRAME (moldura de 3 px) que cortava a base do Dock.
                return IntPtr.Zero;
            case NativeMethods.WM_DISPLAYCHANGE:
            case NativeMethods.WM_DPICHANGED:
                DispatcherQueue.TryEnqueue(ApplySettings);
                break;
            case NativeMethods.WM_SETTINGCHANGE when wParam.ToInt32() == NativeMethods.SPI_SETWORKAREA:
                DispatcherQueue.TryEnqueue(ApplySettings);
                break;
        }
        return NativeMethods.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    // ================================================================== configuração / layout

    private void ApplySettings()
    {
        if (_closing) return;
        var s = _settings.Current;

        _dark = ThemeHelper.IsDark(s.Appearance.Theme);
        _reduceMotion = !_ui.AnimationsEnabled;
        Root.RequestedTheme = _dark ? ElementTheme.Dark : ElementTheme.Light;
        Palette.Theme = Root.RequestedTheme;

        _p = s.ToMagnificationParameters();
        _anim.Duration = s.Magnification.AnimationDurationMs / 1000.0;
        _anim.FollowTimeConstant = s.Magnification.HoverSmoothingMs / 1000.0;
        _anim.Curve = s.Magnification.Easing;
        _maxIcon = _p.BaseIconSize * Math.Max(1.0, _p.MaxScale);

        ApplyMaterial(s);
        ApplyTooltipStyle();
        // Depois de _p: a 1ª checagem de contexto decide quais widgets contextuais existem (sem disparar SyncItems reentrante).
        _widgets.EnableContext(s.Mode == DockMode.Extended && !IsVertical(s.Layout.Edge));
        SyncItems(animate: false);   // pins podem ter mudado (adicionar/remover/reordenar)
        EnsureLayout(force: true);
        RenderFrame(0);
        UpdateRegion(_anim.IsHovering || _dragging, force: true);
        ApplyVisibilityMode(s);
    }

    /// <summary>Dimensiona a janela para a capacidade atual de itens (só quando muda — não por quadro).</summary>
    private void EnsureLayout(bool force = false)
    {
        int count = (int)Math.Ceiling(_items.Where(i => !i.IsSeparator).Sum(i => i.IsWidget ? i.Slots : 1));
        var s = _settings.Current;
        string sig = $"{_p}|{s.Layout.Padding}|{s.Layout.ScreenMargin}|{s.Layout.Edge}";
        bool capacityOk = _geo != null && count <= _geo.Count && count >= _geo.Count - 4;
        if (!force && capacityOk && sig == _layoutSignature) return;
        _layoutSignature = sig;

        bool vertical = IsVertical(s.Layout.Edge);
        _geo = new DockGeometry(_p, count + 2, s.Layout.Padding, vertical ? DockGeometry.SideTooltipSpace : DockGeometry.TooltipSpace);
        _orient = new DockOrientation(s.Layout.Edge, _geo.WindowWidth, _geo.WindowHeight);

        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(), NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref mi);
        _work = mi.rcWork;
        _monitorRect = mi.rcMonitor;
        double newScale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        _scale = newScale;

        var work = mi.rcWork;
        var workDip = new RectD(0, 0, work.Width / _scale, work.Height / _scale);
        var (x, y) = _orient.WindowPosition(workDip, s.Layout.ScreenMargin);
        int px = work.Left + (int)Math.Round(x * _scale);
        int py = work.Top + (int)Math.Round(y * _scale);
        int pw = (int)Math.Ceiling(_orient.WindowWidth * _scale);
        int ph = (int)Math.Ceiling(_orient.WindowHeight * _scale);
        _window = new NativeMethods.RECT { Left = px, Top = py, Right = px + pw, Bottom = py + ph };
        AppWindow.MoveAndResize(new RectInt32(px + _hideDx, py + _hideDy, pw, ph)); // mantém o deslocamento do auto-ocultar

        foreach (var v in _items) { LayoutItemStatic(v); LoadIconIfNeeded(v); }
        _regionApplied = default;
    }

    private void ApplyMaterial(DockSettings s)
    {
        var a = s.Appearance;
        var material = a.Material;
        if (material is GlassMaterial.Acrylic or GlassMaterial.AcrylicThin && !GlassBackdrop.IsAcrylicSupported)
        {
            _log.LogWarning("Desktop Acrylic não suportado neste sistema → Translúcido");
            material = GlassMaterial.Tint;
        }
        if (material == GlassMaterial.Mica && !GlassBackdrop.IsMicaSupported)
        {
            _log.LogWarning("Mica não suportado neste sistema → Translúcido");
            material = GlassMaterial.Tint;
        }
        _effectiveMaterial = material;
        Color? tint = ThemeHelper.ParseTint(a.TintColor);
        PanelStyle.Update(material, a, tint, _dark);

        if (material == GlassMaterial.Tint)
        {
            _glass.HideGlass();
            Color baseColor = tint ?? (_dark ? Color.FromArgb(255, 28, 28, 30) : Color.FromArgb(255, 246, 246, 248));
            TintPill.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(a.TintOpacity * 255), baseColor.R, baseColor.G, baseColor.B));
            byte border = (byte)Math.Round(a.BorderOpacity * 255);
            TintPill.BorderBrush = new SolidColorBrush(a.BorderVisible
                ? (_dark ? Color.FromArgb(border, 255, 255, 255) : Color.FromArgb(border, 0, 0, 0))
                : Colors.Transparent);
            TintPill.CornerRadius = new CornerRadius(Math.Min(a.CornerRadius, (s.Layout.IconSize + 2 * s.Layout.Padding) / 2));
            TintPill.Visibility = Visibility.Visible;
        }
        else
        {
            TintPill.Visibility = Visibility.Collapsed;
            _glass.Backdrop.Configure(material, tint, a.TintOpacity, a.LuminosityOpacity, _dark);
            _glass.SetBorder(a.BorderVisible, _dark);
            _tipGlass.Backdrop.Configure(material, tint, a.TintOpacity, a.LuminosityOpacity, _dark);
            _tipGlass.SetBorder(a.BorderVisible, _dark);
            _glass.ShowGlass();
        }

        var accent = _dark ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(190, 20, 20, 20);
        foreach (var v in _items) ApplyItemColors(v, accent);
        DropMarker.Background = new SolidColorBrush(accent);
    }

    /// <summary>
    /// Rótulo do hover com o mesmo material do Dock: nos materiais DWM (Acrylic/Mica) o fundo é uma 2ª janela de vidro
    /// atrás do texto; no "Translúcido" usa a mesma cor/borda da pílula.
    /// </summary>
    private void ApplyTooltipStyle()
    {
        if (_effectiveMaterial == GlassMaterial.Tint)
        {
            Tooltip.Background = TintPill.Background;
            Tooltip.BorderBrush = TintPill.BorderBrush;
            Tooltip.BorderThickness = new Thickness(1);
            _tipGlass.HideGlass();
        }
        else
        {
            Tooltip.Background = null;
            Tooltip.BorderBrush = null;
            Tooltip.BorderThickness = new Thickness(0);
        }
        TooltipText.Foreground = new SolidColorBrush(_dark ? Colors.White : Color.FromArgb(255, 20, 20, 20));
    }

    private void ApplyItemColors(ItemView v, Color accent)
    {
        v.Indicator.Background = new SolidColorBrush(accent);
        if (v.Line != null)
            v.Line.Background = new SolidColorBrush(_dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0));
    }

    // ================================================================== itens

    /// <summary>Reconstrói a lista de entradas (fixados + em execução) e anima a diferença.</summary>
    private int _syncCount;
    private readonly Stopwatch _syncClock = Stopwatch.StartNew();
    private int _frameCount;

    private void SyncItems(bool animate)
    {
        if (_closing || _dragging) return; // durante um arrasto a ordem visual é do usuário
        if (++_syncCount % 50 == 0)
        {
            _log.LogDebug("Diag: {Syncs} sincronizações e {Frames} quadros em {Sec:0}s", _syncCount, _frameCount, _syncClock.Elapsed.TotalSeconds);
        }
        var s = _settings.Current;
        var pins = s.PinnedApps.Select(p => (p, _info.Resolve(p))).ToList();
        var result = DockItemsBuilder.Build(pins, _tracker.Windows, _tracker.Foreground,
            s.Behavior.ShowRunningApps, _runningOrder, _info.Present);
        _runningOrder = result.RunningOrder;

        var desired = new List<(string Key, DockEntry? Entry, WidgetKind? Widget)>();
        int pinnedCount = result.Entries.Count(e => e.IsPinned);
        for (int i = 0; i < result.Entries.Count; i++)
        {
            if (i == pinnedCount && pinnedCount > 0) desired.Add(("sep", null, null));
            desired.Add((result.Entries[i].Key, result.Entries[i], null));
        }
        // Modo Estendido: widgets à direita, depois de um separador. A chave inclui o tamanho → recria ao mudar.
        if (s.Mode == DockMode.Extended && !IsVertical(s.Layout.Edge)) // widgets são horizontais; no Dock vertical ficam ocultos
        {
            var enabled = s.Widgets.Items.Where(w => w.Enabled && _widgets.IsShown(w.Kind)).ToList();
            if (enabled.Count > 0 && desired.Count > 0) desired.Add(("sep-w", null, null));
            string size = $"{s.Layout.IconSize}:{s.Layout.IconSpacing}:{s.Widgets.ClockSeconds}";
            foreach (var w in enabled) desired.Add(($"w:{w.Kind}:{w.Style}:{_widgets.Variant(w.Kind)}:{size}", null, w.Kind));
        }

        animate &= !_reduceMotion;
        var byKey = _items.ToDictionary(v => v.Key);
        var next = new List<ItemView>(desired.Count + 4);
        foreach (var (key, entry, widget) in desired)
        {
            if (byKey.Remove(key, out var view))
            {
                if (view.Removing) { view.Removing = false; view.Presence.Target = 1; }
                view.Entry = entry;
            }
            else
            {
                view = CreateView(key, entry, widget);
                if (animate) { view.Presence.Snap(0); view.Presence.Target = 1; } else view.Presence.Snap(1);
            }
            next.Add(view);
        }

        // Itens que saíram ficam na posição antiga até a presença chegar a 0.
        foreach (var gone in byKey.Values)
        {
            gone.Removing = true;
            gone.Presence.Target = 0;
            if (!animate) gone.Presence.Snap(0);
            int oldIndex = _items.IndexOf(gone);
            int insertAt = 0;
            for (int j = oldIndex - 1; j >= 0; j--)
            {
                int k = next.IndexOf(_items[j]);
                if (k >= 0) { insertAt = k + 1; break; }
            }
            next.Insert(Math.Min(insertAt, next.Count), gone);
        }

        _items.Clear();
        _items.AddRange(next);
        foreach (var v in _items) UpdateIndicatorTargets(v, s, animate);
        PurgeRemoved();
        _widgets.SetViews(_items.Where(v => v.Widget != null && !v.Removing).Select(v => v.Widget!));
        EnsureCapacity();
        if (_geo != null) { EnsureLayout(); EnsureRendering(); }
    }

    private ItemView CreateView(string key, DockEntry? entry, WidgetKind? widgetKind = null)
    {
        WidgetView? widget = widgetKind is { } wk ? _widgets.Create(wk, _p.BaseIconSize, _p.Spacing, _reduceMotion) : null;
        var root = widget?.Root ?? new Grid { IsHitTestVisible = false };
        var indicator = new Border { Height = 5, Width = 5, CornerRadius = new CornerRadius(2.5), Opacity = 0 };
        var v = new ItemView
        {
            Key = key,
            Entry = entry,
            Root = root,
            Indicator = indicator,
            Presence = new Spring(0, PresenceResponse, 1.0, 0.002),
            Offset = new Spring(0, ReorderResponse, 1.0, 0.05),
            Running = new Spring(0, 0.2, 1.0, 0.002),
            Active = new Spring(0, 0.2, 1.0, 0.002),
            Widget = widget,
            Slots = widgetKind is { } k2 ? _widgets.Slots(k2) : 1,
        };
        if (widget != null)
        {
            // widget: conteúdo próprio, sem indicador
        }
        else if (entry is null)
        {
            v.Line = new Border { Width = 1, CornerRadius = new CornerRadius(0.5), HorizontalAlignment = HorizontalAlignment.Center };
            root.Children.Add(v.Line);
        }
        else
        {
            v.Fallback = new FontIcon { Glyph = "", Opacity = 0.55, Visibility = Visibility.Collapsed };
            v.Image = new Image { Stretch = Stretch.Uniform };
            root.Children.Add(v.Fallback);
            root.Children.Add(v.Image);
            IndicatorLayer.Children.Add(indicator);
        }
        IconLayer.Children.Add(root);
        ApplyItemColors(v, _dark ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(190, 20, 20, 20));
        if (_geo != null) LayoutItemStatic(v);
        LoadIconIfNeeded(v);
        return v;
    }

    /// <summary>Tamanhos/âncoras que dependem da borda. A posição é aplicada a cada quadro via <see cref="Place"/>.</summary>
    private void LayoutItemStatic(ItemView v)
    {
        Canvas.SetLeft(v.Root, 0);
        Canvas.SetTop(v.Root, 0);
        if (v.Widget != null) return;
        // Ícones: caixa do tamanho em repouso (bitmap 1:1 com os pixels da tela); a ampliação é só escala.
        double box = v.Line != null ? _maxIcon : _p.BaseIconSize;
        v.Root.Width = box;
        v.Root.Height = box;
        v.Root.CenterPoint = EdgeAnchor(box, box); // ampliação cresce para longe da borda da tela
        // Nitidez: o bitmap tem exatamente GlyphPx pixels e fica alinhado à grade de pixels físicos.
        int glyphPx = GlyphPx;
        double glyph = glyphPx / _scale, offset = Math.Round((box * _scale - glyphPx) / 2) / _scale;
        if (v.Image != null)
        {
            v.Image.Width = glyph;
            v.Image.Height = glyph;
            v.Image.HorizontalAlignment = HorizontalAlignment.Left;
            v.Image.VerticalAlignment = VerticalAlignment.Top;
            v.Image.Margin = new Thickness(offset, offset, 0, 0);
        }
        if (v.Fallback != null) v.Fallback.FontSize = glyph * 0.7;
        if (v.Line != null) LayoutSeparatorLine(v.Line);
        Canvas.SetTop(v.Indicator, 0);
    }

    /// <summary>Ponto do elemento (w × h, em coordenadas da janela) voltado para a borda da tela.</summary>
    private Vector3 EdgeAnchor(double w, double h) => _orient.Edge switch
    {
        DockEdge.Top => new Vector3((float)(w / 2), 0, 0),
        DockEdge.Left => new Vector3(0, (float)(h / 2), 0),
        DockEdge.Right => new Vector3((float)w, (float)(h / 2), 0),
        _ => new Vector3((float)(w / 2), (float)h, 0),
    };

    private void LayoutSeparatorLine(Border line)
    {
        double len = _p.BaseIconSize * 0.72, gap = _p.BaseIconSize * 0.14;
        bool vertical = _orient.IsVertical;
        line.Width = vertical ? len : 1;
        line.Height = vertical ? 1 : len;
        line.HorizontalAlignment = _orient.Edge switch { DockEdge.Left => HorizontalAlignment.Left, DockEdge.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        line.VerticalAlignment = _orient.Edge switch { DockEdge.Top => VerticalAlignment.Top, DockEdge.Bottom => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
        line.Margin = _orient.Edge switch
        {
            DockEdge.Top => new Thickness(0, gap, 0, 0),
            DockEdge.Left => new Thickness(gap, 0, 0, 0),
            DockEdge.Right => new Thickness(0, 0, gap, 0),
            _ => new Thickness(0, 0, 0, gap),
        };
    }

    /// <summary>Tamanho do desenho do ícone em pixels físicos (inteiro → bitmap 1:1).</summary>
    private int GlyphPx => Math.Max(16, (int)Math.Round(_p.BaseIconSize * IconInset * _scale));

    /// <summary>Arredonda para a grade de pixels físicos (evita a interpolação que borra bitmaps).</summary>
    private double Snap(double dip) => _scale > 0 ? Math.Round(dip * _scale) / _scale : dip;

    /// <summary>Posiciona um elemento a partir de um retângulo canônico; <paramref name="sized"/> também ajusta Width/Height.</summary>
    private void Place(FrameworkElement el, RectD canonical, bool sized = false)
    {
        var r = _orient.ToWindow(canonical);
        r = r with { X = Snap(r.X), Y = Snap(r.Y) };
        if (sized)
        {
            if (Math.Abs(el.Width - r.Width) > 0.05) el.Width = Math.Max(0, r.Width);
            if (Math.Abs(el.Height - r.Height) > 0.05) el.Height = Math.Max(0, r.Height);
        }
        el.Translation = new Vector3((float)r.X, (float)r.Y, 0);
    }

    private MenuFlyoutSubItem PositionMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = L.T("Position"), Icon = new FontIcon { Glyph = "\uE8A0" } };
        foreach (var (edge, label) in new[] { (DockEdge.Bottom, L.T("Bottom")), (DockEdge.Top, L.T("Top")), (DockEdge.Left, L.T("Left (vertical)")), (DockEdge.Right, L.T("Right (vertical)")) })
        {
            var opt = new RadioMenuFlyoutItem { Text = label, GroupName = "edge", IsChecked = _settings.Current.Layout.Edge == edge };
            opt.Click += (_, _) => _settings.Update(s => s.Layout.Edge = edge);
            sub.Items.Add(opt);
        }
        return sub;
    }

    /// <summary>
    /// Ponto de abertura dos painéis: na borda externa da pílula (com uma folga), alinhado ao clique —
    /// o painel nunca cobre o Dock.
    /// </summary>
    private global::Windows.Foundation.Point PanelPoint(global::Windows.Foundation.Point p)
    {
        const double gap = 8;
        var r = _lastPillWindow;
        if (r.Width <= 0 || r.Height <= 0) return p;
        return _orient.Edge switch
        {
            DockEdge.Top => new(p.X, r.Y + r.Height + gap),
            DockEdge.Left => new(r.X + r.Width + gap, p.Y),
            DockEdge.Right => new(r.X - gap, p.Y),
            _ => new(p.X, r.Y - gap),
        };
    }

    /// <summary>Menus abrem para longe da borda da tela.</summary>
    private FlyoutPlacementMode MenuPlacement => _orient.Edge switch
    {
        DockEdge.Top => FlyoutPlacementMode.Bottom,
        DockEdge.Left => FlyoutPlacementMode.Right,
        DockEdge.Right => FlyoutPlacementMode.Left,
        _ => FlyoutPlacementMode.Top,
    };

    private static bool IsVertical(DockEdge edge) => edge is DockEdge.Left or DockEdge.Right;

    private global::Windows.Foundation.Point Canon(global::Windows.Foundation.Point p)
    {
        var (u, v) = _orient.FromWindow(p.X, p.Y);
        return new global::Windows.Foundation.Point(u, v);
    }

    private void LoadIconIfNeeded(ItemView v)
    {
        if (v.Entry is null || _geo is null) return;
        string path = v.Entry.IconPath;
        int px = GlyphPx;
        string key = $"{path}|{px}"; // muda com o caminho, o tamanho do ícone ou o DPI
        if (v.IconPath == key) return;
        v.IconPath = key;
        _ = LoadIconAsync(v, path, px, key);
    }

    private async Task LoadIconAsync(ItemView v, string path, int px, string key)
    {
        var pixels = await _icons.GetPixelsAsync(path, px);
        if (_closing || v.IconPath != key) return;
        var source = IconService.ToImageSource(pixels);
        if (source is null) { if (v.Fallback != null) v.Fallback.Visibility = Visibility.Visible; }
        else if (v.Image != null) { v.Image.Source = source; if (v.Fallback != null) v.Fallback.Visibility = Visibility.Collapsed; }
    }

    private void UpdateIndicatorTargets(ItemView v, DockSettings s, bool animate)
    {
        if (v.Entry is null) return;
        LoadIconIfNeeded(v);
        bool show = s.Behavior.ShowIndicators && v.Entry.IsRunning && !v.Removing;
        v.Running.Target = show ? 1 : 0;
        v.Active.Target = show && v.Entry.IsActive ? 1 : 0;
        if (!animate) { v.Running.Snap(v.Running.Target); v.Active.Snap(v.Active.Target); }
    }

    private void PurgeRemoved()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var v = _items[i];
            if (!v.Removing || v.Presence.Value > 0.001 || v.Presence.Target > 0) continue;
            IconLayer.Children.Remove(v.Root);
            IndicatorLayer.Children.Remove(v.Indicator);
            if (ReferenceEquals(_bounceItem, v)) _bounceItem = null;
            _items.RemoveAt(i);
        }
    }

    private void EnsureCapacity()
    {
        if (_frames.Length < _items.Count) _frames = new IconFrame[_items.Count * 2];
        if (_specs.Length < _items.Count) _specs = new DockItemSpec[_items.Count * 2];
    }

    private void SnapAll()
    {
        foreach (var v in _items)
        {
            v.Presence.Snap(v.Removing ? 0 : 1);
            v.Offset.Snap(0);
            v.Running.Snap(v.Running.Target);
            v.Active.Snap(v.Active.Target);
        }
        PurgeRemoved();
    }

    // ================================================================== renderização

    private bool StepSprings(double dt)
    {
        bool moving = false;
        foreach (var v in _items)
        {
            v.Presence.Response = _reduceMotion ? 0 : PresenceResponse;
            v.Offset.Response = _reduceMotion ? 0 : ReorderResponse;
            v.Presence.Step(dt); v.Offset.Step(dt); v.Running.Step(dt); v.Active.Step(dt);
            moving |= !v.Presence.IsSettled || !v.Offset.IsSettled || !v.Running.IsSettled || !v.Active.IsSettled;
            if (v.Widget != null) moving |= v.Widget.Step(dt);
        }
        _tooltip.Response = _reduceMotion ? 0 : TooltipResponse;
        _tooltip.Step(dt);
        _dropMarker.Step(dt);
        moving |= !_tooltip.IsSettled || !_dropMarker.IsSettled;
        return moving;
    }

    private void RenderFrame(double dt)
    {
        if (_geo is null) return;
        int count = _items.Count;
        EnsureCapacity();
        for (int i = 0; i < count; i++)
        {
            var v = _items[i];
            double presence = Math.Clamp(v.Presence.Value, 0, 1);
            _specs[i] = v.IsSeparator ? DockItemSpec.Separator(DockGeometry.SeparatorWeight, presence)
                : v.IsWidget ? new DockItemSpec { Weight = v.Slots * presence, Magnifies = false }
                : DockItemSpec.Icon(presence);
        }
        var specs = new ReadOnlySpan<DockItemSpec>(_specs, 0, count);
        double restWidth = MagnificationEngine.BaseWidth(_p, specs);
        _origin = _geo.ContentOriginFor(restWidth);
        // Com widgets, a ampliação dos apps cresce só para a esquerda: widgets e borda direita ficam parados.
        bool pinEnd = _items.Any(v => v.IsWidget && !v.Removing);
        var ext = MagnificationEngine.Compute(_p, specs, _anim.Focus - _origin, _anim.Intensity, _frames, pinEnd);

        double now = _clock.Elapsed.TotalSeconds;
        double iconTop = _geo.IconBaseline - _maxIcon;
        double indicatorV = _geo.PillTop + _geo.PillHeight - _geo.Padding / 2 - 2.5; // entre a base do ícone e a borda

        for (int i = 0; i < count; i++)
        {
            var v = _items[i];
            ref var f = ref _frames[i];
            double presence = Math.Clamp(v.Presence.Value, 0, 1);
            double center = _origin + f.Center + v.Offset.Value;
            if (ReferenceEquals(v, _dragItem) && _dragging) center = _dragX - _dragGrabOffset;

            if (v.IsSeparator)
            {
                v.Root.Scale = Vector3.One;
                Place(v.Root, new RectD(center - _maxIcon / 2, iconTop, _maxIcon, _maxIcon));
                v.Root.Opacity = presence;
                continue;
            }
            if (v.Widget != null)
            {
                double wlift = ReferenceEquals(v, _dragItem) && _dragging ? 6 : 0; // levantado enquanto arrasta
                Place(v.Root, new RectD(center - v.Widget.Width / 2, _geo.IconBaseline - _p.BaseIconSize - wlift, v.Widget.Width, _p.BaseIconSize));
                v.Root.Opacity = Math.Pow(presence, 1.5) * (v.Pressed ? 0.6 : 1.0);
                continue;
            }

            // Tamanho cheio do ícone (a presença afeta o espaço; o ícone entra de 92% → 100%, nunca de 0).
            double full = _p.BaseIconSize * f.Scale;
            double appear = 0.92 + 0.08 * presence;
            double s = full * appear / _p.BaseIconSize;
            if (Math.Abs(s - 1) < 0.003) s = 1; // em repouso, escala exata 1 → pixels 1:1
            double lift = 0;
            if (ReferenceEquals(v, _bounceItem))
            {
                double t = (now - _bounceStart) / BounceDuration;
                if (t >= 1 || _reduceMotion) _bounceItem = null;
                else lift = BounceHeight * Math.Abs(Math.Sin(t * Math.PI * 2)) * (1 - t) * (1 - t);
            }
            if (ReferenceEquals(v, _dragItem) && _dragging) lift = 6; // levantado enquanto arrasta

            v.Root.Scale = new Vector3((float)s, (float)s, 1);
            Place(v.Root, new RectD(center - _p.BaseIconSize / 2, _geo.IconBaseline - _p.BaseIconSize - lift, _p.BaseIconSize, _p.BaseIconSize));
            v.Root.Opacity = Math.Pow(presence, 1.5);
            if (v.Image != null) v.Image.Opacity = v.Pressed ? 0.62 : 1.0; // feedback imediato no pointer-down

            // Indicador: ponto sob o ícone; alonga para o app em primeiro plano.
            double run = Math.Clamp(v.Running.Value, 0, 1), act = Math.Clamp(v.Active.Value, 0, 1);
            double w = 5 + 9 * act;
            Place(v.Indicator, new RectD(center - w / 2, indicatorV, w, 5), sized: true);
            v.Indicator.CenterPoint = new Vector3((float)(v.Indicator.Width / 2), (float)(v.Indicator.Height / 2), 0);
            v.Indicator.Opacity = run * presence * (0.8 + 0.2 * act);
            v.Indicator.Scale = new Vector3((float)(0.6 + 0.4 * run), (float)(0.6 + 0.4 * run), 1);
        }

        _lastPill = _geo.PillRect(ext, _origin, restWidth);
        var pill = _lastPillWindow = _orient.ToWindow(_lastPill);

        if (_effectiveMaterial == GlassMaterial.Tint)
        {
            Canvas.SetTop(TintPill, 0);
            Place(TintPill, _lastPill, sized: true);
        }
        else
        {
            var r = new NativeMethods.RECT
            {
                Left = _window.Left + _hideDx + (int)Math.Round(pill.X * _scale),
                Top = _window.Top + _hideDy + (int)Math.Round(pill.Y * _scale),
            };
            r.Right = _window.Left + _hideDx + (int)Math.Round(pill.Right * _scale);
            r.Bottom = _window.Top + _hideDy + (int)Math.Round(pill.Bottom * _scale);
            _glass.Place(r, Hwnd);
        }

        UpdateDropMarker();
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        int i = _hoverIndex;
        bool valid = i >= 0 && i < _items.Count && !_items[i].IsSeparator && !_items[i].Removing;
        _tooltip.Target = valid && !_dragging && !_menuOpen && !_dropHover && _anim.Intensity > 0.3 ? 1 : 0;
        double t = Math.Clamp(_tooltip.Value, 0, 1);
        if (t <= 0.001 && _tooltip.Target == 0)
        {
            if (Tooltip.Opacity != 0) Tooltip.Opacity = 0;
            _tipGlass.HideGlass();
            return;
        }
        if (valid)
        {
            string text = _items[i].Widget?.Tooltip ?? _items[i].Entry!.DisplayName;
            if (!ReferenceEquals(TooltipText.Tag, _items[i]) || TooltipText.Text != text)
            {
                TooltipText.Text = text;
                TooltipText.Tag = _items[i];
            }
        }
        if (TooltipText.Tag is not ItemView tv) return;
        int ti = _items.IndexOf(tv);
        if (ti < 0) return;
        var f = _frames[ti];
        double w = Tooltip.ActualWidth, h = Tooltip.ActualHeight;
        double cx = _origin + f.Center + tv.Offset.Value;
        // Ancorado no lado do ícone oposto à borda; cresce de 96 % e se afasta 4 DIP enquanto aparece.
        double anchorV = _geo.IconBaseline - _p.BaseIconSize * f.Scale - 8 + 4 * (1 - t);
        var (ax, ay) = _orient.ToWindow(cx, anchorV);
        var (x, y) = _orient.Edge switch
        {
            DockEdge.Top => (ax - w / 2, ay),
            DockEdge.Left => (ax, ay - h / 2),
            DockEdge.Right => (ax - w, ay - h / 2),
            _ => (ax - w / 2, ay - h),
        };
        x = Math.Clamp(x, 0, Math.Max(0, _orient.WindowWidth - w));
        y = Math.Clamp(y, 0, Math.Max(0, _orient.WindowHeight - h));
        Tooltip.CenterPoint = _orient.Edge switch // escala a partir do lado voltado para o ícone
        {
            DockEdge.Top => new Vector3((float)(w / 2), 0, 0),
            DockEdge.Left => new Vector3(0, (float)(h / 2), 0),
            DockEdge.Right => new Vector3((float)w, (float)(h / 2), 0),
            _ => new Vector3((float)(w / 2), (float)h, 0),
        };
        Tooltip.Scale = new Vector3((float)(0.96 + 0.04 * t), (float)(0.96 + 0.04 * t), 1);
        Tooltip.Translation = new Vector3((float)Math.Round(x), (float)Math.Round(y), 0);
        Tooltip.Opacity = t;
        PlaceTipGlass(Math.Round(x), Math.Round(y), w, h, t);
    }

    /// <summary>Vidro do rótulo: aparece junto com o texto (a janela DWM não faz fade; entra quando o rótulo já está visível).</summary>
    private void PlaceTipGlass(double x, double y, double w, double h, double t)
    {
        if (_effectiveMaterial == GlassMaterial.Tint || t < 0.5 || w <= 0 || h <= 0 || _hideDx != 0 || _hideDy != 0)
        {
            _tipGlass.HideGlass();
            return;
        }
        var r = new NativeMethods.RECT
        {
            Left = _window.Left + (int)Math.Round(x * _scale),
            Top = _window.Top + (int)Math.Round(y * _scale),
        };
        r.Right = r.Left + (int)Math.Round(w * _scale);
        r.Bottom = r.Top + (int)Math.Round(h * _scale);
        _tipGlass.Place(r, Hwnd);
        _tipGlass.ShowGlass();
    }

    private void UpdateDropMarker()
    {
        double t = Math.Clamp(_dropMarker.Value, 0, 1);
        DropMarker.Opacity = t;
        if (t <= 0.001 || _dropIndex < 0) return;
        double x = DropGapX(_dropIndex);
        Place(DropMarker, new RectD(x - 1.5, _geo.IconBaseline - _p.BaseIconSize * 0.9, 3, _p.BaseIconSize * 0.8), sized: true);
    }

    /// <summary>X (janela) do espaço antes do item fixado de índice <paramref name="pinIndex"/>.</summary>
    private double DropGapX(int pinIndex)
    {
        var pinned = PinnedViews();
        if (pinned.Count == 0) return _origin;
        if (pinIndex >= pinned.Count)
        {
            int last = _items.IndexOf(pinned[^1]);
            return _origin + _frames[last].Center + _frames[last].Size / 2 + _p.Spacing / 2;
        }
        int k = _items.IndexOf(pinned[pinIndex]);
        return _origin + _frames[k].Center - _frames[k].Size / 2 - _p.Spacing / 2;
    }

    private void EnsureRendering()
    {
        if (_renderHooked || _closing) return;
        _renderHooked = true;
        _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void UnhookRendering()
    {
        if (!_renderHooked) return;
        _renderHooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        var t = e is RenderingEventArgs re ? re.RenderingTime : _clock.Elapsed;
        double dt = _lastRenderTime == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.1, (t - _lastRenderTime).TotalSeconds);
        _lastRenderTime = t;

        _frameCount++;
        bool animating = _anim.Advance(dt);
        animating |= StepSprings(dt);
        animating |= StepHide(dt);
        PurgeRemoved();
        RenderFrame(dt);
        animating |= _bounceItem != null || _dragging;

        if (!animating)
        {
            UnhookRendering();
            EnsureLayout();
            if (!_anim.IsHovering && !_dropHover) UpdateRegion(false);
        }
    }

    // ================================================================== região clicável

    /// <summary>
    /// Em repouso, só a pílula captura o mouse (cliques fora passam para o que está atrás).
    /// Com o cursor no Dock (magnificação, arrasto, soltar arquivo), a janela inteira captura.
    /// </summary>
    private void UpdateRegion(bool expanded, bool force = false)
    {
        if (_geo is null) return;
        if (expanded)
        {
            if (_regionExpanded && !force) return;
            _regionExpanded = true;
            _regionApplied = default;
            NativeMethods.SetWindowRgn(Hwnd, IntPtr.Zero, true);
            return;
        }
        var pill = _lastPillWindow;
        var r = new NativeMethods.RECT
        {
            Left = (int)Math.Floor(pill.X * _scale), Top = (int)Math.Floor(pill.Y * _scale),
            Right = (int)Math.Ceiling(pill.Right * _scale), Bottom = (int)Math.Ceiling(pill.Bottom * _scale),
        };
        if (!force && !_regionExpanded && r.Left == _regionApplied.Left && r.Right == _regionApplied.Right
            && r.Top == _regionApplied.Top && r.Bottom == _regionApplied.Bottom) return;
        _regionExpanded = false;
        _regionApplied = r;
        IntPtr rgn = NativeMethods.CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
        if (NativeMethods.SetWindowRgn(Hwnd, rgn, true) == 0) NativeMethods.DeleteObject(rgn); // o sistema assume a posse em caso de sucesso
    }

    // ================================================================== entrada

    private bool IsInsideDock(global::Windows.Foundation.Point p)
    {
        double top = _anim.IsHovering ? _geo.PillTop - _geo.MaxIconOverflow : _geo.PillTop;
        return p.X >= _lastPill.X && p.X < _lastPill.Right && p.Y >= top && p.Y < _lastPill.Bottom;
    }

    private int HitItem(double windowX)
    {
        int i = MagnificationEngine.HitTest(_frames, _items.Count, windowX - _origin, _p.Spacing);
        return i >= 0 && !_items[i].IsSeparator && !_items[i].Removing ? i : -1;
    }

    private void Hover(global::Windows.Foundation.Point pos)
    {
        UpdateRegion(true);
        _anim.PointerMoved(pos.X);
        _hoverIndex = HitItem(pos.X);
        EnsureRendering();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = Canon(e.GetCurrentPoint(Root).Position);

        if (_pressIndex >= 0 && !_dragging &&
            (Math.Abs(pos.X - _pressPoint.X) > DragThreshold || Math.Abs(pos.Y - _pressPoint.Y) > DragThreshold * 1.5))
        {
            BeginDrag(_pressIndex, pos);
        }

        if (_dragging)
        {
            _dragX = pos.X;
            _anim.PointerMoved(pos.X);
            ReorderDuringDrag();
            EnsureRendering();
            return;
        }

        if (IsInsideDock(pos)) Hover(pos);
        else Leave();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) Leave();
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e) => CancelPress();
    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) { if (_dragging) EndDrag(commit: true); }

    private void Leave()
    {
        if (_dragging) return;
        CancelPress();
        if (!_anim.IsHovering && _hoverIndex < 0) return;
        _anim.PointerExited();
        _hoverIndex = -1;
        EnsureRendering();
    }

    private void CancelPress()
    {
        if (_pressIndex >= 0 && _pressIndex < _items.Count) _items[_pressIndex].Pressed = false;
        _pressIndex = -1;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Root);
        if (!pt.Properties.IsLeftButtonPressed && !pt.Properties.IsMiddleButtonPressed) return;
        var pos = Canon(pt.Position);
        int i = HitItem(pos.X);
        if (i < 0 || !IsInsideDock(pos)) return;
        if (pt.Properties.IsMiddleButtonPressed) { if (_items[i].Entry is { } me) NewWindow(me); return; }

        _pressIndex = i;
        _pressPoint = pos;
        _items[i].Pressed = _items[i].Widget?.Clickable != false; // widget decorativo não "afunda"
        _capturedPointer = e.Pointer.PointerId;
        Root.CapturePointer(e.Pointer);
        EnsureRendering();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Root);
        if (pt.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
        Root.ReleasePointerCaptures();

        if (_dragging) { EndDrag(commit: true); return; }

        int index = _pressIndex;
        CancelPress();
        EnsureRendering();
        var pos = Canon(pt.Position);
        if (index >= 0 && index < _items.Count && HitItem(pos.X) == index && IsInsideDock(pos))
        {
            if (_items[index].Widget is { } w) { if (w.Clickable) { WidgetActions.PanelPlacement = MenuPlacement; w.OnClick(Root, PanelPoint(pt.Position)); } }
            else Activate(_items[index]);
            _hoverIndex = -1; // o clique costuma abrir outra janela: esconde o rótulo (volta no próximo movimento)
        }
        if (!IsInsideDock(pos)) Leave();
    }

    // ---------------- ações

    private void Activate(ItemView v)
    {
        var entry = v.Entry!;
        var action = ClickPolicy.Decide(entry, _tracker.Foreground, _settings.Current.Behavior.MinimizeIfActive);
        switch (action.Kind)
        {
            case ClickActionKind.Launch:
                if (_launcher.Launch(entry.LaunchPath, entry.DisplayName)) Bounce(v);
                break;
            case ClickActionKind.Activate:
                if (!WindowActions.Activate(action.Window)) _log.LogWarning("Não foi possível ativar a janela de {Name}", entry.DisplayName);
                break;
            case ClickActionKind.Minimize:
                WindowActions.Minimize(action.Window);
                break;
        }
    }

    private void NewWindow(DockEntry entry)
    {
        if (string.IsNullOrEmpty(entry.LaunchPath)) return;
        var v = _items.FirstOrDefault(i => ReferenceEquals(i.Entry, entry));
        if (_launcher.Launch(entry.LaunchPath, entry.DisplayName) && v != null) Bounce(v);
    }

    private void Bounce(ItemView v)
    {
        if (_reduceMotion) return;
        _bounceItem = v;
        _bounceStart = _clock.Elapsed.TotalSeconds;
        EnsureRendering();
    }

    // ---------------- reordenar arrastando (1:1, vizinhos com mola)

    private List<ItemView> PinnedViews() => _items.Where(v => v.Entry?.IsPinned == true && !v.Removing).ToList();

    private void BeginDrag(int index, global::Windows.Foundation.Point pos)
    {
        var v = _items[index];
        if (v.Entry is null && v.Widget is null) return; // separadores não arrastam
        _dragging = true;
        _dragItem = v;
        v.Pressed = false;
        double center = _origin + _frames[index].Center + v.Offset.Value;
        _dragGrabOffset = pos.X - center;   // respeita onde o usuário pegou o ícone
        _dragX = pos.X;
        _dragHome = index;
        _hoverIndex = -1;
        UpdateRegion(true);
        EnsureRendering();
    }

    /// <summary>
    /// Move o item arrastado na lista conforme o cursor cruza o centro dos vizinhos (FLIP com mola).
    /// Fixados reordenam entre si; um app aberto arrastado para a esquerda do separador vira fixado.
    /// </summary>
    private void ReorderDuringDrag()
    {
        if (_dragItem is null) return;
        int from = _items.IndexOf(_dragItem);
        if (from < 0) return;

        double x = _dragX - _dragGrabOffset;
        if (_dragItem.Widget != null) { ReorderWidgetDuringDrag(from, x); return; }
        int sep = _items.FindIndex(v => v.IsSeparator);
        double sepX = sep >= 0 ? _origin + _frames[sep].Center : double.MaxValue;
        bool pinnedDrag = _dragItem.Entry!.IsPinned;

        int to;
        if (pinnedDrag || x < sepX)
        {
            // Posição dentro da região de fixados = quantos itens dessa região (exceto o arrastado) ficam à esquerda do cursor.
            to = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (sep >= 0 && i >= sep) break;
                if (ReferenceEquals(_items[i], _dragItem)) continue;
                if (_origin + _frames[i].Center < x) to++;
            }
        }
        else
        {
            to = _dragHome; // ainda à direita do separador: volta para a vaga original
        }

        MoveDragged(from, to);
    }

    /// <summary>Widgets reordenam entre si, na região depois do separador dos widgets.</summary>
    private void ReorderWidgetDuringDrag(int from, double x)
    {
        int first = _items.FindIndex(v => v.Key == "sep-w") + 1;
        int to = first;
        for (int i = first; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i], _dragItem) || _items[i].Widget is null) continue;
            if (_origin + _frames[i].Center < x) to++;
        }
        MoveDragged(from, to);
    }

    private void MoveDragged(int from, int to)
    {
        if (_dragItem is null || to == from) return;

        // FLIP: guarda a posição atual de cada vizinho, reordena, e anima o deslocamento residual até 0.
        var before = new Dictionary<ItemView, double>();
        for (int i = 0; i < _items.Count; i++) before[_items[i]] = _origin + _frames[i].Center + _items[i].Offset.Value;
        _items.RemoveAt(from);
        _items.Insert(Math.Clamp(to, 0, _items.Count), _dragItem);
        RenderFrame(0); // recalcula alvos na nova ordem
        for (int i = 0; i < _items.Count; i++)
        {
            var v = _items[i];
            if (ReferenceEquals(v, _dragItem)) continue;
            double now = _origin + _frames[i].Center;
            v.Offset.Snap(before[v] - now);
            v.Offset.Target = 0;
        }
    }

    private void EndDrag(bool commit)
    {
        if (!_dragging || _dragItem is null) return;
        var dragged = _dragItem;
        int index = _items.IndexOf(dragged);
        _dragging = false;
        _dragItem = null;
        _pressIndex = -1;

        // O ícone solto desliza da posição do cursor até a vaga (sem salto).
        if (index >= 0)
        {
            double slot = _origin + _frames[index].Center;
            dragged.Offset.Snap(_dragX - _dragGrabOffset - slot);
            dragged.Offset.Target = 0;
        }

        if (commit && dragged.Widget != null)
        {
            SaveWidgetOrder();
        }
        else if (commit)
        {
            int sep = _items.FindIndex(v => v.IsSeparator);
            bool pinNew = !dragged.Entry!.IsPinned && index >= 0 && sep >= 0 && index < sep;
            var newPins = _items.Where(v => v.Entry != null && !v.Removing &&
                (v.Entry.IsPinned || (pinNew && ReferenceEquals(v, dragged)))).ToList();

            _settings.Update(s =>
            {
                var byId = s.PinnedApps.ToDictionary(p => p.Id);
                var list = new List<PinnedApp>();
                foreach (var v in newPins)
                {
                    if (v.Entry!.Pin is { } pin && byId.TryGetValue(pin.Id, out var existing)) list.Add(existing);
                    else if (ReferenceEquals(v, dragged) && SettingsValidator.IsAcceptablePath(v.Entry.LaunchPath, out _))
                        list.Add(new PinnedApp { Path = v.Entry.LaunchPath, DisplayName = v.Entry.DisplayName });
                }
                // Qualquer pin que não estava visível (ex.: removido no meio do arrasto) é preservado no fim.
                foreach (var p in s.PinnedApps) if (!list.Any(x => x.Id == p.Id) && list.All(x => !ReferenceEquals(x, p))) list.Add(p);
                s.PinnedApps = list;
            });
        }
        EnsureRendering();
    }

    /// <summary>
    /// Grava a nova ordem dos widgets (salva na hora). Widgets desligados/ocultos mantêm suas posições relativas:
    /// só as vagas ocupadas pelos visíveis recebem a nova sequência.
    /// </summary>
    private void SaveWidgetOrder()
    {
        var visible = _items.Where(v => v.Widget != null && !v.Removing).Select(v => v.Widget!.Kind).ToList();
        _settings.Update(s =>
        {
            var items = s.Widgets.Items;
            var slots = new List<int>();
            for (int i = 0; i < items.Count; i++) if (visible.Contains(items[i].Kind)) slots.Add(i);
            var byKind = items.Where(w => visible.Contains(w.Kind)).ToDictionary(w => w.Kind);
            var ordered = visible.Where(byKind.ContainsKey).Select(k => byKind[k]).ToList();
            for (int j = 0; j < slots.Count && j < ordered.Count; j++) items[slots[j]] = ordered[j];
        });
    }

    // ---------------- soltar arquivos do Explorer para fixar

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) { e.AcceptedOperation = DataPackageOperation.None; return; }
        var at = Canon(e.GetPosition(Root));
        if (HitItem(at.X) is int wi and >= 0 && _items[wi].Widget is { AcceptsDrops: true })
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = L.T("Keep on shelf");
            e.DragUIOverride.IsGlyphVisible = false;
            _dropHover = true;
            Hover(at);
            _dropMarker.Target = 0;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Link;
        e.DragUIOverride.Caption = L.T("Pin to Dock");
        e.DragUIOverride.IsGlyphVisible = false;
        var pos = Canon(e.GetPosition(Root));
        _dropHover = true;
        Hover(pos);
        _dropIndex = InsertionIndex(pos.X);
        _dropMarker.Target = 1;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        _dropHover = false;
        _dropMarker.Target = 0;
        Leave();
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        _dropHover = false;
        _dropMarker.Target = 0;
        int at = _dropIndex;
        int hit = HitItem(Canon(e.GetPosition(Root)).X);
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (hit >= 0 && _items[hit].Widget is { AcceptsDrops: true } shelf)
            {
                shelf.AcceptDrop(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList());
                return;
            }
            foreach (var item in items.Reverse()) Pin(item.Path, at);
        }
        finally { deferral.Complete(); Leave(); }
    }

    /// <summary>Índice entre os fixados onde um item solto em <paramref name="windowX"/> entraria.</summary>
    private int InsertionIndex(double windowX)
    {
        var pinned = PinnedViews();
        for (int k = 0; k < pinned.Count; k++)
        {
            int i = _items.IndexOf(pinned[k]);
            if (windowX < _origin + _frames[i].Center) return k;
        }
        return pinned.Count;
    }

    // ---------------- roda do mouse (volume)

    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Root);
        var pos = Canon(pt.Position);
        int i = IsInsideDock(pos) ? HitItem(pos.X) : -1;
        if (i >= 0 && _items[i].Widget is { } w)
        {
            w.OnWheel(pt.Properties.MouseWheelDelta);
            EnsureRendering();
            e.Handled = true;
        }
    }

    // ---------------- menu de contexto

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var raw = e.GetPosition(Root);
        var pos = Canon(raw);
        int index = IsInsideDock(pos) ? HitItem(pos.X) : -1;
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false, Placement = MenuPlacement };

        if (index >= 0 && _items[index].Widget is { } widget)
        {
            int before = menu.Items.Count;
            widget.AddMenuItems(menu);
            var kind = widget.Kind;
            if (WidgetSettings.Styles(kind) is { Count: > 1 } styles)
            {
                var current = _settings.Current.Widgets.StyleOf(kind);
                var sub = new MenuFlyoutSubItem { Text = L.T("Style"), Icon = new FontIcon { Glyph = "\uE790" } };
                foreach (var st in styles)
                {
                    var opt = new RadioMenuFlyoutItem { Text = WidgetLabels.Style(st), IsChecked = st == current, GroupName = "style" };
                    opt.Click += (_, _) => _widgets.SetStyle(kind, st);
                    sub.Items.Add(opt);
                }
                menu.Items.Add(sub);
            }
            if (menu.Items.Count > before) menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item(L.T("Hide this widget"), "\uED1A", () => _settings.Update(s =>
            {
                var w = s.Widgets.Items.FirstOrDefault(x => x.Kind == kind);
                if (w != null) w.Enabled = false;
            })));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        else if (index >= 0)
        {
            var v = _items[index];
            var entry = v.Entry!;
            if (!entry.IsRunning) menu.Items.Add(Item(L.T("Open"), "", () => Activate(v)));
            else
            {
                if (!string.IsNullOrEmpty(entry.LaunchPath)) menu.Items.Add(Item(L.T("New window"), "", () => NewWindow(entry)));
                if (entry.Windows.Count == 1)
                {
                    var w = entry.Windows[0];
                    menu.Items.Add(Item(L.T("Show window"), "", () => WindowActions.Activate(w.Handle)));
                }
                else
                {
                    var sub = new MenuFlyoutSubItem { Text = L.F("Windows ({0})", entry.Windows.Count), Icon = new FontIcon { Glyph = "" } };
                    foreach (var w in entry.Windows)
                    {
                        var h = w.Handle;
                        string title = w.Title.Length > 60 ? w.Title[..60] + "…" : w.Title;
                        var it = new MenuFlyoutItem { Text = title };
                        it.Click += (_, _) => WindowActions.Activate(h);
                        sub.Items.Add(it);
                    }
                    menu.Items.Add(sub);
                }
                var handles = entry.Windows.Select(w => w.Handle).ToList();
                menu.Items.Add(Item(handles.Count == 1 ? L.T("Close window") : L.F("Close all windows ({0})", handles.Count), "",
                    () => { foreach (var h in handles) WindowActions.Close(h); }));
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            if (entry.IsPinned) menu.Items.Add(Item(L.T("Remove from Dock"), "", () => Unpin(entry.Pin!)));
            else if (SettingsValidator.IsAcceptablePath(entry.LaunchPath, out _))
                menu.Items.Add(Item(L.T("Pin to Dock"), "", () => Pin(entry.LaunchPath, int.MaxValue, entry.DisplayName)));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        menu.Items.Add(PositionMenu());
        menu.Items.Add(Item(L.T("Add app…"), "", () => _ = PickAndPinAsync()));
        menu.Items.Add(Item(L.T("Settings…"), "", _openSettings));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(L.T("Quit CleanWinVista"), "", _exit));

        _menuOpen = true;
        CancelPress();
        menu.Closed += (_, _) => { _menuOpen = false; Leave(); };
        OutsideClickDismiss.Attach(menu);
        PanelStyle.Apply(menu);
        menu.ShowAt(Root, new FlyoutShowOptions { Position = raw, Placement = MenuPlacement });
        EnsureRendering();
        e.Handled = true;
    }

    private static MenuFlyoutItem Item(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    private void Unpin(PinnedApp app)
    {
        _settings.Update(s => s.PinnedApps.RemoveAll(a => a.Id == app.Id));
        _log.LogInformation("Removido do Dock: {Name}", app.DisplayName);
    }

    internal async Task PickAndPinAsync()
    {
        var picker = new global::Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.Desktop,
            ViewMode = global::Windows.Storage.Pickers.PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".lnk");
        InitializeWithWindow.Initialize(picker, Hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        Pin(file.Path, int.MaxValue);
    }

    internal void Pin(string path, int index, string? displayName = null)
    {
        if (!SettingsValidator.IsAcceptablePath(path, out var reason))
        {
            _log.LogWarning("Não foi possível fixar {Path}: {Reason}", path, reason);
            return;
        }
        string id = SettingsValidator.MakeId(path);
        _settings.Update(s =>
        {
            if (s.PinnedApps.Any(a => a.Id == id)) return;
            var app = new PinnedApp { Path = path, DisplayName = displayName ?? AppInfoProvider.FriendlyName(path) };
            s.PinnedApps.Insert(Math.Clamp(index, 0, s.PinnedApps.Count), app);
        });
        _log.LogInformation("Fixado no Dock: {Path}", path);
    }
}
