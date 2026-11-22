using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.Core.Magnification;
using NovaTaskbar.Core.Widgets;
using Windows.UI;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

internal sealed class FocusWidget : WidgetView
{
    private readonly FocusTimer _timer;
    private readonly WidgetSettings _cfg;
    private readonly Ring _ring;
    private readonly TextBlock _text;
    private readonly FontIcon _idle;
    private Spring _pulse = new(0, 0.5, 0.35, 0.002);
    private DateTime _now;
    private readonly Action _changed;

    public FocusWidget(double iconSize, double width, FocusTimer timer, WidgetSettings cfg, Action changed) : base(WidgetKind.Focus, iconSize, width)
    {
        _timer = timer;
        _cfg = cfg;
        _changed = changed;
        _ring = new Ring(iconSize * 0.82, Math.Max(2.5, iconSize * 0.09));
        _text = Text(F(0.22), true);
        _text.FontFeatures();
        _idle = Glyphs.Create(Glyphs.Stopwatch, iconSize * 0.34);
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(_ring.Element);
        g.Children.Add(_text);
        g.Children.Add(_idle);
        Root.Children.Add(g);
        Root.CenterPoint = new Vector3((float)(width / 2), (float)(iconSize / 2), 0);
    }

    public override string Tooltip => _timer.Current switch
    {
        FocusTimer.State.Running => L.F("Focus: {0} left", FocusTimer.Format(_timer.Remaining(_now))) + "\n" + L.T("Click: pause"),
        FocusTimer.State.Paused => L.F("Focus paused ({0})", FocusTimer.Format(_timer.Remaining(_now))) + "\n" + L.T("Click: resume"),
        FocusTimer.State.Finished => L.T("Focus done!") + "\n" + L.T("Click: start again"),
        _ => L.F("{0} min focus", _cfg.FocusMinutes) + "\n" + L.T("Click: start · Right-click: duration"),
    };

    public override void Update(WidgetData d)
    {
        _now = d.Now;
        if (_timer.Tick(d.Now)) { _pulse.Snap(0); _pulse.Velocity = 6; _changed(); } // concluiu: pulso com leve quique
        bool active = _timer.Current is FocusTimer.State.Running or FocusTimer.State.Paused;
        _idle.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        _text.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        var remaining = _timer.Remaining(d.Now);
        _text.Text = remaining.TotalMinutes >= 1 ? $"{(int)Math.Ceiling(remaining.TotalMinutes)}" : $"{remaining.Seconds}s";
        var color = _timer.Current == FocusTimer.State.Paused ? Palette.InkSoft : Palette.Ink;
        _ring.Set(active ? _timer.Fraction(d.Now) : _timer.Current == FocusTimer.State.Finished ? 1 : 0, color);
    }

    public override bool Step(double dt)
    {
        bool moving = _ring.Step(dt);
        _pulse.Step(dt);
        float s = 1 + (float)(_pulse.Value * 0.12);
        Root.Scale = new Vector3(s, s, 1);
        return moving || !_pulse.IsSettled;
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        _timer.Toggle(DateTime.Now);
        _changed();
    }

    public override void AddMenuItems(MenuFlyout menu)
    {
        if (_timer.Current != FocusTimer.State.Idle) menu.Items.Add(Item(L.T("Restart"), "\uE72C", () => { _timer.Reset(); _changed(); }));
        foreach (int m in new[] { 15, 25, 45, 60 })
            menu.Items.Add(Item(L.F("{0} min focus", m), m == _cfg.FocusMinutes ? "\uE73E" : "\uE916", () => WidgetHost.Instance?.SetFocusMinutes(m)));
    }
}
