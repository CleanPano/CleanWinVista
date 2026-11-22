using NovaTaskbar.Core.Magnification;

namespace NovaTaskbar.UnitTests;

public class PinEndTests
{
    [Fact]
    public void PinEnd_KeepsTrailingWidgetStill_WhileAppsMagnify()
    {
        var p = new MagnificationParameters(40, 8, 1.6, 120, 1, 200);
        var items = new[]
        {
            DockItemSpec.Icon(), DockItemSpec.Icon(), DockItemSpec.Icon(), DockItemSpec.Icon(),
            new DockItemSpec { Weight = 2, Magnifies = false }, // widget
        };
        var rest = new IconFrame[5];
        var hover = new IconFrame[5];
        var restExt = MagnificationEngine.Compute(p, items, 60, 0, rest, pinEnd: true);
        var hoverExt = MagnificationEngine.Compute(p, items, 60, 1, hover, pinEnd: true);
        Assert.Equal(rest[4].Center, hover[4].Center, 6);
        Assert.Equal(restExt.Right, hoverExt.Right, 6);
        Assert.True(hoverExt.Left < restExt.Left); // a expansão vai para a esquerda
    }
}

public class CalendarMathTests
{
    [Fact]
    public void MonthGrid_StartsOnFirstDayOfWeek()
    {
        var g = NovaTaskbar.Core.Tools.CalendarMath.MonthGrid(2026, 11, DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 11, 1), g[0]); // 1/11/2026 é domingo
        Assert.Equal(42, g.Length);
    }

    [Fact]
    public void Expand_WeeklyAndMonthly()
    {
        var weekly = new NovaTaskbar.Core.Tools.CalEvent { Title = "A", Start = new DateTime(2026, 11, 2, 9, 0, 0), End = new DateTime(2026, 11, 2, 10, 0, 0), Repeat = NovaTaskbar.Core.Tools.Repeat.Weekly };
        var monthly = new NovaTaskbar.Core.Tools.CalEvent { Title = "B", Start = new DateTime(2026, 1, 31), End = new DateTime(2026, 2, 1), AllDay = true, Repeat = NovaTaskbar.Core.Tools.Repeat.Monthly };
        var occ = NovaTaskbar.Core.Tools.CalendarMath.Expand([weekly, monthly], new DateTime(2026, 11, 1), new DateTime(2026, 12, 1));
        Assert.Equal(5, occ.Count(o => o.Source == weekly)); // 2, 9, 16, 23, 30/11
        Assert.Single(occ, o => o.Source == monthly && o.Start.Month == 11); // 30/11 (mês com 30 dias)
    }

    [Fact]
    public void Ics_RoundTrip_HasRrule()
    {
        var e = new NovaTaskbar.Core.Tools.CalEvent { Title = "Reunião, time", Start = new DateTime(2026, 11, 2, 9, 0, 0), End = new DateTime(2026, 11, 2, 10, 0, 0), Repeat = NovaTaskbar.Core.Tools.Repeat.Weekly };
        string ics = NovaTaskbar.Core.Tools.CalendarMath.ToIcs([e]);
        Assert.Contains("RRULE:FREQ=WEEKLY", ics);
        var back = NovaTaskbar.Core.Tools.ICal.Parse(ics);
        Assert.Single(back);
        Assert.Equal("Reunião, time", back[0].Summary);
    }
}
