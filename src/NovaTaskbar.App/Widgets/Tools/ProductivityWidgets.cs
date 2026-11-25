using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NovaTaskbar.Core.Tools;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

// ============================================================================================================
// Lote 2 — produtividade: notas, tarefas, água, postura, progresso, relógio analógico, relógio mundial.
// ============================================================================================================

/// <summary>Lista de tarefas; o selo mostra quantas faltam.</summary>
internal sealed class TodosWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.Todos, iconSize, width, Glyphs.Checklist)
{
    private int Pending => ctx.Store.Data.Todos.Count(t => !t.Done);

    public override string Tooltip => Pending == 0 ? L.T("To-dos: all done") : L.F("To-dos: {0} pending", Pending);

    public override void Update(WidgetData d) => SetBadge(Pending > 0 ? Pending.ToString(CultureInfo.InvariantCulture) : null);

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("To-dos"), 320);
        var list = new StackPanel { Spacing = 2 };
        void Render()
        {
            list.Children.Clear();
            foreach (var t in ctx.Store.Data.Todos.ToList())
            {
                var item = t;
                var check = new CheckBox { Content = item.Text, IsChecked = item.Done, MinWidth = 0 };
                check.Click += (_, _) => { ctx.Store.Update(_ => item.Done = check.IsChecked == true); ctx.Refresh(); };
                var del = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(6), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
                ToolTipService.SetToolTip(del, L.T("Remove"));
                del.Click += (_, _) => { ctx.Store.Update(x => x.Todos.Remove(item)); Render(); ctx.Refresh(); };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(check);
                Grid.SetColumn(del, 1);
                g.Children.Add(del);
                list.Children.Add(g);
            }
        }
        var input = new TextBox { PlaceholderText = L.T("New to-do, then Enter") };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            string text = input.Text.Trim();
            ctx.Store.Update(x => x.Todos.Add(new Services.TodoItem { Text = text.Length > 300 ? text[..300] : text }));
            input.Text = "";
            Render();
            ctx.Refresh();
        };
        Render();
        p.Children.Add(input);
        p.Children.Add(Scroll(list, 280));
        p.Children.Add(Btn(L.T("Clear completed"), () => { ctx.Store.Update(x => x.Todos.RemoveAll(t => t.Done)); Render(); ctx.Refresh(); }, ""));
        return p;
    }
}

/// <summary>Lembrete de beber água: anel = copos do dia / meta; destaca e toca um som quando passa o intervalo.</summary>
internal sealed class WaterWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.Water, iconSize, width, Glyphs.Cup, withRing: true)
{
    private bool _chimed;

    private Services.WaterState W
    {
        get
        {
            var w = ctx.Store.Data.Water;
            if (w.Day.Date != DateTime.Today) ctx.Store.Update(x => { x.Water.Day = DateTime.Today; x.Water.Glasses = 0; });
            return w;
        }
    }

    public override string Tooltip
    {
        get
        {
            var w = W;
            var left = Reminder.Remaining(w.LastAck, w.IntervalMinutes, DateTime.Now);
            return L.F("Water: {0} of {1} glasses today", w.Glasses, w.Goal) + "\n" + (left == TimeSpan.Zero ? L.T("Time to drink water! Click: +1 glass") : L.F("Next reminder in {0:0} min · Click: +1 glass", left.TotalMinutes));
        }
    }

    public override void Update(WidgetData d)
    {
        var w = W;
        bool due = Reminder.IsDue(w.LastAck, w.IntervalMinutes, DateTime.Now);
        if (due && !_chimed) { _chimed = true; ScreenTools.Chime(); }
        if (!due) _chimed = false;
        SetAccent(due ? Palette.Blue : null);
        SetRing(w.Goal > 0 ? (double)w.Glasses / w.Goal : 0, Palette.Blue);
        SetBadge(due ? "!" : null);
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        ctx.Store.Update(x => { x.Water.Glasses++; x.Water.LastAck = DateTime.Now; });
        ctx.Refresh();
    }

