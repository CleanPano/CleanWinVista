using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaTaskbar.Core.Configuration;

public static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// Valida e corrige configurações (vindas do disco ou de importação). Nunca confia no conteúdo:
/// valores fora da faixa são limitados, entradas inválidas são removidas e cada correção é relatada.
/// </summary>
public static class SettingsValidator
{
    public const int MaxPinnedApps = 64;
    public const int MaxPathLength = 1024;
    public const int MaxNameLength = 128;

    private static readonly string[] AllowedExtensions = [".exe", ".lnk", ".appref-ms"];

    public static IReadOnlyList<string> Sanitize(DockSettings s)
    {
        var issues = new List<string>();

        s.Appearance ??= new AppearanceSettings();
        s.Layout ??= new LayoutSettings();
        s.Magnification ??= new MagnificationSettings();
        s.Behavior ??= new BehaviorSettings();
        s.Behavior.Language = Localization.L.Normalize(s.Behavior.Language);
        s.Widgets ??= new Widgets.WidgetSettings();
        issues.AddRange(s.Widgets.Sanitize());
        if (!Enum.IsDefined(s.Behavior.Visibility)) { issues.Add("visibilidade inválida → Inteligente"); s.Behavior.Visibility = DockVisibility.Smart; }
        s.PinnedApps ??= new List<PinnedApp>();

        if (!Enum.IsDefined(s.Mode)) { issues.Add($"mode inválido → {DockModePresets.FactoryMode}"); s.Mode = DockModePresets.FactoryMode; }

        var a = s.Appearance;
        if (!Enum.IsDefined(a.Material)) { issues.Add("material inválido → Acrylic"); a.Material = GlassMaterial.Acrylic; }
        if (!Enum.IsDefined(a.Theme)) { issues.Add("tema inválido → System"); a.Theme = ThemePreference.System; }
        a.TintColor ??= "";
        if (a.TintColor.Length > 0 && !TryParseHexColor(a.TintColor, out _, out _, out _))
        {
            issues.Add($"tintColor inválida '{Trim(a.TintColor)}' → automática");
            a.TintColor = "";
        }
        a.TintOpacity = Clamp(a.TintOpacity, 0, 1, 0.6, "tintOpacity", issues);
        a.LuminosityOpacity = Clamp(a.LuminosityOpacity, 0, 1, 0.38, "luminosityOpacity", issues);
        a.CornerRadius = Clamp(a.CornerRadius, 0, 40, 12, "cornerRadius", issues);
        a.BorderOpacity = Clamp(a.BorderOpacity, 0, 1, 0.22, "borderOpacity", issues);

        var l = s.Layout;
        if (!Enum.IsDefined(l.Edge)) { issues.Add("edge inválida → Bottom"); l.Edge = DockEdge.Bottom; }
        l.IconSize = Clamp(l.IconSize, 24, 128, 40, "iconSize", issues);
        l.IconSpacing = Clamp(l.IconSpacing, 0, 32, 8, "iconSpacing", issues);
        l.Padding = Clamp(l.Padding, 0, 32, 12, "padding", issues);
        l.ScreenMargin = Clamp(l.ScreenMargin, 0, 200, 10, "screenMargin", issues);
        if (l.MaxExpansion < 0 && l.MaxExpansion != -1) { issues.Add("maxExpansion negativa → -1 (sem limite)"); l.MaxExpansion = -1; }
        l.MaxExpansion = l.MaxExpansion == -1 ? -1 : Clamp(l.MaxExpansion, 0, 2000, 160, "maxExpansion", issues);

        var m = s.Magnification;
        m.MaxScale = Clamp(m.MaxScale, 1.0, 3.0, 1.31, "maxScale", issues);
        m.Radius = Clamp(m.Radius, 20, 600, 53, "radius", issues);
        m.NeighborInfluence = Clamp(m.NeighborInfluence, 0.05, 1.0, 1.0, "neighborInfluence", issues);
        m.AnimationDurationMs = (int)Clamp(m.AnimationDurationMs, 0, 2000, 40, "animationDurationMs", issues);
        m.HoverSmoothingMs = (int)Clamp(m.HoverSmoothingMs, 0, 500, 70, "hoverSmoothingMs", issues);
        if (!Enum.IsDefined(m.Easing)) { issues.Add("easing inválido → EaseOutCubic"); m.Easing = Magnification.EasingCurve.EaseOutCubic; }

        // Apps fixados: só caminhos locais absolutos, sem argumentos, sem duplicados, quantidade limitada.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<PinnedApp>();
        foreach (var app in s.PinnedApps)
        {
            if (app is null) continue;
            if (!IsAcceptablePath(app.Path, out string reason))
            {
                issues.Add($"app removido ({reason}): '{Trim(app.Path)}'");
                continue;
            }
            string id = MakeId(app.Path);
            if (!seen.Add(id)) { issues.Add($"app duplicado removido: '{Trim(app.Path)}'"); continue; }
            app.Id = id;
            app.DisplayName = string.IsNullOrWhiteSpace(app.DisplayName)
                ? (Apps.AppIdentity.TryGetPackagedAumid(app.Path, out var aumid)
                    ? aumid.Split('!')[0].Split('_')[0]
                    : System.IO.Path.GetFileNameWithoutExtension(app.Path.TrimEnd('\\', '/')))
                : app.DisplayName.Trim();
            if (app.DisplayName.Length > MaxNameLength) app.DisplayName = app.DisplayName[..MaxNameLength];
            if (clean.Count >= MaxPinnedApps) { issues.Add("limite de apps fixados atingido"); break; }
            clean.Add(app);
        }
        s.PinnedApps = clean;

        if (s.SchemaVersion != DockSettings.CurrentSchemaVersion) s.SchemaVersion = DockSettings.CurrentSchemaVersion;
        return issues;
    }

