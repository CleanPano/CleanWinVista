using System.Globalization;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Core.Widgets;

namespace NovaTaskbar.UnitTests;

public class WidgetSettingsTests
{
    [Fact]
    public void Defaults_HaveEveryKindOnce()
    {
        var w = new WidgetSettings();
        Assert.Equal(Enum.GetValues<WidgetKind>().Length, w.Items.Count);
        Assert.Empty(w.Sanitize());
    }

    [Fact]
    public void Sanitize_RemovesDuplicates_AndAddsMissing()
    {
        var w = new WidgetSettings { Items = [new() { Kind = WidgetKind.Clock }, new() { Kind = WidgetKind.Clock }], FocusMinutes = 999 };
        var issues = w.Sanitize();
        Assert.Single(w.Items, i => i.Kind == WidgetKind.Clock);
        Assert.Equal(Enum.GetValues<WidgetKind>().Length, w.Items.Count);
        // Faltantes entram desligados, exceto os contextuais (já se escondem sozinhos quando não há o que mostrar).
        Assert.All(w.Items.Where(i => i.Kind != WidgetKind.Clock), i => Assert.Equal(WidgetSettings.IsContextual(i.Kind) && WidgetSettings.DefaultEnabled(i.Kind), i.Enabled));
        Assert.Equal(180, w.FocusMinutes);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void LegacySettingsFile_GetsWidgets()
    {
        Assert.True(SettingsStore.TryParse("{\"schemaVersion\":1}", out var s, out _));
        Assert.Equal(Enum.GetValues<WidgetKind>().Length, s!.Widgets.Items.Count);
    }

    [Fact]
    public void ClockWidth_GrowsWithSeconds()
    {
        var w = new WidgetSettings();
        double a = w.WidthInSlots(WidgetKind.Clock);
        w.ClockSeconds = true;
        Assert.True(w.WidthInSlots(WidgetKind.Clock) > a);
    }
}

public class SystemMathTests
{
    [Fact]
    public void Cpu_HalfBusy()
    {
        var a = new CpuUsage.Sample(Idle: 1000, Kernel: 2000, User: 1000);
        var b = new CpuUsage.Sample(Idle: 1500, Kernel: 2600, User: 1400); // total 1000, idle 500
        Assert.Equal(50, CpuUsage.Percent(a, b)!.Value, 6);
    }

    [Fact]
    public void Cpu_NoElapsedOrWrap_IsNull()
    {
        var a = new CpuUsage.Sample(10, 20, 30);
        Assert.Null(CpuUsage.Percent(a, a));
        Assert.Null(CpuUsage.Percent(a, new CpuUsage.Sample(5, 20, 30)));
    }

    [Fact]
    public void Rate_HandlesResetAndZeroTime()
    {
        Assert.Equal(1024, Rate.PerSecond(0, 2048, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, Rate.PerSecond(5000, 10, TimeSpan.FromSeconds(1)));
        Assert.Equal(0, Rate.PerSecond(0, 10, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(1536, "1,5 KB/s")]
    [InlineData(5 * 1024 * 1024, "5,0 MB/s")]
    [InlineData(250 * 1024, "250 KB/s")]
    public void Rate_FormatsPtBr(double v, string expected) => Assert.Equal(expected, Rate.Format(v, System.Globalization.CultureInfo.GetCultureInfo("pt-BR")));
}

public class FocusTimerTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 22, 0, 0);

    [Fact]
    public void RunsPausesResumesAndFinishes()
    {
        var f = new FocusTimer(TimeSpan.FromMinutes(25));
        f.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(20), f.Remaining(T0.AddMinutes(5)));
        f.Pause(T0.AddMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(20), f.Remaining(T0.AddMinutes(60))); // pausado não corre
        f.Start(T0.AddMinutes(60));
        Assert.False(f.Tick(T0.AddMinutes(79)));
        Assert.True(f.Tick(T0.AddMinutes(80)));
        Assert.Equal(FocusTimer.State.Finished, f.Current);
        Assert.Equal(TimeSpan.Zero, f.Remaining(T0.AddMinutes(90)));
        Assert.False(f.Tick(T0.AddMinutes(91))); // só avisa uma vez
    }

    [Fact]
    public void Toggle_AndReset()
    {
        var f = new FocusTimer(TimeSpan.FromMinutes(1));
        f.Toggle(T0);
        Assert.Equal(FocusTimer.State.Running, f.Current);
        f.Toggle(T0.AddSeconds(10));
        Assert.Equal(FocusTimer.State.Paused, f.Current);
        f.Reset();
        Assert.Equal(TimeSpan.FromMinutes(1), f.Remaining(T0));
        Assert.Equal(1.0, f.Fraction(T0));
    }

