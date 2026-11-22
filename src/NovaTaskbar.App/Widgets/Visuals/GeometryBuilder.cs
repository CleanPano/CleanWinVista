using Microsoft.UI.Xaml.Media;
using Point = Windows.Foundation.Point;

namespace NovaTaskbar.App.Widgets;

/// <summary>Construção de geometrias simples (polilinhas evitam as ambiguidades de ArcSegment).</summary>
internal static class GeometryBuilder
{
    /// <summary>Arco centrado em (c, c), raio r, a partir do ângulo <paramref name="start"/> (rad, 0 = 3 h, horário) por <paramref name="sweep"/> rad.</summary>
    public static PathGeometry Arc(double c, double r, double start, double sweep) => Arc(c, c, r, start, sweep);

    public static PathGeometry Arc(double cx, double cy, double r, double start, double sweep)
    {
        int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (2 * Math.PI) * 72));
        var pts = new List<Point>(n + 1);
        for (int k = 0; k <= n; k++)
        {
            double a = start + sweep * k / n;
            pts.Add(new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }
        return Polyline(pts, closed: false);
    }

    public static PathGeometry Polyline(IReadOnlyList<Point> pts, bool closed)
    {
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = closed, IsFilled = closed };
        var seg = new PolyLineSegment();
        for (int i = 1; i < pts.Count; i++) seg.Points.Add(pts[i]);
        fig.Segments.Add(seg);
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }
}
