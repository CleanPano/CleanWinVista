using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Cores dos widgets. A janela do Dock é transparente via DWM e não compõe alfa parcial de formas corretamente
/// (uma cor a 18 % aparece sólida), então trilhos/áreas usam cores <b>opacas</b> já misturadas com o fundo da pílula.
/// </summary>
internal static class Palette
{
    // Estilo clássico minimalista: os widgets não usam cores — tudo na cor do texto do Dock (branco/grafite).
    // Os nomes antigos continuam para não espalhar mudanças; todos devolvem a mesma "tinta".
    public static Color Ink => Foreground;
    public static Color Green => Ink;
    public static Color Orange => Ink;
    public static Color Red => Ink;
    public static Color Blue => Ink;
    public static Color Purple => Ink;
    /// <summary>Tinta secundária (ex.: upload no gráfico de rede).</summary>
    public static Color InkSoft => Mix(Foreground, 0.55);
    public static readonly Color Neutral = Color.FromArgb(255, 96, 96, 98);

    /// <summary>Tema do Dock (definido pelo DockWindow). Mais confiável que ActualTheme de elementos recém-criados.</summary>
    public static ElementTheme Theme { get; set; } = ElementTheme.Dark;

    public static Color Foreground => Theme == ElementTheme.Light ? Color.FromArgb(255, 40, 40, 40) : Color.FromArgb(255, 235, 235, 235);

    /// <summary>Cor do nível (monocromático: sempre a tinta do Dock).</summary>
    public static Color Level(double pct) => Ink;

    public static Color Background => Theme == ElementTheme.Light ? Color.FromArgb(255, 236, 236, 236) : Color.FromArgb(255, 44, 44, 46);

    /// <summary>Mistura opaca: <paramref name="amount"/> = 0 → fundo, 1 → cor.</summary>
    public static Color Mix(Color color, double amount)
    {
        var bg = Background;
        byte M(byte a, byte b) => (byte)Math.Round(b + (a - b) * amount);
        return Color.FromArgb(255, M(color.R, bg.R), M(color.G, bg.G), M(color.B, bg.B));
    }

    public static Color Track(Color color) => Mix(color, 0.2);

    /// <summary>Contorno dos botões de widget: preto com alfa baixo — um pouco mais escuro que o vidro, dá relevo sutil.</summary>
    public static Color Hairline => Color.FromArgb(56, 0, 0, 0);

    public static SolidColorBrush Brush(Color c) => new(c);
}