    public override void AddMenuItems(MenuFlyout menu)
    {
        menu.Items.Add(Item(L.T("Undo last glass"), "", () => { ctx.Store.Update(x => x.Water.Glasses = Math.Max(0, x.Water.Glasses - 1)); ctx.Refresh(); }));
        var goal = new MenuFlyoutSubItem { Text = L.T("Daily goal") };
        foreach (int g in new[] { 6, 8, 10, 12 }) { int v = g; goal.Items.Add(new RadioMenuFlyoutItem { Text = L.F("{0} glasses", v), GroupName = "wgoal", IsChecked = W.Goal == v }.With(i => i.Click += (_, _) => { ctx.Store.Update(x => x.Water.Goal = v); ctx.Refresh(); })); }
        menu.Items.Add(goal);
        var every = new MenuFlyoutSubItem { Text = L.T("Remind every") };
        foreach (int m in new[] { 30, 45, 60, 90, 120 }) { int v = m; every.Items.Add(new RadioMenuFlyoutItem { Text = $"{v} min", GroupName = "wint", IsChecked = W.IntervalMinutes == v }.With(i => i.Click += (_, _) => { ctx.Store.Update(x => x.Water.IntervalMinutes = v); ctx.Refresh(); })); }
        menu.Items.Add(every);
    }

    protected override FrameworkElement BuildPanel() => new Grid();
}

/// <summary>Lembrete de postura/pausa: destaca no intervalo escolhido; clique confirma.</summary>
internal sealed class PostureWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.Posture, iconSize, width, Glyphs.Walk, withRing: true)
{
    private bool _chimed;

    public override string Tooltip
    {
        get
        {
            var left = Reminder.Remaining(ctx.Store.Data.PostureAck, ctx.Store.Data.PostureMinutes, DateTime.Now);
            return left == TimeSpan.Zero ? L.T("Time to fix your posture and stretch! Click to confirm") : L.F("Posture: next reminder in {0:0} min", left.TotalMinutes);
        }
    }

    public override void Update(WidgetData d)
    {
        var data = ctx.Store.Data;
        bool due = Reminder.IsDue(data.PostureAck, data.PostureMinutes, DateTime.Now);
        if (due && !_chimed) { _chimed = true; ScreenTools.Chime(); }
        if (!due) _chimed = false;
        SetAccent(due ? Palette.Orange : null);
        var left = Reminder.Remaining(data.PostureAck, data.PostureMinutes, DateTime.Now);
        SetRing(data.PostureMinutes > 0 ? 1 - left.TotalMinutes / data.PostureMinutes : 0, Palette.Orange);
        SetBadge(due ? "!" : null);
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        ctx.Store.Update(x => x.PostureAck = DateTime.Now);
        ctx.Refresh();
    }

    public override void AddMenuItems(MenuFlyout menu)
    {
        var every = new MenuFlyoutSubItem { Text = L.T("Remind every") };
        foreach (int m in new[] { 20, 30, 45, 60, 90 }) { int v = m; every.Items.Add(new RadioMenuFlyoutItem { Text = $"{v} min", GroupName = "pint", IsChecked = ctx.Store.Data.PostureMinutes == v }.With(i => i.Click += (_, _) => { ctx.Store.Update(x => x.PostureMinutes = v); ctx.Refresh(); })); }
        menu.Items.Add(every);
    }

    protected override FrameworkElement BuildPanel() => new Grid();
}

/// <summary>Progresso do dia/semana/mês/ano (anel + %). Clique direito escolhe o período.</summary>
internal sealed class ProgressWidget : WidgetView
{
    private readonly WidgetContext _ctx;
    private readonly Ring _ring;
    private readonly TextBlock _pct;
    private ProgressSpan _span;

