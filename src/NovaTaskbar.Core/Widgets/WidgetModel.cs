using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaTaskbar.Core.Widgets;

[JsonConverter(typeof(TolerantWidgetKindConverter))]
public enum WidgetKind
{
    /// <summary>Faixa de mídia (o que está tocando, play/pausa).</summary>
    Media,
    /// <summary>Anéis de CPU e RAM.</summary>
    System,
    /// <summary>Download/upload por segundo.</summary>
    Network,
    /// <summary>Volume do dispositivo de saída padrão.</summary>
    Volume,
    /// <summary>Nível e estado da bateria.</summary>
    Battery,
    /// <summary>Timer de foco (Pomodoro).</summary>
    Focus,
    /// <summary>Hora e data (clique abre a semana).</summary>
    Clock,
    /// <summary>Idioma/layout do teclado (clique troca). Aparece com 2+ idiomas instalados.</summary>
    Keyboard,
    /// <summary>Periféricos conectados com bateria (fones, mouse, teclado, controle). Aparece quando há algum.</summary>
    Devices,

    // ---- Ferramentas (abrem um painel ao clicar). Inspiradas na lista de widgets do CoolDock; implementação própria.
    Clipboard, Screenshot, ColorPicker, Emoji, Converter, Calculator, Stopwatch, Timer,
    // ---- Produtividade
    Notes, Todos, Water, Posture, Progress, AnalogClock, WorldClock,
    // ---- Arquivos e acesso rápido
    Downloads, FileShelf, Bookmarks, Search, AppSwitcher, Photos,
    // ---- Sistema
    QuickToggles, VoiceMemo,
    // ---- Com internet (só com "Permitir widgets com internet" ligado)
    Weather, Calendar, Services,
}

/// <summary>Forma de exibir uma métrica (CPU/RAM/rede). Inspirado nos tipos de visualização do Stat Space; desenho próprio.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetStyle>))]
public enum WidgetStyle
{
    /// <summary>Anel de progresso.</summary>
    Ring,
    /// <summary>Gráfico de linha com área (histórico).</summary>
    Line,
    /// <summary>Gráfico de barras (histórico).</summary>
    Bars,
    /// <summary>Velocímetro semicircular com ponteiro.</summary>
    Gauge,
    /// <summary>Só números.</summary>
    Text,
}

/// <summary>
/// Lê <see cref="WidgetKind"/> por nome; um nome desconhecido (widget removido numa versão nova, ex. "ClaudeUsage")
/// vira um valor inválido que <see cref="WidgetSettings.Sanitize"/> descarta — sem perder o resto das configurações.
/// </summary>
public sealed class TolerantWidgetKindConverter : JsonConverter<WidgetKind>
{
    public const WidgetKind Unknown = (WidgetKind)(-1);

