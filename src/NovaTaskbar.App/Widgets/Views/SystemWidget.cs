using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

/// <summary>CPU e RAM em um dos estilos: anéis, velocímetros, linha, barras ou texto.</summary>
internal sealed class SystemWidget : WidgetView
{
    private readonly WidgetStyle _style;
    private readonly Metric _cpu, _ram;
    private double? _cpuValue;
    private MemoryInfo? _mem;

    public SystemWidget(double iconSize, double width, WidgetStyle style) : base(WidgetKind.System, iconSize, width)
    {
        _style = style;
        _cpu = new Metric(this, "CPU");
        _ram = new Metric(this, "RAM");
        Root.Children.Add(style switch
        {
            WidgetStyle.Line or WidgetStyle.Bars or WidgetStyle.Text => Rows(),
            _ => Columns(),
        });
    }

    public override string Tooltip =>
        $"CPU {(_cpuValue is { } c ? $"{c:0}%" : L.T("unavailable"))} · RAM " +
        (_mem is { } m ? $"{m.UsedPercent:0}% (" + L.F("{0:0.0} of {1:0.0} GB", (m.TotalBytes - m.AvailableBytes) / 1073741824.0, m.TotalBytes / 1073741824.0) + ")" : L.T("unavailable")) +
        "\n" + L.T("Click: Task Manager · right-click: style");

    public override void Update(WidgetData d)
    {
        if (d.Cpu.HasValue) _cpuValue = d.Cpu;
        _mem = d.Memory ?? _mem;
        _cpu.Update(_cpuValue, d.CpuHistory);
        _ram.Update(_mem?.UsedPercent, d.RamHistory);
    }

    public override bool Step(double dt) => _cpu.Step(dt) | _ram.Step(dt);

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) =>
        WidgetActions.Open(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe"));

    /// <summary>Anéis / velocímetros lado a lado, com o número no centro.</summary>
    private StackPanel Columns()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = IconSize * 0.18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_cpu.BuildRound(_style));
        panel.Children.Add(_ram.BuildRound(_style));
        return panel;
    }

    /// <summary>Duas linhas: rótulo + valor à esquerda, gráfico (ou nada, no estilo Texto) à direita.</summary>
    private Grid Rows()
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, RowSpacing = IconSize * 0.08 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        double chartW = _style == WidgetStyle.Text ? 0 : Width * 0.52, chartH = IconSize * 0.42;
        _cpu.BuildRow(grid, 0, _style, chartW, chartH);
        _ram.BuildRow(grid, 1, _style, chartW, chartH);
        if (_style == WidgetStyle.Text) grid.HorizontalAlignment = HorizontalAlignment.Center;
        return grid;
    }

    /// <summary>Um indicador (CPU ou RAM) com o visual do estilo escolhido.</summary>
    private sealed class Metric(SystemWidget owner, string label)
    {
        private Ring? _ring;
        private Gauge? _gauge;
        private Chart? _chart;
        private TextBlock? _value;

        public FrameworkElement BuildRound(WidgetStyle style)
        {
            double s = owner.IconSize, thick = Math.Max(2.5, s * 0.09);
            if (style == WidgetStyle.Gauge)
            {
                _gauge = new Gauge(s * 0.95, thick);
                _value = Text(owner.F(0.22), true);
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(_gauge.Element);
                stack.Children.Add(_value);
                return stack;
            }
            _ring = new Ring(s * 0.82, thick);
            _value = Text(owner.F(0.25), true);
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(_ring.Element);
            g.Children.Add(_value);
            return g;
        }

        public void BuildRow(Grid grid, int row, WidgetStyle style, double chartW, double chartH)
        {
            var name = Text(owner.F(0.22), false, 0.65);
            name.Text = label;
            _value = Text(owner.F(0.27), true);
            _value.FontFeatures();
            _value.MinWidth = owner.IconSize * 0.62;
            _value.TextAlignment = TextAlignment.Right;
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            cell.Children.Add(name);
            cell.Children.Add(_value);
            Grid.SetRow(cell, row);
            grid.Children.Add(cell);
            if (chartW <= 0) return;
            _chart = new Chart(chartW, chartH, style);
            _chart.Element.Margin = new Thickness(6, 0, 0, 0);
            _chart.Element.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(_chart.Element, row);
            Grid.SetColumn(_chart.Element, 1);
            grid.Children.Add(_chart.Element);
        }

        public void Update(double? pct, History history)
        {
            var color = Palette.Level(pct ?? 0);
            _ring?.Set(pct / 100, color);
            _gauge?.Set(pct / 100, color);
            _chart?.Draw([new Series(history, color)], 100);
            if (_value != null) _value.Text = pct is { } p ? (_ring != null ? $"{p:0}" : $"{p:0}%") : "—";
        }

        public bool Step(double dt) => (_ring?.Step(dt) ?? false) | (_gauge?.Step(dt) ?? false);
    }
}