    [Theory]
    [InlineData(25 * 60, "25:00")]
    [InlineData(65, "01:05")]
    [InlineData(3725, "1:02:05")]
    public void Formats(int seconds, string expected) => Assert.Equal(expected, FocusTimer.Format(TimeSpan.FromSeconds(seconds)));
}

public class CalendarTests
{
    [Fact]
    public void WeekStartsOnSunday_ForPtBr()
    {
        var week = DockCalendar.WeekOf(new DateOnly(2026, 11, 20), DayOfWeek.Sunday); // sexta
        Assert.Equal(new DateOnly(2026, 11, 15), week[0]);
        Assert.Equal(new DateOnly(2026, 11, 21), week[6]);
        Assert.Contains(new DateOnly(2026, 11, 20), week);
    }

    [Fact]
    public void WeekStartsOnMonday_WhenRequested()
    {
        var week = DockCalendar.WeekOf(new DateOnly(2026, 11, 22), DayOfWeek.Monday); // domingo
        Assert.Equal(new DateOnly(2026, 11, 16), week[0]);
        Assert.Equal(new DateOnly(2026, 11, 22), week[6]);
    }

    [Fact]
    public void TimeFormats()
    {
        var t = new DateTime(2026, 11, 20, 21, 58, 7);
        Assert.Equal("21:58", DockCalendar.Time(t, true, false));
        Assert.Equal("21:58:07", DockCalendar.Time(t, true, true));
        Assert.Equal("9:58 PM", DockCalendar.Time(t, false, false));
    }
}

public class WideItemTests
{
    private static readonly NovaTaskbar.Core.Magnification.MagnificationParameters P = new(33, 8, 1.31, 53, 1, -1);

    [Fact]
    public void WideWidget_OccupiesItsSlots_AndNeverMagnifies()
    {
        Span<NovaTaskbar.Core.Magnification.DockItemSpec> s = [NovaTaskbar.Core.Magnification.DockItemSpec.Icon(),
            new() { Weight = 1.9, Magnifies = false }, NovaTaskbar.Core.Magnification.DockItemSpec.Icon()];
        Span<NovaTaskbar.Core.Magnification.IconFrame> f = stackalloc NovaTaskbar.Core.Magnification.IconFrame[3];
        NovaTaskbar.Core.Magnification.MagnificationEngine.Compute(P, s, 60, 1, f);
        Assert.Equal(1.0, f[1].Scale);
        Assert.Equal((33 + 8) * 1.9 - 8, f[1].Size, 6);
        // vizinhos mantêm o espaçamento de 8 DIP até a borda do widget
        double gap = (f[1].Center - f[1].Size / 2) - (f[0].Center + f[0].Size / 2);
        Assert.Equal(8, gap, 6);
        Assert.Equal(1, NovaTaskbar.Core.Magnification.MagnificationEngine.HitTest(f, 3, f[1].Center + f[1].Size / 2 - 1, 8));
    }
}

public class WidgetStyleAndHistoryTests
{
    [Fact]
    public void History_KeepsLastNInOrder()
    {
        var h = new History(3);
        foreach (var v in new[] { 1.0, 2, 3, 4, 5 }) h.Add(v);
        Assert.Equal(3, h.Count);
        Assert.Equal(new[] { 3.0, 4, 5 }, Enumerable.Range(0, h.Count).Select(i => h[i]));
        Assert.Equal(5, h.Last);
        Assert.Equal(5, h.Max());
    }

    [Fact]
    public void History_SanitizesNaN() { var h = new History(2); h.Add(double.NaN); Assert.Equal(0, h.Last); }

    [Theory]
    [InlineData(0, 1000, 1000)]
    [InlineData(1300, 1000, 2000)]
    [InlineData(4100, 1000, 5000)]
    [InlineData(7000, 1000, 10000)]
    public void History_NiceMax(double sample, double floor, double expected)
    {
        var h = new History(4); h.Add(sample);
        Assert.Equal(expected, h.NiceMax(floor));
    }

    [Fact]
    public void Sanitize_FixesInvalidStyle()
    {
        var w = new WidgetSettings { Items = [new WidgetConfig { Kind = WidgetKind.Network, Style = WidgetStyle.Gauge }] };
        w.Sanitize();
        Assert.Equal(WidgetStyle.Line, w.StyleOf(WidgetKind.Network));
    }

