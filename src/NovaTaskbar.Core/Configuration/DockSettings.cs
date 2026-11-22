using System.Text.Json.Serialization;
using NovaTaskbar.Core.Magnification;
using NovaTaskbar.Core.Widgets;

namespace NovaTaskbar.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<DockMode>))]
public enum DockMode { Classic, Floating, Extended }

/// <summary>Material de fundo. Ver docs/WINDOWS-INTEGRATION.md para o que cada um realmente faz.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GlassMaterial>))]
public enum GlassMaterial
{
    /// <summary>Desktop Acrylic (desfoque real do que está atrás, via DWM/Windows App SDK).</summary>
    Acrylic,
    /// <summary>Desktop Acrylic variante "Thin" (mais transparente).</summary>
    AcrylicThin,
    /// <summary>Mica Alt (tinge com o papel de parede; não desfoca o que está atrás da janela).</summary>
    Mica,
    /// <summary>Sem desfoque: vidro desenhado pelo app (cor + opacidade), cantos com raio livre.</summary>
    Tint,
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
public enum ThemePreference { System, Light, Dark }

[JsonConverter(typeof(JsonStringEnumConverter<DockEdge>))]
public enum DockEdge { Bottom, Top, Left, Right }

public sealed class AppearanceSettings
{
    public GlassMaterial Material { get; set; } = GlassMaterial.Acrylic;
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    /// <summary>Cor de tingimento #RRGGBB. Vazio = cor automática do tema.</summary>
    public string TintColor { get; set; } = "";
    /// <summary>0..1 — opacidade do tingimento (Acrylic/Mica) ou do fundo (Tint).</summary>
    public double TintOpacity { get; set; } = 0.6;
    /// <summary>0..1 — opacidade de luminosidade do Acrylic.</summary>
    public double LuminosityOpacity { get; set; } = 0.04;
    /// <summary>Raio dos cantos (DIP). Aplicado integralmente no material Tint; nos materiais DWM o Windows usa o arredondamento do sistema.</summary>
    public double CornerRadius { get; set; } = 12;
    public bool BorderVisible { get; set; } = false;
    public double BorderOpacity { get; set; } = 0.22;
    public bool ShadowEnabled { get; set; } = true;
}

public sealed class LayoutSettings
{
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    public double IconSize { get; set; } = 40;
    public double IconSpacing { get; set; } = 6;
    public double Padding { get; set; } = 10;
    public double ScreenMargin { get; set; } = 16;
    /// <summary>Alargamento máximo do Dock durante a magnificação (DIP). -1 = sem limite.</summary>
    public double MaxExpansion { get; set; } = 160;
}

public sealed class MagnificationSettings
{
    public bool Enabled { get; set; } = true;
    public double MaxScale { get; set; } = 1.1;
    public double Radius { get; set; } = 170;
    public double NeighborInfluence { get; set; } = 0.2;
    public int AnimationDurationMs { get; set; } = 30;
    public EasingCurve Easing { get; set; } = EasingCurve.EaseOutCubic;
    /// <summary>Suavização do acompanhamento do cursor (ms). Menor = mais sensível.</summary>
    public int HoverSmoothingMs { get; set; } = 25;
}

/// <summary>Quando o Dock sai do caminho (inspirado nos modos "dodge" do Latte Dock; implementação própria).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DockVisibility>))]
public enum DockVisibility
{
    /// <summary>Sempre visível.</summary>
    AlwaysVisible,
    /// <summary>Some com app em tela cheia ou maximizado, navegador ou Explorador de Arquivos em primeiro plano; volta ao encostar o mouse na borda.</summary>
    Smart,
    /// <summary>Sempre escondido; aparece ao encostar o mouse na borda.</summary>
    AutoHide,
}

public sealed class BehaviorSettings
{
    /// <summary>Idioma da interface (um dos 20 de <see cref="NovaTaskbar.Core.Localization.L.Languages"/>). Padrão: inglês.</summary>
    public string Language { get; set; } = "en";
    /// <summary>Já perguntamos se o app deve iniciar com o Windows (pergunta do 1º uso). O estado real fica no registro.</summary>
    public bool StartupAsked { get; set; }

