using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NovaTaskbar.App.Services;

/// <summary>
/// Chaves de API dos widgets de serviço, criptografadas com a DPAPI do Windows (só este usuário neste PC consegue ler).
/// Ficam em %LOCALAPPDATA%\NovaTaskbar\secrets.bin; nunca vão para logs nem para o arquivo de configurações.
/// </summary>
internal sealed class SecretStore
{
    private readonly string _file;
    private Dictionary<string, string> _items;

    public SecretStore(AppPaths paths)
    {
        _file = Path.Combine(paths.Root, "secrets.bin");
        _items = Load();
    }

    public string? Get(string id) => _items.TryGetValue(id, out var v) ? v : null;
    public bool Has(string id) => _items.ContainsKey(id);

    public void Set(string id, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) _items.Remove(id);
        else _items[id] = secret.Trim();
        Save();
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_file)) return new();
            byte[] plain = Unprotect(File.ReadAllBytes(_file));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(plain)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or System.ComponentModel.Win32Exception) { return new(); }
    }

    private void Save()
    {
        byte[] cipher = Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_items)));
        string tmp = _file + ".tmp";
        File.WriteAllBytes(tmp, cipher);
        File.Move(tmp, _file, overwrite: true);
    }

    // ---------------- DPAPI (CryptProtectData/CryptUnprotectData, escopo do usuário atual)
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    private static byte[] Protect(byte[] data) => Transform(data, protect: true);
    private static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new DATA_BLOB { cbData = data.Length, pbData = handle.AddrOfPinnedObject() };
            bool ok = protect
                ? CryptProtectData(ref input, "CleanWinVista", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally { LocalFree(output.pbData); }
        }
        finally { handle.Free(); }
    }
}
