using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

/// <summary>Idioma do teclado da janela ativa ("PT", "EN"). Clique/roda: próximo idioma; clique direito: escolher.</summary>
internal sealed class KeyboardWidget : WidgetView
{
    private readonly KeyboardLayouts _keyboards;
    private readonly Action _changed;
    private readonly TextBlock _code;
    private IReadOnlyList<KeyboardLanguage> _all = [];
    private IntPtr _current;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _verify;
    private KeyboardLanguage? _pending;

    public KeyboardWidget(double iconSize, double width, KeyboardLayouts keyboards, Action changed) : base(WidgetKind.Keyboard, iconSize, width)
    {
        _keyboards = keyboards;
        _changed = changed;
        // Discreto: só o código do idioma, sem moldura.
        _code = Text(F(0.27), true, 0.85);
        Root.Children.Add(_code);
    }

    private KeyboardLanguage? Current => _all.FirstOrDefault(k => k.Handle == _current) is { Name: not null } k ? k : null;

    public override string Tooltip =>
        (Current is { } c ? L.F("Keyboard: {0}", c.Name) : L.T("Keyboard")) + "\n" +
        string.Join("  ·  ", _all.Select(k => k.Handle == _current ? $"[{k.Code}]" : k.Code)) +
        "\n" + L.T("Click or scroll: switch · Right-click: choose");

    public override void Update(WidgetData d)
    {
        _all = d.Keyboards;
        _current = d.KeyboardCurrent;
        _code.Text = Current?.Code ?? (_all.Count > 0 ? "…" : "—");
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => Cycle(1);

    public override void OnWheel(int delta) => Cycle(delta > 0 ? 1 : -1);

    public override void AddMenuItems(MenuFlyout menu)
    {
        foreach (var k in _all)
        {
            var item = new RadioMenuFlyoutItem { Text = $"{k.Code} — {k.Name}", GroupName = "kbd", IsChecked = k.Handle == _current };
            var target = k;
            item.Click += (_, _) => Switch(target);
            menu.Items.Add(item);
        }
    }

    private void Cycle(int direction)
    {
        if (KeyboardLanguages.Next(_all, _current, direction) is { } next) Switch(next);
    }

    private void Switch(KeyboardLanguage target)
    {
        _keyboards.Switch(target);
        _current = target.Handle; // otimista; a próxima amostra confirma
        _code.Text = target.Code;
        _changed();

        // Alguns apps ignoram o pedido (o clique "não funcionava"): confere logo depois e, se preciso, usa Win + Espaço.
        _pending = target;
        if (_verify is null)
        {
            _verify = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _verify.Interval = TimeSpan.FromMilliseconds(250);
            _verify.IsRepeating = false;
            _verify.Tick += (_, _) =>
            {
                if (_pending is { } p) _keyboards.EnsureSwitched(p, _all);
                _pending = null;
                _changed();
            };
        }
        _verify.Stop();
        _verify.Start();
    }
}
