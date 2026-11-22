using System.Globalization;
using System.Text;

namespace NovaTaskbar.Core.Tools;

public enum Repeat { None, Daily, Weekdays, Weekly, Monthly, Yearly }

/// <summary>Compromisso do calendário do Dock (local, editável) ou vindo de um iCal (somente leitura).</summary>
public sealed class CalEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool AllDay { get; set; }
    public Repeat Repeat { get; set; }
    /// <summary>Última data da repetição (inclusiva); null = sem fim.</summary>
    public DateTime? RepeatUntil { get; set; }
    public string Location { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>Lembrete em minutos antes (null = sem lembrete).</summary>
    public int? RemindMinutes { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool ReadOnly { get; set; }

    public TimeSpan Duration => End > Start ? End - Start : AllDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
}

/// <summary>Uma ocorrência concreta (as repetições viram várias ocorrências).</summary>
public readonly record struct Occurrence(CalEvent Source, DateTime Start, DateTime End)
{
    public bool AllDay => Source.AllDay;
    /// <summary>Toca o dia <paramref name="day"/> (eventos de dia inteiro: End exclusivo).</summary>
    public bool Touches(DateOnly day)
    {
        var d0 = day.ToDateTime(TimeOnly.MinValue);
        var d1 = d0.AddDays(1);
        return Start < d1 && (End > d0 || (End == Start && Start >= d0));
    }
}

/// <summary>Cálculos do calendário (inspirados nas visões do FullCalendar: mês, semana, dia, lista).</summary>
public static class CalendarMath
{
    private const int MaxOccurrences = 2000;

    /// <summary>Expande repetições no intervalo [from, to) e ordena (dia inteiro primeiro, depois por início).</summary>
    public static List<Occurrence> Expand(IEnumerable<CalEvent> events, DateTime from, DateTime to)
    {
        var list = new List<Occurrence>();
        foreach (var e in events)
        {
            var dur = e.Duration;
            if (e.Repeat == Repeat.None)
            {
                if (e.Start < to && e.Start + dur > from) list.Add(new Occurrence(e, e.Start, e.Start + dur));
                continue;
            }
            int n = 0;
            var first = e.Start;
            // Avança direto até perto do intervalo (repetição diária/semanal antiga não gasta o limite de ocorrências).
            int step = e.Repeat == Repeat.Weekly ? 7 : e.Repeat is Repeat.Daily or Repeat.Weekdays ? 1 : 0;
            if (step > 0 && first + dur < from)
            {
                int k = (int)Math.Floor((from - dur - first).TotalDays / step);
                if (k > 0) first = first.AddDays((double)k * step);
            }
            for (var s = first; s < to && n < MaxOccurrences; s = Next(e, s, ++n))
            {
                if (e.RepeatUntil is { } until && s.Date > until.Date) break;
                if (e.Repeat == Repeat.Weekdays && s.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (s + dur > from) list.Add(new Occurrence(e, s, s + dur));
            }
        }
        return list.OrderBy(o => o.AllDay ? 0 : 1).ThenBy(o => o.Start).ThenBy(o => o.Source.Title).ToList();
    }

    private static DateTime Next(CalEvent e, DateTime current, int n) => e.Repeat switch
    {
        Repeat.Daily or Repeat.Weekdays => current.AddDays(1),
        Repeat.Weekly => current.AddDays(7),
        Repeat.Monthly => e.Start.AddMonths(n), // a partir do original: 31/jan → 28/fev → 31/mar
        Repeat.Yearly => e.Start.AddYears(n),
        _ => DateTime.MaxValue,
    };

    /// <summary>42 dias (6 semanas) da grade do mês, começando no primeiro dia da semana.</summary>
    public static DateOnly[] MonthGrid(int year, int month, DayOfWeek firstDay)
    {
        var first = new DateOnly(year, month, 1);
        int back = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var start = first.AddDays(-back);
        var days = new DateOnly[42];
        for (int i = 0; i < 42; i++) days[i] = start.AddDays(i);
        return days;
    }

    public static DateOnly StartOfWeek(DateOnly day, DayOfWeek firstDay) => day.AddDays(-(((int)day.DayOfWeek - (int)firstDay + 7) % 7));

    public static int IsoWeek(DateOnly day) => ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>Exporta eventos locais em iCalendar (.ics) — abre no Outlook/Google Agenda.</summary>
    public static string ToIcs(IEnumerable<CalEvent> events)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//CleanWinVista//Dock//PT\r\n");
        foreach (var e in events)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append("UID:").Append(e.Id).Append("@cleanwinvista\r\n");
            sb.Append("DTSTAMP:").Append(DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
            if (e.AllDay)
            {
                sb.Append("DTSTART;VALUE=DATE:").Append(e.Start.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
                sb.Append("DTEND;VALUE=DATE:").Append((e.Start + e.Duration).ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
            }
            else
            {
                sb.Append("DTSTART:").Append(e.Start.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)).Append("\r\n");
                sb.Append("DTEND:").Append((e.Start + e.Duration).ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)).Append("\r\n");
            }
            string? freq = e.Repeat switch
            {
                Repeat.Daily => "FREQ=DAILY", Repeat.Weekdays => "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", Repeat.Weekly => "FREQ=WEEKLY",
                Repeat.Monthly => "FREQ=MONTHLY", Repeat.Yearly => "FREQ=YEARLY", _ => null,
            };
            if (freq != null)
            {
                sb.Append("RRULE:").Append(freq);
                if (e.RepeatUntil is { } u) sb.Append(";UNTIL=").Append(u.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                sb.Append("\r\n");
            }
            sb.Append("SUMMARY:").Append(Escape(e.Title)).Append("\r\n");
            if (e.Location.Length > 0) sb.Append("LOCATION:").Append(Escape(e.Location)).Append("\r\n");
            if (e.Notes.Length > 0) sb.Append("DESCRIPTION:").Append(Escape(e.Notes)).Append("\r\n");
            if (e.RemindMinutes is { } m)
                sb.Append("BEGIN:VALARM\r\nACTION:DISPLAY\r\nDESCRIPTION:Lembrete\r\nTRIGGER:-PT").Append(m).Append("M\r\nEND:VALARM\r\n");
            sb.Append("END:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>Converte eventos de um iCal (somente leitura no Dock).</summary>
    public static IEnumerable<CalEvent> FromIcs(IEnumerable<ICal.Event> events) => events.Select(e => new CalEvent
    {
        Id = "ics:" + e.StartLocal.Ticks + ":" + e.Summary.GetHashCode(StringComparison.Ordinal),
        Title = e.Summary, Start = e.StartLocal, End = e.EndLocal, AllDay = e.AllDay, Location = e.Location, ReadOnly = true,
    });

    /// <summary>Lembretes vencidos entre <paramref name="since"/> e <paramref name="now"/>.</summary>
    public static IEnumerable<Occurrence> DueReminders(IEnumerable<CalEvent> events, DateTime since, DateTime now) =>
        Expand(events.Where(e => e.RemindMinutes != null), since.AddDays(-1), now.AddDays(2))
            .Where(o => o.Start.AddMinutes(-o.Source.RemindMinutes!.Value) is var t && t > since && t <= now);
}
