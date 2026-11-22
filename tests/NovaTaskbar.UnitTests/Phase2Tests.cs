using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Core.Apps;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Core.Magnification;

namespace NovaTaskbar.UnitTests;

public class WeightedEngineTests
{
    private static readonly MagnificationParameters P = new(48, 6, 1.35, 120, 1.0, -1);

    [Fact]
    public void UniformSpecs_MatchLegacyLayout()
    {
        Span<IconFrame> a = stackalloc IconFrame[5];
        Span<DockItemSpec> specs = stackalloc DockItemSpec[5];
        specs.Fill(DockItemSpec.Icon());
        Assert.Equal(P.BaseWidth(5), MagnificationEngine.BaseWidth(P, specs), 9);
        MagnificationEngine.Compute(P, specs, 0, 0, a);
        for (int i = 0; i < 5; i++) Assert.Equal(P.BaseCenter(i), a[i].Center, 9);
    }

    [Fact]
    public void ZeroPresence_TakesNoSpace()
    {
        Span<DockItemSpec> three = [DockItemSpec.Icon(), DockItemSpec.Icon(0), DockItemSpec.Icon()];
        Span<DockItemSpec> two = [DockItemSpec.Icon(), DockItemSpec.Icon()];
        Assert.Equal(MagnificationEngine.BaseWidth(P, two), MagnificationEngine.BaseWidth(P, three), 9);

        Span<IconFrame> f3 = stackalloc IconFrame[3];
        Span<IconFrame> f2 = stackalloc IconFrame[2];
        MagnificationEngine.Compute(P, three, 0, 0, f3);
        MagnificationEngine.Compute(P, two, 0, 0, f2);
        Assert.Equal(f2[1].Center, f3[2].Center, 9);
        Assert.Equal(0, f3[1].Size, 9);
    }

    [Fact]
    public void PresenceGrowsContinuously()
    {
        Span<IconFrame> f = stackalloc IconFrame[3];
        double prev = -1;
        for (double w = 0; w <= 1.0001; w += 0.05)
        {
            Span<DockItemSpec> s = [DockItemSpec.Icon(), DockItemSpec.Icon(w), DockItemSpec.Icon()];
            var ext = MagnificationEngine.Compute(P, s, 0, 0, f);
            Assert.True(ext.Width > prev);
            prev = ext.Width;
        }
    }

    [Fact]
    public void Separator_DoesNotMagnify_AndIsNarrow()
    {
        Span<DockItemSpec> s = [DockItemSpec.Icon(), DockItemSpec.Separator(0.35), DockItemSpec.Icon()];
        Span<IconFrame> f = stackalloc IconFrame[3];
        MagnificationEngine.Compute(P, s, P.BaseIconSize + 10, 1, f); // cursor sobre o separador
        Assert.Equal(1.0, f[1].Scale, 9);
        Assert.True(f[1].Size < P.BaseIconSize / 2);
        Assert.True(f[0].Scale > 1.0 && f[2].Scale > 1.0);
    }

    [Fact]
    public void HitTest_SkipsVanishedItems()
    {
        Span<DockItemSpec> s = [DockItemSpec.Icon(), DockItemSpec.Icon(0), DockItemSpec.Icon()];
        Span<IconFrame> f = stackalloc IconFrame[3];
        MagnificationEngine.Compute(P, s, 0, 0, f);
        Assert.Equal(2, MagnificationEngine.HitTest(f, 3, f[2].Center, P.Spacing));
    }
}

public class SpringTests
{
    [Fact]
    public void CriticallyDamped_ConvergesWithoutOvershoot()
    {
        var s = new Spring(0, 0.3, 1.0) { Target = 1 };
        double max = 0;
        for (int i = 0; i < 120; i++) { s.Step(1 / 60.0); max = Math.Max(max, s.Value); }
        Assert.True(max <= 1.0005, $"overshoot {max}");
        Assert.True(s.IsSettled);
        Assert.Equal(1, s.Value, 6);
    }

    [Fact]
    public void Underdamped_Overshoots()
    {
        var s = new Spring(0, 0.4, 0.5) { Target = 1 };
        double max = 0;
        for (int i = 0; i < 60; i++) { s.Step(1 / 60.0); max = Math.Max(max, s.Value); }
        Assert.True(max > 1.05);
    }

    [Fact]
    public void Retarget_KeepsValueAndVelocity()
    {
        var s = new Spring(0, 0.3) { Target = 1 };
        for (int i = 0; i < 6; i++) s.Step(1 / 60.0);
        double v = s.Value, vel = s.Velocity;
        s.Target = -1; // interrupção no meio do movimento
        Assert.Equal(v, s.Value);
        Assert.Equal(vel, s.Velocity);
        s.Step(1 / 240.0);
        Assert.True(Math.Abs(s.Value - v) < 0.05); // sem salto
    }