    public ProgressWidget(double iconSize, double width, WidgetContext ctx) : base(WidgetKind.Progress, iconSize, width)
    {
        _ctx = ctx;
        _span = Enum.TryParse<ProgressSpan>(ctx.Store.Data.ProgressSpan, out var s) ? s : ProgressSpan.Day;
        _ring = new Ring(iconSize * 0.82, Math.Max(2.5, iconSize * 0.09));
        _pct = Text(F(0.22), true);
        _pct.FontFeatures();
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(_ring.Element);
        g.Children.Add(_pct);
        Root.Children.Add(g);
    }

    public override string Tooltip => L.F("{0}: {1:0.0}% done", TimeProgress.Label(_span), TimeProgress.Fraction(_span, DateTime.Now) * 100) + "\n" + L.T("Right-click: change period");

    public override void Update(WidgetData d)
    {
        double f = TimeProgress.Fraction(_span, DateTime.Now);
        _ring.Set(f, Palette.Purple);
        _pct.Text = $"{Math.Floor(f * 100):0}";
    }

    public override bool Step(double dt) => _ring.Step(dt);

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => Cycle();

    public override void AddMenuItems(MenuFlyout menu)
    {
        foreach (var s in Enum.GetValues<ProgressSpan>())
        {
            var span = s;
            menu.Items.Add(new RadioMenuFlyoutItem { Text = TimeProgress.Label(span), GroupName = "prog", IsChecked = _span == span }.With(i => i.Click += (_, _) => Set(span)));
        }
    }

    private void Cycle() => Set((ProgressSpan)(((int)_span + 1) % 4));

    private void Set(ProgressSpan s)
    {
        _span = s;
        _ctx.Store.Update(x => x.ProgressSpan = s.ToString());
        _ctx.Refresh();
    }
}

/// <summary>Relógio analógico (ponteiros vetoriais, segundos suaves).</summary>
internal sealed class AnalogClockWidget : WidgetView
{
    private readonly Line _hour, _minute, _second;
    private readonly double _c, _r;

