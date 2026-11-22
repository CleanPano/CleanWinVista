using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace NovaTaskbar.App.Services;

/// <summary>
/// Único ponto de rede dos widgets. Só funciona com "Permitir widgets com internet" ligado, só HTTPS, com tempo-limite
/// curto, e registra no log apenas o host consultado (nunca chaves, nunca respostas) — inspirado no "privacy ledger"
/// do OpenDock; implementação própria.
/// </summary>
internal sealed class NetGate(SettingsService settings, ILogger<NetGate> log) : IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    public bool Allowed => settings.Current.Widgets.AllowNetwork;

    /// <summary>GET de texto; null se a rede estiver desligada, o endereço não for HTTPS ou a requisição falhar.</summary>
    public Task<string?> GetStringAsync(string url, IEnumerable<KeyValuePair<string, string>>? headers = null) =>
        SendAsync(HttpMethod.Get, url, headers, null);

    private async Task<string?> SendAsync(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>>? headers, HttpContent? body)
    {
        if (!Allowed || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            return null;
        try
        {
            using var req = new HttpRequestMessage(method, uri) { Content = body };
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("CleanWinVista", "1.0"));
            foreach (var (k, v) in headers ?? []) req.Headers.TryAddWithoutValidation(k, v);
            using var res = await _http.SendAsync(req);
            log.LogInformation("Rede: {Host} → {Status}", uri.Host, (int)res.StatusCode);
            if (!res.IsSuccessStatusCode) return null;
            if (res.Content.Headers.ContentLength is > 4 * 1024 * 1024) return null;
            return await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            log.LogInformation("Rede: {Host} falhou ({Error})", uri.Host, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>POST com corpo JSON (ex.: GraphQL do Linear); mesmas regras do GET.</summary>
    public Task<string?> PostJsonAsync(string url, string json, IEnumerable<KeyValuePair<string, string>>? headers = null) =>
        SendAsync(HttpMethod.Post, url, headers, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    public void Dispose() => _http.Dispose();
}
