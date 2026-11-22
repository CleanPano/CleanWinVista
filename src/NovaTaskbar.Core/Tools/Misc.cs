using System.Globalization;
using System.Text.Json;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.Core.Tools;

/// <summary>Quanto do dia/semana/mês/ano já passou.</summary>
public enum ProgressSpan { Day, Week, Month, Year }

public static class TimeProgress
{
    public static double Fraction(ProgressSpan span, DateTime now)
    {
        (DateTime a, DateTime b) = span switch
        {
            ProgressSpan.Day => (now.Date, now.Date.AddDays(1)),
            ProgressSpan.Week => WeekOf(now),
            ProgressSpan.Month => (new DateTime(now.Year, now.Month, 1), new DateTime(now.Year, now.Month, 1).AddMonths(1)),
            _ => (new DateTime(now.Year, 1, 1), new DateTime(now.Year + 1, 1, 1)),
        };
        return Math.Clamp((now - a).TotalSeconds / (b - a).TotalSeconds, 0, 1);
    }

    private static (DateTime, DateTime) WeekOf(DateTime now)
    {
        int diff = ((int)now.DayOfWeek + 6) % 7; // semana começa na segunda
        var start = now.Date.AddDays(-diff);
        return (start, start.AddDays(7));
    }

    public static string Label(ProgressSpan s) => s switch { ProgressSpan.Day => L.T("Day"), ProgressSpan.Week => L.T("Week"), ProgressSpan.Month => L.T("Month"), _ => L.T("Year") };
}

/// <summary>Regras de links seguros (favoritos e serviços): só http/https absolutos, sem credenciais embutidas.</summary>
public static class UrlRules
{
    public static bool IsSafeWebUrl(string? url, bool httpsOnly = false) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || (!httpsOnly && u.Scheme == Uri.UriSchemeHttp))
        && string.IsNullOrEmpty(u.UserInfo) && !string.IsNullOrEmpty(u.Host);

    /// <summary>Completa "exemplo.com" → "https://exemplo.com".</summary>
    public static string Normalize(string url)
    {
        url = url.Trim();
        return url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
    }
}

/// <summary>Extrai um número de um JSON por caminho com pontos ("data.0.total", "count").</summary>
public static class JsonPath
{
    public static double? Number(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;
            foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (el.ValueKind == JsonValueKind.Array && int.TryParse(part, out int i))
                {
                    if (i < 0 || i >= el.GetArrayLength()) return null;
                    el = el[i];
                }
                else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(part, out var child)) el = child;
                else return null;
            }
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDouble(),
                JsonValueKind.String when double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                JsonValueKind.Array => el.GetArrayLength(),
                _ => null,
            };
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Formatação curta de números grandes (1,2 mil · 3,4 mi).</summary>
public static class Compact
{
    private static CultureInfo Pt => L.Culture;

    public static string Number(double n) => Math.Abs(n) switch
    {
        < 1_000 => Math.Round(n, Math.Abs(n) < 10 && n % 1 != 0 ? 1 : 0).ToString("#,0.#", Pt),
        < 1_000_000 => L.F("{0}K", (n / 1_000).ToString(Math.Abs(n) < 10_000 ? "0.#" : "0", Pt)),
        < 1_000_000_000 => L.F("{0}M", (n / 1_000_000).ToString(Math.Abs(n) < 10_000_000 ? "0.#" : "0", Pt)),
        _ => L.F("{0}B", (n / 1_000_000_000).ToString("0.#", Pt)),
    };
}

/// <summary>Lembrete periódico (água, postura): próximo horário a partir do último confirmado.</summary>
public static class Reminder
{
    public static bool IsDue(DateTime lastAck, int intervalMinutes, DateTime now) =>
        intervalMinutes > 0 && now - lastAck >= TimeSpan.FromMinutes(intervalMinutes);

    public static TimeSpan Remaining(DateTime lastAck, int intervalMinutes, DateTime now)
    {
        var left = lastAck.AddMinutes(intervalMinutes) - now;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }
}
