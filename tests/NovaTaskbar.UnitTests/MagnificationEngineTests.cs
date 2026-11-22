using NovaTaskbar.Core.Magnification;

namespace NovaTaskbar.UnitTests;

public class MagnificationEngineTests
{
    private static readonly MagnificationParameters Default = new(48, 6, 1.35, 120, 1.0, -1);

    [Fact]
    public void ScaleAt_Focus_IsMaxScale()
        => Assert.Equal(1.35, MagnificationEngine.ScaleAt(0, Default), 6);

    [Theory]
    [InlineData(120)]
    [InlineData(500)]
    [InlineData(-121)]
    public void ScaleAt_OutsideRadius_IsOne(double d)
        => Assert.Equal(1.0, MagnificationEngine.ScaleAt(d, Default), 9);

    [Fact]
    public void ScaleAt_IsMonotonicDecreasingWithDistance()
    {
        double prev = double.MaxValue;
        for (double d = 0; d <= 130; d += 2)
        {
            double s = MagnificationEngine.ScaleAt(d, Default);
            Assert.True(s <= prev + 1e-12, $"not monotonic at {d}");
            prev = s;
        }
    }

    [Fact]
    public void ScaleAt_IsSymmetric()
        => Assert.Equal(MagnificationEngine.ScaleAt(37, Default), MagnificationEngine.ScaleAt(-37, Default), 12);

    [Fact]
    public void LowerNeighborInfluence_NarrowsEffect()
    {
        var narrow = Default with { NeighborInfluence = 0.4 };
        Assert.True(MagnificationEngine.ScaleAt(60, narrow) < MagnificationEngine.ScaleAt(60, Default));
        Assert.Equal(1.35, MagnificationEngine.ScaleAt(0, narrow), 6);
    }

    [Fact]
    public void Compute_ZeroIntensity_IsRestLayout()
    {
        Span<IconFrame> f = stackalloc IconFrame[5];
        var ext = MagnificationEngine.Compute(Default, 5, 100, 0, f);
        Assert.Equal(0, ext.Left, 9);
        Assert.Equal(Default.BaseWidth(5), ext.Right, 9);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(1.0, f[i].Scale, 9);
            Assert.Equal(Default.BaseCenter(i), f[i].Center, 9);
        }
    }

    [Fact]
    public void Compute_IconsNeverOverlap()
    {
        Span<IconFrame> f = stackalloc IconFrame[8];
        double w = Default.BaseWidth(8);
        for (double focus = -50; focus <= w + 50; focus += 3.3)
        {
            MagnificationEngine.Compute(Default, 8, focus, 1, f);
            for (int i = 1; i < 8; i++)
            {
                double gap = (f[i].Center - f[i].Size / 2) - (f[i - 1].Center + f[i - 1].Size / 2);
                Assert.Equal(Default.Spacing, gap, 6);
            }
        }
    }

    [Fact]
    public void Compute_IsContinuousInFocus()
    {
        // Pequenos movimentos do cursor nunca causam saltos grandes de posição (sem "layout jumps").
        Span<IconFrame> a = stackalloc IconFrame[6];
        Span<IconFrame> b = stackalloc IconFrame[6];
        double w = Default.BaseWidth(6);
        for (double focus = -20; focus <= w + 20; focus += 1)
        {
            MagnificationEngine.Compute(Default, 6, focus, 1, a);
            MagnificationEngine.Compute(Default, 6, focus + 0.5, 1, b);
            for (int i = 0; i < 6; i++)
            {
                Assert.True(Math.Abs(a[i].Center - b[i].Center) < 2.0, $"jump at focus {focus}, icon {i}");
                Assert.True(Math.Abs(a[i].Scale - b[i].Scale) < 0.02);
            }
        }
    }

    [Fact]
    public void Compute_HoveredIconStaysNearCursor()
    {
        Span<IconFrame> f = stackalloc IconFrame[7];
        for (int i = 0; i < 7; i++)
        {
            double focus = Default.BaseCenter(i);
            MagnificationEngine.Compute(Default, 7, focus, 1, f);
            Assert.True(Math.Abs(f[i].Center - focus) < Default.BaseIconSize / 2, $"icon {i} drifted from cursor");
            Assert.Equal(i, MagnificationEngine.HitTest(f, 7, focus, Default.Spacing));
        }
    }

    [Fact]
    public void Compute_RespectsMaxExpansion()
    {
        var p = Default with { MaxExpansion = 20 };
        Span<IconFrame> f = stackalloc IconFrame[6];
        var ext = MagnificationEngine.Compute(p, 6, p.BaseWidth(6) / 2, 1, f);
        Assert.True(ext.Width - p.BaseWidth(6) <= 20 + 1e-9);
    }

    [Fact]
    public void Compute_MaxScaleOne_DisablesMagnification()
    {
        var p = Default with { MaxScale = 1.0 };
        Span<IconFrame> f = stackalloc IconFrame[3];
        MagnificationEngine.Compute(p, 3, 50, 1, f);
        foreach (var fr in f) Assert.Equal(1.0, fr.Scale, 12);
    }

    [Fact]
    public void Compute_ThrowsWhenSpanTooSmall()
    {
        var arr = new IconFrame[2];
        Assert.Throws<ArgumentException>(() => MagnificationEngine.Compute(Default, 3, 0, 1, arr));
    }

    [Fact]
    public void Compute_EmptyDock_ReturnsZeroExtent()
    {
        var ext = MagnificationEngine.Compute(Default, 0, 0, 1, Span<IconFrame>.Empty);
        Assert.Equal(0, ext.Width);
    }

    [Theory]
    [InlineData(1.0)]   // 100% DPI
    [InlineData(1.5)]   // 150%
    [InlineData(2.0)]   // 200%
    public void Compute_IsResolutionIndependent(double dpiScale)
    {
        // O motor trabalha em DIP; multiplicar todas as medidas pela escala multiplica o resultado igualmente.
        var physical = new MagnificationParameters(48 * dpiScale, 6 * dpiScale, 1.35, 120 * dpiScale, 1, -1);
        Span<IconFrame> a = stackalloc IconFrame[5];
        Span<IconFrame> b = stackalloc IconFrame[5];
        MagnificationEngine.Compute(Default, 5, 70, 1, a);
        MagnificationEngine.Compute(physical, 5, 70 * dpiScale, 1, b);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(a[i].Scale, b[i].Scale, 9);
            Assert.Equal(a[i].Center * dpiScale, b[i].Center, 6);
        }
    }
}

