using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.App.Services;
using NovaTaskbar.Core.Tools;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

// ============================================================================================================
// Lotes 5 e 6 — widgets com internet. Todos dependem de "Permitir widgets com internet" (desligado de fábrica),
// só HTTPS, sem redirecionamento, e o log guarda só o host. Chaves de API ficam criptografadas (DPAPI).
// ============================================================================================================

/// <summary>Clima atual + próximos dias (Open-Meteo, sem chave). Atualiza a cada 30 min.</summary>
internal sealed class WeatherWidget : WidgetView
{
    private readonly WidgetContext _ctx;
    private readonly FontIcon _icon;
    private readonly TextBlock _temp;
    private static Forecast? s_data;
    private static DateTime s_fetched = DateTime.MinValue;
    private static string s_city = "";
    private static bool s_loading;

    private sealed record Day(DateTime Date, double Max, double Min, int Code);
    private sealed record Forecast(string Place, double Temp, int Code, IReadOnlyList<Day> Days);

    public WeatherWidget(double iconSize, double width, WidgetContext ctx) : base(WidgetKind.Weather, iconSize, width)
    {
        _ctx = ctx;
        _icon = Glyphs.Create(Glyphs.Weather, iconSize * 0.42);
        _temp = Text(F(0.34), true);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = iconSize * 0.12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_icon);
        row.Children.Add(_temp);
        Root.Children.Add(row);
    }

    public override string Tooltip => !_ctx.Net.Allowed ? L.T("Weather: turn on “Allow internet widgets” in settings") :
        string.IsNullOrWhiteSpace(_ctx.Store.Data.WeatherCity) ? L.T("Weather: click to choose the city") :
        s_data is { } f ? $"{f.Place}: {f.Temp:0}° · {Describe(f.Code)}" : L.T("Weather: loading…");

    public override void Update(WidgetData d)
    {
        string city = _ctx.Store.Data.WeatherCity.Trim();
        if (_ctx.Net.Allowed && city.Length > 0 && !s_loading && (city != s_city || DateTime.Now - s_fetched > TimeSpan.FromMinutes(30)))
            _ = LoadAsync(city);
        if (s_data is { } f && city == s_city)
        {
            Glyphs.Set(_icon, GlyphFor(f.Code));
            _temp.Text = $"{f.Temp:0}°";
        }
        else
        {
            Glyphs.Set(_icon, Glyphs.Weather);
            _temp.Text = _ctx.Net.Allowed ? "—" : "off";
        }
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        var p = new StackPanel { Spacing = 8, Padding = new Thickness(4), Width = 300 };
        p.Children.Add(new TextBlock { Text = L.T("Weather"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (!_ctx.Net.Allowed)
            p.Children.Add(new TextBlock { Text = L.T("This widget uses the internet (open-meteo.com). Turn on “Allow internet widgets” in the Dock settings."), TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        if (s_data is { } f && s_city == _ctx.Store.Data.WeatherCity.Trim())
        {
            p.Children.Add(new TextBlock { Text = $"{f.Place} · {f.Temp:0}° · {Describe(f.Code)}", TextWrapping = TextWrapping.Wrap });
            var culture = L.Culture;
            foreach (var day in f.Days)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                row.Children.Add(new TextBlock { Text = day.Date.ToString("ddd d", culture), Width = 64, Opacity = 0.7 });
                row.Children.Add(Glyphs.Create(GlyphFor(day.Code), 16));
                row.Children.Add(new TextBlock { Text = $"{day.Max:0}° / {day.Min:0}°" });
                p.Children.Add(row);
            }
        }
        var city = new TextBox { Header = L.T("City"), Text = _ctx.Store.Data.WeatherCity, PlaceholderText = L.T("e.g. London") };
        var save = new Button { Content = L.T("Save city") };
        save.Click += (_, _) =>
        {
            string c = city.Text.Trim();
            _ctx.Store.Update(x => x.WeatherCity = c.Length > 80 ? c[..80] : c);
            s_fetched = DateTime.MinValue;
            _ctx.Refresh();
        };
        p.Children.Add(city);
        p.Children.Add(save);
        WidgetActions.ShowFlyout(p, anchor, at);
    }

    private async Task LoadAsync(string city)
    {
        s_loading = true;
        try
        {
            string? geo = await _ctx.Net.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&language=pt&name=" + Uri.EscapeDataString(city));
            if (geo is null) return;
            double? lat = JsonPath.Number(geo, "results.0.latitude"), lon = JsonPath.Number(geo, "results.0.longitude");
            if (lat is null || lon is null) { s_city = city; s_data = null; s_fetched = DateTime.Now; return; }
            string place = city;
            using (var doc = JsonDocument.Parse(geo))
                if (doc.RootElement.GetProperty("results")[0].TryGetProperty("name", out var n)) place = n.GetString() ?? city;
            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat.Value:0.####}&longitude={lon.Value:0.####}&current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min,weather_code&timezone=auto&forecast_days=4");
            string? json = await _ctx.Net.GetStringAsync(url);
            if (json is null) return;
            var days = new List<Day>();
            using (var doc = JsonDocument.Parse(json))
            {
                var daily = doc.RootElement.GetProperty("daily");
                var dates = daily.GetProperty("time");
                for (int i = 0; i < dates.GetArrayLength(); i++)
                    days.Add(new Day(DateTime.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture),
                        daily.GetProperty("temperature_2m_max")[i].GetDouble(), daily.GetProperty("temperature_2m_min")[i].GetDouble(), daily.GetProperty("weather_code")[i].GetInt32()));
            }
            s_data = new Forecast(place, JsonPath.Number(json, "current.temperature_2m") ?? 0, (int)(JsonPath.Number(json, "current.weather_code") ?? 0), days);
            s_city = city;
            s_fetched = DateTime.Now;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            s_fetched = DateTime.Now; // resposta inesperada: tenta de novo em 30 min
        }
        finally
        {
            s_loading = false;
            _ctx.UI.TryEnqueue(() => _ctx.Refresh());
        }
    }

    // Códigos WMO usados pelo Open-Meteo.
    private static string GlyphFor(int code) => code switch
    {
        0 => "",            // sol
        1 or 2 => "",       // parcialmente nublado... (fallback abaixo se a fonte não tiver)
        3 => "",
        45 or 48 => "",
        >= 51 and <= 67 or >= 80 and <= 82 => "",
        >= 71 and <= 77 or 85 or 86 => "",
        >= 95 => "",
        _ => "",
    };

    private static string Describe(int code) => code switch
    {
        0 => L.T("clear sky"),
        1 or 2 => L.T("few clouds"),
        3 => L.T("cloudy"),
        45 or 48 => L.T("fog"),
        >= 51 and <= 57 => L.T("drizzle"),
        >= 61 and <= 67 or >= 80 and <= 82 => L.T("rain"),
        >= 71 and <= 77 or 85 or 86 => L.T("snow"),
        >= 95 => L.T("thunderstorm"),
        _ => "—",
    };
}

/// <summary>Agenda: próximo compromisso de um calendário iCal (link "endereço secreto" do Google/Outlook). Atualiza a cada 15 min.</summary>
internal sealed class CalendarWidget : WidgetView
{
    private readonly WidgetContext _ctx;
    private readonly TextBlock _when, _what;
    private static IReadOnlyList<ICal.Event> s_events = [];
    /// <summary>Eventos da agenda iCal assinada (mostrados no calendário do relógio, somente leitura).</summary>
    internal static IReadOnlyList<ICal.Event> Subscribed => s_events;
    private static DateTime s_fetched = DateTime.MinValue;
    private static string s_url = "";
    private static bool s_loading;

    public CalendarWidget(double iconSize, double width, WidgetContext ctx) : base(WidgetKind.Calendar, iconSize, width)
    {
        _ctx = ctx;
        _when = Text(F(0.22), false, 0.7);
        _what = Text(F(0.28), true);
        _what.TextTrimming = TextTrimming.CharacterEllipsis;
        _what.MaxWidth = width - iconSize * 0.3;
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(_when);
        sp.Children.Add(_what);
        Root.Children.Add(sp);
    }

    private IReadOnlyList<ICal.Event> Next => ICal.Upcoming(s_events, DateTime.Now, 8);

    public override string Tooltip => !_ctx.Net.Allowed ? L.T("Agenda: turn on “Allow internet widgets” in settings") :
        string.IsNullOrWhiteSpace(_ctx.Store.Data.CalendarUrl) ? L.T("Agenda: click to paste your calendar's iCal link") :
        Next.FirstOrDefault() is { } e ? $"{Format(e)} · {e.Summary}" : L.T("Agenda: nothing in the coming days");

    public override void Update(WidgetData d)
    {
        string url = _ctx.Store.Data.CalendarUrl.Trim();
        if (_ctx.Net.Allowed && url.Length > 0 && !s_loading && (url != s_url || DateTime.Now - s_fetched > TimeSpan.FromMinutes(15)))
            _ = LoadAsync(url);
        var next = url == s_url ? Next.FirstOrDefault() : null;
        _when.Text = next is null ? L.T("Agenda") : Format(next);
        _what.Text = !_ctx.Net.Allowed ? L.T("internet off") : url.Length == 0 ? L.T("set up") : next?.Summary ?? L.T("no events");
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        var p = new StackPanel { Spacing = 8, Padding = new Thickness(4), Width = 340 };
        p.Children.Add(new TextBlock { Text = L.T("Agenda"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var e in Next)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = e.Summary, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = Format(e) + (e.Location.Length > 0 ? " · " + e.Location : ""), FontSize = 11, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
            p.Children.Add(sp);
        }
        var url = new TextBox { Header = "Link iCal (https)", Text = _ctx.Store.Data.CalendarUrl, PlaceholderText = "https://calendar.google.com/…/basic.ics" };
        var info = new TextBlock { Text = L.T("Google Calendar: Settings → your calendar → “Secret address in iCal format”. The link stays on this PC only."), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 };
        var save = new Button { Content = L.T("Save") };
        save.Click += (_, _) =>
        {
            string u = url.Text.Trim();
            if (u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) u = "https://" + u[9..];
            if (u.Length > 0 && !UrlRules.IsSafeWebUrl(u, httpsOnly: true)) { info.Text = L.T("Use an https link."); return; }
            _ctx.Store.Update(x => x.CalendarUrl = u);
            s_fetched = DateTime.MinValue;
            _ctx.Refresh();
        };
        p.Children.Add(url);
        p.Children.Add(info);
        p.Children.Add(save);
        p.Children.Add(new HyperlinkButton { Content = L.T("Open Windows Calendar") }.With(b => b.Click += (_, _) => WidgetActions.OpenWindowsUri("outlookcal:")));
        WidgetActions.ShowFlyout(p, anchor, at);
    }

    private async Task LoadAsync(string url)
    {
        s_loading = true;
        try
        {
            string? ics = await _ctx.Net.GetStringAsync(url);
            if (ics != null) s_events = ICal.Parse(ics);
            s_url = url;
            s_fetched = DateTime.Now;
        }
        finally
        {
            s_loading = false;
            _ctx.UI.TryEnqueue(() => _ctx.Refresh());
        }
    }

    private static string Format(ICal.Event e)
    {
        var culture = L.Culture;
        string day = e.StartLocal.Date == DateTime.Today ? L.T("Today") : e.StartLocal.Date == DateTime.Today.AddDays(1) ? L.T("Tomorrow") : e.StartLocal.ToString("M", culture);
        return e.AllDay ? day : $"{day} {e.StartLocal:HH:mm}";
    }
}

/// <summary>
/// Métricas de serviços (GitHub, Stripe, Plausible, Shopify, Linear ou qualquer API JSON). Mostra o 1º serviço no Dock;
/// o painel lista todos e permite adicionar. Chaves: criptografadas no PC (DPAPI), nunca no log.
/// </summary>
internal sealed class ServicesWidget : WidgetView
{
    private readonly WidgetContext _ctx;
    private readonly TextBlock _value, _label;
    private static readonly Dictionary<string, (double? Value, DateTime At)> s_values = new();
    private static bool s_loading;

    private static readonly (string Kind, string Name, string Target)[] Kinds =
    [
        ("github", L.T("GitHub · notifications"), ""),
        ("stripe", L.T("Stripe · available balance"), ""),
        ("plausible", L.T("Plausible · current visitors"), L.T("site (e.g. mysite.com)")),
        ("shopify", L.T("Shopify · open orders"), L.T("store (e.g. mystore)")),
        ("linear", L.T("Linear · assigned issues"), ""),
        ("custom", L.T("Other (JSON URL)"), "URL https"),
    ];

    public ServicesWidget(double iconSize, double width, WidgetContext ctx) : base(WidgetKind.Services, iconSize, width)
    {
        _ctx = ctx;
        _value = Text(F(0.34), true);
        _value.FontFeatures();
        _label = Text(F(0.2), false, 0.65);
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _label.MaxWidth = width - 4;
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(_value);
        sp.Children.Add(_label);
        Root.Children.Add(sp);
    }

    private List<ServiceConfig> Services => _ctx.Store.Data.Services;

    public override string Tooltip => !_ctx.Net.Allowed ? L.T("Services: turn on “Allow internet widgets” in settings") :
        Services.Count == 0 ? L.T("Services: click to connect GitHub, Stripe, Plausible, Shopify, Linear or another API") :
        string.Join("\n", Services.Select(s => $"{Title(s)}: {Show(s)}"));

    public override void Update(WidgetData d)
    {
        if (_ctx.Net.Allowed && !s_loading && Services.Any(s => !s_values.TryGetValue(s.Id, out var v) || DateTime.Now - v.At > TimeSpan.FromMinutes(5)))
            _ = LoadAsync();
        var first = Services.FirstOrDefault();
        _value.Text = !_ctx.Net.Allowed ? "off" : first is null ? "+" : Show(first);
        _label.Text = first is null ? L.T("Services") : Title(first);
    }

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at)
    {
        var p = new StackPanel { Spacing = 8, Padding = new Thickness(4), Width = 360 };
        p.Children.Add(new TextBlock { Text = L.T("Connected services"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (!_ctx.Net.Allowed) p.Children.Add(new TextBlock { Text = L.T("Turn on “Allow internet widgets” in settings to fetch the numbers."), TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        var list = new StackPanel { Spacing = 4 };
        void Render()
        {
            list.Children.Clear();
            foreach (var s in Services.ToList())
            {
                var svc = s;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock { Text = $"{Title(svc)}  —  {Show(svc)}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var del = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 } };
                ToolTipService.SetToolTip(del, L.T("Remove (deletes the saved key)"));
                del.Click += (_, _) => { _ctx.Store.Update(x => x.Services.Remove(svc)); _ctx.Secrets.Set("svc:" + svc.Id, null); s_values.Remove(svc.Id); Render(); _ctx.Refresh(); };
                Grid.SetColumn(del, 1);
                g.Children.Add(del);
                list.Children.Add(g);
            }
        }
        Render();
        p.Children.Add(list);

        // ---- adicionar
        var kind = new ComboBox { Header = L.T("Add service"), ItemsSource = Kinds.Select(k => k.Name).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var target = new TextBox { Visibility = Visibility.Collapsed };
        var path = new TextBox { Header = L.T("Path to the number in the JSON"), PlaceholderText = L.T("e.g. data.total"), Visibility = Visibility.Collapsed };
        var header = new TextBox { Header = "Cabeçalho da chave (opcional)", PlaceholderText = "Authorization (padrão: Bearer)", Visibility = Visibility.Collapsed };
        var label = new TextBox { Header = L.T("Name (optional)") };
        var key = new PasswordBox { Header = L.T("API key / token (saved encrypted)") };
        var msg = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 };
        void KindChanged()
        {
            var k = Kinds[Math.Max(0, kind.SelectedIndex)];
            target.Visibility = k.Target.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            target.Header = k.Target;
            bool custom = k.Kind == "custom";
            path.Visibility = header.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            key.Header = custom ? L.T("Key (optional, saved encrypted)") : L.T("API key / token (saved encrypted)");
            msg.Text = Help(k.Kind);
        }
        kind.SelectionChanged += (_, _) => KindChanged();
        KindChanged();
        var add = new Button { Content = L.T("Add") };
        add.Click += (_, _) =>
        {
            var k = Kinds[Math.Max(0, kind.SelectedIndex)];
            string t = target.Text.Trim();
            string? error = Validate(k.Kind, t, path.Text.Trim(), key.Password);
            if (error != null) { msg.Text = error; return; }
            var svc = new ServiceConfig { Kind = k.Kind, Label = Trim(label.Text, 40), Target = t, JsonPath = path.Text.Trim(), Header = Trim(header.Text, 60) };
            if (key.Password.Length > 0) _ctx.Secrets.Set("svc:" + svc.Id, key.Password.Trim());
            _ctx.Store.Update(x => x.Services.Add(svc));
            label.Text = target.Text = path.Text = header.Text = key.Password = "";
            msg.Text = L.T("Added.");
            Render();
            _ctx.Refresh();
        };
        foreach (var c in new UIElement[] { kind, target, path, header, label, key, add, msg }) p.Children.Add(c);
        WidgetActions.ShowFlyout(new ScrollViewer { Content = p, MaxHeight = 560 }, anchor, at);
    }

    private static string Trim(string s, int max) { s = s.Trim(); return s.Length > max ? s[..max] : s; }

    private static string Help(string kind) => kind switch
    {
        "github" => L.T("Classic token with the “notifications” scope (github.com → Settings → Developer settings)."),
        "stripe" => L.T("Use a restricted key with balance read only (rk_live_… with Balance: Read)."),
        "plausible" => L.T("Stats API key (plausible.io → Settings → API keys)."),
        "shopify" => L.T("Custom app token with order read access (Admin → Apps → Develop apps)."),
        "linear" => L.T("Personal key (linear.app → Settings → API)."),
        _ => L.T("Any HTTPS endpoint that returns JSON. Enter the path to the number (e.g. data.0.count)."),
    };

    private static string? Validate(string kind, string target, string path, string key)
    {
        if (kind != "custom" && key.Trim().Length == 0) return L.T("Enter the API key.");
        return kind switch
        {
            "plausible" when !System.Text.RegularExpressions.Regex.IsMatch(target, @"^[A-Za-z0-9.-]{3,120}$") => L.T("Invalid site (e.g. mysite.com)."),
            "shopify" when !System.Text.RegularExpressions.Regex.IsMatch(target, @"^[a-z0-9][a-z0-9-]{1,60}$") => L.T("Invalid store: only the name before .myshopify.com."),
            "custom" when !UrlRules.IsSafeWebUrl(target, httpsOnly: true) => L.T("Use a valid https URL."),
            "custom" when path.Length == 0 => L.T("Enter the path to the number in the JSON (e.g. data.total)."),
            _ => null,
        };
    }

    private static string Title(ServiceConfig s) => s.Label.Length > 0 ? s.Label : s.Kind switch
    {
        "github" => "GitHub",
        "stripe" => "Stripe",
        "plausible" => L.T("Visitors"),
        "shopify" => L.T("Orders"),
        "linear" => "Linear",
        _ => L.T("Metric"),
    };

    private static string Show(ServiceConfig s) =>
        !s_values.TryGetValue(s.Id, out var v) ? "…" : v.Value is not { } n ? L.T("error") : s.Kind == "stripe" ? Compact.Number(n / 100) : Compact.Number(n);

    private async Task LoadAsync()
    {
        s_loading = true;
        try
        {
            foreach (var s in Services.ToList())
            {
                if (s_values.TryGetValue(s.Id, out var old) && DateTime.Now - old.At < TimeSpan.FromMinutes(5)) continue;
                double? value = await FetchAsync(s);
                s_values[s.Id] = (value, DateTime.Now);
            }
        }
        finally
        {
            s_loading = false;
            _ctx.UI.TryEnqueue(() => _ctx.Refresh());
        }
    }

    private async Task<double?> FetchAsync(ServiceConfig s)
    {
        string? key = _ctx.Secrets.Get("svc:" + s.Id);
        KeyValuePair<string, string> H(string name, string value) => new(name, value);
        switch (s.Kind)
        {
            case "github":
                return Num(await _ctx.Net.GetStringAsync("https://api.github.com/notifications", [H("Authorization", "Bearer " + key), H("Accept", "application/vnd.github+json")]), "");
            case "stripe":
                return Num(await _ctx.Net.GetStringAsync("https://api.stripe.com/v1/balance", [H("Authorization", "Bearer " + key)]), "available.0.amount");
            case "plausible":
                return Num(await _ctx.Net.GetStringAsync("https://plausible.io/api/v1/stats/realtime/visitors?site_id=" + Uri.EscapeDataString(s.Target), [H("Authorization", "Bearer " + key)]), "");
            case "shopify":
                return Num(await _ctx.Net.GetStringAsync($"https://{s.Target}.myshopify.com/admin/api/2024-10/orders/count.json?status=open", [H("X-Shopify-Access-Token", key ?? "")]), "count");
            case "linear":
                const string query = "{\"query\":\"{ viewer { assignedIssues(first: 100, filter: { state: { type: { nin: [\\\"completed\\\", \\\"canceled\\\"] } } }) { nodes { id } } } }\"}";
                return Num(await _ctx.Net.PostJsonAsync("https://api.linear.app/graphql", query, [H("Authorization", key ?? "")]), "data.viewer.assignedIssues.nodes");
            default:
                var headers = new List<KeyValuePair<string, string>>();
                if (!string.IsNullOrEmpty(key)) headers.Add(s.Header.Length > 0 ? H(s.Header, key) : H("Authorization", "Bearer " + key));
                return Num(await _ctx.Net.GetStringAsync(s.Target, headers), s.JsonPath);
        }
    }

    private static double? Num(string? json, string path) => json is null ? null : JsonPath.Number(json, path);
}