    public override WidgetKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => Enum.TryParse<WidgetKind>(reader.GetString(), ignoreCase: true, out var k) && Enum.IsDefined(k) ? k : Unknown,
        JsonTokenType.Number => reader.TryGetInt32(out int n) && Enum.IsDefined((WidgetKind)n) ? (WidgetKind)n : Unknown,
        _ => Unknown,
    };

    public override void Write(Utf8JsonWriter writer, WidgetKind value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

public sealed class WidgetConfig
{
    /// <summary>Conversor no próprio campo: tem prioridade sobre o JsonStringEnumConverter global das opções.</summary>
    [JsonConverter(typeof(TolerantWidgetKindConverter))]
    public WidgetKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public WidgetStyle Style { get; set; } = WidgetStyle.Ring;
}

/// <summary>Widgets aparecem no modo Estendido, à direita dos apps, na ordem desta lista.</summary>
public sealed class WidgetSettings
{
    public List<WidgetConfig> Items { get; set; } = DefaultItems();
    public bool Clock24h { get; set; } = true;
    public bool ClockSeconds { get; set; }
    public int FocusMinutes { get; set; } = 25;
    /// <summary>Widgets que usam a internet (clima, agenda iCal, cotações, serviços). Desligado por padrão.</summary>
    public bool AllowNetwork { get; set; }

    /// <summary>
    /// Padrão de fábrica = o Dock do dono do app (22/11/2026): ordem, ligados e estilos. Tipos fora da lista
    /// (novos em versões futuras) entram no fim, desligados.
    /// </summary>
    private static readonly (WidgetKind Kind, bool On, WidgetStyle Style)[] Factory =
    [
        (WidgetKind.Media, true, WidgetStyle.Ring), (WidgetKind.System, true, WidgetStyle.Text), (WidgetKind.Network, true, WidgetStyle.Text),
        (WidgetKind.Clipboard, true, WidgetStyle.Ring), (WidgetKind.Battery, true, WidgetStyle.Ring), (WidgetKind.Calculator, true, WidgetStyle.Ring),
        (WidgetKind.Photos, true, WidgetStyle.Ring), (WidgetKind.Downloads, true, WidgetStyle.Ring), (WidgetKind.Devices, true, WidgetStyle.Ring),
        (WidgetKind.Focus, true, WidgetStyle.Ring), (WidgetKind.Screenshot, false, WidgetStyle.Ring), (WidgetKind.ColorPicker, false, WidgetStyle.Ring),
        (WidgetKind.Emoji, false, WidgetStyle.Ring), (WidgetKind.Converter, false, WidgetStyle.Ring), (WidgetKind.Volume, true, WidgetStyle.Ring),
        (WidgetKind.Stopwatch, false, WidgetStyle.Ring), (WidgetKind.Timer, false, WidgetStyle.Ring), (WidgetKind.Notes, true, WidgetStyle.Ring),
        (WidgetKind.Todos, false, WidgetStyle.Ring), (WidgetKind.Water, false, WidgetStyle.Ring), (WidgetKind.Posture, false, WidgetStyle.Ring),
        (WidgetKind.Progress, false, WidgetStyle.Ring), (WidgetKind.Keyboard, true, WidgetStyle.Ring), (WidgetKind.WorldClock, false, WidgetStyle.Ring),
        (WidgetKind.Weather, true, WidgetStyle.Ring), (WidgetKind.FileShelf, false, WidgetStyle.Ring), (WidgetKind.Bookmarks, false, WidgetStyle.Ring),
        (WidgetKind.Search, false, WidgetStyle.Ring), (WidgetKind.AppSwitcher, false, WidgetStyle.Ring), (WidgetKind.AnalogClock, true, WidgetStyle.Ring),
        (WidgetKind.QuickToggles, false, WidgetStyle.Ring), (WidgetKind.VoiceMemo, false, WidgetStyle.Ring), (WidgetKind.Clock, true, WidgetStyle.Ring),
        (WidgetKind.Calendar, false, WidgetStyle.Ring), (WidgetKind.Services, false, WidgetStyle.Ring),
    ];

    /// <summary>Ligados para quem instala do zero (padrão de fábrica). Os demais ficam disponíveis em Configurações.</summary>
    public static bool DefaultEnabled(WidgetKind kind)
    {
        foreach (var f in Factory) if (f.Kind == kind) return f.On;
        return false;
    }

    /// <summary>Widgets que dependem da internet.</summary>
    public static bool NeedsNetwork(WidgetKind kind) => kind is WidgetKind.Weather or WidgetKind.Calendar or WidgetKind.Services;
    /// <summary>
    /// Widgets contextuais só aparecem quando há algo a mostrar: música tocando, bateria (notebook),
    /// 2+ idiomas de teclado, periféricos com bateria.
    /// </summary>
    public static bool IsContextual(WidgetKind kind) => kind is WidgetKind.Media or WidgetKind.Battery or WidgetKind.Keyboard or WidgetKind.Devices;

    public static List<WidgetConfig> DefaultItems()
    {
        var list = Factory.Select(f => new WidgetConfig { Kind = f.Kind, Enabled = f.On, Style = Styles(f.Kind).Contains(f.Style) || Styles(f.Kind).Count == 0 ? f.Style : DefaultStyle(f.Kind) }).ToList();
        foreach (var k in Enum.GetValues<WidgetKind>())
            if (!list.Any(i => i.Kind == k)) list.Add(new WidgetConfig { Kind = k, Enabled = false, Style = DefaultStyle(k) });
        return list;
    }

    /// <summary>Estilos que cada widget suporta (o primeiro é o padrão). Vazio = sem escolha.</summary>
    public static IReadOnlyList<WidgetStyle> Styles(WidgetKind kind) => kind switch
    {
        WidgetKind.System => [WidgetStyle.Ring, WidgetStyle.Line, WidgetStyle.Bars, WidgetStyle.Gauge, WidgetStyle.Text],
        WidgetKind.Network => [WidgetStyle.Line, WidgetStyle.Bars, WidgetStyle.Text],
        _ => [],
    };

    public static WidgetStyle DefaultStyle(WidgetKind kind) => Styles(kind) is { Count: > 0 } s ? s[0] : WidgetStyle.Ring;

    public WidgetStyle StyleOf(WidgetKind kind) => Items.FirstOrDefault(i => i.Kind == kind)?.Style ?? DefaultStyle(kind);

    /// <summary>Remove duplicados/inválidos e acrescenta (desligados) os tipos que faltarem.</summary>
    public List<string> Sanitize()
    {
        var issues = new List<string>();
        Items ??= DefaultItems();
        var seen = new HashSet<WidgetKind>();
        var clean = new List<WidgetConfig>();
        foreach (var w in Items)
        {
            if (w is null || !Enum.IsDefined(w.Kind)) { issues.Add("widget inválido removido"); continue; }
            if (!seen.Add(w.Kind)) { issues.Add($"widget duplicado removido: {w.Kind}"); continue; }
            var styles = Styles(w.Kind);
            if (!Enum.IsDefined(w.Style) || (styles.Count > 0 && !styles.Contains(w.Style)))
            {
                if (styles.Count > 0) issues.Add($"estilo {w.Style} inválido para {w.Kind}");
                w.Style = DefaultStyle(w.Kind);
            }
            clean.Add(w);
        }
        foreach (var k in Enum.GetValues<WidgetKind>())
            if (!seen.Contains(k)) clean.Add(new WidgetConfig { Kind = k, Enabled = IsContextual(k) && DefaultEnabled(k), Style = DefaultStyle(k) }); // contextuais já se escondem sozinhos
        Items = clean;
        if (FocusMinutes is < 1 or > 180) { issues.Add($"focusMinutes {FocusMinutes} fora de 1–180"); FocusMinutes = Math.Clamp(FocusMinutes, 1, 180); }
        return issues;
    }

    /// <summary>Largura de cada widget em "vagas" de ícone (o tamanho acompanha o tamanho dos ícones).</summary>
    /// <summary>Largura do widget de periféricos: um anel por aparelho (até <see cref="MaxDevicesShown"/>).</summary>
    public static double DevicesWidth(int count) => 0.15 + 0.95 * Math.Clamp(count, 1, MaxDevicesShown);
    public const int MaxDevicesShown = 3;

    public double WidthInSlots(WidgetKind kind) => kind switch
    {
        WidgetKind.Media => 3.2,
        WidgetKind.System => StyleOf(kind) is WidgetStyle.Line or WidgetStyle.Bars ? 2.6 : 1.9,
        WidgetKind.Network => StyleOf(kind) == WidgetStyle.Text ? 1.9 : 2.6,
        WidgetKind.Volume => 1.0,
        WidgetKind.Battery => 1.5,
        WidgetKind.Focus => 1.0,
        WidgetKind.Clock => ClockSeconds ? 2.3 : 1.9,
        WidgetKind.Keyboard => 1.0,
        WidgetKind.Devices => DevicesWidth(1),
        WidgetKind.WorldClock => 2.4,
        WidgetKind.Weather => 1.7,
        WidgetKind.Calendar => 2.8,
        WidgetKind.Services => 1.9,
        _ => 1,
    };
}

/// <summary>Uso de CPU a partir de GetSystemTimes (o tempo de kernel inclui o ocioso).</summary>
public static class CpuUsage
{
    public readonly record struct Sample(ulong Idle, ulong Kernel, ulong User);

    /// <returns>0..100, ou null se não houver tempo decorrido.</returns>
    public static double? Percent(Sample previous, Sample current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User) return null;
        double idle = current.Idle - previous.Idle;
        double total = (current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total <= 0) return null;
        return Math.Clamp((total - idle) / total * 100.0, 0, 100);
    }
}