    [Fact]
    public void ZeroResponse_Snaps()
    {
        var s = new Spring(0, 0) { Target = 5 };
        s.Step(1 / 60.0);
        Assert.Equal(5, s.Value);
    }

    [Fact]
    public void SpringAnimator_EnterExitAndReverseMidway()
    {
        var a = new MagnificationAnimator { Curve = EasingCurve.Spring, Duration = 0.18 };
        a.PointerMoved(100);
        for (int i = 0; i < 4; i++) a.Advance(1 / 60.0);
        double mid = a.Intensity;
        Assert.InRange(mid, 0.01, 0.99);
        a.PointerExited(); // reverte no meio
        a.Advance(1 / 240.0);
        Assert.True(Math.Abs(a.Intensity - mid) < 0.05, "intensidade saltou na reversão");
        for (int i = 0; i < 120; i++) a.Advance(1 / 60.0);
        Assert.Equal(0, a.Intensity, 3);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void StrongEaseOut_IsMonotonicWithExactEndpoints()
    {
        Assert.Equal(0, Easing.StrongEaseOut(0), 9);
        Assert.Equal(1, Easing.StrongEaseOut(1), 9);
        double prev = -1;
        for (double t = 0; t <= 1; t += 0.02)
        {
            double v = Easing.StrongEaseOut(t);
            Assert.True(v >= prev - 1e-9);
            prev = v;
        }
        Assert.True(Easing.StrongEaseOut(0.2) > 0.6); // ease-out forte: a maior parte do movimento acontece cedo
    }
}

public class WindowFilterTests
{
    private static WindowInfo W(string title = "Doc", long ex = 0, nint owner = 0, bool visible = true, bool cloaked = false,
        string cls = "Notepad", int pid = 100) =>
        new(1, pid, title, cls, @"C:\x\app.exe", null, 0, ex, owner, visible, cloaked, false);

    [Fact] public void NormalWindow_IsApp() => Assert.True(WindowFilter.IsAppWindow(W(), 1));
    [Fact] public void Hidden_IsNot() => Assert.False(WindowFilter.IsAppWindow(W(visible: false), 1));
    [Fact] public void Cloaked_IsNot() => Assert.False(WindowFilter.IsAppWindow(W(cloaked: true), 1));
    [Fact] public void Untitled_IsNot() => Assert.False(WindowFilter.IsAppWindow(W(title: " "), 1));
    [Fact] public void ToolWindow_IsNot() => Assert.False(WindowFilter.IsAppWindow(W(ex: WindowFilter.WS_EX_TOOLWINDOW), 1));
    [Fact] public void Owned_IsNot() => Assert.False(WindowFilter.IsAppWindow(W(owner: 42), 1));
    [Fact] public void NoActivate_IsNot() => Assert.False(WindowFilter.IsAppWindow(W(ex: WindowFilter.WS_EX_NOACTIVATE), 1));
    [Fact] public void OwnedButAppWindow_Is() => Assert.True(WindowFilter.IsAppWindow(W(owner: 42, ex: WindowFilter.WS_EX_APPWINDOW), 1));
    [Fact] public void OwnProcess_IsNot() => Assert.False(WindowFilter.IsAppWindow(W(pid: 7), 7));
    [Fact] public void ShellWindows_AreNot() => Assert.False(WindowFilter.IsAppWindow(W(cls: "Shell_TrayWnd"), 1));
    [Fact] public void UwpCoreWindow_IsNot() => Assert.False(WindowFilter.IsAppWindow(W(cls: "Windows.UI.Core.CoreWindow"), 1));
}

public class DockItemsBuilderTests
{
    private static (PinnedApp, PinIdentity) Pin(string path, string? exe = null, string? aumid = null)
    {
        var p = new PinnedApp { Path = path, DisplayName = Path.GetFileNameWithoutExtension(path), Id = path.ToLowerInvariant() };
        return (p, new PinIdentity(exe ?? (path.EndsWith(".exe") ? path : null), aumid));
    }

    private static WindowInfo Win(nint h, string? exe, string? aumid = null) =>
        new(h, (int)h, "T" + h, "C", exe, aumid, 0, 0, 0, true, false, false);

    private static AppPresentation Present(WindowInfo w) => new(Path.GetFileNameWithoutExtension(w.ExePath ?? "app"), w.ExePath ?? "");

