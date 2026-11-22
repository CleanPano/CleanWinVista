using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using NovaTaskbar.Core.Apps;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Windows.Apps;
using NovaTaskbar.Windows.Interop;
using Xunit.Abstractions;

namespace NovaTaskbar.IntegrationTests;

/// <summary>Cria janelas Win32 reais neste processo para validar a coleta e o filtro.</summary>
public sealed class TestWindow : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);

    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000;

    public TestWindow(string title, uint exStyle = 0, IntPtr owner = default, bool visible = true)
    {
        Handle = CreateWindowEx(exStyle, "STATIC", title, WS_OVERLAPPEDWINDOW | (visible ? WS_VISIBLE : 0),
            -32000, -32000, 200, 100, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, Handle);
    }

    public IntPtr Handle { get; }
    public void Dispose() => DestroyWindow(Handle);
}

public class WindowEnumerationTests(ITestOutputHelper output)
{
    private const int FakeOwnPid = -1; // as janelas de teste são deste processo; o filtro real exclui o próprio PID

    [Fact]
    public void Describe_ReadsRealWindowData()
    {
        using var w = new TestWindow("Nova teste janela");
        var info = new WindowEnumerator().Describe(w.Handle);
        Assert.NotNull(info);
        Assert.Equal("Nova teste janela", info!.Title);
        Assert.Equal("Static", info.ClassName, ignoreCase: true);
        Assert.Equal(Environment.ProcessId, info.ProcessId);
        Assert.False(string.IsNullOrEmpty(info.ExePath));
        Assert.True(File.Exists(info.ExePath));
        Assert.True(info.IsVisible);
        Assert.True(WindowFilter.IsAppWindow(info, FakeOwnPid));
    }

    [Fact]
    public void ToolWindow_IsFiltered()
    {
        using var w = new TestWindow("ferramenta", (uint)WindowFilter.WS_EX_TOOLWINDOW);
        var info = new WindowEnumerator().Describe(w.Handle)!;
        Assert.False(WindowFilter.IsAppWindow(info, FakeOwnPid));
    }

    [Fact]
    public void OwnedWindow_IsFiltered_UnlessAppWindow()
    {
        using var owner = new TestWindow("dono");
        using var owned = new TestWindow("filha", 0, owner.Handle);
        using var ownedApp = new TestWindow("filha app", (uint)WindowFilter.WS_EX_APPWINDOW, owner.Handle);
        var e = new WindowEnumerator();
        Assert.False(WindowFilter.IsAppWindow(e.Describe(owned.Handle)!, FakeOwnPid));
        Assert.True(WindowFilter.IsAppWindow(e.Describe(ownedApp.Handle)!, FakeOwnPid));
    }

    [Fact]
    public void HiddenWindow_IsFiltered()
    {
        using var w = new TestWindow("oculta", visible: false);
        Assert.False(WindowFilter.IsAppWindow(new WindowEnumerator().Describe(w.Handle)!, FakeOwnPid));
    }

    [Fact]
    public void Snapshot_ExcludesOwnProcess_AndIsFast()
    {
        using var w = new TestWindow("não deve aparecer");
        var e = new WindowEnumerator();
        e.Snapshot(); // aquece caches
        var sw = Stopwatch.StartNew();
        const int runs = 20;
        List<WindowInfo> list = new();
        for (int i = 0; i < runs; i++) list = e.Snapshot();
        double ms = sw.Elapsed.TotalMilliseconds / runs;
        output.WriteLine($"PERF Snapshot: {ms:0.00} ms/enumeração, {list.Count} janelas de app");
        foreach (var x in list) output.WriteLine($"  {x.Title} | {Path.GetFileName(x.ExePath)} | {x.Aumid}");
        Assert.DoesNotContain(list, x => x.ProcessId == Environment.ProcessId);
        Assert.True(ms < 50, $"enumeração lenta: {ms} ms");
    }

    [Fact]
    public void MinimizeRestore_OnRealWindow()
    {
        using var w = new TestWindow("minimizar");
        Assert.True(WindowActions.Minimize(w.Handle));
        Assert.True(NativeMethods.IsIconic(w.Handle));
        WindowActions.Activate(w.Handle); // foco pode ser negado ao processo de teste; restaurar não
        Assert.False(NativeMethods.IsIconic(w.Handle));
    }

    [Fact]
    public void Actions_OnInvalidHandle_ReturnFalse()
    {
        Assert.False(WindowActions.Activate(0x7FFF0001));
        Assert.False(WindowActions.Minimize(0x7FFF0001));
        Assert.False(WindowActions.Close(0x7FFF0001));
    }
}

public class ShellIntegrationTests(ITestOutputHelper output)
{
    private static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    [Fact]
    public void IconExtraction_ReturnsHighResPremultipliedPixels()
    {
        var px = ShellIcons.Extract(Explorer, 96);
        Assert.NotNull(px);
        Assert.True(px!.Width >= 96 && px.Height >= 96, $"{px.Width}x{px.Height}");
        for (int i = 0; i < px.Bgra.Length; i += 4)
        {
            byte a = px.Bgra[i + 3];
            Assert.True(px.Bgra[i] <= a && px.Bgra[i + 1] <= a && px.Bgra[i + 2] <= a, "pixel não pré-multiplicado");
        }
    }

