using System.Globalization;
using System.Text.Json;

namespace NovaTaskbar.Core.Localization;

/// <summary>Idioma disponível: código BCP-47, nome nativo e se escreve da direita para a esquerda.</summary>
public sealed record LanguageInfo(string Code, string NativeName, string EnglishName, bool RightToLeft = false);

/// <summary>
/// Traduções do app. O texto-fonte é o inglês (padrão); cada idioma é um JSON { "texto em inglês": "tradução" }
/// em i18n\{código}.json ao lado do executável. Texto sem tradução cai no inglês.
/// </summary>
public static class L
{
    /// <summary>As 20 línguas mais faladas do mundo (inglês primeiro, padrão do app).</summary>
    public static readonly IReadOnlyList<LanguageInfo> Languages =
    [
        new("en", "English", "English"),
        new("zh-Hans", "简体中文", "Chinese (Simplified)"),
        new("hi", "हिन्दी", "Hindi"),
        new("es", "Español", "Spanish"),
        new("fr", "Français", "French"),
        new("ar", "العربية", "Arabic", true),
        new("bn", "বাংলা", "Bengali"),
        new("pt-BR", "Português (Brasil)", "Portuguese (Brazil)"),
        new("ru", "Русский", "Russian"),
        new("ur", "اردو", "Urdu", true),
        new("id", "Bahasa Indonesia", "Indonesian"),
        new("de", "Deutsch", "German"),
        new("ja", "日本語", "Japanese"),
        new("tr", "Türkçe", "Turkish"),
        new("vi", "Tiếng Việt", "Vietnamese"),
        new("ko", "한국어", "Korean"),
        new("it", "Italiano", "Italian"),
        new("fa", "فارسی", "Persian", true),
        new("ta", "தமிழ்", "Tamil"),
        new("pl", "Polski", "Polish"),
    ];

    private static Dictionary<string, string> s_map = new();

    public static LanguageInfo Current { get; private set; } = Languages[0];
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
    public static bool IsRightToLeft => Current.RightToLeft;

    /// <summary>Disparado quando o idioma muda (janelas reconstroem os textos).</summary>
    public static event EventHandler? Changed;

    /// <summary>Normaliza um código salvo/da máquina para um dos 20 (ex.: "pt" → "pt-BR", "zh-CN" → "zh-Hans").</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        var exact = Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Code;
        string prefix = code.Split('-')[0].ToLowerInvariant();
        if (prefix == "zh") return "zh-Hans";
        if (prefix == "pt") return "pt-BR";
        return Languages.FirstOrDefault(l => l.Code.Split('-')[0] == prefix)?.Code ?? "en";
    }

    /// <summary>Carrega o idioma (pasta i18n com os JSON). Arquivo ausente ou inválido → inglês.</summary>
    public static void Load(string? code, string folder)
    {
        string c = Normalize(code);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (c != "en")
        {
            try
            {
                string file = Path.Combine(folder, c + ".json");
                if (File.Exists(file) && new FileInfo(file).Length < 4 * 1024 * 1024)
                    map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? map;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        s_map = map;
        Current = Languages.First(l => l.Code == c);
        Culture = CultureFor(c);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Usado pelos testes e pelo instalador (sem arquivo).</summary>
    public static void Use(string code, IDictionary<string, string> map)
    {
        string c = Normalize(code);
        s_map = new Dictionary<string, string>(map, StringComparer.Ordinal);
        Current = Languages.First(l => l.Code == c);
        Culture = CultureFor(c);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static CultureInfo CultureFor(string code)
    {
        try { return CultureInfo.GetCultureInfo(code == "en" ? "en-US" : code == "zh-Hans" ? "zh-CN" : code); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en-US"); }
    }

    /// <summary>Traduz um texto (inglês) para o idioma atual.</summary>
    public static string T(string english) =>
        s_map.TryGetValue(english, out var t) && !string.IsNullOrEmpty(t) ? t : english;

    /// <summary>Traduz e formata: L.F("{0} items", n). A tradução mantém os marcadores {0}, {1}…</summary>
    public static string F(string english, params object?[] args)
    {
        string pattern = T(english);
        try { return string.Format(Culture, pattern, args); }
        catch (FormatException) { return string.Format(Culture, english, args); }
    }

    /// <summary>Idioma do Windows mapeado para um dos 20 (sugestão do instalador).</summary>
    public static string SystemDefault() => Normalize(CultureInfo.CurrentUICulture.Name);
}
