using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using Windows.UI;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

internal sealed class VolumeWidget : WidgetView
{
    private readonly AudioVolume _audio;
    private readonly Ring _ring;
    private readonly FontIcon _icon;
    private AudioVolume.State _state;

    public VolumeWidget(double iconSize, double width, AudioVolume audio) : base(WidgetKind.Volume, iconSize, width)
    {
        _audio = audio;
        _ring = new Ring(iconSize * 0.82, Math.Max(2.5, iconSize * 0.09));
        _icon = Glyphs.Create(Glyphs.Volume3, iconSize * 0.34);
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(_ring.Element);
        g.Children.Add(_icon);
        Root.Children.Add(g);
    }

    public override string Tooltip => _state.Available
        ? $"Volume {Math.Round(_state.Level * 100)}%{(_state.Muted ? " (" + L.T("muted") + ")" : "")}\n{_state.DeviceName}\n" + L.T("Click: adjust · Scroll: ±2%")
        : L.T("No audio device");

    public override void Update(WidgetData d) => Apply(d.Volume);

    private void Apply(AudioVolume.State s)
    {
        _state = s;
        if (!s.Available) { _ring.Set(null, Colors.Gray); Glyphs.Set(_icon, Glyphs.VolumeMute); return; }
        _ring.Set(s.Muted ? 0 : s.Level, Palette.Ink);
        Glyphs.Set(_icon, s.Muted || s.Level <= 0.001 ? Glyphs.VolumeMute : s.Level < 0.34 ? Glyphs.Volume1 : s.Level < 0.67 ? Glyphs.Volume2 : Glyphs.Volume3);
    }

    public override bool Step(double dt) => _ring.Step(dt);

    public override void OnWheel(int delta)
    {
        if (!_state.Available) return;
        float level = (float)Math.Clamp(_state.Level + Math.Sign(delta) * 0.02, 0, 1);
        if (_audio.SetLevel(level)) Apply(_audio.Read());
    }

    /// <summary>Painel: dispositivos de saída (troca o padrão do Windows), volume e mudo.</summary>
    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(4), MinWidth = 280 };
        var title = Text(12, true, 0.7);
        title.Text = L.T("Audio output");
        title.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(title);

        var devices = _audio.Devices();
        if (devices.Count == 0)
        {
            var none = Text(13, false, 0.7);
            none.Text = L.T("No active output device");
            panel.Children.Add(none);
        }
        foreach (var d in devices)
        {
            var option = new RadioButton { Content = d.Name, GroupName = "audio-out", IsChecked = d.IsDefault, MinWidth = 0 };
            var id = d.Id;
            option.Checked += (_, _) => { if (_audio.SetDefault(id)) Apply(_audio.Read()); };
            panel.Children.Add(option);
        }

        if (_state.Available)
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(_state.Level * 100), Width = 220, StepFrequency = 1 };
            var mute = new ToggleButton { Content = Glyphs.Create(Glyphs.VolumeMute, 16), IsChecked = _state.Muted, Padding = new Thickness(8) };
            ToolTipService.SetToolTip(mute, L.T("Mute"));
            slider.ValueChanged += (_, e) => { _audio.SetLevel((float)(e.NewValue / 100)); Apply(_audio.Read()); };
            mute.Click += (_, _) => { _audio.SetMuted(mute.IsChecked == true); Apply(_audio.Read()); };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(mute);
            row.Children.Add(slider);
            panel.Children.Add(row);
        }
        WidgetActions.ShowFlyout(panel, anchor, at);
    }

    public override void AddMenuItems(MenuFlyout menu)
    {
        menu.Items.Add(Item(_state.Muted ? L.T("Unmute") : L.T("Mute"), "\uE74F", () => { _audio.SetMuted(!_state.Muted); Apply(_audio.Read()); }));
        menu.Items.Add(Item(L.T("Sound settings"), "\uE767", () => WidgetActions.OpenSettings("ms-settings:sound")));
    }
}
