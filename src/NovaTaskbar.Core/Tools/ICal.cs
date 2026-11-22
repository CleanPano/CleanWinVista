using System.Globalization;
using System.Text;

namespace NovaTaskbar.Core.Tools;

/// <summary>Leitor mínimo de iCalendar (.ics): eventos com início, fim, título e local. Sem regras de repetição.</summary>
public static class ICal
{
    public sealed record Event(DateTime StartLocal, DateTime EndLocal, string Summary, string Location, bool AllDay);

    public static IReadOnlyList<Event> Parse(string ics)
    {
        var events = new List<Event>();
        if (string.IsNullOrEmpty(ics)) return events;
        // Desdobra linhas continuadas (RFC 5545: quebra seguida de espaço/tab).
        var lines = Unfold(ics);
        Dictionary<string, (string Value, string Params)>? cur = null;
        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") { cur = new(StringComparer.OrdinalIgnoreCase); continue; }
            if (line == "END:VEVENT")
            {
                if (cur != null && cur.TryGetValue("DTSTART", out var ds) && TryDate(ds.Value, ds.Params, out var start, out bool allDay))
                {
                    DateTime end = cur.TryGetValue("DTEND", out var de) && TryDate(de.Value, de.Params, out var e, out _) ? e : start.AddHours(allDay ? 24 : 1);
                    events.Add(new Event(start, end, Text(cur, "SUMMARY"), Text(cur, "LOCATION"), allDay));
                }
                cur = null;
                continue;
            }
            if (cur is null) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string head = line[..colon], value = line[(colon + 1)..];
            int semi = head.IndexOf(';');
            string name = semi < 0 ? head : head[..semi], prms = semi < 0 ? "" : head[(semi + 1)..];
            cur[name] = (value, prms);
        }
        return events.OrderBy(e => e.StartLocal).ToList();
    }

    /// <summary>Próximos eventos (em andamento ou futuros) a partir de <paramref name="now"/>.</summary>
    public static IReadOnlyList<Event> Upcoming(IEnumerable<Event> events, DateTime now, int max = 5) =>
        events.Where(e => e.EndLocal > now).OrderBy(e => e.StartLocal).Take(max).ToList();

    private static List<string> Unfold(string ics)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t')) { sb.Append(raw, 1, raw.Length - 1); continue; }
            if (sb.Length > 0) result.Add(sb.ToString());
            sb.Clear().Append(raw.TrimEnd('\r'));
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    private static string Text(Dictionary<string, (string Value, string Params)> d, string key) =>
        d.TryGetValue(key, out var v) ? v.Value.Replace("\\n", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim() : "";

    private static bool TryDate(string value, string prms, out DateTime local, out bool allDay)
    {
        local = default;
        allDay = prms.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) && !prms.Contains("DATE-TIME", StringComparison.OrdinalIgnoreCase) || value.Length == 8;
        var inv = CultureInfo.InvariantCulture;
        if (allDay)
            return DateTime.TryParseExact(value, "yyyyMMdd", inv, DateTimeStyles.None, out local);
        if (value.EndsWith('Z'))
        {
            if (!DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc)) return false;
            local = utc.ToLocalTime();
            return true;
        }
        if (!DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", inv, DateTimeStyles.None, out var naive)) return false;
        // TZID=Area/City: converte quando o Windows conhece o fuso; senão trata como hora local.
        string? tzid = prms.Split(';').FirstOrDefault(p => p.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))?[5..].Trim('"');
        if (tzid != null && TimeZoneInfo.TryFindSystemTimeZoneById(tzid, out var tz))
        {
            local = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(naive, DateTimeKind.Unspecified), tz, TimeZoneInfo.Local);
            return true;
        }
        local = naive;
        return true;
    }
}
