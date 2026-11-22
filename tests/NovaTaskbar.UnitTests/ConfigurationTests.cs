using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Core.Layout;

namespace NovaTaskbar.UnitTests;

public class SettingsValidatorTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var s = new DockSettings();
        Assert.Empty(SettingsValidator.Sanitize(s));
    }

    [Fact]
    public void OutOfRangeValues_AreClamped()
    {
        var s = new DockSettings();
        s.Layout.IconSize = 9999;
        s.Magnification.MaxScale = -3;
        s.Appearance.TintOpacity = double.NaN;
        var issues = SettingsValidator.Sanitize(s);
        Assert.Equal(128, s.Layout.IconSize);
        Assert.Equal(1.0, s.Magnification.MaxScale);
        Assert.Equal(0.6, s.Appearance.TintOpacity);
        Assert.Equal(3, issues.Count);
    }

    [Theory]
    [InlineData(@"\\server\share\evil.exe")]
    [InlineData(@"relative\app.exe")]
    [InlineData(@"C:\Windows\System32\cmd.bat")]
    [InlineData(@"C:\x\script.ps1")]
    [InlineData("https://example.com/a.exe")]
    [InlineData(@"C:\app.exe"" /c del")]
    [InlineData("")]
    public void UnsafePaths_AreRejected(string path)
        => Assert.False(SettingsValidator.IsAcceptablePath(path, out _));

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Users\x\Desktop\App.lnk")]
    [InlineData(@"D:\Projects")]
    public void LocalPaths_AreAccepted(string path)
        => Assert.True(SettingsValidator.IsAcceptablePath(path, out _));

    [Fact]
    public void DuplicatePins_AreRemoved_CaseInsensitive()
    {
        var s = new DockSettings();
        s.PinnedApps.Add(new PinnedApp { Path = @"C:\Windows\explorer.exe" });
        s.PinnedApps.Add(new PinnedApp { Path = @"c:\windows\EXPLORER.exe" });
        SettingsValidator.Sanitize(s);
        Assert.Single(s.PinnedApps);
        Assert.Equal("explorer", s.PinnedApps[0].DisplayName);
    }

    [Fact]
    public void InvalidTint_FallsBackToAutomatic()
    {
        var s = new DockSettings();
        s.Appearance.TintColor = "#GG0000";
        SettingsValidator.Sanitize(s);
        Assert.Equal("", s.Appearance.TintColor);
    }

    [Fact]
    public void ModePresets_ChangeLayout()
    {
        var s = new DockSettings();
        DockModePresets.Apply(s, DockMode.Floating);
        Assert.Equal(DockMode.Floating, s.Mode);
        Assert.True(s.Layout.IconSize < 48);
        Assert.Empty(SettingsValidator.Sanitize(s));
    }
}

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nova-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void MissingFile_CreatesDefaults()
    {
        var r = new SettingsStore(FilePath).Load();
        Assert.True(r.CreatedNew);
        Assert.Equal(DockModePresets.FactoryMode, r.Settings.Mode);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(FilePath);
        var s = new DockSettings();
        s.Appearance.Material = GlassMaterial.Mica;
        s.Layout.IconSize = 56;
        s.PinnedApps.Add(new PinnedApp { Path = @"C:\Windows\explorer.exe", DisplayName = "Explorador" });
        store.Save(s);

        var r = store.Load();
        Assert.False(r.RecoveredFromCorruption);
        Assert.Equal(GlassMaterial.Mica, r.Settings.Appearance.Material);
        Assert.Equal(56, r.Settings.Layout.IconSize);
        Assert.Equal("Explorador", r.Settings.PinnedApps[0].DisplayName);
    }

    [Fact]
    public void SecondSave_KeepsBackup()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new DockSettings());
        store.Save(new DockSettings());
        Assert.True(File.Exists(store.BackupPath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void CorruptFile_RecoversFromBackup()
    {
        var store = new SettingsStore(FilePath);
        var good = new DockSettings(); good.Layout.IconSize = 60;
        store.Save(good);
        store.Save(good); // cria .bak com a versão boa
        File.WriteAllText(FilePath, "{ isto não é json");

        var r = store.Load();
        Assert.True(r.RecoveredFromCorruption);
        Assert.Equal(60, r.Settings.Layout.IconSize);
        Assert.Contains(Directory.GetFiles(_dir), f => f.Contains(".corrupt-"));
    }

    [Fact]
    public void CorruptFile_WithoutBackup_FallsBackToDefaults()
    {
        File.WriteAllText(FilePath, "[1,2,3]");
        var r = new SettingsStore(FilePath).Load();
        Assert.True(r.RecoveredFromCorruption);
        Assert.Equal(40, r.Settings.Layout.IconSize);
    }

    [Fact]
    public void FutureSchema_IsRejected()
    {
        Assert.False(SettingsStore.TryParse("{\"schemaVersion\": 99}", out _, out var issues));
        Assert.Contains(issues, i => i.Contains("99"));
    }

    [Fact]
    public void LegacySchema_IsMigrated()
    {
        Assert.True(SettingsStore.TryParse("{\"layout\": {\"iconSize\": 40}}", out var s, out var issues));
        Assert.Equal(DockSettings.CurrentSchemaVersion, s!.SchemaVersion);
        Assert.Equal(40, s.Layout.IconSize);
        Assert.Contains(issues, i => i.Contains("migrado"));
    }

    [Fact]
    public void Enums_AreSerializedAsStrings()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new DockSettings());
        string json = File.ReadAllText(FilePath);
        Assert.Contains("\"Acrylic\"", json);
        Assert.Contains("\"Extended\"", json);
    }
}

public class DockGeometryTests
{
    private static DockGeometry Make(int count = 6) =>
        new(new DockSettings().ToMagnificationParameters(), count, 8);

    [Fact]
    public void Window_FitsMaximumExpansion()
    {
        var g = Make(8);
        Span<NovaTaskbar.Core.Magnification.IconFrame> f = stackalloc NovaTaskbar.Core.Magnification.IconFrame[8];
        double w = g.Parameters.BaseWidth(8);
        for (double focus = 0; focus <= w; focus += 2)
        {
            var ext = NovaTaskbar.Core.Magnification.MagnificationEngine.Compute(g.Parameters, 8, focus, 1, f);
            var pill = g.PillRect(ext);
            Assert.True(pill.X >= 0, $"pill exits left at {focus}");
            Assert.True(pill.Right <= g.WindowWidth, $"pill exits right at {focus}");
        }
    }

    [Fact]
    public void Headroom_FitsMagnifiedIcon()
    {
        var g = Make();
        double magnifiedTop = g.IconBaseline - g.Parameters.BaseIconSize * g.Parameters.MaxScale;
        Assert.True(magnifiedTop >= 0);
    }

    [Fact]
    public void Position_IsBottomCenteredInWorkArea()
    {
        var g = Make();
        var wa = new RectD(0, 0, 1707, 912);
        var (x, y) = g.WindowPosition(wa, 10);
        Assert.Equal(Math.Round((1707 - g.WindowWidth) / 2), x);
        Assert.Equal(912 - 10, y + g.PillTop + g.PillHeight, 0);
    }

    [Fact]
    public void EmptyDock_StillHasPill()
    {
        var g = Make(0);
        Assert.True(g.RestPillRect.Width > 0);
        Assert.Equal(0, g.MaxExpansion);
    }

    [Fact]
    public void RestPill_IsCentered()
    {
        var g = Make(5);
        var p = g.RestPillRect;
        Assert.Equal(g.WindowWidth / 2, p.X + p.Width / 2, 6);
    }
}