    [Fact]
    public void WindowsAttachToPinnedByExe_CaseInsensitive()
    {
        var pins = new[] { Pin(@"C:\Windows\explorer.exe") };
        var r = DockItemsBuilder.Build(pins, [Win(1, @"c:\windows\EXPLORER.EXE")], 0, true, [], Present);
        Assert.Single(r.Entries);
        Assert.True(r.Entries[0].IsRunning);
    }

    [Fact]
    public void LnkPin_MatchesByResolvedTarget()
    {
        var pins = new[] { Pin(@"C:\Users\x\Desktop\Code.lnk", exe: @"C:\Code\Code.exe") };
        var r = DockItemsBuilder.Build(pins, [Win(1, @"C:\Code\Code.exe")], 0, true, [], Present);
        Assert.Single(r.Entries);
        Assert.Single(r.Entries[0].Windows);
    }

    [Fact]
    public void PackagedPin_MatchesByAumid()
    {
        const string aumid = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";
        var pins = new[] { Pin(AppIdentity.PackagedPath(aumid), aumid: aumid) };
        var r = DockItemsBuilder.Build(pins, [Win(1, @"C:\Program Files\WindowsApps\Notepad\Notepad.exe", aumid)], 0, true, [], Present);
        Assert.Single(r.Entries);
        Assert.True(r.Entries[0].IsRunning);
    }

    [Fact]
    public void AumidMatchWinsOverExe()
    {
        var pins = new[] { Pin(@"C:\c\chrome.exe"), Pin(AppIdentity.PackagedPath("Chrome._crx_app"), aumid: "Chrome._crx_app") };
        var r = DockItemsBuilder.Build(pins, [Win(1, @"C:\c\chrome.exe", "Chrome._crx_app")], 0, true, [], Present);
        Assert.False(r.Entries[0].IsRunning);
        Assert.True(r.Entries[1].IsRunning);
    }

    [Fact]
    public void UnpinnedRunning_GroupedAfterPins_InStableOrder()
    {
        var pins = new[] { Pin(@"C:\a\a.exe") };
        var wins = new[] { Win(1, @"C:\b\b.exe"), Win(2, @"C:\c\c.exe"), Win(3, @"C:\b\b.exe") };
        var r1 = DockItemsBuilder.Build(pins, wins, 0, true, [], Present);
        Assert.Equal(3, r1.Entries.Count);
        Assert.Equal(2, r1.Entries[1].Windows.Count); // duas janelas de b agrupadas

        // z-order muda (c fica por cima), mas a ordem no Dock é estável
        var r2 = DockItemsBuilder.Build(pins, [wins[1], wins[0]], 0, true, r1.RunningOrder, Present);
        Assert.Equal(r1.Entries[1].Key, r2.Entries[1].Key);
        Assert.Equal(r1.Entries[2].Key, r2.Entries[2].Key);
    }

    [Fact]
    public void HideRunning_KeepsOnlyPins()
    {
        var r = DockItemsBuilder.Build([Pin(@"C:\a\a.exe")], [Win(1, @"C:\b\b.exe")], 0, false, [], Present);
        Assert.Single(r.Entries);
    }

    [Fact]
    public void ForegroundWindow_MarksEntryActive()
    {
        var r = DockItemsBuilder.Build([Pin(@"C:\a\a.exe")], [Win(5, @"C:\a\a.exe"), Win(6, @"C:\b\b.exe")], 5, true, [], Present);
        Assert.True(r.Entries[0].IsActive);
        Assert.False(r.Entries[1].IsActive);
    }

    [Fact]
    public void RunningPackaged_LaunchesViaAppsFolder()
    {
        var w = Win(1, @"C:\Program Files\WindowsApps\X\x.exe", "X_123!App");
        var r = DockItemsBuilder.Build([], [w], 0, true, [], Present);
        Assert.Equal(@"shell:AppsFolder\X_123!App", r.Entries[0].LaunchPath);
    }

    [Fact]
    public void ClosedApp_DropsFromRunningOrder()
    {
        var r1 = DockItemsBuilder.Build([], [Win(1, @"C:\b\b.exe")], 0, true, [], Present);
        var r2 = DockItemsBuilder.Build([], [], 0, true, r1.RunningOrder, Present);
        Assert.Empty(r2.Entries);
        Assert.Empty(r2.RunningOrder);
    }
}

public class PackagedPathValidationTests
{
    [Theory]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData(@"shell:AppsFolder\Microsoft.Windows.Explorer")]
    public void ValidAumids_Accepted(string p) => Assert.True(SettingsValidator.IsAcceptablePath(p, out _));

