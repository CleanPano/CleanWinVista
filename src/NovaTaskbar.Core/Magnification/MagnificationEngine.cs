namespace NovaTaskbar.Core.Magnification;

/// <summary>Parâmetros imutáveis do motor de magnificação (unidades em DIP).</summary>
public readonly record struct MagnificationParameters(
    double BaseIconSize,
    double Spacing,
    double MaxScale,
    double Radius,
    double NeighborInfluence,
    double MaxExpansion)
{
    /// <summary>Largura ocupada por <paramref name="count"/> ícones uniformes em repouso.</summary>
    public double BaseWidth(int count) => count <= 0 ? 0 : count * BaseIconSize + (count - 1) * Spacing;

    /// <summary>Centro do ícone <paramref name="index"/> em repouso, para ícones uniformes.</summary>
    public double BaseCenter(int index) => index * (BaseIconSize + Spacing) + BaseIconSize / 2.0;
}

/// <summary>
/// Item do Dock para o motor. <see cref="Weight"/> é a presença do item (0 = some, 1 = vaga inteira) —
/// animar o peso faz os vizinhos abrirem/fecharem espaço suavemente. Itens que não ampliam (separador)
/// usam peso menor que 1 para serem estreitos.
/// </summary>
public struct DockItemSpec
{
    public double Weight;
    public bool Magnifies;

    public static DockItemSpec Icon(double presence = 1) => new() { Weight = presence, Magnifies = true };
    public static DockItemSpec Separator(double width, double presence = 1) => new() { Weight = width * presence, Magnifies = false };
}

/// <summary>Posição calculada de um item. Center é horizontal (eixo principal do Dock).</summary>
public struct IconFrame
{
    public double Center;
    public double Scale;
    public double Size;
}

/// <summary>Extensão ocupada pelos ícones após a magnificação.</summary>
public readonly record struct LayoutExtent(double Left, double Right)
{
    public double Width => Right - Left;
}

/// <summary>
/// Motor de magnificação estilo macOS. Função pura e sem alocações:
/// recebe o foco (posição do cursor no eixo do Dock) e a intensidade (0..1, animada)
/// e escreve escala/posição de cada item no span de saída.
/// </summary>
/// <remarks>
/// Cada item ocupa uma vaga = (tamanho × escala + espaçamento) × peso, com o ícone centrado na vaga.
/// Assim um item com peso 0 não ocupa espaço nem deixa espaçamento sobrando.
/// </remarks>
public static class MagnificationEngine
{
    /// <summary>Fator de escala para um ícone a <paramref name="distance"/> DIP do foco, com intensidade total.</summary>
    public static double ScaleAt(double distance, in MagnificationParameters p)
    {
        if (p.MaxScale <= 1.0 || p.Radius <= 0) return 1.0;
        double d = Math.Abs(distance);
        if (d >= p.Radius) return 1.0;
        double t = d / p.Radius;
        // Sino suave (cosseno elevado): 1 no foco, 0 na borda do raio, derivada contínua nas duas pontas.
        double bell = (Math.Cos(Math.PI * t) + 1.0) / 2.0;
        // Influência dos vizinhos: 1 = sino completo; valores menores concentram o efeito no ícone sob o cursor.
        double influence = Math.Clamp(p.NeighborInfluence, 0.05, 1.0);
        double falloff = influence >= 0.999 ? bell : Math.Pow(bell, 1.0 / influence);
        return 1.0 + (p.MaxScale - 1.0) * falloff;
    }

    /// <summary>Largura em repouso de uma sequência de itens (ícone a ícone, sem magnificação).</summary>
    public static double BaseWidth(in MagnificationParameters p, ReadOnlySpan<DockItemSpec> items)
    {
        if (items.Length == 0) return 0;
        double total = 0;
        for (int i = 0; i < items.Length; i++) total += (p.BaseIconSize + p.Spacing) * Math.Max(0, items[i].Weight);
        // Remove meio espaçamento das pontas (proporcional ao peso dos itens das pontas).
        total -= p.Spacing / 2 * (Math.Max(0, items[0].Weight) + Math.Max(0, items[^1].Weight));
        return Math.Max(0, total);
    }

    /// <summary>Atalho para ícones uniformes (todos com peso 1 e magnificáveis).</summary>
    public static LayoutExtent Compute(in MagnificationParameters p, int count, double focus, double intensity, Span<IconFrame> output)
    {
        if (count <= 0) return new LayoutExtent(0, 0);
        if (output.Length < count) throw new ArgumentException("Output span too small.", nameof(output));
        Span<DockItemSpec> specs = count <= 256 ? stackalloc DockItemSpec[count] : new DockItemSpec[count];
        specs.Fill(DockItemSpec.Icon());
        return Compute(p, specs, focus, intensity, output);
    }

