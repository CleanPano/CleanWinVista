using NovaTaskbar.Core.Tools;
using NovaTaskbar.Core.Widgets;

namespace NovaTaskbar.UnitTests;

public class ToolsTests
{
    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2^3^2", 512)]
    [InlineData("-3+5", 2)]
    [InlineData("10/4", 2.5)]
    [InlineData("1,5+1.5", 3)]
    [InlineData("1.234,5*2", 2469)]
    [InlineData("200*10%", 20)]
    [InlineData("50%", 0.5)]
    [InlineData("2×3÷4", 1.5)]
    [InlineData("sin(30)", 0.5)]
    [InlineData("cos 60", 0.5)]
    [InlineData("√9+2", 5)]
    [InlineData("2π", 6.283185307)]
    [InlineData("3(1+2)", 9)]
    [InlineData("5!", 120)]
    [InlineData("log(1000)", 3)]
    [InlineData("ln(e)", 1)]
    [InlineData("2^10", 1024)]
    [InlineData("sin(180)", 0)]
    [InlineData("exp(0)", 1)]
    public void Calculator_Evaluates(string expr, double expected)
    {
        Assert.True(Calculator.TryEvaluate(expr, out var r));
        Assert.Equal(expected, r, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1/0")]
    [InlineData("2+")]
    [InlineData("(1")]
    [InlineData("abc")]
    [InlineData("√-4")]
    [InlineData("tan(90)")]
    [InlineData("(-1)!")]
    public void Calculator_RejectsInvalid(string expr) => Assert.False(Calculator.TryEvaluate(expr, out _));

    [Fact]
    public void Units_Convert()
    {
        Assert.Equal(100, UnitConverter.Convert("length", "m", "cm", 1), 6);
        Assert.Equal(212, UnitConverter.Convert("temp", "c", "f", 100), 6);
        Assert.Equal(0, UnitConverter.Convert("temp", "k", "c", 273.15), 6);
        Assert.Equal(2.20462, UnitConverter.Convert("mass", "kg", "lb", 1), 4);
    }

    [Fact]
    public void ICal_ParsesAndFiltersUpcoming()
    {
        const string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20261122T150000\r\nDTEND:20261122T160000\r\nSUMMARY:Reunião\\, time\r\nLOCATION:Sala 1\r\nEND:VEVENT\r\n" +
                           "BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20261120\r\nSUMMARY:Passado\r\nEND:VEVENT\r\n" +
                           "BEGIN:VEVENT\r\nDTSTART:20261123T090000\r\nSUMMARY:Linha muito\r\n  longa\r\nEND:VEVENT\r\nEND:VCALENDAR";
        var all = ICal.Parse(ics);
        Assert.Equal(3, all.Count);
        var next = ICal.Upcoming(all, new DateTime(2026, 11, 22, 10, 0, 0));
        Assert.Equal(["Reunião, time", "Linha muito longa"], next.Select(e => e.Summary));
        Assert.Equal("Sala 1", next[0].Location);
        Assert.True(all.First(e => e.Summary == "Passado").AllDay);
    }

    [Fact]
    public void Progress_Fractions()
    {
        var noon = new DateTime(2026, 11, 21, 12, 0, 0);
        Assert.Equal(0.5, TimeProgress.Fraction(ProgressSpan.Day, noon), 6);
        Assert.InRange(TimeProgress.Fraction(ProgressSpan.Year, noon), 0.88, 0.90);
        Assert.Equal(0, TimeProgress.Fraction(ProgressSpan.Week, new DateTime(2026, 11, 16)), 6); // segunda 00:00
    }

    [Theory]
    [InlineData("https://exemplo.com", true)]
    [InlineData("http://x.io/a", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///c:/x", false)]
    [InlineData("https://user:pw@x.com", false)]
    public void Urls(string url, bool ok) => Assert.Equal(ok, UrlRules.IsSafeWebUrl(url));

    [Fact]
    public void Json_PathNumbers()
    {
        const string json = """{"data":[{"total":"12.5"},{"total":3}],"count":7,"items":[1,2,3]}""";
        Assert.Equal(12.5, JsonPath.Number(json, "data.0.total"));
        Assert.Equal(3, JsonPath.Number(json, "data.1.total"));
        Assert.Equal(7, JsonPath.Number(json, "count"));
        Assert.Equal(3, JsonPath.Number(json, "items"));
        Assert.Null(JsonPath.Number(json, "nope"));
        Assert.Null(JsonPath.Number("not json", "a"));
    }

    [Fact]
    public void Reminder_Due()
    {
        var t = new DateTime(2026, 11, 21, 10, 0, 0);
        Assert.False(Reminder.IsDue(t, 60, t.AddMinutes(59)));
        Assert.True(Reminder.IsDue(t, 60, t.AddMinutes(60)));
        Assert.Equal(TimeSpan.FromMinutes(15), Reminder.Remaining(t, 60, t.AddMinutes(45)));
    }

    [Fact]
    public void NewToolWidgets_AreOffByDefault_AndNetworkFlagged()
    {
        Assert.True(WidgetSettings.DefaultEnabled(WidgetKind.Clock));
        Assert.True(WidgetSettings.DefaultEnabled(WidgetKind.Notes));
        Assert.False(WidgetSettings.DefaultEnabled(WidgetKind.Todos));
        Assert.True(WidgetSettings.NeedsNetwork(WidgetKind.Weather));
        Assert.False(new WidgetSettings().AllowNetwork);
    }

    [Fact]
    public void Calculator_Radians()
    {
        Assert.True(Calculator.TryEvaluate("sin(π/2)", AngleMode.Radians, out var r));
        Assert.Equal(1, r, 9);
    }

    [Theory]
    [InlineData("255", 10, "FF", 16)]
    [InlineData("ff", 16, "255", 10)]
    [InlineData("1010", 2, "12", 8)]
    [InlineData("10", 10, "1010", 2)]
    public void NumberBases_Convert(string input, int from, string expected, int to)
    {
        Assert.True(NumberBases.TryParse(input, from, out var v));
        Assert.Equal(expected, NumberBases.Format(v, to));
    }

    [Fact]
    public void DateCalc_Difference()
    {
        var s = DateCalc.Difference(new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 1));
        Assert.Equal(29, s.TotalDays);
        Assert.Equal(1, s.Months);
        var w = DateCalc.Difference(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 23)); // seg → seg
        Assert.Equal(5, w.Weekdays);
    }
}