    public AnalogClockWidget(double iconSize, double width) : base(WidgetKind.AnalogClock, iconSize, width)
    {
        double d = iconSize * 0.86;
        _c = d / 2; _r = d / 2 - 1;
        var canvas = new Canvas { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var face = new Ellipse { Width = d, Height = d, StrokeThickness = 1, Stroke = Palette.Brush(Palette.Hairline) };
        canvas.Children.Add(face);
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI / 6, inner = i % 3 == 0 ? _r * 0.72 : _r * 0.82;
            canvas.Children.Add(new Line
            {
                X1 = _c + inner * Math.Sin(a), Y1 = _c - inner * Math.Cos(a), X2 = _c + _r * 0.92 * Math.Sin(a), Y2 = _c - _r * 0.92 * Math.Cos(a),
                Stroke = Palette.Brush(Palette.Mix(Palette.Foreground, 0.6)), StrokeThickness = i % 3 == 0 ? 1.6 : 1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        }
        _hour = Hand(2.6, Palette.Foreground);
        _minute = Hand(1.8, Palette.Foreground);
        _second = Hand(1, Palette.Orange);
        canvas.Children.Add(_hour); canvas.Children.Add(_minute); canvas.Children.Add(_second);
        double hub = Math.Max(3, d * 0.08);
        var dot = new Ellipse { Width = hub, Height = hub, Fill = Palette.Brush(Palette.Orange) };
        Canvas.SetLeft(dot, _c - hub / 2); Canvas.SetTop(dot, _c - hub / 2);
        canvas.Children.Add(dot);
        Root.Children.Add(canvas);
    }

    private Line Hand(double thickness, Color color) => new()
    {
        X1 = _c, Y1 = _c, X2 = _c, Y2 = _c, StrokeThickness = thickness, Stroke = Palette.Brush(color), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
    };

    public override string Tooltip => DateTime.Now.ToString("D", L.Culture) + " · " + DateTime.Now.ToString("t", L.Culture);
    public override bool Clickable => false; // decorativo: só mostra a hora

    public override void Update(WidgetData d)
    {
        var n = DateTime.Now;
        Point(_hour, (n.Hour % 12 + n.Minute / 60.0) / 12, 0.5);
        Point(_minute, (n.Minute + n.Second / 60.0) / 60, 0.74);
        Point(_second, n.Second / 60.0, 0.82);
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) => WidgetActions.OpenWindowsUri("ms-clock:");

    private void Point(Line l, double turn, double len)
    {
        double a = turn * 2 * Math.PI;
        l.X2 = _c + _r * len * Math.Sin(a);
        l.Y2 = _c - _r * len * Math.Cos(a);
    }
}

/// <summary>Relógio mundial: até 2 cidades no Dock; o painel lista e permite trocar os fusos.</summary>
internal sealed class WorldClockWidget : WidgetView
{
    private readonly WidgetContext _ctx;
    private readonly StackPanel _row;

    public WorldClockWidget(double iconSize, double width, WidgetContext ctx) : base(WidgetKind.WorldClock, iconSize, width)
    {
        _ctx = ctx;
        _row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = iconSize * 0.3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Root.Children.Add(_row);
    }

    private IEnumerable<TimeZoneInfo> Zones => _ctx.Store.Data.WorldClocks
        .Select(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : null).OfType<TimeZoneInfo>();

    public override string Tooltip => string.Join("\n", Zones.Select(z => $"{City(z)}: {TimeZoneInfo.ConvertTime(DateTime.Now, z):HH:mm}")) + "\n" + L.T("Click: choose time zones");

    public override void Update(WidgetData d)
    {
        var zones = Zones.Take(2).ToList();
        while (_row.Children.Count < zones.Count)
        {
            var sp = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Text(F(0.3), true));
            sp.Children.Add(Text(F(0.2), false, 0.65));
            ((TextBlock)sp.Children[0]).FontFeatures();
            _row.Children.Add(sp);
        }
        while (_row.Children.Count > zones.Count) _row.Children.RemoveAt(_row.Children.Count - 1);
        for (int i = 0; i < zones.Count; i++)
        {
            var sp = (StackPanel)_row.Children[i];
            ((TextBlock)sp.Children[0]).Text = TimeZoneInfo.ConvertTime(DateTime.Now, zones[i]).ToString("HH:mm");
            ((TextBlock)sp.Children[1]).Text = City(zones[i]);
        }
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        var p = new StackPanel { Spacing = 8, Padding = new Thickness(4), Width = 320 };
        p.Children.Add(new TextBlock { Text = L.T("World clock"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var all = TimeZoneInfo.GetSystemTimeZones();
        for (int slot = 0; slot < 2; slot++)
        {
            int s = slot;
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = all.Select(z => z.DisplayName).ToList() };
            string current = _ctx.Store.Data.WorldClocks.ElementAtOrDefault(s) ?? "";
            combo.SelectedIndex = all.ToList().FindIndex(z => z.Id == current);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                string id = all[combo.SelectedIndex].Id;
                _ctx.Store.Update(x =>
                {
                    while (x.WorldClocks.Count <= s) x.WorldClocks.Add("UTC");
                    x.WorldClocks[s] = id;
                });
                _ctx.Refresh();
            };
            p.Children.Add(combo);
        }
        WidgetActions.ShowFlyout(p, anchor, at);
    }

    /// <summary>Nome curto: a parte do nome de exibição depois do ")" — ex. "(UTC+09:00) Osaka, Sapporo, Tokyo" → "Tokyo".</summary>
    private static string City(TimeZoneInfo z)
    {
        string name = z.DisplayName;
        int paren = name.IndexOf(')');
        string rest = paren >= 0 ? name[(paren + 1)..].Trim() : name;
        var parts = rest.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return z.Id == "UTC" ? "UTC" : parts.Length > 0 ? parts[^1] : z.Id;
    }
}

internal static class FluentExtensions
{
    /// <summary>Configura um objeto em linha: <c>new X().With(x =&gt; ...)</c>.</summary>
    public static T With<T>(this T obj, Action<T> configure)
    {
        configure(obj);
        return obj;
    }
}