    [Theory]
    [InlineData(@"shell:AppsFolder\")]
    [InlineData(@"shell:AppsFolder\x y")]
    [InlineData(@"shell:AppsFolder\..\..\cmd")]
    [InlineData(@"shell:AppsFolder\a""b")]
    public void InvalidAumids_Rejected(string p) => Assert.False(SettingsValidator.IsAcceptablePath(p, out _));

    [Fact]
    public void PackagedPin_GetsReadableDefaultName()
    {
        var s = new DockSettings();
        s.PinnedApps.Add(new PinnedApp { Path = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" });
        SettingsValidator.Sanitize(s);
        Assert.Equal("Microsoft.WindowsCalculator", s.PinnedApps[0].DisplayName);
    }

    [Fact]
    public void BehaviorDefaults()
    {
        var s = new DockSettings();
        Assert.True(s.Behavior.MinimizeIfActive);
        Assert.True(s.Behavior.ShowRunningApps);
        Assert.Equal(EasingCurve.EaseOutCubic, s.Magnification.Easing);
    }

    [Fact]
    public void FactoryDefaults_MatchOwnerChoice()
    {
        var s = new DockSettings();
        Assert.Equal((DockMode.Extended, GlassMaterial.Acrylic, ThemePreference.System), (s.Mode, s.Appearance.Material, s.Appearance.Theme));
        Assert.Equal((0.6, 0.04, 12.0, false), (s.Appearance.TintOpacity, s.Appearance.LuminosityOpacity, s.Appearance.CornerRadius, s.Appearance.BorderVisible));
        Assert.Equal((40.0, 6.0, 10.0, 16.0), (s.Layout.IconSize, s.Layout.IconSpacing, s.Layout.Padding, s.Layout.ScreenMargin));
        Assert.Equal((1.1, 170.0, 30, 25, 0.2), (s.Magnification.MaxScale, s.Magnification.Radius, s.Magnification.AnimationDurationMs, s.Magnification.HoverSmoothingMs, s.Magnification.NeighborInfluence));
        Assert.True(s.Behavior.MinimizeIfActive);
        Assert.False(s.Widgets.AllowNetwork); // internet continua opt-in
        Assert.Equal(new[] { WidgetKind.Media, WidgetKind.System, WidgetKind.Network, WidgetKind.Clipboard }, s.Widgets.Items.Take(4).Select(i => i.Kind));
        Assert.Equal(WidgetKind.Clock, s.Widgets.Items.Last(i => i.Enabled).Kind);
        Assert.Equal(Enum.GetValues<WidgetKind>().Length, s.Widgets.Items.Select(i => i.Kind).Distinct().Count());

        Assert.Equal(DockEdge.Bottom, s.Layout.Edge);
        Assert.All(s.Widgets.Items, w => Assert.Equal(WidgetSettings.DefaultEnabled(w.Kind), w.Enabled));

        // "Estendido" e "Restaurar padrão" reproduzem exatamente o padrão de fábrica.
        var c = new DockSettings();
        DockModePresets.Apply(c, DockMode.Floating);
        DockModePresets.Apply(c, DockModePresets.FactoryMode);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(s), System.Text.Json.JsonSerializer.Serialize(c));
    }
}

public class ClickPolicyTests
{
    private static DockEntry Entry(params nint[] windows)
    {
        var e = new DockEntry { Key = "k", DisplayName = "A", IconPath = "", LaunchPath = @"C:\a\a.exe" };
        foreach (var h in windows) e.Windows.Add(new WindowInfo(h, 1, "t", "c", @"C:\a\a.exe", null, 0, 0, 0, true, false, false));
        return e;
    }

    [Fact] public void NotRunning_Launches() => Assert.Equal(ClickActionKind.Launch, ClickPolicy.Decide(Entry(), 0, false).Kind);

    [Fact]
    public void Background_ActivatesMostRecent()
    {
        var a = ClickPolicy.Decide(Entry(10, 11), 99, false);
        Assert.Equal(new ClickAction(ClickActionKind.Activate, 10), a);
    }

    [Fact]
    public void ActiveSingle_MinimizesOnlyWhenConfigured()
    {
        Assert.Equal(ClickActionKind.Minimize, ClickPolicy.Decide(Entry(10), 10, true).Kind);
        Assert.Equal(ClickActionKind.Activate, ClickPolicy.Decide(Entry(10), 10, false).Kind);
    }

    [Fact]
    public void ActiveMulti_CyclesToLeastRecent()
    {
        var a = ClickPolicy.Decide(Entry(10, 11, 12), 10, true);
        Assert.Equal(new ClickAction(ClickActionKind.Activate, 12), a);
    }

    [Fact]
    public void NoLaunchPath_DoesNothing()
    {
        var e = new DockEntry { Key = "k", DisplayName = "A", IconPath = "", LaunchPath = "" };
        Assert.Equal(ClickActionKind.None, ClickPolicy.Decide(e, 0, false).Kind);
    }
}
