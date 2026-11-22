using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaTaskbar.App.Services;
using NovaTaskbar.App.Widgets;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Core.Magnification;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Settings;

public sealed partial class WidgetItem : ObservableObject
{
    private readonly Action<WidgetKind, bool> _toggle;
    private readonly Action<WidgetKind, WidgetStyle> _setStyle;
    private readonly IReadOnlyList<WidgetStyle> _styles;
    private bool _enabled;
    private int _styleIndex;

    internal WidgetItem(WidgetConfig cfg, Action<WidgetKind, bool> toggle, Action<WidgetKind, WidgetStyle> setStyle)
    {
        Kind = cfg.Kind;
        _enabled = cfg.Enabled;
        _toggle = toggle;
        _setStyle = setStyle;
        _styles = WidgetSettings.Styles(cfg.Kind);
        _styleIndex = Math.Max(0, _styles.ToList().IndexOf(cfg.Style));
        StyleNames = _styles.Select(WidgetLabels.Style).ToList();
    }

    public WidgetKind Kind { get; }
    public string Name => WidgetLabels.Name(Kind);
    public string Glyph => WidgetLabels.Glyph(Kind);
    public IReadOnlyList<string> StyleNames { get; }
    public bool HasStyles => _styles.Count > 1;

    public bool Enabled
    {
        get => _enabled;
        set { if (SetProperty(ref _enabled, value)) _toggle(Kind, value); }
    }

    public int StyleIndex
    {
        get => _styleIndex;
        set { if (value >= 0 && value < _styles.Count && SetProperty(ref _styleIndex, value)) _setStyle(Kind, _styles[value]); }
    }
}

public sealed partial class PinnedAppItem : ObservableObject
{
    public PinnedAppItem(PinnedApp app) { App = app; }
    public PinnedApp App { get; }
    public string Name => App.DisplayName;
    public string Path => App.Path;
}

