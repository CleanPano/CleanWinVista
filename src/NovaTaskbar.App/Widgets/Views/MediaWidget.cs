using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using Windows.UI;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

internal sealed class MediaWidget : WidgetView
{
    private readonly MediaWatcher _media;
    private readonly Border _art;
    private readonly Image _artImage;
    private readonly FontIcon _artGlyph;
    private readonly TextBlock _title, _artist;
    private readonly StackPanel _eq;
    private readonly List<Rectangle> _bars = new();
    private readonly bool _reduceMotion;
    private MediaState _state = MediaState.None;
    private bool _eqRunning;

    public MediaWidget(double iconSize, double width, MediaWatcher media, bool reduceMotion) : base(WidgetKind.Media, iconSize, width)
    {
        _media = media;
        _reduceMotion = reduceMotion;
        double a = iconSize * 0.84;
        _artImage = new Image { Stretch = Stretch.UniformToFill };
        _artGlyph = Glyphs.Create(Glyphs.Music, a * 0.45, 0.7);
        var artGrid = new Grid();
        artGrid.Children.Add(_artGlyph);
        artGrid.Children.Add(_artImage);
        _art = new Border { Width = a, Height = a, CornerRadius = new CornerRadius(a * 0.2), Child = artGrid, Background = new SolidColorBrush(Palette.Mix(Palette.Foreground, 0.15)) };

        _title = Text(F(0.29), true);
        _artist = Text(F(0.25), false, 0.65);
        _title.HorizontalAlignment = _artist.HorizontalAlignment = HorizontalAlignment.Left;
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        texts.Children.Add(_title);
        texts.Children.Add(_artist);

        _eq = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Height = iconSize * 0.4 };
        for (int i = 0; i < 3; i++)
        {
            var r = new Rectangle { Width = 3, Height = iconSize * 0.4, RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Bottom, Fill = new SolidColorBrush(Palette.Ink) };
            _bars.Add(r);
            _eq.Children.Add(r);
        }

        var grid = new Grid { ColumnSpacing = iconSize * 0.22, Padding = new Thickness(iconSize * 0.05, 0, iconSize * 0.12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(_eq, 2);
        grid.Children.Add(_art);
        grid.Children.Add(texts);
        grid.Children.Add(_eq);
        Root.Children.Add(grid);
        Update(new WidgetData());
    }

    public override string Tooltip => _state.HasSession
        ? $"{_state.Title}{(string.IsNullOrEmpty(_state.Artist) ? "" : " — " + _state.Artist)}\n" + L.T(_state.Playing ? "Click: pause" : "Click: play") + " · " + L.T("Right-click: previous/next track")
        : L.T("Nothing playing");

    public override void Update(WidgetData d)
    {
        _state = d.Media;
        if (!_state.HasSession)
        {
            _title.Text = L.T("Nothing playing");
            _artist.Text = "";
            _artImage.Source = null;
        }
        else
        {
            _title.Text = string.IsNullOrWhiteSpace(_state.Title) ? L.T("Untitled") : _state.Title;
            _artist.Text = _state.Artist;
            _artImage.Source = d.MediaArt;
        }
        SetEqualizer(_state.Playing);
    }

    /// <summary>Equalizador: animação contínua no compositor (não acorda o UI thread a cada quadro).</summary>
    private void SetEqualizer(bool playing)
    {
        _eq.Visibility = _state.HasSession ? Visibility.Visible : Visibility.Collapsed;
        if (playing == _eqRunning) return;
        _eqRunning = playing;
        double[] periods = [0.62, 0.48, 0.71];
        for (int i = 0; i < _bars.Count; i++)
        {
            var v = ElementCompositionPreview.GetElementVisual(_bars[i]);
            v.CenterPoint = new Vector3(1.5f, (float)_bars[i].Height, 0);
            if (!playing || _reduceMotion)
            {
                v.StopAnimation("Scale.Y");
                v.Scale = new Vector3(1, playing ? 0.6f : 0.25f, 1);
                continue;
            }
            var comp = v.Compositor;
            var anim = comp.CreateScalarKeyFrameAnimation();
            var ease = comp.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
            anim.InsertKeyFrame(0f, 0.25f);
            anim.InsertKeyFrame(0.5f, 1f, ease);
            anim.InsertKeyFrame(1f, 0.25f, ease);
            anim.Duration = TimeSpan.FromSeconds(periods[i]);
            anim.IterationBehavior = AnimationIterationBehavior.Forever;
            anim.DelayTime = TimeSpan.FromSeconds(0.12 * i);
            v.StartAnimation("Scale.Y", anim);
        }
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        if (_state.HasSession) _ = _media.TogglePlayPauseAsync();
    }

    public override void AddMenuItems(MenuFlyout menu)
    {
        if (!_state.HasSession) return;
        if (_state.CanPrevious) menu.Items.Add(Item(L.T("Previous track"), "\uE892", () => _ = _media.PreviousAsync()));
        menu.Items.Add(Item(_state.Playing ? L.T("Pause") : L.T("Play"), _state.Playing ? "\uE769" : "\uE768", () => _ = _media.TogglePlayPauseAsync()));
        if (_state.CanNext) menu.Items.Add(Item(L.T("Next track"), "\uE893", () => _ = _media.NextAsync()));
    }
}