    [Fact]
    public void ShortcutResolution_ReadsTarget()
    {
        string lnk = Path.Combine(Path.GetTempPath(), $"nova-test-{Guid.NewGuid():N}.lnk");
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = Explorer;
            sc.Save();

            var info = ShellLink.Read(lnk);
            Assert.NotNull(info);
            Assert.True(AppIdentity.SamePath(Explorer, info!.TargetPath), info.TargetPath);

            var id = new AppInfoProvider().Resolve(new PinnedApp { Path = lnk, Id = lnk.ToLowerInvariant() });
            Assert.True(AppIdentity.SamePath(Explorer, id.ExePath));
        }
        finally { try { File.Delete(lnk); } catch (IOException) { } }
    }

    [Fact]
    public void FriendlyName_FromVersionInfo()
    {
        string name = AppInfoProvider.FriendlyName(Explorer);
        output.WriteLine($"explorer.exe → '{name}'");
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void PackagedApp_NameAndIcon_ViaAppsFolder()
    {
        // Calculadora vem com o Windows 11; se o usuário a desinstalou, o teste informa e não falha.
        const string calc = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        string? name = AppInfoProvider.ShellItemName(calc);
        output.WriteLine($"Calculadora → '{name}'");
        if (name is null) return;
        Assert.NotNull(ShellIcons.Extract(calc, 64));
    }

    [Fact]
    public void Launcher_RejectsUnsafeAndMissing()
    {
        var l = new AppLauncher(NullLogger<AppLauncher>.Instance);
        Assert.False(l.Launch(@"C:\nao\existe\app.exe", "x"));
        Assert.False(l.Launch(@"\\servidor\share\x.exe", "x"));
        Assert.False(l.Launch(@"C:\Windows\System32\cmd.bat", "x"));
    }
}

/// <summary>
/// Ponta a ponta com um app real (Bloco de Notas do Windows 11, empacotado): abrir → aparecer na enumeração
/// com AUMID → associar a um pin shell:AppsFolder → minimizar → restaurar → fechar.
/// Só mexe na janela que ele mesmo abriu. Se o Bloco de Notas já estiver aberto, o teste é pulado
/// (o Bloco de Notas abriria uma aba na janela existente).
/// </summary>
public class RealAppEndToEndTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Notepad_LaunchDiscoverMatchMinimizeRestoreClose()
    {
        var e = new WindowEnumerator();
        bool IsNotepad(WindowInfo w) =>
            (w.Aumid?.Contains("Notepad", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (w.ExePath?.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase) ?? false);

        if (e.Snapshot().Any(IsNotepad))
        {
            output.WriteLine("PULADO: Bloco de Notas já está aberto.");
            return;
        }
        var before = e.Snapshot().Select(w => w.Handle).ToHashSet();

        string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Assert.True(new AppLauncher(NullLogger<AppLauncher>.Instance).Launch(notepad, "Bloco de Notas"));

        WindowInfo? win = null;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(15) && win is null)
        {
            await Task.Delay(250);
            win = e.Snapshot().FirstOrDefault(w => !before.Contains(w.Handle) && IsNotepad(w));
        }
        Assert.NotNull(win);
        output.WriteLine($"Descoberto em {sw.ElapsedMilliseconds} ms: '{win!.Title}' exe={win.ExePath} aumid={win.Aumid}");

        try
        {
            // Identidade: empacotado → AUMID → associa ao pin shell:AppsFolder\<AUMID>
            if (win.Aumid is { } aumid)
            {
                var pin = new PinnedApp { Path = AppIdentity.PackagedPath(aumid), Id = "p", DisplayName = "Bloco de Notas" };
                var r = DockItemsBuilder.Build([(pin, new PinIdentity(null, aumid))], [win], win.Handle, true, [], w => new(w.Title, ""));
                Assert.Single(r.Entries);
                Assert.True(r.Entries[0].IsRunning && r.Entries[0].IsActive);
            }

            Assert.True(WindowActions.Minimize(win.Handle));
            await Task.Delay(400);
            Assert.True(NativeMethods.IsIconic(win.Handle));
            WindowActions.Activate(win.Handle);
            await Task.Delay(400);
            Assert.False(NativeMethods.IsIconic(win.Handle));
        }
        finally
        {
            WindowActions.Close(win.Handle);
            var t = Stopwatch.StartNew();
            while (t.Elapsed < TimeSpan.FromSeconds(5) && NativeMethods.IsWindow(win.Handle)) await Task.Delay(100);
        }
        Assert.False(NativeMethods.IsWindow(win.Handle), "janela não fechou com WM_CLOSE");
    }
}