public class MagnificationAnimatorTests
{
    [Fact]
    public void EnterThenExit_ReachesFullThenRest()
    {
        var a = new MagnificationAnimator { Duration = 0.2 };
        a.PointerMoved(100);
        for (int i = 0; i < 30; i++) a.Advance(1 / 60.0);
        Assert.Equal(1.0, a.Intensity, 6);
        Assert.Equal(100, a.Focus, 1);

        a.PointerExited();
        for (int i = 0; i < 30; i++) a.Advance(1 / 60.0);
        Assert.Equal(0.0, a.Intensity, 6);
        Assert.False(a.IsAnimating);
    }

    [Fact]
    public void FirstEntry_SnapsFocusWithoutJump()
    {
        var a = new MagnificationAnimator();
        a.PointerMoved(250);
        Assert.Equal(250, a.Focus);
    }

    [Fact]
    public void RapidMovement_FollowsSmoothly()
    {
        var a = new MagnificationAnimator { FollowTimeConstant = 0.035 };
        a.PointerMoved(0);
        a.Advance(1 / 60.0);
        a.PointerMoved(400); // salto brusco do mouse
        a.Advance(1 / 60.0);
        Assert.InRange(a.Focus, 1, 399); // não teleporta
        for (int i = 0; i < 60; i++) a.Advance(1 / 60.0);
        Assert.Equal(400, a.Focus, 0);
    }

    [Fact]
    public void LongFrameGap_IsClamped()
    {
        var a = new MagnificationAnimator { Duration = 1.0 };
        a.PointerMoved(10);
        a.Advance(5.0); // UI travou 5 s
        Assert.True(a.Intensity < 1.0);
    }

    [Fact]
    public void Idle_IsNotAnimating()
    {
        var a = new MagnificationAnimator();
        Assert.False(a.IsAnimating);
        Assert.False(a.Advance(1 / 60.0));
    }

    [Theory]
    [InlineData(EasingCurve.Linear)]
    [InlineData(EasingCurve.EaseOutCubic)]
    [InlineData(EasingCurve.EaseInOutSine)]
    [InlineData(EasingCurve.EaseOutQuint)]
    public void Easing_EndpointsAreExact(EasingCurve c)
    {
        Assert.Equal(0, Easing.Apply(c, 0), 12);
        Assert.Equal(1, Easing.Apply(c, 1), 12);
    }
}