    /// <summary>
    /// Calcula escala e centro de cada item.
    /// </summary>
    /// <param name="p">Parâmetros.</param>
    /// <param name="items">Itens na ordem visual.</param>
    /// <param name="focus">Posição do cursor no sistema de repouso (0 = borda esquerda do conteúdo em repouso).</param>
    /// <param name="intensity">0 = repouso, 1 = magnificação total.</param>
    /// <param name="output">Destino; tamanho mínimo = quantidade de itens.</param>
    /// <param name="pinEnd">
    /// true = a ponta final fica parada (toda a expansão cresce para o início). Usado quando há widgets à direita:
    /// a área dos apps amplia sem empurrar widgets nem mover a borda direita do Dock.
    /// </param>
    public static LayoutExtent Compute(in MagnificationParameters p, ReadOnlySpan<DockItemSpec> items, double focus, double intensity, Span<IconFrame> output, bool pinEnd = false)
    {
        int count = items.Length;
        if (count == 0) return new LayoutExtent(0, 0);
        if (output.Length < count) throw new ArgumentException("Output span too small.", nameof(output));

        intensity = Math.Clamp(intensity, 0.0, 1.0);
        double baseWidth = BaseWidth(p, items);
        double extra = 0;

        // 1) Escala de cada item pela distância do foco ao seu centro em repouso.
        double x = -p.Spacing / 2 * Math.Max(0, items[0].Weight);
        for (int i = 0; i < count; i++)
        {
            double w = Math.Max(0, items[i].Weight);
            double slot = (p.BaseIconSize + p.Spacing) * w;
            double baseCenter = x + slot / 2;
            x += slot;

            double s = 1.0;
            if (items[i].Magnifies && w > 0)
                s = 1.0 + (ScaleAt(focus - baseCenter, p) - 1.0) * intensity * Math.Min(1.0, w);
            output[i].Scale = s;
            extra += p.BaseIconSize * w * (s - 1.0);
        }

        // 2) Expansão máxima: reduz proporcionalmente o excesso de escala (nunca sobrepõe ícones).
        if (p.MaxExpansion >= 0 && extra > p.MaxExpansion && extra > 0)
        {
            double f = p.MaxExpansion / extra;
            extra = 0;
            for (int i = 0; i < count; i++)
            {
                output[i].Scale = 1.0 + (output[i].Scale - 1.0) * f;
                extra += p.BaseIconSize * Math.Max(0, items[i].Weight) * (output[i].Scale - 1.0);
            }
        }

        // 3) Ancoragem: a expansão é distribuída proporcionalmente à posição do cursor,
        //    mantendo o ícone sob o cursor praticamente parado (comportamento do Dock do macOS).
        double anchor = pinEnd ? 1.0 : baseWidth <= 0 ? 0.5 : Math.Clamp(focus / baseWidth, 0.0, 1.0);
        x = -extra * anchor - p.Spacing / 2 * Math.Max(0, items[0].Weight);

        double left = double.MaxValue, right = double.MinValue;
        for (int i = 0; i < count; i++)
        {
            double w = Math.Max(0, items[i].Weight);
            double size = p.BaseIconSize * output[i].Scale;
            double slot = (size + p.Spacing) * w;
            // Tamanho visível: ícone entrando/saindo ocupa só a parte presente; itens que não ampliam
            // (separador, widgets largos) ocupam a vaga inteira menos o espaçamento.
            output[i].Size = items[i].Magnifies
                ? size * Math.Min(1.0, w)
                : Math.Max(0, slot - p.Spacing * Math.Min(1.0, w));
            output[i].Center = x + slot / 2;
            x += slot;

            if (w > 0)
            {
                left = Math.Min(left, output[i].Center - output[i].Size / 2);
                right = Math.Max(right, output[i].Center + output[i].Size / 2);
            }
        }

        if (left > right) { left = 0; right = 0; }
        return new LayoutExtent(left, right);
    }

    /// <summary>Índice do item sob a coordenada (sistema de saída), ou -1. Ignora itens com peso ~0.</summary>
    public static int HitTest(ReadOnlySpan<IconFrame> frames, int count, double x, double spacing)
    {
        double half = spacing / 2.0;
        for (int i = 0; i < count && i < frames.Length; i++)
        {
            if (frames[i].Size <= 0) continue;
            double l = frames[i].Center - frames[i].Size / 2.0 - half;
            double r = frames[i].Center + frames[i].Size / 2.0 + half;
            if (x >= l && x < r) return i;
        }
        return -1;
    }
}
