using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NovaTaskbar.Core.Magnification;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace NovaTaskbar.App.Widgets;

/// <summary>Velocímetro semicircular (180°) com ponteiro; valor animado por mola.</summary>
internal sealed class Gauge
{
    private readonly double _w, _t, _r, _c;
    private readonly Path _track, _value;
    private readonly Line _needle;
    private readonly Ellipse _hub;
    private Spring _spring = new(0, 0.45, 0.85, 0.001);
    private Color _color = Palette.Neutral;

    public Gauge(double width, double thickness)
    {
        _w = width;
        _t = thickness;
        _c = width / 2;
        _r = (width - thickness) / 2;
        Element = new Grid { Width = width, Height = _c + thickness };
        _track = Arc();
        _value = Arc();
        _track.Data = GeometryBuilder.Arc(_c, _c, _r, Math.PI, Math.PI);
        double hub = Math.Max(3, thickness * 1.3);
        _needle = new Line { StrokeThickness = Math.Max(1.2, thickness * 0.5), StrokeEndLineCap = PenLineCap.Round, X1 = _c, Y1 = _c };
        _hub = new Ellipse { Width = hub, Height = hub, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(_c - hub / 2, _c - hub / 2, 0, 0) };
        foreach (var e in new UIElement[] { _track, _value, _needle, _hub }) Element.Children.Add(e);
        Recolor();
        Draw();
    }

    public Grid Element { get; }

    private Path Arc() => new() { StrokeThickness = _t, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };

    public void Set(double? fraction, Color color)
    {
        _color = color;
        Recolor();
        _spring.Target = Math.Clamp(fraction ?? 0, 0, 1);
    }

    public bool Step(double dt)
    {
        _spring.Step(dt);
        Draw();
        return !_spring.IsSettled;
    }

    private void Recolor()
    {
        _track.Stroke = Palette.Brush(Palette.Track(_color));
        _value.Stroke = Palette.Brush(_color);
        var fg = Palette.Brush(Palette.Foreground);
        _needle.Stroke = fg;
        _hub.Fill = fg;
    }

    private void Draw()
    {
        double f = Math.Clamp(_spring.Value, 0, 1), a = Math.PI + Math.PI * f, len = _r * 0.72;
        _value.Data = f <= 0.004 ? null : GeometryBuilder.Arc(_c, _c, _r, Math.PI, Math.PI * f);
        _needle.X2 = _c + len * Math.Cos(a);
        _needle.Y2 = _c + len * Math.Sin(a);
    }
}
