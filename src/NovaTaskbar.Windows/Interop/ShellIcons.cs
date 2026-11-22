using System.Runtime.InteropServices;

namespace NovaTaskbar.Windows.Interop;

/// <summary>Pixels BGRA8 pré-multiplicados (formato esperado pelo WriteableBitmap).</summary>
public sealed record IconPixels(byte[] Bgra, int Width, int Height);

/// <summary>
/// Extração de ícones em alta resolução via IShellItemImageFactory (mesma fonte do Explorer),
/// válida para .exe, .lnk e pastas. Deve rodar em thread STA.
/// </summary>
public static class ShellIcons
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int c, out BITMAP bmp);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    public static IconPixels? Extract(string path, int sizePx)
    {
        Guid iid = typeof(IShellItemImageFactory).GUID;
        int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
        if (hr < 0 || factory is null) return null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            hr = factory.GetImage(new SIZE { cx = sizePx, cy = sizePx }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbm);
            if (hr < 0 || hbm == IntPtr.Zero) return null;
            return ReadBitmap(hbm);
        }
        finally
        {
            if (hbm != IntPtr.Zero) NativeMethods.DeleteObject(hbm);
            Marshal.FinalReleaseComObject(factory);
        }
    }

    private static IconPixels? ReadBitmap(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bmp) == 0) return null;
        int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight);
        if (w <= 0 || h <= 0 || w > 1024 || h > 1024) return null;

        var bi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // de cima para baixo
            biPlanes = 1,
            biBitCount = 32,
        };
        var bits = new byte[w * h * 4];
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hbm, 0, (uint)h, bits, ref bi, 0) == 0) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, hdc); }

        NormalizeAlpha(bits);
        return new IconPixels(bits, w, h);
    }

    /// <summary>Garante BGRA pré-multiplicado: ícones sem canal alfa ficam opacos; alfa "reto" é pré-multiplicado.</summary>
    public static void NormalizeAlpha(byte[] p)
    {
        bool anyAlpha = false, straight = false;
        for (int i = 0; i < p.Length; i += 4)
        {
            byte a = p[i + 3];
            if (a != 0) anyAlpha = true;
            if (p[i] > a || p[i + 1] > a || p[i + 2] > a) straight = true;
        }
        if (!anyAlpha)
        {
            for (int i = 3; i < p.Length; i += 4) p[i] = 255;
            return;
        }
        if (!straight) return;
        for (int i = 0; i < p.Length; i += 4)
        {
            int a = p[i + 3];
            p[i] = (byte)(p[i] * a / 255);
            p[i + 1] = (byte)(p[i + 1] * a / 255);
            p[i + 2] = (byte)(p[i + 2] * a / 255);
        }
    }
}
