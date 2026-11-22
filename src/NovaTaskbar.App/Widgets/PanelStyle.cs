using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.App.Dock;
using NovaTaskbar.Core.Configuration;
using Windows.UI;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Visual único de todas as janelas que saem do Dock (painéis dos widgets, menus, lembretes):
/// mesmo material (Acrylic/Mica com a mesma cor e opacidade), mesmos cantos e mesma borda da pílula.
/// O DockWindow chama <see cref="Update"/> sempre que a aparência muda; cada flyout chama <see cref="Apply"/>.
/// </summary>
internal static class PanelStyle
{
    /// <summary>Raio que o DWM usa nas janelas arredondadas do Windows 11 (é o canto da pílula nos materiais DWM).</summary>
    private const double DwmRadius = 8;

    private static GlassMaterial s_material = GlassMaterial.Acrylic;
    private static Color? s_tint;
    private static double s_tintOpacity = 0.6, s_luminosity = 0.38, s_borderOpacity = 0.22, s_corner = 12;
    private static bool s_dark = true, s_border;

    public static void Update(GlassMaterial effectiveMaterial, AppearanceSettings a, Color? tint, bool dark)
    {
        s_material = effectiveMaterial;
        s_tint = tint;
        s_tintOpacity = a.TintOpacity;
        s_luminosity = a.LuminosityOpacity;
        s_borderOpacity = a.BorderOpacity;
        s_corner = a.CornerRadius;
        s_border = a.BorderVisible;
        s_dark = dark;
    }

    private static bool UsesDwm => s_material != GlassMaterial.Tint;

    private static CornerRadius Corner => new(UsesDwm ? DwmRadius : Math.Clamp(s_corner, 0, 24));

    private static Brush Background
    {
        get
        {
            if (UsesDwm) return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            Color c = s_tint ?? (s_dark ? Color.FromArgb(255, 28, 28, 30) : Color.FromArgb(255, 246, 246, 248));
            // Painéis têm texto: um piso de opacidade mantém a leitura mesmo com a pílula bem transparente.
            byte alpha = (byte)Math.Round(Math.Max(s_tintOpacity, 0.82) * 255);
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }
    }

    private static Brush Border
    {
        get
        {
            // Sem borda configurada, ainda assim um fio quase invisível (como o contorno dos botões) separa o painel do fundo.
            byte a = (byte)Math.Round((s_border ? s_borderOpacity : 0.10) * 255);
            return new SolidColorBrush(s_dark ? Color.FromArgb(a, 255, 255, 255) : Color.FromArgb(a, 0, 0, 0));
        }
    }

    private static SystemBackdrop? NewBackdrop()
    {
        if (!UsesDwm) return null;
        var b = new GlassBackdrop();
        b.Configure(s_material, s_tint, s_tintOpacity, s_luminosity, s_dark);
        return b;
    }

    /// <summary>Estilo do painel. <paramref name="wide"/>: libera a largura (o padrão limita a ~456 px).</summary>
    private static Style PresenterStyle(bool wide)
    {
        var st = new Style(typeof(FlyoutPresenter));
        st.Setters.Add(new Setter(Control.BackgroundProperty, Background));
        st.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        st.Setters.Add(new Setter(Control.CornerRadiusProperty, Corner));
        if (wide)
        {
            st.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 900d));
            st.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 900d));
            st.Setters.Add(new Setter(ScrollViewer.HorizontalScrollModeProperty, ScrollMode.Disabled));
            st.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        }
        return st;
    }

    private static Style MenuStyle()
    {
        var st = new Style(typeof(MenuFlyoutPresenter));
        st.Setters.Add(new Setter(Control.BackgroundProperty, Background));
        st.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        st.Setters.Add(new Setter(Control.CornerRadiusProperty, Corner));
        return st;
    }

    /// <summary>Aplica o visual do Dock a um painel ou menu. Chamar antes de ShowAt.</summary>
    public static void Apply(FlyoutBase flyout, bool wide = false)
    {
        try
        {
            switch (flyout)
            {
                case Flyout f:
                    f.FlyoutPresenterStyle = PresenterStyle(wide);
                    break;
                case MenuFlyout m:
                    m.MenuFlyoutPresenterStyle = MenuStyle();
                    break;
            }
            flyout.SystemBackdrop = NewBackdrop();
        }
        catch (Exception)
        {
            // Visual é cosmético: se o sistema recusar o material, o painel abre com o fundo padrão.
        }
    }
}
