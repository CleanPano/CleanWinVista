using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Core.Widgets;
using Windows.UI;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Widget nativo do Dock. Design original inspirado nos cards de d3motion.pro/resources (relógio com minutos
/// rolando, anéis de atividade, bateria, player com equalizador, timer de foco) — sem código de terceiros.
/// Movimento: valores animados por mola criticamente amortecida; troca de texto com ease-out forte (≤ 300 ms);
/// animações contínuas (equalizador) rodam no compositor, fora do UI thread.
/// </summary>
internal abstract class WidgetView
{
    protected WidgetView(WidgetKind kind, double iconSize, double width)
    {
        Kind = kind;
        IconSize = iconSize;
        Width = width;
        Root = new Grid { Width = width, Height = iconSize, IsHitTestVisible = false };
    }

    public WidgetKind Kind { get; }
    public double IconSize { get; }
    public double Width { get; }
    public Grid Root { get; }
    public virtual string Tooltip => "";

    public abstract void Update(WidgetData d);
    /// <summary>Avança animações próprias; true enquanto ainda se move.</summary>
    public virtual bool Step(double dt) => false;
    public virtual void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) { }
    public virtual void OnWheel(int delta) { }
    public virtual void AddMenuItems(MenuFlyout menu) { }
    /// <summary>Aceita arquivos soltos sobre o widget (ex.: prateleira).</summary>
    public virtual bool AcceptsDrops => false;
    public virtual bool AcceptDrop(IReadOnlyList<string> paths) => false;

    protected double F(double fraction) => Math.Max(9, IconSize * fraction); // tamanho de fonte relativo ao ícone

    protected static TextBlock Text(double size, bool semibold = false, double opacity = 1) => new()
    {
        FontSize = size,
        FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
        Opacity = opacity,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    protected static MenuFlyoutItem Item(string text, string glyph, Action action)
    {
        var i = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        i.Click += (_, _) => action();
        return i;
    }
}

internal static class TextBlockExtensions
{
    /// <summary>Algarismos tabulares: os dígitos não "dançam" de largura ao mudar.</summary>
    public static void FontFeatures(this TextBlock t) => Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
}