/// <summary>Taxa (bytes/s) entre duas leituras de contador; contador zerado/reiniciado → 0.</summary>
public static class Rate
{
    public static double PerSecond(long previousBytes, long currentBytes, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero || currentBytes < previousBytes) return 0;
        return (currentBytes - previousBytes) / elapsed.TotalSeconds;
    }

    /// <summary>"0 B/s", "850 KB/s", "1,2 MB/s" (cultura pt-BR por padrão).</summary>
    public static string Format(double bytesPerSecond, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.GetCultureInfo("pt-BR");
        if (bytesPerSecond < 1) return "0 B/s";
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        int u = 0;
        double v = bytesPerSecond;
        while (v >= 1000 && u < units.Length - 1) { v /= 1024; u++; }
        string num = v >= 100 || u == 0 ? v.ToString("0", culture) : v.ToString("0.0", culture);
        return $"{num} {units[u]}";
    }
}

/// <summary>Timer de foco (Pomodoro) — máquina de estados pura, com o relógio injetado.</summary>
public sealed class FocusTimer
{
    private DateTime _endsAt;
    private TimeSpan _remainingWhenPaused;

    public FocusTimer(TimeSpan duration) { Duration = duration; _remainingWhenPaused = duration; }

    public enum State { Idle, Running, Paused, Finished }