    [Fact]
    public void Width_DependsOnStyle()
    {
        var w = new WidgetSettings();
        w.Items.First(i => i.Kind == WidgetKind.System).Style = WidgetStyle.Line;
        Assert.True(w.WidthInSlots(WidgetKind.System) > 2);
        w.Items.First(i => i.Kind == WidgetKind.System).Style = WidgetStyle.Ring;
        Assert.Equal(1.9, w.WidthInSlots(WidgetKind.System));
    }
}

public class LegacyWidgetTests
{
    [Fact]
    public void RemovedWidgetKind_IsDropped_WithoutLosingSettings()
    {
        // settings.json de uma versão com o widget "ClaudeUsage" (removido na 0.6.1).
        const string json = """{"mode":"Extended","layout":{"iconSize":44},"widgets":{"items":[{"kind":"Clock","enabled":true},{"kind":"ClaudeUsage","enabled":true}]}}""";
        var s = System.Text.Json.JsonSerializer.Deserialize<DockSettings>(json, SettingsJson.Options)!;
        SettingsValidator.Sanitize(s);
        Assert.Equal(44, s.Layout.IconSize);
        Assert.Equal(Enum.GetValues<WidgetKind>().Length, s.Widgets.Items.Count);
        Assert.DoesNotContain(s.Widgets.Items, i => !Enum.IsDefined(i.Kind));
        Assert.True(s.Widgets.Items.First(i => i.Kind == WidgetKind.Clock).Enabled);
    }
}

public class PeripheralAndKeyboardTests
{
    [Theory]
    [InlineData("Logi M650", null, null, 0x03C2, PeripheralKind.Mouse)]
    [InlineData("K380", null, null, 0x03C1, PeripheralKind.Keyboard)]
    [InlineData("WH-1000XM4", 4, null, null, PeripheralKind.Headphones)]
    [InlineData("Xbox Wireless Controller", null, null, null, PeripheralKind.Gamepad)]
    [InlineData("Galaxy Buds2", null, null, null, PeripheralKind.Headphones)]
    [InlineData("Gizmo", 5, 0x20, null, PeripheralKind.Mouse)]
    [InlineData("Gizmo", null, null, null, PeripheralKind.Other)]
    public void Classify(string name, int? major, int? minor, int? appearance, PeripheralKind expected) =>
        Assert.Equal(expected, Peripherals.Classify(name, major, minor, appearance));

    [Fact]
    public void Normalize_DedupesAndSortsLowestFirst()
    {
        var list = Peripherals.Normalize([
            new("Mouse", 80, PeripheralKind.Mouse), new("Fone", 15, PeripheralKind.Headphones),
            new("mouse", 70, PeripheralKind.Mouse), new("", 50, PeripheralKind.Other), new("X", 150, PeripheralKind.Other)]);
        Assert.Equal(["Fone", "Mouse"], list.Select(p => p.Name));
        Assert.Equal(70, list[1].Percent);
    }

    [Theory]
    [InlineData(10, 2)]
    [InlineData(30, 1)]
    [InlineData(90, 0)]
    public void Severity(int pct, int sev) => Assert.Equal(sev, Peripherals.Severity(pct));

    [Fact]
    public void Keyboard_CodesAndCycle()
    {
        var all = KeyboardLanguages.Describe([(IntPtr)0x04160416, (IntPtr)0x04090409, unchecked((IntPtr)(long)0xF0020409)]);
        Assert.Equal(["PT", "EN", "EN2"], all.Select(k => k.Code));
        Assert.Equal("EN", KeyboardLanguages.Next(all, (IntPtr)0x04160416)!.Value.Code);
        Assert.Equal("PT", KeyboardLanguages.Next(all, unchecked((IntPtr)(long)0xF0020409))!.Value.Code);
        Assert.Equal("EN2", KeyboardLanguages.Next(all, (IntPtr)0x04160416, -1)!.Value.Code);
    }

    [Fact]
    public void NewKinds_AreContextual_AndAddedEnabledForOldSettings()
    {
        Assert.True(WidgetSettings.IsContextual(WidgetKind.Keyboard));
        Assert.True(WidgetSettings.IsContextual(WidgetKind.Devices));
        Assert.True(WidgetSettings.IsContextual(WidgetKind.Battery));
        var w = new WidgetSettings { Items = [new WidgetConfig { Kind = WidgetKind.Clock }] };
        w.Sanitize();
        Assert.True(w.Items.First(i => i.Kind == WidgetKind.Keyboard).Enabled);
        Assert.Equal(1.1, WidgetSettings.DevicesWidth(1), 3);
        Assert.Equal(WidgetSettings.DevicesWidth(3), WidgetSettings.DevicesWidth(9));
    }
}
