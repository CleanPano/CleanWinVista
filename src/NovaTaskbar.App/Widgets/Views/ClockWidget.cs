using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

internal sealed class ClockWidget : WidgetView
{
    private readonly WidgetSettings _cfg;
    private readonly RollingText _time;
    private readonly TextBlock _date;
    private readonly WidgetContext _ctx;
    private DateTime _now;

    public ClockWidget(double iconSize, double width, WidgetSettings cfg, bool reduceMotion, WidgetContext ctx) : base(WidgetKind.Clock, iconSize, width)
    {
        _cfg = cfg;
        _ctx = ctx;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _time = new RollingText(() => { var t = Text(F(0.44), true); t.FontFeatures(); return t; }, reduceMotion);
        _date = Text(F(0.27), false, 0.7);
        stack.Children.Add(_time.Element);
        stack.Children.Add(_date);
        Root.Children.Add(stack);
    }

    public override string Tooltip
    {
        get
        {
            string date = _now.ToString("D", L.Culture);
            var next = NovaTaskbar.Core.Tools.CalendarMath.Expand(_ctx.Store.Data.Events, _now, _now.Date.AddDays(1)).FirstOrDefault(o => o.End > _now);
            return next.Source is null ? date : date + "\n" + L.T("Next:") + $" {(next.AllDay ? L.T("all day") : next.Start.ToString("t", L.Culture))} · {next.Source.Title}";
        }
    }

    public override void Update(WidgetData d)
    {
        _now = d.Now;
        _time.Set(DockCalendar.Time(d.Now, _cfg.Clock24h, _cfg.ClockSeconds));
        _date.Text = ShortDate(d.Now);
        if (Root.XamlRoot != null) CalendarFlyout.CheckReminders(_ctx, Root); // lembretes dos compromissos
    }

    /// <summary>Data curta no idioma do app (ordem e nomes da cultura: "Sat, 21 Nov", "sáb, 21 nov", "11月21日 周六"…).</summary>
    private static string ShortDate(DateTime t) =>
        L.Current.Code is "zh-Hans" or "ja" or "ko"
            ? t.ToString("M/d ddd", L.Culture)
            : t.ToString("ddd d MMM", L.Culture).Replace(".", "");

    public override void OnClick(FrameworkElement anchor, global::Windows.Foundation.Point at) =>
        CalendarFlyout.Show(_ctx, anchor, at, DateOnly.FromDateTime(_now));
}