    public TimeSpan Duration { get; private set; }
    public State Current { get; private set; } = State.Idle;

    public void SetDuration(TimeSpan d)
    {
        Duration = d;
        if (Current is State.Idle or State.Finished) { _remainingWhenPaused = d; Current = State.Idle; }
    }

    public void Start(DateTime now)
    {
        if (Current == State.Running) return;
        if (Current is State.Idle or State.Finished) _remainingWhenPaused = Duration;
        _endsAt = now + _remainingWhenPaused;
        Current = State.Running;
    }

    public void Pause(DateTime now)
    {
        if (Current != State.Running) return;
        _remainingWhenPaused = Remaining(now);
        Current = State.Paused;
    }

    public void Toggle(DateTime now)
    {
        if (Current == State.Running) Pause(now); else Start(now);
    }

    public void Reset()
    {
        Current = State.Idle;
        _remainingWhenPaused = Duration;
    }

    /// <summary>Atualiza o estado; retorna true no instante em que termina.</summary>
    public bool Tick(DateTime now)
    {
        if (Current != State.Running || now < _endsAt) return false;
        Current = State.Finished;
        _remainingWhenPaused = TimeSpan.Zero;
        return true;
    }

    public TimeSpan Remaining(DateTime now) => Current switch
    {
        State.Running => _endsAt > now ? _endsAt - now : TimeSpan.Zero,
        State.Finished => TimeSpan.Zero,
        _ => _remainingWhenPaused,
    };

    /// <summary>Fração restante 1→0 (para o anel).</summary>
    public double Fraction(DateTime now) => Duration <= TimeSpan.Zero ? 0 : Remaining(now).TotalSeconds / Duration.TotalSeconds;

    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
}

