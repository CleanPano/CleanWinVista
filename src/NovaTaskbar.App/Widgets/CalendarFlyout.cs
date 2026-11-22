using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Core.Tools;
using NovaTaskbar.Windows.SystemInfo;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Calendário do relógio — funções inspiradas no FullCalendar (código próprio, visual do Dock):
/// visões Mês / Semana / Dia / Lista, navegação ‹ › Hoje, nº da semana, linha "agora", compromissos com dia inteiro,
/// repetição, lembrete, local e notas; criar clicando no dia/horário; editar/excluir; importar e exportar .ics;
/// agenda iCal assinada (widget Agenda) aparece somente leitura.
/// </summary>
internal sealed class CalendarFlyout
{
    private enum View { Month, Week, Day, List }

    private const double HourHeight = 40, TimeColumn = 46, PanelWidth = 600, BodyHeight = 430;
    private static CultureInfo Pt => L.Culture;

    private readonly WidgetContext _ctx;
    private readonly DayOfWeek _firstDay = Pt.DateTimeFormat.FirstDayOfWeek;
    private DateOnly _cursor;
    private View _view;
    private readonly StackPanel _root = new() { Width = PanelWidth, Padding = new Thickness(6), Spacing = 8 };
    private readonly Grid _body = new() { Height = BodyHeight };
    private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Dictionary<View, ToggleButton> _viewButtons = new();

    private CalendarFlyout(WidgetContext ctx, DateOnly today)
    {
        _ctx = ctx;
        _cursor = today;
        _view = ctx.Store.Data.CalendarView switch { "week" => View.Week, "day" => View.Day, "list" => View.List, _ => View.Month };
        _root.Children.Add(Toolbar());
        _root.Children.Add(_body);
        Render();
    }

    public static void Show(WidgetContext ctx, FrameworkElement anchor, global::Windows.Foundation.Point at, DateOnly today) =>
        WidgetActions.ShowFlyout(new CalendarFlyout(ctx, today)._root, anchor, at);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================= barra