/// <summary>
/// Configurações básicas de aparência/comportamento. Cada alteração vai direto para o SettingsService,
/// que valida, aplica no Dock imediatamente e grava com debounce.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _svc;
    private readonly Func<Task> _pickAndPin;
    private bool _syncing;

    internal SettingsViewModel(SettingsService svc, Func<Task> pickAndPin)
    {
        _svc = svc;
        _pickAndPin = pickAndPin;
        Swatches = ["", "#1C1C1E", "#F5F5F7", "#0A84FF", "#30D158", "#FF9F0A", "#FF375F", "#BF5AF2"];
        Reload();
        _svc.Changed += (_, _) => { if (!_syncing) Reload(); };
    }

    public ObservableCollection<PinnedAppItem> PinnedApps { get; } = new();
    public ObservableCollection<WidgetItem> Widgets { get; } = new();
    public IReadOnlyList<string> Swatches { get; }

    private DockSettings S => _svc.Current;

    private void Set(Action<DockSettings> mutate, params string[] dependents)
    {
        if (_syncing) return;
        _syncing = true;
        try { _svc.Update(mutate); }
        finally { _syncing = false; }
        foreach (var d in dependents) OnPropertyChanged(d);
    }

    public void Reload()
    {
        _syncing = true;
        try
        {
            OnPropertyChanged(string.Empty); // todas as propriedades
            PinnedApps.Clear();
            foreach (var a in S.PinnedApps) PinnedApps.Add(new PinnedAppItem(a));
            Widgets.Clear();
            foreach (var w in S.Widgets.Items)
                Widgets.Add(new WidgetItem(w,
                    (k, on) => Set(s => s.Widgets.Items.First(i => i.Kind == k).Enabled = on),
                    (k, st) => Set(s => s.Widgets.Items.First(i => i.Kind == k).Style = st)));
        }
        finally { _syncing = false; }
    }

    // ---------------- Modo
    public int ModeIndex
    {
        get => (int)S.Mode;
        set
        {
            if (value < 0 || value == (int)S.Mode) return;
            Set(s => DockModePresets.Apply(s, (DockMode)value));
            Reload();
        }
    }

    // ---------------- Aparência
    public int MaterialIndex
    {
        get => (int)S.Appearance.Material;
        set
        {
            if (value < 0 || value == (int)S.Appearance.Material) return;
            Set(s =>
            {
                var m = (GlassMaterial)value;
                // Tint sem desfoque precisa de mais opacidade para ser legível.
                if (m == GlassMaterial.Tint && s.Appearance.TintOpacity < 0.4) s.Appearance.TintOpacity = 0.6;
                if (m != GlassMaterial.Tint && s.Appearance.TintOpacity > 0.6) s.Appearance.TintOpacity = 0.15;
                s.Appearance.Material = m;
            }, nameof(TintOpacity), nameof(MaterialNote), nameof(IsTint), nameof(IsDwmMaterial));
        }
    }

    public bool IsTint => S.Appearance.Material == GlassMaterial.Tint;
    public bool IsDwmMaterial => !IsTint;

    public string MaterialNote => S.Appearance.Material switch
    {
        GlassMaterial.Acrylic => L.T("Real blur of what is behind the Dock (Windows Desktop Acrylic). Corners rounded by Windows itself."),
        GlassMaterial.AcrylicThin => L.T("More transparent Acrylic (Thin variant)."),
        GlassMaterial.Mica => L.T("Mica tints with the wallpaper color; it does not blur windows behind the Dock."),
        _ => L.T("No blur: translucent glass drawn by the app, with any corner radius."),
    };

    public int ThemeIndex
    {
        get => (int)S.Appearance.Theme;
        set { if (value >= 0 && value != (int)S.Appearance.Theme) Set(s => s.Appearance.Theme = (ThemePreference)value); }
    }

    public string TintColor => S.Appearance.TintColor;

    [RelayCommand]
    private void SetTint(string? hex) => Set(s => s.Appearance.TintColor = hex ?? "", nameof(TintColor));

    public double TintOpacity
    {
        get => Math.Round(S.Appearance.TintOpacity * 100);
        set => Set(s => s.Appearance.TintOpacity = value / 100.0);
    }

    public double LuminosityOpacity
    {
        get => Math.Round(S.Appearance.LuminosityOpacity * 100);
        set => Set(s => s.Appearance.LuminosityOpacity = value / 100.0);
    }

    public double CornerRadius
    {
        get => S.Appearance.CornerRadius;
        set => Set(s => s.Appearance.CornerRadius = value);
    }

    public bool BorderVisible
    {
        get => S.Appearance.BorderVisible;
        set => Set(s => s.Appearance.BorderVisible = value);
    }

    // ---------------- Layout
    public double IconSize
    {
        get => S.Layout.IconSize;
        set => Set(s => s.Layout.IconSize = Math.Round(value));
    }

    public double IconSpacing
    {
        get => S.Layout.IconSpacing;
        set => Set(s => s.Layout.IconSpacing = Math.Round(value));
    }

    public double DockPadding
    {
        get => S.Layout.Padding;
        set => Set(s => s.Layout.Padding = Math.Round(value));
    }

    public double ScreenMargin
    {
        get => S.Layout.ScreenMargin;
        set => Set(s => s.Layout.ScreenMargin = Math.Round(value));
    }

    // ---------------- Magnificação
    public bool MagnificationEnabled
    {
        get => S.Magnification.Enabled;
        set => Set(s => s.Magnification.Enabled = value);
    }

    public double MaxScalePercent
    {
        get => Math.Round(S.Magnification.MaxScale * 100);
        set => Set(s => s.Magnification.MaxScale = value / 100.0);
    }

    public double MagnificationRadius
    {
        get => S.Magnification.Radius;
        set => Set(s => s.Magnification.Radius = Math.Round(value));
    }

    public double NeighborInfluence
    {
        get => Math.Round(S.Magnification.NeighborInfluence * 100);
        set => Set(s => s.Magnification.NeighborInfluence = value / 100.0);
    }

    public double AnimationDuration
    {
        get => S.Magnification.AnimationDurationMs;
        set => Set(s => s.Magnification.AnimationDurationMs = (int)Math.Round(value));
    }

    public double HoverSmoothing
    {
        get => S.Magnification.HoverSmoothingMs;
        set => Set(s => s.Magnification.HoverSmoothingMs = (int)Math.Round(value));
    }

    public int EasingIndex
    {
        get => (int)S.Magnification.Easing;
        set { if (value >= 0 && value != (int)S.Magnification.Easing) Set(s => s.Magnification.Easing = (EasingCurve)value); }
    }

    // ---------------- Idioma
    public IReadOnlyList<string> LanguageNames { get; } =
        L.Languages.Select(l => l.Code == "en" ? l.NativeName : $"{l.NativeName} — {l.EnglishName}").ToList();

    private readonly string _startLanguage = L.Current.Code;

    public int LanguageIndex
    {
        get => Math.Max(0, L.Languages.ToList().FindIndex(l => l.Code == S.Behavior.Language));
        set
        {
            if (value < 0 || value >= L.Languages.Count) return;
            string code = L.Languages[value].Code;
            if (code == S.Behavior.Language) return;
            Set(s => s.Behavior.Language = code);
            OnPropertyChanged(nameof(LanguageChanged));
        }
    }

    /// <summary>O idioma escolhido é diferente do que está carregado (pede reinício).</summary>
    public bool LanguageChanged => S.Behavior.Language != _startLanguage;

    // ---------------- Comportamento
    /// <summary>Iniciar com o Windows (chave Run do usuário; lida do registro, não do arquivo de configurações).</summary>
    public bool StartWithWindows
    {
        get => StartupRegistration.IsEnabled();
        set
        {
            if (StartupRegistration.Set(value, Environment.ProcessPath ?? "")) OnPropertyChanged();
            if (!S.Behavior.StartupAsked) AnswerStartup(value);
        }
    }

    /// <summary>Pergunta do 1º uso (cartão no topo das Configurações) ainda sem resposta.</summary>
    public bool ShowStartupPrompt => !S.Behavior.StartupAsked;

    public void AnswerStartup(bool start)
    {
        StartupRegistration.Set(start, Environment.ProcessPath ?? "");
        Set(s => s.Behavior.StartupAsked = true);
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(ShowStartupPrompt));
    }

    /// <summary>Índice = ordem de <see cref="DockVisibility"/> (Sempre, Inteligente, Auto-ocultar).</summary>
    public int VisibilityIndex
    {
        get => (int)S.Behavior.Visibility;
        set { if (Enum.IsDefined((DockVisibility)value)) Set(s => s.Behavior.Visibility = (DockVisibility)value); }
    }

    public bool MinimizeIfActive
    {
        get => S.Behavior.MinimizeIfActive;
        set => Set(s => s.Behavior.MinimizeIfActive = value);
    }

    public bool ShowRunningApps
    {
        get => S.Behavior.ShowRunningApps;
        set => Set(s => s.Behavior.ShowRunningApps = value);
    }

    public bool ShowIndicators
    {
        get => S.Behavior.ShowIndicators;
        set => Set(s => s.Behavior.ShowIndicators = value);
    }

    // ---------------- Posição
    /// <summary>Índice = ordem de <see cref="DockEdge"/> (Embaixo, Em cima, Esquerda, Direita).</summary>
    public int EdgeIndex
    {
        get => (int)S.Layout.Edge;
        set { if (Enum.IsDefined((DockEdge)value)) Set(s => s.Layout.Edge = (DockEdge)value); }
    }

    // ---------------- Widgets (modo Estendido)
    public bool IsExtended => S.Mode == DockMode.Extended;
    public bool IsNotExtended => !IsExtended; // x:Bind em Window não aceita conversores

    public bool Clock24h
    {
        get => S.Widgets.Clock24h;
        set => Set(s => s.Widgets.Clock24h = value);
    }

    public bool ClockSeconds
    {
        get => S.Widgets.ClockSeconds;
        set => Set(s => s.Widgets.ClockSeconds = value);
    }

    /// <summary>Widgets com internet (clima, agenda, moedas, serviços). Desligado de fábrica.</summary>
    public bool AllowNetwork
    {
        get => S.Widgets.AllowNetwork;
        set => Set(s => s.Widgets.AllowNetwork = value);
    }

    public double FocusMinutes
    {
        get => S.Widgets.FocusMinutes;
        set => Set(s => s.Widgets.FocusMinutes = (int)Math.Round(value));
    }

    [RelayCommand]
    private void MoveWidgetUp(WidgetItem? item) => MoveWidget(item, -1);

    [RelayCommand]
    private void MoveWidgetDown(WidgetItem? item) => MoveWidget(item, +1);

    private void MoveWidget(WidgetItem? item, int delta)
    {
        if (item is null) return;
        Set(s =>
        {
            int i = s.Widgets.Items.FindIndex(w => w.Kind == item.Kind);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= s.Widgets.Items.Count) return;
            (s.Widgets.Items[i], s.Widgets.Items[j]) = (s.Widgets.Items[j], s.Widgets.Items[i]);
        });
        Reload();
    }

    [RelayCommand]
    private void UseExtended()
    {
        Set(s => DockModePresets.Apply(s, DockMode.Extended));
        Reload();
    }

    // ---------------- Apps
    [RelayCommand]
    private Task AddApp() => _pickAndPin();

    [RelayCommand]
    private void RemoveApp(PinnedAppItem? item)
    {
        if (item is null) return;
        Set(s => s.PinnedApps.RemoveAll(a => a.Id == item.App.Id));
        Reload();
    }

    [RelayCommand]
    private void MoveUp(PinnedAppItem? item) => Move(item, -1);

    [RelayCommand]
    private void MoveDown(PinnedAppItem? item) => Move(item, +1);

    private void Move(PinnedAppItem? item, int delta)
    {
        if (item is null) return;
        Set(s =>
        {
            int i = s.PinnedApps.FindIndex(a => a.Id == item.App.Id);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= s.PinnedApps.Count) return;
            (s.PinnedApps[i], s.PinnedApps[j]) = (s.PinnedApps[j], s.PinnedApps[i]);
        });
        Reload();
    }

    [RelayCommand]
    private void RestoreFactory()
    {
        Set(s =>
        {
            var fresh = new DockSettings();
            s.Appearance = fresh.Appearance;
            s.Magnification = fresh.Magnification;
            s.Behavior = fresh.Behavior;
            DockModePresets.Apply(s, DockModePresets.FactoryMode);
        });
        Reload();
    }
}
