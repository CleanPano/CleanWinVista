using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Core.Magnification;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using Windows.UI;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

internal sealed class BatteryWidget : WidgetView
{
    private readonly Border _shell;
    private readonly Border _fill;
    private readonly TextBlock _text;
    private readonly FontIcon _bolt;
    private Spring _level = new(0, 0.4, 1.0, 0.002);
    private BatteryInfo _info;
    private readonly double _innerWidth;

    public BatteryWidget(double iconSize, double width) : base(WidgetKind.Battery, iconSize, width)
    {
        double w = iconSize * 0.62, h = iconSize * 0.34;
        _innerWidth = w - 4;
        _shell = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(h * 0.3), BorderThickness = new Thickness(1.4), Padding = new Thickness(1.5) };
        _fill = new Border { CornerRadius = new CornerRadius(h * 0.2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _shell.Child = _fill;
        var nub = new Border { Width = 2, Height = h * 0.4, CornerRadius = new CornerRadius(1), Margin = new Thickness(1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _bolt = Glyphs.Create(Glyphs.Bolt, h * 0.8);
        _bolt.Visibility = Visibility.Collapsed;
        var battery = new Grid();
        battery.Children.Add(_shell);
        battery.Children.Add(_bolt);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(battery);
        row.Children.Add(nub);
        _text = Text(F(0.27), true);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        stack.Children.Add(row);
        stack.Children.Add(_text);
        Root.Children.Add(stack);
        Root.ActualThemeChanged += (_, _) => Recolor();
        Recolor();
        void Recolor()
        {
            var fg = Root.ActualTheme == ElementTheme.Light ? Color.FromArgb(200, 0, 0, 0) : Color.FromArgb(220, 255, 255, 255);
            _shell.BorderBrush = new SolidColorBrush(fg);
            nub.Background = new SolidColorBrush(fg);
        }
    }

    public override string Tooltip => !_info.HasBattery
        ? L.T("No battery — plugged in")
        : L.F("Battery {0}%", _info.Percent) + (_info.Charging ? " · " + L.T("charging") : _info.OnAc ? " · " + L.T("plugged in") : "") +
          (_info.Remaining is { } r && !_info.OnAc ? " · " + L.F("~{0}h{1:00} left", (int)r.TotalHours, r.Minutes) : "") + "\n" + L.T("Click: power options");

    public override void Update(WidgetData d)
    {
        _info = d.Battery;
        if (!_info.HasBattery || _info.Percent is null)
        {
            _text.Text = _info.HasBattery ? "—" : "AC";
            _level.Target = _info.HasBattery ? 0 : 1;
            _fill.Background = new SolidColorBrush(Palette.InkSoft);
            _bolt.Visibility = _info.HasBattery ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
        int p = _info.Percent.Value;
        _text.Text = $"{p}%";
        _level.Target = p / 100.0;
        _bolt.Visibility = _info.Charging ? Visibility.Visible : Visibility.Collapsed;
        var c = p <= 20 && !_info.Charging ? Palette.InkSoft : Palette.Ink;
        _fill.Background = new SolidColorBrush(c);
    }

    public override bool Step(double dt)
    {
        _level.Step(dt);
        _fill.Width = Math.Max(0, _innerWidth * Math.Clamp(_level.Value, 0, 1));
        return !_level.IsSettled;
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => WidgetActions.OpenSettings("ms-settings:powersleep");
}