    private FrameworkElement Toolbar()
    {
        var g = new Grid { ColumnSpacing = 6 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        nav.Children.Add(Icon("", L.T("Previous"), () => Move(-1)));
        nav.Children.Add(Icon("", L.T("Next"), () => Move(1)));
        nav.Children.Add(Text(L.T("Today"), L.T("Go to today"), () => { _cursor = Today; Render(); }));
        g.Children.Add(nav);

        _title.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_title, 1);
        g.Children.Add(_title);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (v, label) in new[] { (View.Month, L.T("Month")), (View.Week, L.T("Week")), (View.Day, L.T("Day")), (View.List, L.T("List")) })
        {
            var b = new ToggleButton { Content = label, Padding = new Thickness(10, 4, 10, 5), FontSize = 12 };
            var view = v;
            b.Click += (_, _) => SetView(view);
            _viewButtons[v] = b;
            right.Children.Add(b);
        }
        right.Children.Add(Icon("", L.T("New event"), () => Edit(null, NewStart(_cursor, null))));
        var more = Icon("", L.T("More options"), () => { });
        var menu = new MenuFlyout();
        var weeks = new ToggleMenuFlyoutItem { Text = L.T("Show week numbers"), IsChecked = _ctx.Store.Data.CalendarWeekNumbers };
        weeks.Click += (_, _) => { _ctx.Store.Update(x => x.CalendarWeekNumbers = weeks.IsChecked); Render(); };
        menu.Items.Add(weeks);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(L.T("Import .ics file…"), "", () => _ = ImportAsync()));
        menu.Items.Add(MenuItem(L.T("Export events (.ics)…"), "", () => _ = ExportAsync()));
        menu.Items.Add(MenuItem(L.T("Open Windows Calendar"), "", () => WidgetActions.OpenWindowsUri("outlookcal:")));
        PanelStyle.Apply(menu);
        more.Flyout = menu;
        right.Children.Add(more);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }

    private void SetView(View v)
    {
        _view = v;
        _ctx.Store.Update(x => x.CalendarView = v.ToString().ToLowerInvariant());
        Render();
    }

    private void Move(int dir)
    {
        _cursor = _view switch
        {
            View.Month => _cursor.AddMonths(dir),
            View.Week => _cursor.AddDays(7 * dir),
            View.Day => _cursor.AddDays(dir),
            _ => _cursor.AddDays(30 * dir),
        };
        Render();
    }

    // ================================================================= dados

    private IEnumerable<CalEvent> AllEvents() =>
        _ctx.Store.Data.Events.Concat(CalendarMath.FromIcs(CalendarWidget.Subscribed));

    private List<Occurrence> Range(DateOnly from, DateOnly toExclusive) =>
        CalendarMath.Expand(AllEvents(), from.ToDateTime(TimeOnly.MinValue), toExclusive.ToDateTime(TimeOnly.MinValue));

    private static DateTime NewStart(DateOnly day, int? hour)
    {
        if (hour is { } h) return day.ToDateTime(new TimeOnly(Math.Clamp(h, 0, 23), 0));
        var now = DateTime.Now;
        int next = Math.Min(23, now.Hour + 1);
        return day.ToDateTime(new TimeOnly(day == Today ? next : 9, 0));
    }

    // ================================================================= render

    private void Render()
    {
        foreach (var (v, b) in _viewButtons) b.IsChecked = v == _view;
        _body.Children.Clear();
        _title.Text = _view switch
        {
            View.Month => Cap(_cursor.ToString(Pt.DateTimeFormat.YearMonthPattern, Pt)),
            View.Week => WeekTitle(),
            View.Day => Cap(_cursor.ToString("D", Pt)),
            _ => L.T("Upcoming events"),
        };
        _body.Children.Add(_view switch
        {
            View.Month => MonthView(),
            View.Week => TimeGrid(CalendarMath.StartOfWeek(_cursor, _firstDay), 7),
            View.Day => TimeGrid(_cursor, 1),
            _ => ListView(),
        });
    }

    private string WeekTitle()
    {
        var s = CalendarMath.StartOfWeek(_cursor, _firstDay);
        var e = s.AddDays(6);
        string t = (s.Month == e.Month ? $"{s.Day} – {e.Day} {e.ToString("MMM yyyy", Pt)}" : $"{s.ToString("d MMM", Pt)} – {e.ToString("d MMM yyyy", Pt)}").Replace(".", "");
        return _ctx.Store.Data.CalendarWeekNumbers ? t + " · " + L.F("wk {0}", CalendarMath.IsoWeek(s.AddDays(3))) : t;
    }

    // ---------------------------------------------------------------- mês

    private FrameworkElement MonthView()
    {
        bool weekNo = _ctx.Store.Data.CalendarWeekNumbers;
        var days = CalendarMath.MonthGrid(_cursor.Year, _cursor.Month, _firstDay);
        var occ = Range(days[0], days[^1].AddDays(1));
        var g = new Grid();
        if (weekNo) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        for (int c = 0; c < 7; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        for (int r = 0; r < 6; r++) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        int off = weekNo ? 1 : 0;

        for (int c = 0; c < 7; c++)
        {
            var name = new TextBlock
            {
                Text = Cap(Pt.DateTimeFormat.GetAbbreviatedDayName(days[c].DayOfWeek)).TrimEnd('.'),
                FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetColumn(name, c + off);
            g.Children.Add(name);
        }
        for (int r = 0; r < 6; r++)
        {
            if (weekNo)
            {
                var wn = new TextBlock { Text = CalendarMath.IsoWeek(days[r * 7 + 3]).ToString(Pt), FontSize = 10, Opacity = 0.45, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                Grid.SetRow(wn, r + 1);
                g.Children.Add(wn);
            }
            for (int c = 0; c < 7; c++)
            {
                var day = days[r * 7 + c];
                var cell = DayCell(day, occ.Where(o => o.Touches(day)).ToList(), day.Month == _cursor.Month);
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c + off);
                g.Children.Add(cell);
            }
        }
        return g;
    }

    private FrameworkElement DayCell(DateOnly day, List<Occurrence> events, bool inMonth)
    {
        var cell = new Border
        {
            BorderBrush = Res("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(0.5), Padding = new Thickness(3),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        var stack = new StackPanel { Spacing = 2 };
        var num = new TextBlock { Text = day.Day.ToString(Pt), FontSize = 12, Opacity = inMonth ? 0.9 : 0.35, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (day == Today)
        {
            num.Foreground = Res("SolidBackgroundFillColorBaseBrush");
            num.FontWeight = FontWeights.SemiBold;
            num.Opacity = 1;
            stack.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Res("TextFillColorPrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Left, Child = num });
        }
        else
        {
            num.HorizontalAlignment = HorizontalAlignment.Left;
            num.Margin = new Thickness(4, 2, 0, 2);
            stack.Children.Add(num);
        }
        const int max = 2;
        foreach (var o in events.Take(max)) stack.Children.Add(Chip(o, compact: true));
        if (events.Count > max)
            stack.Children.Add(new TextBlock { Text = L.F("+{0} more", events.Count - max), FontSize = 10, Opacity = 0.65, Margin = new Thickness(4, 0, 0, 0) });
        cell.Child = stack;
        ToolTipService.SetToolTip(cell, L.T("Click the number: view the day · Double-click: new event"));
        var numHost = (FrameworkElement)stack.Children[0];
        numHost.Tapped += (_, e) => { e.Handled = true; _cursor = day; SetView(View.Day); };
        cell.DoubleTapped += (_, e) => { if (e.OriginalSource is FrameworkElement { Tag: "chip" }) return; Edit(null, NewStart(day, null)); };
        return cell;
    }

    private Border Chip(Occurrence o, bool compact)
    {
        bool allDay = o.AllDay;
        string label = allDay ? o.Source.Title : $"{o.Start:HH:mm} {o.Source.Title}";
        var text = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(o.Source.Title) ? L.T("(untitled)") : label, FontSize = compact ? 10.5 : 12,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Tag = "chip",
            Foreground = allDay ? Res("SolidBackgroundFillColorBaseBrush") : Res("TextFillColorPrimaryBrush"),
        };
        var chip = new Border
        {
            Background = allDay ? Res("TextFillColorSecondaryBrush") : Res("SubtleFillColorSecondaryBrush"),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), Child = text, Tag = "chip",
        };
        ToolTipService.SetToolTip(chip, Describe(o));
        chip.Tapped += (_, e) => { e.Handled = true; Edit(o.Source, o.Start); };
        return chip;
    }

    // ---------------------------------------------------------------- semana / dia

    private FrameworkElement TimeGrid(DateOnly first, int count)
    {
        var occ = Range(first, first.AddDays(count));
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // cabeçalho dos dias
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // dia inteiro
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        double colW = (PanelWidth - 12 - TimeColumn - 14) / count;

        // Cabeçalho
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(TimeColumn, 0, 0, 4) };
        for (int i = 0; i < count; i++)
        {
            var d = first.AddDays(i);
            var sp = new StackPanel { Width = colW };
            sp.Children.Add(new TextBlock { Text = Cap(d.ToString("ddd", Pt)).TrimEnd('.'), FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center });
            var n = new TextBlock { Text = d.Day.ToString(Pt), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = d == Today ? FontWeights.Bold : FontWeights.Normal };
            sp.Children.Add(n);
            if (count > 1)
            {
                var day = d;
                sp.Tapped += (_, _) => { _cursor = day; SetView(View.Day); };
            }
            head.Children.Add(sp);
        }
        outer.Children.Add(head);

        // Dia inteiro
        var allDay = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        allDay.Children.Add(new TextBlock { Text = L.T("all day"), FontSize = 10, Opacity = 0.5, Width = TimeColumn, VerticalAlignment = VerticalAlignment.Center });
        bool anyAllDay = false;
        for (int i = 0; i < count; i++)
        {
            var d = first.AddDays(i);
            var col = new StackPanel { Width = colW, Spacing = 2, Padding = new Thickness(1, 0, 1, 0) };
            foreach (var o in occ.Where(o => o.AllDay && o.Touches(d))) { col.Children.Add(Chip(o, compact: count > 1)); anyAllDay = true; }
            allDay.Children.Add(col);
        }
        if (anyAllDay) { Grid.SetRow(allDay, 1); outer.Children.Add(allDay); }

        // Horas
        var canvas = new Canvas { Height = HourHeight * 24, Width = TimeColumn + colW * count, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        for (int h = 0; h < 24; h++)
        {
            double y = h * HourHeight;
            var label = new TextBlock { Text = $"{h:00}:00", FontSize = 10, Opacity = 0.5 };
            Canvas.SetLeft(label, 2); Canvas.SetTop(label, y - 7);
            if (h > 0) canvas.Children.Add(label);
            var line = new Border { Width = colW * count, Height = 1, Background = Res("DividerStrokeColorDefaultBrush") };
            Canvas.SetLeft(line, TimeColumn); Canvas.SetTop(line, y);
            canvas.Children.Add(line);
        }
        for (int i = 1; i < count; i++)
        {
            var v = new Border { Width = 1, Height = HourHeight * 24, Background = Res("DividerStrokeColorDefaultBrush") };
            Canvas.SetLeft(v, TimeColumn + colW * i); Canvas.SetTop(v, 0);
            canvas.Children.Add(v);
        }

        // Compromissos com horário (lado a lado quando se sobrepõem)
        for (int i = 0; i < count; i++)
        {
            var d = first.AddDays(i);
            var day0 = d.ToDateTime(TimeOnly.MinValue);
            var items = occ.Where(o => !o.AllDay && o.Touches(d))
                .Select(o => (o, s: o.Start < day0 ? day0 : o.Start, e: o.End > day0.AddDays(1) ? day0.AddDays(1) : o.End))
                .OrderBy(x => x.s).ToList();
            var lanes = new List<DateTime>();
            var placed = new List<(Occurrence o, DateTime s, DateTime e, int lane)>();
            foreach (var x in items)
            {
                int lane = lanes.FindIndex(end => end <= x.s);
                if (lane < 0) { lane = lanes.Count; lanes.Add(x.e); } else lanes[lane] = x.e;
                placed.Add((x.o, x.s, x.e, lane));
            }
            int laneCount = Math.Max(1, lanes.Count);
            foreach (var (o, s, e, lane) in placed)
            {
                double top = (s - day0).TotalMinutes / 60 * HourHeight;
                double height = Math.Max(18, (e - s).TotalMinutes / 60 * HourHeight - 2);
                double w = colW / laneCount - 3;
                var block = new Border
                {
                    Width = w, Height = height, CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 2, 4, 2),
                    Background = Res("SubtleFillColorSecondaryBrush"), BorderBrush = Res("TextFillColorSecondaryBrush"), BorderThickness = new Thickness(3, 0, 0, 0),
                };
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(o.Source.Title) ? L.T("(untitled)") : o.Source.Title, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxLines = height > 40 ? 2 : 1 });
                if (height > 30) t.Children.Add(new TextBlock { Text = $"{o.Start:HH:mm} – {o.End:HH:mm}" + (o.Source.Location.Length > 0 ? " · " + o.Source.Location : ""), FontSize = 10, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
                block.Child = t;
                ToolTipService.SetToolTip(block, Describe(o));
                block.Tapped += (_, ev) => { ev.Handled = true; Edit(o.Source, o.Start); };
                Canvas.SetLeft(block, TimeColumn + colW * i + 1 + lane * (colW / laneCount));
                Canvas.SetTop(block, top + 1);
                canvas.Children.Add(block);
            }
        }

        // Linha "agora"
        var now = DateTime.Now;
        int todayCol = Today.DayNumber - first.DayNumber;
        if (todayCol >= 0 && todayCol < count)
        {
            double y = now.TimeOfDay.TotalHours * HourHeight;
            var line = new Border { Width = colW, Height = 2, Background = Res("TextFillColorPrimaryBrush") };
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Res("TextFillColorPrimaryBrush") };
            Canvas.SetLeft(line, TimeColumn + colW * todayCol); Canvas.SetTop(line, y - 1);
            Canvas.SetLeft(dot, TimeColumn + colW * todayCol - 4); Canvas.SetTop(dot, y - 4);
            canvas.Children.Add(line); canvas.Children.Add(dot);
        }

        // Clique num horário vazio: novo compromisso naquela hora.
        canvas.Tapped += (_, e) =>
        {
            if (e.OriginalSource != canvas && e.OriginalSource is not Border { Height: 1 }) return;
            var p = e.GetPosition(canvas);
            if (p.X < TimeColumn) return;
            int col = Math.Clamp((int)((p.X - TimeColumn) / colW), 0, count - 1);
            int hour = Math.Clamp((int)(p.Y / HourHeight), 0, 23);
            Edit(null, NewStart(first.AddDays(col), hour));
        };
        ToolTipService.SetToolTip(canvas, L.T("Click an empty time slot to create an event"));

        var scroll = new ScrollViewer { Content = canvas, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        double target = (todayCol >= 0 && todayCol < count ? Math.Max(0, now.Hour - 1) : 7) * HourHeight;
        scroll.Loaded += (_, _) => scroll.ChangeView(null, target, null, disableAnimation: true);
        Grid.SetRow(scroll, 2);
        outer.Children.Add(scroll);
        return outer;
    }

    // ---------------------------------------------------------------- lista

    private FrameworkElement ListView()
    {
        var from = _cursor < Today && _cursor.AddDays(30) > Today ? Today : _cursor;
        var occ = Range(from, from.AddDays(60));
        var list = new StackPanel { Spacing = 2 };
        if (occ.Count == 0)
            list.Children.Add(new TextBlock { Text = L.T("No events in the next 60 days. Use + to create one."), Opacity = 0.7, Margin = new Thickness(4) });
        foreach (var group in occ.GroupBy(o => DateOnly.FromDateTime(o.Start < from.ToDateTime(TimeOnly.MinValue) ? from.ToDateTime(TimeOnly.MinValue) : o.Start)))
        {
            string label = group.Key == Today ? L.T("Today") : group.Key == Today.AddDays(1) ? L.T("Tomorrow") : "";
            list.Children.Add(new TextBlock
            {
                Text = (label.Length > 0 ? label + " · " : "") + Cap(group.Key.ToString("D", Pt)),
                FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(2, 10, 0, 2),
            });
            foreach (var o in group)
            {
                var row = new Grid { Padding = new Thickness(6, 5, 6, 5), Background = Res("SubtleFillColorTransparentBrush"), CornerRadius = new CornerRadius(4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = o.AllDay ? L.T("all day") : $"{o.Start:HH:mm} – {o.End:HH:mm}", Opacity = 0.7, FontSize = 12 });
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(o.Source.Title) ? L.T("(untitled)") : o.Source.Title, TextTrimming = TextTrimming.CharacterEllipsis });
                if (o.Source.Location.Length > 0) t.Children.Add(new TextBlock { Text = o.Source.Location, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                row.PointerEntered += (_, _) => row.Background = Res("SubtleFillColorSecondaryBrush");
                row.PointerExited += (_, _) => row.Background = Res("SubtleFillColorTransparentBrush");
                row.Tapped += (_, _) => Edit(o.Source, o.Start);
                list.Children.Add(row);
            }
        }
        return new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ================================================================= editor

    private void Edit(CalEvent? existing, DateTime start)
    {
        bool isNew = existing is null;
        var e = existing ?? new CalEvent { Start = start, End = start.AddHours(1) };
        _body.Children.Clear();
        _title.Text = isNew ? L.T("New event") : e.ReadOnly ? L.T("Event (subscribed calendar)") : L.T("Edit event");

        var form = new StackPanel { Spacing = 8 };
        var title = new TextBox { PlaceholderText = L.T("Title"), Text = e.Title, FontSize = 15, IsReadOnly = e.ReadOnly };
        var allDay = new ToggleSwitch { Header = L.T("All day"), IsOn = e.AllDay, IsEnabled = !e.ReadOnly };
        var date = new CalendarDatePicker { Header = L.T("Date"), Date = e.Start.Date, IsEnabled = !e.ReadOnly, FirstDayOfWeek = (global::Windows.Globalization.DayOfWeek)(int)_firstDay };
        var endDate = new CalendarDatePicker { Header = L.T("Until"), Date = (e.Start + e.Duration).AddDays(e.AllDay ? -1 : 0).Date, IsEnabled = !e.ReadOnly };
        var from = new TimePicker { Header = L.T("Start"), ClockIdentifier = "24HourClock", Time = e.Start.TimeOfDay, MinuteIncrement = 5, IsEnabled = !e.ReadOnly };
        var to = new TimePicker { Header = L.T("End"), ClockIdentifier = "24HourClock", Time = (e.Start + e.Duration).TimeOfDay, MinuteIncrement = 5, IsEnabled = !e.ReadOnly };
        var repeat = new ComboBox
        {
            Header = L.T("Repeat"), MinWidth = 170, IsEnabled = !e.ReadOnly,
            ItemsSource = new[] { L.T("Does not repeat"), L.T("Every day"), L.T("Weekdays (Mon–Fri)"), L.T("Every week"), L.T("Every month"), L.T("Every year") }, SelectedIndex = (int)e.Repeat,
        };
        var until = new CalendarDatePicker { Header = L.T("Repeat until (optional)"), Date = e.RepeatUntil, IsEnabled = !e.ReadOnly, PlaceholderText = L.T("no end") };
        int[] remindValues = [-1, 0, 5, 10, 15, 30, 60, 1440];
        var remind = new ComboBox
        {
            Header = L.T("Reminder"), MinWidth = 170, IsEnabled = !e.ReadOnly,
            ItemsSource = new[] { L.T("None"), L.T("At start time"), L.T("5 min before"), L.T("10 min before"), L.T("15 min before"), L.T("30 min before"), L.T("1 hour before"), L.T("1 day before") },
            SelectedIndex = Math.Max(0, Array.IndexOf(remindValues, e.RemindMinutes ?? -1)),
        };
        var location = new TextBox { Header = L.T("Location"), Text = e.Location, IsReadOnly = e.ReadOnly };
        var notes = new TextBox { Header = L.T("Notes"), Text = e.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64, IsReadOnly = e.ReadOnly };
        var error = new TextBlock { FontSize = 12, Opacity = 0.8 };

        void Sync()
        {
            from.Visibility = to.Visibility = allDay.IsOn ? Visibility.Collapsed : Visibility.Visible;
            endDate.Visibility = allDay.IsOn ? Visibility.Visible : Visibility.Collapsed;
            until.Visibility = repeat.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        allDay.Toggled += (_, _) => Sync();
        repeat.SelectionChanged += (_, _) => Sync();
        Sync();

        form.Children.Add(title);
        form.Children.Add(Row(date, allDay, endDate));
        form.Children.Add(Row(from, to));
        form.Children.Add(Row(repeat, until));
        form.Children.Add(Row(remind));
        form.Children.Add(location);
        form.Children.Add(notes);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!e.ReadOnly)
        {
            var save = new Button { Content = L.T("Save") };
            if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var st) && st is Style style) save.Style = style;
            save.Click += (_, _) =>
            {
                if (date.Date is not { } d) { error.Text = L.T("Pick a date."); return; }
                var day = d.Date;
                DateTime s, en;
                if (allDay.IsOn)
                {
                    var last = (endDate.Date?.Date ?? day) < day ? day : endDate.Date!.Value.Date;
                    s = day; en = last.AddDays(1);
                }
                else
                {
                    s = day + from.Time; en = day + to.Time;
                    if (en <= s) en = en.AddDays(1); // atravessa a meia-noite
                }
                _ctx.Store.Update(x =>
                {
                    e.Title = title.Text.Trim().Length > 200 ? title.Text.Trim()[..200] : title.Text.Trim();
                    e.AllDay = allDay.IsOn; e.Start = s; e.End = en;
                    e.Repeat = (Repeat)Math.Max(0, repeat.SelectedIndex);
                    e.RepeatUntil = e.Repeat == Repeat.None ? null : until.Date?.Date;
                    e.RemindMinutes = remindValues[Math.Max(0, remind.SelectedIndex)] is var m && m >= 0 ? m : null;
                    e.Location = location.Text.Trim(); e.Notes = notes.Text.Length > 5000 ? notes.Text[..5000] : notes.Text;
                    if (isNew) x.Events.Add(e);
                });
                _cursor = DateOnly.FromDateTime(s);
                Render();
            };
            buttons.Children.Add(save);
        }
        var cancel = new Button { Content = e.ReadOnly ? L.T("Back") : L.T("Cancel") };
        cancel.Click += (_, _) => Render();
        buttons.Children.Add(cancel);
        if (!isNew && !e.ReadOnly)
        {
            var del = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "", FontSize = 13 }, new TextBlock { Text = e.Repeat == Repeat.None ? L.T("Delete") : L.T("Delete all repeats") } } } };
            del.Click += (_, _) => { _ctx.Store.Update(x => x.Events.Remove(e)); Render(); };
            buttons.Children.Add(del);
        }
        form.Children.Add(buttons);
        form.Children.Add(error);
        _body.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (!e.ReadOnly) title.Loaded += (_, _) => title.Focus(FocusState.Programmatic);
    }

    // ================================================================= importar / exportar

    private async Task ImportAsync()
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WidgetActions.HostWindow);
            picker.FileTypeFilter.Add(".ics");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var props = await file.GetBasicPropertiesAsync();
            if (props.Size > 5 * 1024 * 1024) return;
            string text = await global::Windows.Storage.FileIO.ReadTextAsync(file);
            var imported = CalendarMath.FromIcs(ICal.Parse(text)).Select(x => { x.ReadOnly = false; x.Id = Guid.NewGuid().ToString("N")[..10]; return x; }).ToList();
            _ctx.Store.Update(x => x.Events.AddRange(imported.Take(5000)));
            Render();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
    }

    private async Task ExportAsync()
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = L.T("CleanWinVista-calendar") };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WidgetActions.HostWindow);
            picker.FileTypeChoices.Add("iCalendar", [".ics"]);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await global::Windows.Storage.FileIO.WriteTextAsync(file, CalendarMath.ToIcs(_ctx.Store.Data.Events));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
    }

    // ================================================================= lembretes (chamado pelo relógio a cada segundo)

    private static DateTime s_lastReminderCheck = DateTime.Now;

    public static void CheckReminders(WidgetContext ctx, FrameworkElement anchor)
    {
        var now = DateTime.Now;
        if ((now - s_lastReminderCheck).TotalSeconds < 20) return;
        var since = s_lastReminderCheck;
        s_lastReminderCheck = now;
        var due = CalendarMath.DueReminders(ctx.Store.Data.Events, since, now).ToList();
        if (due.Count == 0) return;
        ScreenTools.Chime();
        var p = new StackPanel { Spacing = 4, Padding = new Thickness(4), MaxWidth = 320 };
        p.Children.Add(new TextBlock { Text = L.T("Reminder"), FontWeight = FontWeights.SemiBold });
        foreach (var o in due.Take(4))
            p.Children.Add(new TextBlock { Text = (o.AllDay ? L.T("All day") + " · " : $"{o.Start:HH:mm} · ") + o.Source.Title, TextWrapping = TextWrapping.Wrap });
        var f = new Flyout { Content = p, ShouldConstrainToRootBounds = false, Placement = FlyoutPlacementMode.Top };
        PanelStyle.Apply(f);
        OutsideClickDismiss.Attach(f);
        f.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top });
    }

    // ================================================================= peças

    private static string Describe(Occurrence o)
    {
        string when = o.AllDay ? L.T("All day") : $"{o.Start:HH:mm} – {o.End:HH:mm}";
        string rep = o.Source.Repeat switch { Repeat.Daily => " · " + L.T("every day"), Repeat.Weekdays => " · " + L.T("weekdays"), Repeat.Weekly => " · " + L.T("every week"), Repeat.Monthly => " · " + L.T("every month"), Repeat.Yearly => " · " + L.T("every year"), _ => "" };
        string loc = o.Source.Location.Length > 0 ? "\n" + o.Source.Location : "";
        return $"{(string.IsNullOrWhiteSpace(o.Source.Title) ? L.T("(untitled)") : o.Source.Title)}\n{when}{rep}{loc}" + (o.Source.ReadOnly ? "\n(" + L.T("subscribed calendar") + ")" : "");
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Pt) + s[1..];

    private static Brush Res(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private static StackPanel Row(params UIElement[] items)
    {
        var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var i in items) r.Children.Add(i);
        return r;
    }

    private static Button Icon(string glyph, string tip, Action click)
    {
        var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 13 }, Padding = new Thickness(8, 6, 8, 6) };
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static Button Text(string text, string tip, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 5), FontSize = 12 };
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var i = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        i.Click += (_, _) => action();
        return i;
    }
}