public static class DockCalendar
{
    /// <summary>Os 7 dias da semana que contém <paramref name="day"/>, começando em <paramref name="firstDay"/>.</summary>
    public static DateOnly[] WeekOf(DateOnly day, DayOfWeek firstDay)
    {
        int diff = ((int)day.DayOfWeek - (int)firstDay + 7) % 7;
        var start = day.AddDays(-diff);
        return Enumerable.Range(0, 7).Select(start.AddDays).ToArray();
    }

    public static string Time(DateTime t, bool h24, bool seconds, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.GetCultureInfo("pt-BR");
        string fmt = (h24 ? "HH:mm" : "h:mm") + (seconds ? ":ss" : "") + (h24 ? "" : " tt");
        return t.ToString(fmt, h24 ? culture : CultureInfo.InvariantCulture);
    }
}

/// <summary>Histórico circular de amostras de tamanho fixo (mais antiga → mais recente).</summary>
public sealed class History
{
    private readonly double[] _buf;
    private int _start, _count;

    public History(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _buf = new double[capacity];
    }

    public int Capacity => _buf.Length;
    public int Count => _count;
    public double this[int i] => (uint)i < (uint)_count ? _buf[(_start + i) % _buf.Length] : throw new ArgumentOutOfRangeException(nameof(i));
    public double Last => _count == 0 ? 0 : this[_count - 1];

    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
        if (_count < _buf.Length) { _buf[(_start + _count) % _buf.Length] = value; _count++; }
        else { _buf[_start] = value; _start = (_start + 1) % _buf.Length; }
    }

    public double Max()
    {
        double m = 0;
        for (int i = 0; i < _count; i++) m = Math.Max(m, this[i]);
        return m;
    }

    /// <summary>Escala "agradável" para taxas: o próximo 1/2/5 × 10ⁿ acima do máximo (mínimo <paramref name="floor"/>).</summary>
    public double NiceMax(double floor)
    {
        double max = Math.Max(Max(), floor);
        double p = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (var k in new[] { 1.0, 2.0, 5.0, 10.0 })
            if (k * p >= max) return k * p;
        return 10 * p;
    }
}

/// <summary>Tipo de periférico (decide o ícone).</summary>
public enum PeripheralKind { Headphones, Mouse, Keyboard, Gamepad, Phone, Pen, Other }

/// <summary>Periférico conectado com nível de bateria conhecido.</summary>
public readonly record struct PeripheralBattery(string Name, int Percent, PeripheralKind Kind);

/// <summary>Classifica e ordena periféricos. Regras puras (testáveis); a leitura real fica em NovaTaskbar.Windows.</summary>
public static class Peripherals
{
    /// <summary>
    /// Tipo a partir de (em ordem): aparência BLE (GAP Appearance), classe Bluetooth clássica (CoD maior/menor) e nome.
    /// </summary>
    public static PeripheralKind Classify(string? name, int? codMajor = null, int? codMinor = null, int? leAppearance = null)
    {
        if (leAppearance is { } a)
        {
            switch (a)
            {
                case 0x03C1: return PeripheralKind.Keyboard;
                case 0x03C2: return PeripheralKind.Mouse;
                case 0x03C3 or 0x03C4: return PeripheralKind.Gamepad;
                case 0x03C7: return PeripheralKind.Pen;
            }
            int category = a >> 6;
            if (category is 0x25 or 0x0F && a is >= 0x0940 and <= 0x097F) return PeripheralKind.Headphones; // fones/earbuds
            if (category == 0x01) return PeripheralKind.Phone;
        }
        if (codMajor is { } major)
        {
            if (major == 4) return PeripheralKind.Headphones;                 // áudio/vídeo
            if (major == 2) return PeripheralKind.Phone;
            if (major == 5 && codMinor is { } minor)                          // periférico: bits 6–7 = teclado/apontador
            {
                bool kb = (minor & 0x10) != 0, mouse = (minor & 0x20) != 0;    // minor já sem os 2 bits de formato
                if (mouse && !kb) return PeripheralKind.Mouse;
                if (kb) return PeripheralKind.Keyboard;
                if ((minor & 0x0F) is 1 or 2) return PeripheralKind.Gamepad;   // joystick/gamepad
            }
        }
        string n = (name ?? "").ToLowerInvariant();
        if (Has(n, "mouse", "mx master", "trackpad")) return PeripheralKind.Mouse;
        if (Has(n, "keyboard", "teclado", "keys")) return PeripheralKind.Keyboard;
        if (Has(n, "controller", "gamepad", "xbox", "dualsense", "dualshock", "joy-con", "controle")) return PeripheralKind.Gamepad;
        if (Has(n, "buds", "airpods", "headphone", "headset", "earbud", "fone", "wh-", "wf-", "jbl", "beats", "speaker", "caixa")) return PeripheralKind.Headphones;
        if (Has(n, "pen", "caneta", "stylus")) return PeripheralKind.Pen;
        if (Has(n, "phone", "iphone", "galaxy", "pixel", "celular")) return PeripheralKind.Phone;
        return PeripheralKind.Other;
    }

