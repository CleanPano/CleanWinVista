using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using NovaTaskbar.Core.Magnification;
using Windows.UI;

namespace NovaTaskbar.App.Widgets;

/// <summary>Anel de progresso (trilho opaco + arco em polilinha), valor animado por mola.</summary>
internal sealed class Ring
{
    private readonly Ellipse _track;
    private readonly Path _value;
    private readonly double _diameter, _thickness;
    private Spring _spring = new(0, 0.35, 1.0, 0.001);
    private Color _color = Palette.Neutral;

    public Ring(double diameter, double thickness)
    {
        _diameter = diameter;
        _thickness = thickness;
        Element = new Grid { Width = diameter, Height = diameter };
        _track = new Ellipse { Width = diameter, Height = diameter, StrokeThickness = thickness };
        _value = new Path { StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        Element.Children.Add(_track);
        Element.Children.Add(_value);
        Recolor();
    }

    public Grid Element { get; }

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
        _value.Stroke = Palette.Brush(_color);
        _track.Stroke = Palette.Brush(Palette.Track(_color));
    }

    private void Draw()
    {
        double f = Math.Clamp(_spring.Value, 0, 1);
        _value.Data = f <= 0.004 ? null : GeometryBuilder.Arc(_diameter / 2, (_diameter - _thickness) / 2, -Math.PI / 2, f * 2 * Math.PI);
    }
}
