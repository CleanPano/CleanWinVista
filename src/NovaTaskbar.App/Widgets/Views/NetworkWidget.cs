using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

/// <summary>Download/upload por segundo: texto, ou texto + gráfico (linha sobreposta ou barras espelhadas ↓/↑).</summary>
internal sealed class NetworkWidget : WidgetView
{
    /// <summary>Escala mínima do gráfico (64 KB/s) — rede ociosa não vira um "serrote" de ruído.</summary>
    private const double MinScale = 64 * 1024;

    private readonly TextBlock _down, _up;
    private readonly Chart? _chart;
    private (double Down, double Up)? _last;

    public NetworkWidget(double iconSize, double width, WidgetStyle style) : base(WidgetKind.Network, iconSize, width)
    {
        _down = Text(F(0.27), true);
        _up = Text(F(0.27), false, 0.75);
        _down.FontFeatures();
        _up.FontFeatures();
        _down.Text = "↓ …";
        _up.Text = "↑ …";
        var rates = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        rates.Children.Add(_down);
        rates.Children.Add(_up);

        if (style == WidgetStyle.Text)
        {
            rates.HorizontalAlignment = HorizontalAlignment.Center;
            Root.Children.Add(rates);
            return;
        }

        _down.HorizontalAlignment = _up.HorizontalAlignment = HorizontalAlignment.Left;
        _chart = new Chart(width * 0.42, iconSize * 0.7, style, mirrored: true);
        _chart.Element.HorizontalAlignment = HorizontalAlignment.Right;
        _chart.Element.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(rates);
        Grid.SetColumn(_chart.Element, 1);
        grid.Children.Add(_chart.Element);
        Root.Children.Add(grid);
    }

    public override string Tooltip => _last is null
        ? L.T("Network: measuring…")
        : L.T("Network (all active adapters) · ↓ download, ↑ upload") + "\n" + L.T("Click: network settings · right-click: style");

    public override void Update(WidgetData d)
    {
        if (d.Network is not { } n) return; // a 1ª leitura só inicializa os contadores
        _last = n;
        _down.Text = "↓ " + Rate.Format(n.Down, L.Culture);
        _up.Text = "↑ " + Rate.Format(n.Up, L.Culture);
        _chart?.Draw([new Series(d.DownHistory, Palette.Blue), new Series(d.UpHistory, Palette.InkSoft)],
            Math.Max(d.DownHistory.NiceMax(MinScale), d.UpHistory.NiceMax(MinScale)));
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => WidgetActions.OpenSettings("ms-settings:network-status");
}
