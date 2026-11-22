using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace NovaTaskbar.App.Widgets;

/// <summary>Texto que "rola" de baixo para cima quando muda (minutos do relógio).</summary>
internal sealed class RollingText
{
    private readonly Grid _clip = new();
    private TextBlock _current;
    private readonly Func<TextBlock> _factory;
    private readonly bool _reduceMotion;

    public RollingText(Func<TextBlock> factory, bool reduceMotion)
    {
        _factory = factory;
        _reduceMotion = reduceMotion;
        _current = factory();
        _clip.Children.Add(_current);
        _clip.Loaded += (_, _) => UpdateClip();
        _clip.SizeChanged += (_, _) => UpdateClip();
    }

    public FrameworkElement Element => _clip;
    public string Text => _current.Text;

    private void UpdateClip() =>
        _clip.Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, _clip.ActualWidth, _clip.ActualHeight) };

    public void Set(string text)
    {
        if (_current.Text == text) return;
        if (_reduceMotion || string.IsNullOrEmpty(_current.Text)) { _current.Text = text; return; }

        var old = _current;
        var next = _factory();
        next.Text = text;
        _clip.Children.Add(next);
        _current = next;
        double h = Math.Max(10, old.ActualHeight);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();
        Animate(sb, old, 0, -h * 0.8, 1, 0, ease);
        Animate(sb, next, h * 0.8, 0, 0, 1, ease);
        sb.Completed += (_, _) => _clip.Children.Remove(old);
        sb.Begin();
    }

    private static void Animate(Storyboard sb, UIElement el, double fromY, double toY, double fromO, double toO, EasingFunctionBase ease)
    {
        var tt = new TranslateTransform();
        el.RenderTransform = tt;
        var y = new DoubleAnimation { From = fromY, To = toY, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = ease };
        Storyboard.SetTarget(y, tt);
        Storyboard.SetTargetProperty(y, "Y");
        var o = new DoubleAnimation { From = fromO, To = toO, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = ease };
        Storyboard.SetTarget(o, el);
        Storyboard.SetTargetProperty(o, "Opacity");
        sb.Children.Add(y);
        sb.Children.Add(o);
    }
}
