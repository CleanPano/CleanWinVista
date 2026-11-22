using NovaTaskbar.Core.Magnification;

namespace NovaTaskbar.Core.Layout;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>
/// Geometria do Dock (inferior, centralizado) em DIP. A janela tem tamanho fixo para uma "capacidade"
/// de itens (com folga para a expansão máxima e o rótulo); dentro dela o conteúdo fica sempre centralizado,
/// então itens entrando/saindo animam sem redimensionar a janela a cada quadro.
/// </summary>
public sealed class DockGeometry
{
    public const double TooltipSpace = 34;   // espaço acima dos ícones ampliados para o rótulo (Dock horizontal)
    public const double SideTooltipSpace = 260; // Dock vertical: o rótulo fica ao lado do ícone
    public const double ShadowMargin = 0;    // sem margem: a janela termina na base da pílula (não invade a barra nativa)
    public const double SeparatorWeight = 0.35; // largura do separador relativa a uma vaga de ícone

    public DockGeometry(MagnificationParameters p, int capacity, double padding, double tooltipSpace = TooltipSpace)
    {
        Parameters = p;
        Count = Math.Max(0, capacity);
        Padding = padding;

        int slots = Math.Max(Count, 1);
        RestContentWidth = p.BaseWidth(slots);
        PillHeight = p.BaseIconSize + 2 * padding;
        MaxIconOverflow = Math.Max(0, p.BaseIconSize * (p.MaxScale - 1.0));
        MaxExpansion = ComputeMaxExpansion(p, Count);
        Headroom = MaxIconOverflow + tooltipSpace;

        // + espaço de um separador, que pode existir entre fixados e em execução.
        double separator = (p.BaseIconSize + p.Spacing) * SeparatorWeight;
        WindowWidth = Math.Ceiling(RestContentWidth + separator + 2 * padding + 2 * MaxExpansion + 2 * ShadowMargin);
        WindowHeight = Headroom + PillHeight + ShadowMargin;

        ContentOriginX = ContentOriginFor(RestContentWidth);
        PillTop = Headroom;
        IconBaseline = PillTop + padding + p.BaseIconSize; // base (y) onde os ícones se apoiam
    }

    public MagnificationParameters Parameters { get; }
    /// <summary>Capacidade (itens) para a qual a janela foi dimensionada.</summary>
    public int Count { get; }
    public double Padding { get; }
    public double RestContentWidth { get; }
    public double PillHeight { get; }
    public double MaxIconOverflow { get; }
    public double MaxExpansion { get; }
    public double Headroom { get; }
    public double WindowWidth { get; }
    public double WindowHeight { get; }
    /// <summary>Origem do conteúdo quando todos os <see cref="Count"/> itens estão presentes.</summary>
    public double ContentOriginX { get; }
    public double PillTop { get; }
    public double IconBaseline { get; }

    /// <summary>Borda esquerda do conteúdo em repouso (coordenadas da janela) para a largura atual.</summary>
    public double ContentOriginFor(double restWidth) => (WindowWidth - EffectiveRestWidth(restWidth)) / 2.0;

    /// <summary>Dock vazio ainda mostra uma vaga.</summary>
    public double EffectiveRestWidth(double restWidth) => restWidth > 0.5 ? restWidth : Parameters.BaseIconSize;

    /// <summary>Retângulo da pílula para a extensão calculada pelo motor e a origem atual.</summary>
    public RectD PillRect(LayoutExtent extent, double origin, double restWidth)
    {
        double left, right;
        if (restWidth <= 0.5 || extent.Width <= 0)
        {
            left = origin; right = origin + EffectiveRestWidth(restWidth);
        }
        else
        {
            left = origin + extent.Left;
            right = origin + extent.Right;
        }
        return new RectD(left - Padding, PillTop, (right - left) + 2 * Padding, PillHeight);
    }

    /// <summary>Compatibilidade: pílula para ícones uniformes em capacidade total.</summary>
    public RectD PillRect(LayoutExtent extent) =>
        PillRect(extent, ContentOriginX, Count == 0 ? 0 : RestContentWidth);

    public RectD RestPillRectFor(double restWidth)
    {
        double w = EffectiveRestWidth(restWidth);
        return new RectD(ContentOriginFor(restWidth) - Padding, PillTop, w + 2 * Padding, PillHeight);
    }

    public RectD RestPillRect => RestPillRectFor(Count == 0 ? 0 : RestContentWidth);

    /// <summary>Posição da janela (DIP) dentro da área de trabalho para o Dock inferior centralizado.</summary>
    public (double X, double Y) WindowPosition(RectD workArea, double screenMargin)
    {
        double x = workArea.X + (workArea.Width - WindowWidth) / 2.0;
        double pillBottom = workArea.Bottom - screenMargin;
        double y = pillBottom + ShadowMargin - WindowHeight;
        return (Math.Round(x), Math.Round(y));
    }

    /// <summary>Converte X da janela em foco no sistema do motor (capacidade total).</summary>
    public double ToFocus(double windowX) => windowX - ContentOriginX;

    private static double ComputeMaxExpansion(in MagnificationParameters p, int count)
    {
        if (count == 0 || p.MaxScale <= 1.0) return 0;
        // Pior caso: varre o foco de DIP em DIP por toda a largura (barato: só na troca de configuração).
        Span<IconFrame> frames = count <= 128 ? stackalloc IconFrame[count] : new IconFrame[count];
        double best = 0;
        double baseWidth = p.BaseWidth(count);
        int samples = (int)Math.Ceiling(baseWidth);
        for (int i = 0; i <= samples; i++)
        {
            double focus = baseWidth * i / Math.Max(1, samples);
            var ext = MagnificationEngine.Compute(p, count, focus, 1.0, frames);
            // A pílula cresce de forma assimétrica (ancorada no cursor): mede cada lado separadamente.
            best = Math.Max(best, Math.Max(-ext.Left, ext.Right - baseWidth));
        }
        return 2 * (Math.Ceiling(best) + 2); // os dois lados + folga de arredondamento
    }
}
