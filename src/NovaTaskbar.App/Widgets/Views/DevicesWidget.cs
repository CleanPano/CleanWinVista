using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.Core.Widgets;
using Windows.UI;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Periféricos conectados com bateria: um anel por aparelho (o de carga mais baixa primeiro), com o ícone do tipo.
/// Verde &gt; 40 %, laranja 20–40 %, vermelho &lt; 20 % — hora de carregar.
/// </summary>
internal sealed class DevicesWidget : WidgetView
{
    private readonly StackPanel _row;
    private readonly List<(Ring Ring, FontIcon Icon)> _slots = new();
    private IReadOnlyList<PeripheralBattery> _devices = [];

    public DevicesWidget(double iconSize, double width) : base(WidgetKind.Devices, iconSize, width)
    {
        _row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = iconSize * 0.13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Root.Children.Add(_row);
    }

    public override string Tooltip => _devices.Count == 0
        ? L.T("No peripherals with battery connected")
        : string.Join("\n", _devices.Select(d => $"{d.Name}: {d.Percent}%{(Peripherals.Severity(d.Percent) == 2 ? " — " + L.T("charge it!") : "")}")) +
          "\n" + L.T("Click: Bluetooth & devices");

    public override void Update(WidgetData d)
    {
        _devices = d.Devices;
        int n = Math.Min(_devices.Count, WidgetSettings.MaxDevicesShown);
        while (_slots.Count < n)
        {
            var ring = new Ring(IconSize * 0.74, Math.Max(2.5, IconSize * 0.085));
            var icon = Glyphs.Create(Glyphs.Bluetooth, IconSize * 0.3);
            var cell = new Grid { VerticalAlignment = VerticalAlignment.Center };
            cell.Children.Add(ring.Element);
            cell.Children.Add(icon);
            _row.Children.Add(cell);
            _slots.Add((ring, icon));
        }
        for (int i = 0; i < _slots.Count; i++)
        {
            bool visible = i < n;
            ((FrameworkElement)_row.Children[i]).Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) continue;
            var p = _devices[i];
            _slots[i].Ring.Set(p.Percent / 100.0, Level(p.Percent));
            Glyphs.Set(_slots[i].Icon, Glyph(p.Kind));
        }
    }

    public override bool Step(double dt)
    {
        bool more = false;
        foreach (var (ring, _) in _slots) more |= ring.Step(dt);
        return more;
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => WidgetActions.OpenSettings("ms-settings:bluetooth");

    private static Color Level(int pct) => Peripherals.Severity(pct) switch { 2 => Palette.Red, 1 => Palette.Orange, _ => Palette.Green };

    private static string Glyph(PeripheralKind k) => k switch
    {
        PeripheralKind.Headphones => Glyphs.Headphones,
        PeripheralKind.Mouse => Glyphs.Mouse,
        PeripheralKind.Keyboard => Glyphs.Keyboard,
        PeripheralKind.Gamepad => Glyphs.Gamepad,
        PeripheralKind.Phone => Glyphs.Phone,
        PeripheralKind.Pen => Glyphs.Pen,
        _ => Glyphs.Bluetooth,
    };
}
