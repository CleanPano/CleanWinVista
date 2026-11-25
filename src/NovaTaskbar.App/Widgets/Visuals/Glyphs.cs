using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Ícones (Segoe Fluent Icons) centralizados opticamente. Vários glifos não têm o desenho no meio da caixa
/// (ex.: alto-falante com poucas ondas fica à esquerda). As correções, em frações do tamanho da fonte, foram medidas
/// na própria fonte: média entre o centro da caixa do desenho e o centro de massa (centro "visual").
/// </summary>
internal static class Glyphs
{
    public const string Volume0 = "", VolumeMute = "", Volume1 = "", Volume2 = "", Volume3 = "";
    public const string Stopwatch = "", Bolt = "", Music = "";
    // Widgets de ferramenta (Segoe Fluent Icons).
    public const string Clipboard = "\uE77F", Camera = "\uE722", Eyedropper = "\uEF3C", Emoji = "\uE76E", Convert = "\uE8AB", Calculator = "\uE8EF",
        Timer = "\uE823", Note = "\uE70B", Checklist = "\uE762", Cup = "\uEC32", Walk = "\uE805", Download = "\uE896", Shelf = "\uE7B8",
        Star = "\uE734", Search = "\uE721", AppWindows = "\uE8C8", Photo = "\uE91B", Toggles = "\uE9E9", Mic = "\uE720", Weather = "\uE753",
        Calendar = "\uE787", Services = "\uE9F9", Progress = "\uE9D2", WorldClock = "\uE774", AnalogClock = "\uE121";
    // Energia
    public const string Power = "\uE7E8", Lock = "\uE72E", Sleep = "\uE708", SignOut = "\uF3B1", Restart = "\uE777";
    public const string Keyboard = "", Bluetooth = "", Headphones = "", Mouse = "", Gamepad = "", Phone = "", Pen = "";

    private static readonly Dictionary<string, (double Dx, double Dy)> Offsets = new()
    {
        [""] = (-0.020, 0), [VolumeMute] = (0.050, 0), [Volume1] = (0.184, 0), [Volume2] = (0.088, 0), [Volume3] = (-0.020, 0),
        [Stopwatch] = (0.018, 0.009), [Bolt] = (0.030, 0.025), [Music] = (0.016, 0),
        [Keyboard] = (0, 0.024), [Bluetooth] = (0.018, 0), [Headphones] = (0, -0.027), [Mouse] = (0.031, 0.025),
        [Gamepad] = (0.006, 0.031), [Phone] = (0, -0.008),
    };

    /// <summary>Cria um ícone centralizado.</summary>
    public static FontIcon Create(string glyph, double size, double opacity = 1)
    {
        var icon = new FontIcon { FontSize = size, Opacity = opacity, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Set(icon, glyph);
        return icon;
    }

    /// <summary>Troca o glifo e reaplica a correção de centro.</summary>
    public static void Set(FontIcon icon, string glyph)
    {
        icon.Glyph = glyph;
        var (dx, dy) = Offsets.TryGetValue(glyph, out var o) ? o : (0, 0);
        icon.RenderTransform = dx == 0 && dy == 0 ? null : new TranslateTransform { X = dx * icon.FontSize, Y = dy * icon.FontSize };
    }
}
