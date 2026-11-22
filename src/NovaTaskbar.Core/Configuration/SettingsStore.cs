using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovaTaskbar.Core.Configuration;

public sealed record SettingsLoadResult(DockSettings Settings, IReadOnlyList<string> Issues, bool RecoveredFromCorruption, bool CreatedNew);

/// <summary>
/// Persistência JSON versionada: escrita atômica (arquivo temporário + substituição),
/// cópia .bak da última versão boa, migração de esquema e recuperação de arquivo corrompido.
/// </summary>
public sealed class SettingsStore
{
    public const long MaxFileBytes = 1024 * 1024; // 1 MB: configuração legítima é muito menor

    private readonly object _gate = new();

    public SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
    public string BackupPath => FilePath + ".bak";

    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                // Tenta o backup antes de criar padrões (ex.: queda de energia durante a troca de arquivos).
                if (File.Exists(BackupPath) && TryRead(BackupPath, out var fromBak, out var bakIssues))
                    return new SettingsLoadResult(fromBak!, [.. bakIssues, "restaurado do backup"], true, false);
                return new SettingsLoadResult(new DockSettings(), [], false, true);
            }

            if (TryRead(FilePath, out var s, out var issues))
                return new SettingsLoadResult(s!, issues, false, false);

            // Arquivo principal corrompido: preserva uma cópia para diagnóstico e tenta o backup.
            PreserveCorrupt(FilePath);
            if (File.Exists(BackupPath) && TryRead(BackupPath, out var fromBak2, out var bakIssues2))
                return new SettingsLoadResult(fromBak2!, [.. issues, .. bakIssues2, "restaurado do backup"], true, false);

            return new SettingsLoadResult(new DockSettings(), [.. issues, "padrões restaurados"], true, false);
        }
    }

    public void Save(DockSettings settings)
    {
        lock (_gate)
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(settings, SettingsJson.Options);
            string tmp = FilePath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                w.Write(json);
                w.Flush();
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
                File.Replace(tmp, FilePath, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(tmp, FilePath);
        }
    }

    /// <summary>Lê e valida um JSON de qualquer origem (inclusive importação). Nunca lança por conteúdo inválido.</summary>
    public static bool TryParse(string json, out DockSettings? settings, out List<string> issues)
    {
        issues = new List<string>();
        settings = null;
        try
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                MaxDepth = 16,
            });
            if (node is not JsonObject obj) { issues.Add("raiz do JSON não é um objeto"); return false; }

            int version = obj.TryGetPropertyValue("schemaVersion", out var v) && v is JsonValue jv && jv.TryGetValue(out int iv) ? iv : 0;
            if (version > DockSettings.CurrentSchemaVersion)
            {
                issues.Add($"schemaVersion {version} é mais nova que a suportada ({DockSettings.CurrentSchemaVersion})");
                return false;
            }
            SettingsMigrations.Migrate(obj, version, issues);

            settings = obj.Deserialize<DockSettings>(SettingsJson.Options);
            if (settings is null) { issues.Add("conteúdo vazio"); return false; }
            issues.AddRange(SettingsValidator.Sanitize(settings));
            return true;
        }
        catch (JsonException ex)
        {
            issues.Add("JSON inválido: " + ex.Message);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            issues.Add("JSON inválido: " + ex.Message);
            return false;
        }
    }

    private static bool TryRead(string path, out DockSettings? settings, out List<string> issues)
    {
        settings = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes) { issues = ["arquivo de configuração grande demais"]; return false; }
            return TryParse(File.ReadAllText(path, Encoding.UTF8), out settings, out issues);
        }
        catch (IOException ex) { issues = ["erro de leitura: " + ex.Message]; return false; }
        catch (UnauthorizedAccessException ex) { issues = ["sem permissão: " + ex.Message]; return false; }
    }

    private static void PreserveCorrupt(string path)
    {
        try
        {
            string dest = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, dest, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Migrações de esquema em JSON bruto (antes da desserialização).</summary>
public static class SettingsMigrations
{
    public static void Migrate(JsonObject obj, int fromVersion, List<string> issues)
    {
        if (fromVersion < 1)
        {
            // v0 = arquivo sem schemaVersion (pré-lançamento). Nada a renomear; apenas carimba a versão.
            issues.Add("migrado de esquema 0 para 1");
        }
        obj["schemaVersion"] = DockSettings.CurrentSchemaVersion;
    }
}
