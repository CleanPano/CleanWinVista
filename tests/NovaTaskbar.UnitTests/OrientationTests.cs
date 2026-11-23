using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Core.Layout;

namespace NovaTaskbar.UnitTests;

public class OrientationTests
{
    private static readonly RectD Work = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void RoundTrip(DockEdge edge)
    {
        var o = new DockOrientation(edge, 600, 120);
        var (x, y) = o.ToWindow(123, 45);
        var (u, v) = o.FromWindow(x, y);
        Assert.Equal((123.0, 45.0), (u, v));
    }

    [Fact]
    public void Vertical_SwapsWindowSize()
    {
        var o = new DockOrientation(DockEdge.Left, 600, 120);
        Assert.True(o.IsVertical);
        Assert.Equal((120.0, 600.0), (o.WindowWidth, o.WindowHeight));
    }

    [Theory]
    [InlineData(DockEdge.Bottom, 660, 910)]   // y = 1040 - 10 - 120
    [InlineData(DockEdge.Top, 660, 10)]
    [InlineData(DockEdge.Left, 10, 220)]      // centralizado na altura: (1040 - 600) / 2
    [InlineData(DockEdge.Right, 1790, 220)]   // 1920 - 10 - 120
    public void WindowPosition_HugsChosenEdge(DockEdge edge, double x, double y)
    {
        var o = new DockOrientation(edge, 600, 120);
        Assert.Equal((x, y), o.WindowPosition(Work, 10));
    }

    [Fact]
    public void ScreenEdgeSide_MapsToTheEdge()
    {
        // v = Height é o lado colado na borda da tela.
        Assert.Equal(0, new DockOrientation(DockEdge.Top, 600, 120).ToWindow(0, 120).Y);
        Assert.Equal(0, new DockOrientation(DockEdge.Left, 600, 120).ToWindow(0, 120).X);
        Assert.Equal(120, new DockOrientation(DockEdge.Right, 600, 120).ToWindow(0, 120).X);
        Assert.Equal(120, new DockOrientation(DockEdge.Bottom, 600, 120).ToWindow(0, 120).Y);
    }

    [Fact]
    public void Rect_IsNormalized()
    {
        var r = new DockOrientation(DockEdge.Left, 600, 120).ToWindow(new RectD(10, 20, 30, 40));
        Assert.Equal(new RectD(60, 10, 40, 30), r); // x = 120 - (20+40) = 60, y = 10, w = 40, h = 30
    }
}

public class ImageResamplerTests
{
    [Fact]
    public void Uniform_StaysUniform()
    {
        var src = Enumerable.Repeat((byte)200, 7 * 5 * 4).ToArray();
        var dst = NovaTaskbar.Core.Imaging.ImageResampler.Downscale(src, 7, 5, 3, 2);
        Assert.All(dst, b => Assert.Equal(200, b));
    }

    [Fact]
    public void Halving_AveragesBlocks()
    {
        // 2×1 → 1×1: média de 0 e 255 no canal azul.
        var src = new byte[] { 0, 0, 0, 255, 255, 0, 0, 255 };
        var dst = NovaTaskbar.Core.Imaging.ImageResampler.Downscale(src, 2, 1, 1, 1);
        Assert.Equal(new byte[] { 128, 0, 0, 255 }, dst);
    }

    [Fact]
    public void Upscale_Throws() =>
        Assert.Throws<ArgumentException>(() => NovaTaskbar.Core.Imaging.ImageResampler.Downscale(new byte[16], 2, 2, 3, 3));
}

public class VisibilityRuleTests
{
    private static ForegroundApp App(string? exe, string? cls = "Chrome_WidgetWin_1", bool full = false, bool shell = false, bool own = false) => new(exe, cls, full, shell, own);

    [Theory]
    [InlineData("chrome.exe", "Chrome_WidgetWin_1", true)]
    [InlineData("MSEDGE.EXE", "Chrome_WidgetWin_1", true)]
    [InlineData("firefox.exe", "MozillaWindowClass", true)]
    [InlineData("explorer.exe", "CabinetWClass", true)]      // janela do Explorador
    [InlineData("explorer.exe", "Progman", false)]           // área de trabalho (marcada como shell abaixo)
    [InlineData("notepad.exe", "Notepad", false)]
    public void Smart_DodgesBrowsersAndExplorer(string exe, string cls, bool expected) =>
        Assert.Equal(expected, DockVisibilityRules.ShouldDodge(App(exe, cls, shell: cls == "Progman")));

    [Fact]
    public void Fullscreen_IsDodged_ButNotOwnOrShell()
    {
        Assert.True(DockVisibilityRules.ShouldDodge(App("game.exe", "UnityWndClass", full: true)));
        Assert.False(DockVisibilityRules.ShouldDodge(App(null, null, own: true)));
        Assert.False(DockVisibilityRules.ShouldDodge(App("explorer.exe", "WorkerW", full: true, shell: true)));
    }

    [Fact]
    public void Modes()
    {
        var chrome = App("chrome.exe");
        var notepad = App("notepad.exe", "Notepad");
        Assert.False(DockVisibilityRules.ShouldHide(DockVisibility.AlwaysVisible, chrome, false, false));
        Assert.True(DockVisibilityRules.ShouldHide(DockVisibility.Smart, chrome, false, false));
        Assert.False(DockVisibilityRules.ShouldHide(DockVisibility.Smart, notepad, false, false));
        Assert.True(DockVisibilityRules.ShouldHide(DockVisibility.AutoHide, notepad, false, false));
        Assert.False(DockVisibilityRules.ShouldHide(DockVisibility.AutoHide, notepad, pointerNear: true, busy: false));
        Assert.False(DockVisibilityRules.ShouldHide(DockVisibility.Smart, chrome, pointerNear: false, busy: true));
    }

    [Fact]
    public void Fullscreen_HiddenDock_IgnoresPointer()
    {
        var video = new ForegroundApp("vlc.exe", "Qt5QWindowIcon", IsFullscreen: true, IsShell: false, IsOwn: false);
        var normal = new ForegroundApp("notepad.exe", "Notepad", false, false, false);
        Assert.False(DockVisibilityRules.PointerMayReveal(video, currentlyHidden: true));
        Assert.True(DockVisibilityRules.PointerMayReveal(video, currentlyHidden: false));
        Assert.True(DockVisibilityRules.PointerMayReveal(normal, currentlyHidden: true));
    }


    [Fact]
    public void Maximized_IsDodged() =>
        Assert.True(DockVisibilityRules.ShouldDodge(new ForegroundApp("notepad.exe", "Notepad", false, false, false, IsMaximized: true)));

    [Fact]
    public void Default_IsSmart() => Assert.Equal(DockVisibility.Smart, new DockSettings().Behavior.Visibility);
}