    /// <summary>Visibilidade do Dock (padrão: Inteligente).</summary>
    public DockVisibility Visibility { get; set; } = DockVisibility.Smart;

    /// <summary>Clicar no app que já está em primeiro plano minimiza a janela (como a barra do Windows). Desligado = comportamento do macOS.</summary>
    public bool MinimizeIfActive { get; set; } = true;
    /// <summary>Mostra apps abertos que não estão fixados, depois de um separador.</summary>
    public bool ShowRunningApps { get; set; } = true;
    /// <summary>Mostra o indicador sob apps em execução.</summary>
    public bool ShowIndicators { get; set; } = true;
}

public sealed class PinnedApp
{
    /// <summary>Identidade estável (caminho normalizado em minúsculas).</summary>
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Caminho de .exe, .lnk, pasta ou shell:AppsFolder\&lt;AUMID&gt; (app empacotado). Nunca contém argumentos.</summary>
    public string Path { get; set; } = "";
}

public sealed class DockSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DockMode Mode { get; set; } = DockModePresets.FactoryMode;
    public AppearanceSettings Appearance { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public MagnificationSettings Magnification { get; set; } = new();
    public BehaviorSettings Behavior { get; set; } = new();
    /// <summary>Widgets do modo Estendido.</summary>
    public WidgetSettings Widgets { get; set; } = new();
    public List<PinnedApp> PinnedApps { get; set; } = new();

    /// <summary>Verdadeiro depois que os apps padrão foram oferecidos uma vez (não recria se o usuário remover todos).</summary>
    public bool DefaultsSeeded { get; set; }

    public MagnificationParameters ToMagnificationParameters() => new(
        BaseIconSize: Layout.IconSize,
        Spacing: Layout.IconSpacing,
        MaxScale: Magnification.Enabled ? Magnification.MaxScale : 1.0,
        Radius: Magnification.Radius,
        NeighborInfluence: Magnification.NeighborInfluence,
        MaxExpansion: Layout.MaxExpansion);

    public DockSettings Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<DockSettings>(
            System.Text.Json.JsonSerializer.Serialize(this, SettingsJson.Options), SettingsJson.Options)!;
}

public static class DockModePresets
{
    /// <summary>Modo do padrão de fábrica (o que um usuário novo vê): Estendido com widgets.</summary>
    public const DockMode FactoryMode = DockMode.Extended;

    /// <summary>Aplica os valores de layout/aparência característicos do modo. O usuário pode ajustar depois.</summary>
    public static void Apply(DockSettings s, DockMode mode)
    {
        s.Mode = mode;
        switch (mode)
        {
            case DockMode.Classic:
                s.Layout.IconSize = 33; s.Layout.IconSpacing = 8; s.Layout.Padding = 12; s.Layout.ScreenMargin = 10;
                s.Magnification.MaxScale = 1.31; s.Magnification.Radius = 53; s.Magnification.AnimationDurationMs = 40;
                s.Magnification.HoverSmoothingMs = 70;
                s.Appearance.CornerRadius = 10; s.Appearance.TintOpacity = 0.6;
                break;
            case DockMode.Floating:
                s.Layout.IconSize = 28; s.Layout.IconSpacing = 5; s.Layout.Padding = 8; s.Layout.ScreenMargin = 16;
                s.Magnification.MaxScale = 1.3; s.Magnification.AnimationDurationMs = 40;
                s.Appearance.CornerRadius = 10; s.Appearance.TintOpacity = 0.45;
                break;
            case DockMode.Extended: // padrão de fábrica = visual do dono do app (22/11/2026)
                s.Layout.IconSize = 40; s.Layout.IconSpacing = 6; s.Layout.Padding = 10; s.Layout.ScreenMargin = 16;
                s.Magnification.MaxScale = 1.1; s.Magnification.Radius = 170; s.Magnification.AnimationDurationMs = 30;
                s.Magnification.HoverSmoothingMs = 25; s.Magnification.NeighborInfluence = 0.2;
                s.Appearance.CornerRadius = 12; s.Appearance.TintOpacity = 0.6;
                break;
        }
    }
}
