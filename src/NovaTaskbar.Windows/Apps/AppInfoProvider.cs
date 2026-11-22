using System.Diagnostics;
using System.Runtime.InteropServices;
using NovaTaskbar.Core.Apps;
using NovaTaskbar.Core.Configuration;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.Windows.Apps;

/// <summary>Nomes amigáveis, caminhos de ícone e identidade de apps fixados — tudo em cache.</summary>
public sealed class AppInfoProvider
{
    private readonly Dictionary<string, AppPresentation> _presentations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Stamp, PinIdentity Id)> _pins = new(StringComparer.OrdinalIgnoreCase);

    public AppPresentation Present(WindowInfo w)
    {
        string key = AppIdentity.KeyForWindow(w);
        if (_presentations.TryGetValue(key, out var p)) return p;

        if (!string.IsNullOrEmpty(w.Aumid) && DockItemsBuilder.IsPackaged(w))
        {
            string path = AppIdentity.PackagedPath(w.Aumid!);
            p = new AppPresentation(ShellItemName(path) ?? w.Title, path);
        }
        else if (!string.IsNullOrEmpty(w.ExePath))
        {
            p = new AppPresentation(ExeName(w.ExePath) ?? w.Title, w.ExePath);
        }
        else
        {
            p = new AppPresentation(w.Title, "");
        }
        _presentations[key] = p;
        return p;
    }

    public PinIdentity Resolve(PinnedApp pin)
    {
        long stamp = Stamp(pin.Path);
        if (_pins.TryGetValue(pin.Path, out var c) && c.Stamp == stamp) return c.Id;

        PinIdentity id;
        if (AppIdentity.TryGetPackagedAumid(pin.Path, out var aumid)) id = new(null, aumid);
        else if (pin.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var link = ShellLink.Read(pin.Path);
            string? exe = link?.TargetPath is { } t && t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? t : null;
            id = new(exe, link?.Aumid);
        }
        else if (pin.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = new(pin.Path, null);
        else id = new(null, null); // pasta: abre no Explorer, sem janela própria para associar

        _pins[pin.Path] = (stamp, id);
        return id;
    }

    /// <summary>Nome para exibir de um arquivo/app escolhido pelo usuário.</summary>
    public static string FriendlyName(string path)
    {
        if (AppIdentity.TryGetPackagedAumid(path, out _)) return ShellItemName(path) ?? path;
        if (Directory.Exists(path)) return new DirectoryInfo(path).Name;
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return ExeName(path) ?? Path.GetFileNameWithoutExtension(path);
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string? ExeName(string exe)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(exe);
            string? name = !string.IsNullOrWhiteSpace(v.FileDescription) ? v.FileDescription : v.ProductName;
            return string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(exe) : name.Trim();
        }
        catch (FileNotFoundException) { return Path.GetFileNameWithoutExtension(exe); }
        catch (UnauthorizedAccessException) { return Path.GetFileNameWithoutExtension(exe); }
    }

    private static long Stamp(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    // ----- IShellItem: nome de exibição (inclusive de apps empacotados via shell:AppsFolder) -----

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

    public static string? ShellItemName(string parsingName)
    {
        Guid iid = typeof(IShellItem).GUID;
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var item) < 0 || item is null) return null;
        try
        {
            if (item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var p) < 0 || p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Marshal.FreeCoTaskMem(p); }
        }
        finally { Marshal.FinalReleaseComObject(item); }
    }
}
