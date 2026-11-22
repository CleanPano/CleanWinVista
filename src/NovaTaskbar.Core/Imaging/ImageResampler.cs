namespace NovaTaskbar.Core.Imaging;

/// <summary>
/// Redução de imagem BGRA (pré-multiplicada) por média de área: cada pixel de destino é a média ponderada dos pixels
/// de origem que ele cobre. Nítido e sem serrilhado para qualquer proporção — ao contrário da amostragem bilinear da
/// composição, que perde detalhe ao reduzir mais que 2×.
/// </summary>
public static class ImageResampler
{
    public static byte[] Downscale(byte[] src, int sw, int sh, int dw, int dh)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0 || src.Length < sw * sh * 4) throw new ArgumentException("Dimensões inválidas.");
        if (dw > sw || dh > sh) throw new ArgumentException("Só reduz; o destino deve ser menor ou igual à origem.");
        var dst = new byte[dw * dh * 4];
        double sx = (double)sw / dw, sy = (double)sh / dh;
        Span<double> acc = stackalloc double[4];
        for (int dy = 0; dy < dh; dy++)
        {
            double y0 = dy * sy, y1 = y0 + sy;
            for (int dx = 0; dx < dw; dx++)
            {
                double x0 = dx * sx, x1 = x0 + sx;
                acc.Clear();
                double total = 0;
                for (int y = (int)y0; y < Math.Min(sh, (int)Math.Ceiling(y1)); y++)
                {
                    double wy = Math.Min(y + 1, y1) - Math.Max(y, y0);
                    for (int x = (int)x0; x < Math.Min(sw, (int)Math.Ceiling(x1)); x++)
                    {
                        double w = wy * (Math.Min(x + 1, x1) - Math.Max(x, x0));
                        int i = (y * sw + x) * 4;
                        acc[0] += src[i] * w; acc[1] += src[i + 1] * w; acc[2] += src[i + 2] * w; acc[3] += src[i + 3] * w;
                        total += w;
                    }
                }
                int o = (dy * dw + dx) * 4;
                for (int c = 0; c < 4; c++) dst[o + c] = (byte)Math.Clamp(Math.Round(acc[c] / total), 0, 255);
            }
        }
        return dst;
    }
}
