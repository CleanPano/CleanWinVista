using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using NovaTaskbar.Core.Configuration;

namespace NovaTaskbar.App.Services;

/// <summary>Configuração atual em memória + gravação com atraso (debounce) em disco.</summary>
internal sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly ILogger<SettingsService> _log;
    private DispatcherQueueTimer? _saveTimer;

    public SettingsService(AppPaths paths, ILogger<SettingsService> log)
    {
        _log = log;
        _store = new SettingsStore(paths.SettingsFile);
        Current = new DockSettings();
    }

    public DockSettings Current { get; private set; }

    /// <summary>Disparado no UI thread após qualquer alteração.</summary>
    public event EventHandler? Changed;

    public SettingsLoadResult Load()
    {
        var r = _store.Load();
        Current = r.Settings;
        foreach (var issue in r.Issues) _log.LogWarning("Configuração: {Issue}", issue);
        if (r.RecoveredFromCorruption) _log.LogWarning("Configuração recuperada após arquivo inválido.");
        return r;
    }

    /// <summary>Aplica uma alteração, valida, notifica e agenda gravação.</summary>
    public void Update(Action<DockSettings> mutate)
    {
        mutate(Current);
        foreach (var issue in SettingsValidator.Sanitize(Current)) _log.LogInformation("Ajuste: {Issue}", issue);
        Changed?.Invoke(this, EventArgs.Empty);
        ScheduleSave();
    }

    public void SaveNow()
    {
        _saveTimer?.Stop();
        try { _store.Save(Current); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Falha ao gravar configuração");
        }
    }

    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            var q = DispatcherQueue.GetForCurrentThread();
            if (q is null) { SaveNow(); return; }
            _saveTimer = q.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => SaveNow();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Dispose() => _saveTimer?.Stop();
}

internal sealed class AppPaths
{
    public AppPaths()
    {
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaTaskbar");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LogDirectory => Path.Combine(Root, "logs");
}