    /// <summary>Remove duplicados (mesmo nome) e ordena: bateria mais baixa primeiro (o que precisa carregar).</summary>
    public static IReadOnlyList<PeripheralBattery> Normalize(IEnumerable<PeripheralBattery> items) =>
        items.Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.Percent is >= 0 and <= 100)
             .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
             .Select(g => g.OrderBy(p => p.Percent).First() with { Name = g.Key })
             .OrderBy(p => p.Percent).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
             .ToList();

    /// <summary>Nível que pede recarga: vermelho abaixo de 20 %, laranja até 40 %.</summary>
    public static int Severity(int percent) => percent < 20 ? 2 : percent <= 40 ? 1 : 0;

    private static bool Has(string s, params string[] keys) => keys.Any(k => s.Contains(k, StringComparison.Ordinal));
}

/// <summary>Idioma de teclado (HKL do Windows reduzido ao que a UI precisa).</summary>
public readonly record struct KeyboardLanguage(IntPtr Handle, int LangId, string Code, string Name);

public static class KeyboardLanguages
{
    /// <summary>Código curto ("PT", "EN"); repete o idioma com sufixo quando há dois layouts do mesmo idioma ("EN", "EN2").</summary>
    public static IReadOnlyList<KeyboardLanguage> Describe(IEnumerable<IntPtr> layouts)
    {
        var result = new List<KeyboardLanguage>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var h in layouts)
        {
            int lang = (int)((long)h & 0xFFFF);
            string code = "??", name = $"Idioma 0x{lang:X4}";
            try
            {
                var ci = System.Globalization.CultureInfo.GetCultureInfo(lang);
                code = ci.TwoLetterISOLanguageName.ToUpperInvariant();
                name = ci.TextInfo.ToTitleCase(ci.NativeName);
            }
            catch (System.Globalization.CultureNotFoundException) { }
            catch (ArgumentException) { }
            int n = seen.TryGetValue(code, out var c) ? c + 1 : 1;
            seen[code] = n;
            result.Add(new KeyboardLanguage(h, lang, n == 1 ? code : code + n, name));
        }
        return result;
    }

    /// <summary>Próximo idioma da lista (circular).</summary>
    public static KeyboardLanguage? Next(IReadOnlyList<KeyboardLanguage> all, IntPtr current, int direction = 1)
    {
        if (all.Count == 0) return null;
        int i = -1;
        for (int k = 0; k < all.Count; k++) if (all[k].Handle == current) { i = k; break; }
        int next = ((i < 0 ? 0 : i + Math.Sign(direction == 0 ? 1 : direction)) % all.Count + all.Count) % all.Count;
        return all[next];
    }
}
