using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace NovaTaskbar.App.Services;

/// <summary>Dados que o usuário cria nos widgets (notas, tarefas, favoritos…). Só local; nunca sai do PC.</summary>
internal sealed class WidgetUserData
{
    /// <summary>Formato antigo (uma nota só). Migrado para <see cref="NoteList"/> na primeira abertura.</summary>
    public string Notes { get; set; } = "";
    public List<NoteItem> NoteList { get; set; } = new();
    /// <summary>Compromissos do calendário do Dock (locais).</summary>
    public List<NovaTaskbar.Core.Tools.CalEvent> Events { get; set; } = new();
    /// <summary>Calendário: última visão usada (month/week/day/list) e se mostra nº da semana.</summary>
    public string CalendarView { get; set; } = "month";
    public bool CalendarWeekNumbers { get; set; }
    /// <summary>Calculadora: histórico ("expressão = resultado"), radianos e teclado científico.</summary>
    public List<string> CalcHistory { get; set; } = new();
    public bool CalcRadians { get; set; }
    public bool CalcScientific { get; set; }
    public List<TodoItem> Todos { get; set; } = new();
    public List<BookmarkItem> Bookmarks { get; set; } = new();
    public List<string> Shelf { get; set; } = new();
    public List<string> WorldClocks { get; set; } = new() { "UTC", "Tokyo Standard Time" };
    public WaterState Water { get; set; } = new();
    public int PostureMinutes { get; set; } = 45;
    public DateTime PostureAck { get; set; } = DateTime.Now;
    public string ProgressSpan { get; set; } = "Day";
    public int TimerMinutes { get; set; } = 5;
    public string WeatherCity { get; set; } = "";
    public string CalendarUrl { get; set; } = "";
    public List<ServiceConfig> Services { get; set; } = new();
}

/// <summary>Nota rápida: conteúdo em RTF (negrito, itálico…) + texto puro para prévia e busca.</summary>
internal sealed class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Rtf { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>Cor da etiqueta: yellow, green, blue, purple, pink, gray.</summary>
    public string Color { get; set; } = "yellow";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;
}

internal sealed class TodoItem { public string Text { get; set; } = ""; public bool Done { get; set; } }
internal sealed class BookmarkItem { public string Title { get; set; } = ""; public string Url { get; set; } = ""; }

internal sealed class WaterState
{
    public int Goal { get; set; } = 8;
    public int IntervalMinutes { get; set; } = 60;
    public DateTime Day { get; set; } = DateTime.Today;
    public int Glasses { get; set; }
    public DateTime LastAck { get; set; } = DateTime.Now;
}

/// <summary>
/// Serviço conectado: <c>Kind</c> = github | stripe | plausible | shopify | linear | custom. A chave/token NÃO fica aqui:
/// vai criptografada pelo Windows (DPAPI) em <see cref="SecretStore"/>.
/// </summary>
internal sealed class ServiceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Kind { get; set; } = "custom";
    public string Label { get; set; } = "";
    /// <summary>Parâmetro do serviço: site (Plausible), loja (Shopify) ou URL (custom).</summary>
    public string Target { get; set; } = "";
    /// <summary>Custom: caminho no JSON ("data.total"). </summary>
    public string JsonPath { get; set; } = "";
    /// <summary>Custom: nome do cabeçalho da chave (padrão Authorization: Bearer).</summary>
    public string Header { get; set; } = "";
}

/// <summary>Leitura/gravação atômica de <see cref="WidgetUserData"/> em %LOCALAPPDATA%\NovaTaskbar\widgets-data.json.</summary>
internal sealed class WidgetStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        MaxDepth = 16,
    };
    private readonly string _file;
    private readonly ILogger<WidgetStore> _log;

    public WidgetStore(AppPaths paths, ILogger<WidgetStore> log)
    {
        _file = Path.Combine(paths.Root, "widgets-data.json");
        _log = log;
        Data = Load();
    }

    public WidgetUserData Data { get; }

    public event EventHandler? Changed;

    public void Update(Action<WidgetUserData> change)
    {
        change(Data);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private WidgetUserData Load()
    {
        try
        {
            if (File.Exists(_file) && new FileInfo(_file).Length < 4 * 1024 * 1024)
                return JsonSerializer.Deserialize<WidgetUserData>(File.ReadAllText(_file), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Dados dos widgets ilegíveis; começando do zero (arquivo preservado como .bad)");
            try { File.Copy(_file, _file + ".bad", overwrite: true); } catch (IOException) { }
        }
        return new WidgetUserData();
    }

    private void Save()
    {
        try
        {
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Falha ao salvar dados dos widgets");
        }
    }
}
