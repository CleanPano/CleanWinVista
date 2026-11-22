using NovaTaskbar.Core.Configuration;

namespace NovaTaskbar.Core.Layout;

/// <summary>
/// Mapeia o espaço "canônico" do Dock para a janela real. Todo o layout (magnificação, pílula, rótulo) é calculado
/// como se o Dock estivesse na borda inferior: <c>u</c> = eixo principal (esquerda→direita), <c>v</c> = eixo transversal
/// (cresce em direção à borda da tela). Esta estrutura gira/espelha esse espaço para as bordas Superior, Esquerda e Direita.
/// </summary>
public readonly record struct DockOrientation(DockEdge Edge, double Width, double Height)
{
    /// <summary>Esquerda/Direita: ícones empilhados na vertical.</summary>
    public bool IsVertical => Edge is DockEdge.Left or DockEdge.Right;

    public double WindowWidth => IsVertical ? Height : Width;
    public double WindowHeight => IsVertical ? Width : Height;

    public (double X, double Y) ToWindow(double u, double v) => Edge switch
    {
        DockEdge.Top => (u, Height - v),
        DockEdge.Left => (Height - v, u),
        DockEdge.Right => (v, u),
        _ => (u, v),
    };

    public (double U, double V) FromWindow(double x, double y) => Edge switch
    {
        DockEdge.Top => (x, Height - y),
        DockEdge.Left => (y, Height - x),
        DockEdge.Right => (y, x),
        _ => (x, y),
    };

    public RectD ToWindow(RectD r)
    {
        var (x1, y1) = ToWindow(r.X, r.Y);
        var (x2, y2) = ToWindow(r.Right, r.Bottom);
        return new RectD(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    /// <summary>Canto superior esquerdo da janela (DIP, relativo à área de trabalho), centralizada na borda escolhida.</summary>
    public (double X, double Y) WindowPosition(RectD workArea, double screenMargin)
    {
        double cx = workArea.X + (workArea.Width - WindowWidth) / 2.0;
        double cy = workArea.Y + (workArea.Height - WindowHeight) / 2.0;
        var (x, y) = Edge switch
        {
            DockEdge.Top => (cx, workArea.Y + screenMargin),
            DockEdge.Left => (workArea.X + screenMargin, cy),
            DockEdge.Right => (workArea.Right - screenMargin - WindowWidth, cy),
            _ => (cx, workArea.Bottom - screenMargin - WindowHeight),
        };
        return (Math.Round(x), Math.Round(y));
    }
}
