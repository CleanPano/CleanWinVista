using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Core.Tools;
using NovaTaskbar.Core.Widgets;
using Windows.ApplicationModel.DataTransfer;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Calculadora (ideias do app Unitto, código próprio): teclado padrão + científico (sin, cos, tan, ln, log, √, ^, !, π, e),
/// graus/radianos, prévia do resultado enquanto digita, histórico salvo, parênteses inteligentes; abas de
/// calculadora de datas (diferença / somar período) e conversor de bases (DEC/HEX/BIN/OCT).
/// </summary>
internal sealed class CalculatorWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.Calculator, iconSize, width, Glyphs.Calculator)
{
    private static CultureInfo Pt => L.Culture;
    /// <summary>Separador de milhar do idioma (removido ao reaproveitar um resultado na conta).</summary>
    private static string Group => L.Culture.NumberFormat.NumberGroupSeparator;
    private const int MaxHistory = 30;

    private Grid? _body;
    private readonly Dictionary<string, ToggleButton> _tabs = new();

    public override string Tooltip => ctx.Store.Data.CalcHistory.FirstOrDefault() is { } last ? L.T("Calculator · last: ") + last : L.T("Calculator");
    public override void Update(WidgetData d) { }

    protected override FrameworkElement BuildPanel()
    {
        var root = new StackPanel { Width = 340, Padding = new Thickness(4), Spacing = 8 };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (id, label) in new[] { ("calc", L.T("Calculate")), ("dates", L.T("Dates")), ("bases", L.T("Bases")) })
        {
            var b = new ToggleButton { Content = label, Padding = new Thickness(12, 4, 12, 5), FontSize = 12 };
            string tab = id;
            b.Click += (_, _) => Show(tab);
            _tabs[id] = b;
            tabs.Children.Add(b);
        }
        root.Children.Add(tabs);
        _body = new Grid { MinHeight = 430 }; // altura estável ao trocar de aba (o painel não "pula")
        root.Children.Add(_body);
        Show("calc");
        return root;
    }

    private void Show(string tab)
    {
        foreach (var (id, b) in _tabs) b.IsChecked = id == tab;
        if (_body is null) return;
        _body.MinHeight = Math.Max(_body.MinHeight, _body.ActualHeight); // nunca encolhe: o painel fica parado ao trocar de aba
        _body.Children.Clear();
        _body.Children.Add(tab switch { "dates" => DatesTab(), "bases" => BasesTab(), _ => CalcTab() });
    }

    // ================================================================ calculadora

    private FrameworkElement CalcTab()
    {
        var data = ctx.Store.Data;
        var panel = new StackPanel { Spacing = 6 };

        // Histórico (mais recente embaixo, como numa fita)
        var history = new StackPanel { Spacing = 0 };
        var historyScroll = new ScrollViewer { Content = history, MaxHeight = 76, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var input = new TextBox { FontSize = 22, TextAlignment = TextAlignment.Right, PlaceholderText = "0", BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var preview = new TextBlock { FontSize = 14, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Right, IsTextSelectionEnabled = true, MinHeight = 20 };
        AngleMode Angles() => ctx.Store.Data.CalcRadians ? AngleMode.Radians : AngleMode.Degrees;

        void RenderHistory()
        {
            history.Children.Clear();
            foreach (var h in data.CalcHistory.AsEnumerable().Reverse())
            {
                string result = h.Contains(" = ") ? h[(h.LastIndexOf(" = ", StringComparison.Ordinal) + 3)..] : h;
                var b = new Button
                {
                    Content = new TextBlock { Text = h, FontSize = 12, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis },
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Right,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(4, 1, 4, 1),
                };
                ToolTipService.SetToolTip(b, L.T("Use this result"));
                b.Click += (_, _) => Insert(input, result.Replace(Group, ""));
                history.Children.Add(b);
            }
            historyScroll.Visibility = data.CalcHistory.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            historyScroll.UpdateLayout();
            historyScroll.ChangeView(null, historyScroll.ScrollableHeight, null, true);
        }

        void Preview() => preview.Text = input.Text.Length == 0 ? "" :
            Calculator.TryEvaluate(input.Text, Angles(), out var r) && Calculator.Format(r).Replace(Group, "") != input.Text.Trim() ? "= " + Calculator.Format(r) : "";

        void Evaluate()
        {
            if (!Calculator.TryEvaluate(input.Text, Angles(), out var r)) { preview.Text = input.Text.Length == 0 ? "" : L.T("Invalid expression"); return; }
            string res = Calculator.Format(r);
            string entry = $"{input.Text.Trim()} = {res}";
            ctx.Store.Update(x =>
            {
                x.CalcHistory.Remove(entry);
                x.CalcHistory.Insert(0, entry);
                if (x.CalcHistory.Count > MaxHistory) x.CalcHistory.RemoveRange(MaxHistory, x.CalcHistory.Count - MaxHistory);
            });
            input.Text = res.Replace(Group, ""); // sem separador de milhar para continuar a conta
            input.SelectionStart = input.Text.Length;
            preview.Text = "";
            RenderHistory();
        }

        input.TextChanged += (_, _) => Preview();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Enter) { e.Handled = true; Evaluate(); }
            else if (e.Key == global::Windows.System.VirtualKey.Escape && input.Text.Length > 0) { e.Handled = true; input.Text = ""; }
        };

        // Barra: copiar · graus/radianos · científico · limpar histórico
        var tools = new Grid();
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var angle = SmallButton(data.CalcRadians ? "RAD" : "DEG", L.T("Angles in degrees or radians"));
        angle.Click += (_, _) =>
        {
            ctx.Store.Update(x => x.CalcRadians = !x.CalcRadians);
            angle.Content = ctx.Store.Data.CalcRadians ? "RAD" : "DEG";
            Preview();
        };
        var sci = new ToggleButton { Content = "f(x)", IsChecked = data.CalcScientific, Padding = new Thickness(8, 3, 8, 4), FontSize = 12 };
        ToolTipService.SetToolTip(sci, L.T("Scientific functions"));
        left.Children.Add(angle);
        left.Children.Add(sci);
        tools.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var copy = SmallButton(L.T("Copy"), L.T("Copy the result"));
        copy.Click += (_, _) =>
        {
            if (!Calculator.TryEvaluate(input.Text, Angles(), out var r)) return;
            var pkg = new DataPackage();
            pkg.SetText(Calculator.Format(r));
            Clipboard.SetContent(pkg);
            copy.Content = L.T("Copied ✓");
        };
        var clear = SmallButton(L.T("Clear history"), L.T("Delete the calculation history"));
        clear.Click += (_, _) => { ctx.Store.Update(x => x.CalcHistory.Clear()); RenderHistory(); };
        right.Children.Add(copy);
        right.Children.Add(clear);
        Grid.SetColumn(right, 1);
        tools.Children.Add(right);

        // Teclado científico
        var sciPad = Pad(5, [
            ("sin", "sin("), ("cos", "cos("), ("tan", "tan("), ("ln", "ln("), ("log", "log("),
            ("asin", "asin("), ("acos", "acos("), ("atan", "atan("), ("√", "√("), ("∛", "∛("),
            ("xʸ", "^"), ("x²", "^2"), ("n!", "!"), ("π", "π"), ("e", "e"),
        ], t => Insert(input, t));
        sciPad.Visibility = data.CalcScientific ? Visibility.Visible : Visibility.Collapsed;
        sci.Click += (_, _) =>
        {
            ctx.Store.Update(x => x.CalcScientific = sci.IsChecked == true);
            sciPad.Visibility = sci.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        };

        // Teclado padrão
        var pad = Pad(4, [
            ("C", "C"), ("⌫", "BACK"), ("%", "%"), ("÷", "÷"),
            ("7", "7"), ("8", "8"), ("9", "9"), ("×", "×"),
            ("4", "4"), ("5", "5"), ("6", "6"), ("−", "−"),
            ("1", "1"), ("2", "2"), ("3", "3"), ("+", "+"),
            ("( )", "PAREN"), ("0", "0"), (",", ","), ("=", "="),
        ], t =>
        {
            switch (t)
            {
                case "C": input.Text = ""; break;
                case "BACK":
                    int i = input.SelectionStart;
                    if (input.SelectionLength > 0) { input.Text = input.Text.Remove(i, input.SelectionLength); input.SelectionStart = i; }
                    else if (i > 0) { input.Text = input.Text.Remove(i - 1, 1); input.SelectionStart = i - 1; }
                    break;
                case "PAREN": Insert(input, SmartParen(input.Text, input.SelectionStart)); break;
                case "=": Evaluate(); break;
                default: Insert(input, t); break;
            }
            input.Focus(FocusState.Programmatic);
        });

        panel.Children.Add(historyScroll);
        panel.Children.Add(input);
        panel.Children.Add(preview);
        panel.Children.Add(tools);
        panel.Children.Add(sciPad);
        panel.Children.Add(pad);
        panel.Children.Add(Hint(L.T("Enter calculates · Esc clears · click a history entry to reuse its result.")));
        RenderHistory();
        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        return panel;
    }

    private static void Insert(TextBox box, string text)
    {
        int i = Math.Clamp(box.SelectionStart, 0, box.Text.Length);
        string t = box.Text;
        if (box.SelectionLength > 0) t = t.Remove(i, Math.Min(box.SelectionLength, t.Length - i));
        box.Text = t.Insert(i, text);
        box.SelectionStart = i + text.Length;
        box.Focus(FocusState.Programmatic);
    }

    /// <summary>Abre "(" ou fecha ")" conforme o que já está aberto antes do cursor.</summary>
    private static string SmartParen(string text, int caret)
    {
        string before = text[..Math.Clamp(caret, 0, text.Length)];
        int open = before.Count(c => c == '(') - before.Count(c => c == ')');
        char last = before.TrimEnd().LastOrDefault();
        return open > 0 && (char.IsDigit(last) || last is ')' or 'π' or 'e' or '!' or '%') ? ")" : "(";
    }

    private static Grid Pad(int columns, (string Label, string Value)[] keys, Action<string> press)
    {
        var g = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (int c = 0; c < columns; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int rows = (keys.Length + columns - 1) / columns;
        for (int r = 0; r < rows; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int k = 0; k < keys.Length; k++)
        {
            var (label, value) = keys[k];
            bool op = label is "÷" or "×" or "−" or "+" or "=" or "%";
            var b = new Button
            {
                Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 38, FontSize = op ? 17 : 15,
                FontWeight = op || label == "=" ? FontWeights.SemiBold : FontWeights.Normal,
            };
            if (label == "=" && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var st) && st is Style style) b.Style = style;
            b.Click += (_, _) => press(value);
            Grid.SetRow(b, k / columns);
            Grid.SetColumn(b, k % columns);
            g.Children.Add(b);
        }
        return g;
    }

    private static Button SmallButton(string text, string tip)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 4), FontSize = 12 };
        ToolTipService.SetToolTip(b, tip);
        return b;
    }

    // ================================================================ datas

    private FrameworkElement DatesTab()
    {
        var p = new StackPanel { Spacing = 8 };
        var today = DateTime.Today;
        var from = new CalendarDatePicker { Header = L.T("From"), Date = today, HorizontalAlignment = HorizontalAlignment.Stretch };
        var to = new CalendarDatePicker { Header = L.T("To"), Date = today.AddDays(30), HorizontalAlignment = HorizontalAlignment.Stretch };
        var diff = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var details = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        void Diff()
        {
            if (from.Date is not { } a || to.Date is not { } b) { diff.Text = "—"; details.Text = ""; return; }
            var s = DateCalc.Difference(DateOnly.FromDateTime(a.Date), DateOnly.FromDateTime(b.Date));
            diff.Text = DateCalc.Describe(s);
            details.Text = L.F("{0:#,0} days · {1:0.#} weeks · {2:#,0} weekdays (Mon–Fri)", s.TotalDays, s.TotalDays / 7.0, s.Weekdays);
        }
        from.DateChanged += (_, _) => Diff();
        to.DateChanged += (_, _) => Diff();
        Diff();
        p.Children.Add(new TextBlock { Text = L.T("Difference between dates"), FontWeight = FontWeights.SemiBold });
        p.Children.Add(Row(from, to));
        p.Children.Add(diff);
        p.Children.Add(details);

        p.Children.Add(new TextBlock { Text = L.T("Add or subtract"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        var start = new CalendarDatePicker { Header = L.T("Date"), Date = today };
        var amount = new NumberBox { Header = L.T("Amount"), Value = 90, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Minimum = -100000, Maximum = 100000 };
        var unit = new ComboBox { Header = L.T("Unit"), ItemsSource = new[] { L.T("days"), L.T("weeks"), L.T("months"), L.T("years") }, SelectedIndex = 0 };
        var result = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, IsTextSelectionEnabled = true };
        void Add()
        {
            if (start.Date is not { } d || double.IsNaN(amount.Value)) { result.Text = "—"; return; }
            try
            {
                var r = DateCalc.Add(DateOnly.FromDateTime(d.Date), (int)amount.Value, (DateCalc.Unit)Math.Max(0, unit.SelectedIndex));
                result.Text = Cap(r.ToString("D", Pt));
            }
            catch (ArgumentOutOfRangeException) { result.Text = L.T("Out of range"); }
        }
        start.DateChanged += (_, _) => Add();
        amount.ValueChanged += (_, _) => Add();
        unit.SelectionChanged += (_, _) => Add();
        Add();
        p.Children.Add(Row(start, amount, unit));
        p.Children.Add(result);
        return p;
    }

    // ================================================================ bases

    private FrameworkElement BasesTab()
    {
        var p = new StackPanel { Spacing = 8 };
        var input = new TextBox { Header = L.T("Number"), PlaceholderText = "ex.: 255, FF, 1010", FontSize = 16 };
        var from = new ComboBox { Header = L.T("Base"), ItemsSource = NumberBases.All.Select(b => $"{b.Name} ({b.Base})").ToList(), SelectedIndex = 0, MinWidth = 120 };
        var outputs = new StackPanel { Spacing = 4 };
        void Convert()
        {
            outputs.Children.Clear();
            int numberBase = NumberBases.All[Math.Max(0, from.SelectedIndex)].Base;
            if (input.Text.Trim().Length == 0) return;
            if (!NumberBases.TryParse(input.Text, numberBase, out long v))
            {
                outputs.Children.Add(Hint(L.T("Invalid number for this base (64-bit integers).")));
                return;
            }
            foreach (var (b, name) in NumberBases.All)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = name, Opacity = 0.6, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                var value = new TextBlock { Text = NumberBases.Format(v, b), FontFamily = new FontFamily("Consolas"), FontSize = 15, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                outputs.Children.Add(row);
            }
        }
        input.TextChanged += (_, _) => Convert();
        from.SelectionChanged += (_, _) => Convert();
        p.Children.Add(Row(input, from));
        p.Children.Add(outputs);
        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        input.Width = 200;
        return p;
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Pt) + s[1..];
}
