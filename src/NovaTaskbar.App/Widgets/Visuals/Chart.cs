using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Core.Widgets;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Point = Windows.Foundation.Point;

namespace NovaTaskbar.App.Widgets;

/// <summary>Uma série do gráfico: histórico + cor.</summary>
internal readonly record struct Series(History Data, Color Color);

/// <summary>
/// Mini-gráfico de histórico (linha com área, ou barras). Visual inspirado nos gráficos do Stat Space; desenho próprio.
/// Cada série é um único Path (barato); o mais recente fica à direita. Barras "espelhadas" põem a 2ª série
/// para baixo de uma linha central (ex.: download ↑ / upload ↓).
/// </summary>
internal sealed class Chart
{
    private const double LineThickness = 1.5;
    /// <summary>Distância mínima entre barras (px): poucas barras legíveis em vez de um borrão de 1 px.</summary>
    private const double MinBarPitch = 3.5;
    private readonly double _w, _h;
    private readonly WidgetStyle _style;
    private readonly bool _mirrored;
    private readonly List<(Path Area, Path Line)> _paths = new();
    private IReadOnlyList<Series> _series = [];
    private double _max = 1;

    public Chart(double width, double height, WidgetStyle style, bool mirrored = false)
    {
        _w = Math.Max(4, width);
        _h = Math.Max(4, height);
        _style = style == WidgetStyle.Bars ? WidgetStyle.Bars : WidgetStyle.Line;
        _mirrored = mirrored && _style == WidgetStyle.Bars;
        Element = new Grid { Width = _w, Height = _h };
    }

    public Grid Element { get; }

    public void Draw(IReadOnlyList<Series> series, double max)
    {
        _series = series;
        _max = max > 0 ? max : 1;
        Redraw();
    }

    private void Redraw()
    {
        while (_paths.Count < _series.Count)
        {
            var area = new Path();
            var line = new Path { StrokeThickness = LineThickness, StrokeLineJoin = PenLineJoin.Round };
            Element.Children.Add(area);
            Element.Children.Add(line);
            _paths.Add((area, line));
        }
        for (int s = 0; s < _series.Count; s++)
        {
            var (data, color) = _series[s];
            var (area, line) = _paths[s];
            if (data.Count == 0) { area.Data = line.Data = null; continue; }

            if (_style == WidgetStyle.Bars)
            {
                bool down = _mirrored && s == 1;
                double baseY = _mirrored ? _h / 2 : _h, span = _mirrored ? _h / 2 - 0.5 : _h;
                area.Data = Bars(data, baseY, span, down);
                area.Fill = Palette.Brush(_mirrored || _series.Count == 1 ? color : Palette.Mix(color, 0.75));
                line.Data = null;
            }
            else
            {
                var pts = LinePoints(data);
                line.Data = GeometryBuilder.Polyline(pts, closed: false);
                line.Stroke = Palette.Brush(color);
                if (s == 0) // área só na série principal, para não embaralhar
                {
                    var poly = new List<Point>(pts) { new(pts[^1].X, _h), new(pts[0].X, _h) };
                    area.Data = GeometryBuilder.Polyline(poly, closed: true);
                    area.Fill = Palette.Brush(Palette.Mix(color, 0.28));
                }
                else area.Data = null;
            }
        }
    }

    private double X(int i, int count, int capacity) => _w * (capacity - count + i) / Math.Max(1, capacity - 1);

    private List<Point> LinePoints(History data)
    {
        var pts = new List<Point>(data.Count + 1);
        double top = LineThickness / 2, usable = _h - LineThickness;
        for (int i = 0; i < data.Count; i++)
            pts.Add(new Point(X(i, data.Count, data.Capacity), top + usable * (1 - Math.Clamp(data[i] / _max, 0, 1))));
        if (pts.Count == 1) pts.Insert(0, new Point(Math.Max(0, pts[0].X - 1), pts[0].Y));
        return pts;
    }

    private PathGeometry Bars(History data, double baseY, double span, bool down)
    {
        var g = new PathGeometry();
        int slots = Math.Clamp((int)(_w / MinBarPitch), 4, data.Capacity), shown = Math.Min(slots, data.Count);
        double slot = _w / slots, bw = Math.Max(1.5, slot * 0.62);
        for (int k = 0; k < shown; k++)
        {
            double v = Math.Clamp(data[data.Count - shown + k] / _max, 0, 1), len = Math.Max(1, span * v);
            double x = slot * (slots - shown + k) + (slot - bw) / 2;
            double y0 = down ? baseY + 0.5 : baseY - len, y1 = down ? baseY + 0.5 + len : baseY;
            var fig = new PathFigure { StartPoint = new Point(x, y0), IsClosed = true, IsFilled = true };
            var seg = new PolyLineSegment();
            seg.Points.Add(new Point(x + bw, y0));
            seg.Points.Add(new Point(x + bw, y1));
            seg.Points.Add(new Point(x, y1));
            fig.Segments.Add(seg);
            g.Figures.Add(fig);
        }
        return g;
    }
}
