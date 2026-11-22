using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NovaTaskbar.Core.Imaging;
using NovaTaskbar.Windows.Interop;

namespace NovaTaskbar.App.Services;

/// <summary>
/// Extrai ícones numa thread STA dedicada (fora do UI thread), com cache em memória e em disco
/// (%LOCALAPPDATA%\NovaTaskbar\IconCache). A chave inclui caminho, tamanho e data de modificação do arquivo:
/// se o app for atualizado, o ícone é extraído de novo (invalidação automática).
/// </summary>
internal sealed class IconService : IDisposable
{
    private const uint Magic = 0x4E4F5649; // "NOVI"
    private readonly ILogger<IconService> _log;
    private readonly string _diskDir;
    private readonly BlockingCollection<(string Path, int Size, string Key, TaskCompletionSource<IconPixels?> Tcs)> _work = new();
    private readonly ConcurrentDictionary<string, IconPixels?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _thread;

    public IconService(AppPaths paths, ILogger<IconService> log)
    {
        _log = log;
        _diskDir = Path.Combine(paths.Root, "IconCache");
        Directory.CreateDirectory(_diskDir);
        _thread = new Thread(Worker) { IsBackground = true, Name = "Nova icon extractor" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<IconPixels?> GetPixelsAsync(string path, int sizePx)
    {
        if (string.IsNullOrEmpty(path)) return Task.FromResult<IconPixels?>(null);
        string key = CacheKey(path, sizePx);
        if (_cache.TryGetValue(key, out var hit)) return Task.FromResult(hit);
        var tcs = new TaskCompletionSource<IconPixels?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_work.TryAdd((path, sizePx, key, tcs))) tcs.SetResult(null);
        return tcs.Task;
    }

    /// <summary>Cria o ImageSource no UI thread.</summary>
    public static ImageSource? ToImageSource(IconPixels? px)
    {
        if (px is null) return null;
        var wb = new WriteableBitmap(px.Width, px.Height);
        using (var s = wb.PixelBuffer.AsStream()) s.Write(px.Bgra, 0, px.Bgra.Length);
        wb.Invalidate();
        return wb;
    }

    private void Worker()
    {
        foreach (var (path, size, key, tcs) in _work.GetConsumingEnumerable())
        {
            try
            {
                var px = ReadDisk(key);
                if (px is null)
                {
                    px = Fit(ShellIcons.Extract(path, Math.Max(size, SourceSize)), size);
                    if (px is null) _log.LogWarning("Ícone indisponível: {Path}", path);
                    else WriteDisk(key, px);
                }
                _cache[key] = px;
                tcs.TrySetResult(px);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha ao extrair ícone de {Path}", path);
                tcs.TrySetResult(null);
            }
        }
    }

    /// <summary>Extrai grande (256 px, a maior imagem do ícone) e reduz com média de área para o tamanho exato de tela.</summary>
    private const int SourceSize = 256;

    private static IconPixels? Fit(IconPixels? px, int size)
    {
        if (px is null || (px.Width <= size && px.Height <= size)) return px;
        double k = Math.Min((double)size / px.Width, (double)size / px.Height);
        int w = Math.Max(1, (int)Math.Round(px.Width * k)), h = Math.Max(1, (int)Math.Round(px.Height * k));
        return new IconPixels(ImageResampler.Downscale(px.Bgra, px.Width, px.Height, w, h), w, h);
    }

    private string DiskFile(string key) =>
        Path.Combine(_diskDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".bgra");

    private IconPixels? ReadDisk(string key)
    {
        string f = DiskFile(key);
        if (!File.Exists(f)) return null;
        try
        {
            using var br = new BinaryReader(File.OpenRead(f));
            if (br.ReadUInt32() != Magic) return null;
            int w = br.ReadInt32(), h = br.ReadInt32();
            if (w <= 0 || h <= 0 || w > 1024 || h > 1024) return null;
            var bytes = br.ReadBytes(w * h * 4);
            return bytes.Length == w * h * 4 ? new IconPixels(bytes, w, h) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void WriteDisk(string key, IconPixels px)
    {
        try
        {
            string f = DiskFile(key), tmp = f + ".tmp";
            using (var bw = new BinaryWriter(File.Create(tmp)))
            {
                bw.Write(Magic); bw.Write(px.Width); bw.Write(px.Height); bw.Write(px.Bgra);
            }
            File.Move(tmp, f, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string CacheKey(string path, int size)
    {
        long stamp = 0;
        try { if (File.Exists(path)) stamp = File.GetLastWriteTimeUtc(path).Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return $"v2|{path}|{size}|{stamp}"; // v2: redução por média de área
    }

    public void Dispose() => _work.CompleteAdding();
}
