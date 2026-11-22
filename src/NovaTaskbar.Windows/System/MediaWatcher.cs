using Microsoft.Extensions.Logging;
using global::Windows.Media.Control;
using global::Windows.Storage.Streams;

namespace NovaTaskbar.Windows.SystemInfo;

public sealed record MediaState(bool HasSession, string Title, string Artist, string SourceApp, bool Playing, bool CanNext, bool CanPrevious, byte[]? Thumbnail)
{
    public static readonly MediaState None = new(false, "", "", "", false, false, false, null);
}

/// <summary>
/// O que está tocando no Windows (Spotify, navegador, players…) pela API oficial de controles de mídia
/// (GlobalSystemMediaTransportControlsSessionManager). Totalmente por eventos; nenhuma rede.
/// </summary>
public sealed class MediaWatcher(ILogger<MediaWatcher> log) : IDisposable
{
    private const int MaxThumbnailBytes = 2 * 1024 * 1024;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _version;

    /// <summary>Disparado em thread qualquer quando o estado muda — o chamador deve reler com <see cref="ReadAsync"/>.</summary>
    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => { Attach(_manager.GetCurrentSession()); Changed?.Invoke(this, EventArgs.Empty); };
            Attach(_manager.GetCurrentSession());
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            log.LogWarning(ex, "Controles de mídia indisponíveis");
        }
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? s)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
        }
        _session = s;
        _version++;
        if (s != null)
        {
            s.MediaPropertiesChanged += OnSessionChanged;
            s.PlaybackInfoChanged += OnSessionChanged;
        }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => Changed?.Invoke(this, EventArgs.Empty);

    public async Task<MediaState> ReadAsync(bool withThumbnail)
    {
        var s = _session;
        if (s is null) return MediaState.None;
        try
        {
            var info = s.GetPlaybackInfo();
            var props = await s.TryGetMediaPropertiesAsync();
            if (info is null || props is null) return MediaState.None; // sessão trocando de faixa/encerrando
            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            byte[]? thumb = null;
            if (withThumbnail && props.Thumbnail != null) thumb = await ReadThumbnailAsync(props.Thumbnail);
            return new MediaState(true, props.Title ?? "", props.Artist ?? "", s.SourceAppUserModelId ?? "", playing,
                info.Controls?.IsNextEnabled ?? false, info.Controls?.IsPreviousEnabled ?? false, thumb);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return MediaState.None; // sessão encerrada no meio da leitura
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        using var stream = await reference.OpenReadAsync();
        if (stream.Size == 0 || stream.Size > MaxThumbnailBytes) return null;
        var buffer = new global::Windows.Storage.Streams.Buffer((uint)stream.Size);
        var read = await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
        var bytes = new byte[read.Length];
        using (var reader = DataReader.FromBuffer(read)) reader.ReadBytes(bytes);
        return bytes;
    }

    public async Task TogglePlayPauseAsync() { if (_session is { } s) await s.TryTogglePlayPauseAsync(); }
    public async Task NextAsync() { if (_session is { } s) await s.TrySkipNextAsync(); }
    public async Task PreviousAsync() { if (_session is { } s) await s.TrySkipPreviousAsync(); }

    public void Dispose() => Attach(null);
}