    public static string MakeId(string path) => path.Trim().TrimEnd('\\', '/').ToLowerInvariant();

    public static bool IsAcceptablePath(string? path, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path)) { reason = "caminho vazio"; return false; }
        if (path.Length > MaxPathLength) { reason = "caminho longo demais"; return false; }
        // App empacotado: shell:AppsFolder\<AUMID> — só caracteres válidos de AUMID, sem espaços nem separadores extras.
        if (path.StartsWith(Apps.AppIdentity.PackagedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string aumid = path[Apps.AppIdentity.PackagedPrefix.Length..];
            bool ok = aumid.Length is > 0 and <= 256 && aumid.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '!' or '-');
            if (!ok) reason = "AUMID inválido";
            return ok;
        }
        if (path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 || path.Contains('"')) { reason = "caracteres inválidos"; return false; }
        // Rejeita UNC/rede, URIs e caminhos relativos: o Dock só executa itens locais escolhidos pelo usuário.
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) { reason = "caminho de rede"; return false; }
        if (path.Contains("://", StringComparison.Ordinal)) { reason = "URI não permitida"; return false; }
        if (!IsDriveRooted(path)) { reason = "caminho não absoluto"; return false; }
        string ext = System.IO.Path.GetExtension(path.TrimEnd('\\', '/'));
        bool looksLikeFolder = ext.Length == 0;
        if (!looksLikeFolder && !AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) { reason = $"tipo '{ext}' não permitido"; return false; }
        return true;
    }

    private static bool IsDriveRooted(string p) =>
        p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/');

    public static bool TryParseHexColor(string s, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (s is null) return false;
        s = s.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6) return false;
        return byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    private static double Clamp(double v, double min, double max, double fallback, string name, List<string> issues)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) { issues.Add($"{name} inválido → {fallback}"); return fallback; }
        if (v < min) { issues.Add($"{name} {v} < {min}"); return min; }
        if (v > max) { issues.Add($"{name} {v} > {max}"); return max; }
        return v;
    }

    private static string Trim(string s) => s.Length > 80 ? s[..80] + "…" : s;
}
