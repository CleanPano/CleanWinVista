using System.Runtime.InteropServices;
using System.Text;

namespace NovaTaskbar.Windows.Interop;

/// <summary>Lê o destino e o AppUserModelID de um atalho .lnk (IShellLinkW + IPropertyStore). Não executa nada.</summary>
public static class ShellLink
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        // Demais métodos não são usados; a vtable só precisa estar correta até GetPath.
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    }

    public sealed record LinkInfo(string? TargetPath, string? Aumid);

    public static LinkInfo? Read(string lnkPath)
    {
        object? obj = null;
        try
        {
            obj = new CShellLink();
            if (((IPersistFile)obj).Load(lnkPath, 0 /* STGM_READ */) < 0) return null;

            var sb = new StringBuilder(1024);
            string? target = ((IShellLinkW)obj).GetPath(sb, sb.Capacity, IntPtr.Zero, 0) >= 0 && sb.Length > 0 ? sb.ToString() : null;
            if (target != null) target = Environment.ExpandEnvironmentVariables(target);

            string? aumid = obj is NativeMethods.IPropertyStore store
                ? NativeMethods.ReadString(store, NativeMethods.PKEY_AppUserModel_ID)
                : null;
            return new LinkInfo(target, string.IsNullOrWhiteSpace(aumid) ? null : aumid);
        }
        catch (COMException) { return null; }
        catch (InvalidCastException) { return null; }
        finally
        {
            if (obj != null) Marshal.FinalReleaseComObject(obj);
        }
    }
}
