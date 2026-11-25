using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NovaTaskbar.Core.Widgets;
using Windows.UI;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Base dos widgets de ferramenta: no Dock, um ícone num círculo suave (com selo opcional, ex. nº de tarefas);
/// o clique abre um painel com a ferramenta. Cada widget só escreve <see cref="BuildPanel"/>.
/// </summary>
internal abstract class PanelWidget : WidgetView
{
    private readonly Ellipse _disc;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private readonly Ring? _ring;
    private Color _accent = Palette.Neutral;

    protected PanelWidget(WidgetKind kind, double iconSize, double width, string glyph, bool withRing = false) : base(kind, iconSize, width)
    {
        double d = iconSize * 0.82;
        var cell = new Grid { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _disc = new Ellipse { Width = d, Height = d, StrokeThickness = 1 };
        cell.Children.Add(_disc);
        if (withRing)
        {
            _ring = new Ring(d, Math.Max(2.5, iconSize * 0.085));
            cell.Children.Add(_ring.Element);
        }
        Icon = Glyphs.Create(glyph, iconSize * 0.36);
        cell.Children.Add(Icon);
        _badgeText = new TextBlock { FontSize = Math.Max(8, iconSize * 0.2), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Palette.Background), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _badge = new Border
        {
            MinWidth = iconSize * 0.36, Height = iconSize * 0.36, CornerRadius = new CornerRadius(iconSize * 0.18),
            Padding = new Thickness(iconSize * 0.06, 0, iconSize * 0.06, 0),
            Background = new SolidColorBrush(Palette.Ink), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -iconSize * 0.04, -iconSize * 0.06, 0), Child = _badgeText, Visibility = Visibility.Collapsed,
        };
        cell.Children.Add(_badge);
        Root.Children.Add(cell);
        Recolor();
    }

    protected FontIcon Icon { get; }

    /// <summary>Selo no canto (null/vazio esconde).</summary>
    protected void SetBadge(string? text)
    {
        _badge.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        _badgeText.Text = text ?? "";
    }

    /// <summary>Anel de progresso ao redor do ícone (só quando criado com withRing).</summary>
    protected void SetRing(double? fraction, Color color) => _ring?.Set(fraction, color);

    /// <summary>Destaque do círculo (ex. lembrete vencido).</summary>
    protected void SetAccent(Color? color)
    {
        _accent = color ?? Palette.Neutral;
        Recolor();
    }

    /// <summary>
    /// Círculo vazado (só contorno fino), para o vidro do Dock aparecer por dentro — mesmo visual dos anéis dos outros
    /// widgets. Com anel, o próprio trilho do anel faz o contorno. Destaque (lembrete vencido etc.) colore o contorno.
    /// </summary>
    private void Recolor()
    {
        bool accent = _accent != Palette.Neutral;
        _disc.Fill = null;
        _disc.Stroke = _ring != null && !accent ? null : Palette.Brush(accent ? Palette.Mix(_accent, 0.85) : Palette.Hairline);
        Icon.Foreground = Palette.Brush(accent ? Palette.Mix(_accent, 0.95) : Palette.Foreground);
    }

    public override bool Step(double dt) => _ring?.Step(dt) ?? false;

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) =>
        OpenPanel = WidgetActions.ShowFlyout(BuildPanel(), anchor, at);

    /// <summary>Último painel aberto por este widget (para fechá-lo depois de uma ação).</summary>
    protected Flyout? OpenPanel { get; private set; }

    protected abstract FrameworkElement BuildPanel();

    // ---------------- peças de painel
    protected static StackPanel Panel(string title, double width = 300)
    {
        var p = new StackPanel { Spacing = 8, Padding = new Thickness(4), Width = width };
        var t = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
        p.Children.Add(t);
        return p;
    }

    protected static TextBlock Hint(string text) => new()
    {
        Text = text, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
    };

    protected static Button Btn(string text, Action click, string? glyph = null, bool accent = false)
    {
        object content = text;
        if (glyph != null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            sp.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
            sp.Children.Add(new TextBlock { Text = text });
            content = sp;
        }
        var b = new Button { Content = content };
        if (accent && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var st) && st is Style style) b.Style = style;
        b.Click += (_, _) => click();
        return b;
    }

    protected static StackPanel Row(params UIElement[] children)
    {
        var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in children) r.Children.Add(c);
        return r;
    }

    protected static ScrollViewer Scroll(UIElement content, double maxHeight = 320) => new()
    {
        Content = content, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
}
