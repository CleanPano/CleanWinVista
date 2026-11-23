using NovaTaskbar.Core.Localization;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NovaTaskbar.App.Dock;
using NovaTaskbar.App.Services;
using NovaTaskbar.App.Settings;
using NovaTaskbar.Windows.Apps;

namespace NovaTaskbar.App;

/// <summary>
/// Ciclo de vida: instância única → serviços → configuração validada → apps padrão (1ª vez) → Dock.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\NovaTaskbar.SingleInstance";
    private const string ShowSettingsEventName = @"Local\NovaTaskbar.ShowSettings";

    private Mutex? _mutex;
    private EventWaitHandle? _showSettingsEvent;
    private ServiceProvider? _services;
    private ILogger<App>? _log;
    private DockWindow? _dock;
    private SettingsWindow? _settingsWindow;
    private DispatcherQueue? _ui;
    private bool _exiting;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            _log?.LogCritical(e.Exception, "Exceção não tratada no UI thread");
            if (_log is null) Setup.Installer.LogError("UI", e.Exception);
            e.Handled = true; // mantém o Dock vivo; o erro fica registrado no log
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            _log?.LogCritical(e.ExceptionObject as Exception, "Exceção fatal");
            if (_log is null && e.ExceptionObject is Exception fx) Setup.Installer.LogError("Fatal", fx);
        };
        // Diagnóstico: registra a pilha de qualquer falha de ativação COM/WinRT (uma vez por tipo).
        var seenActivation = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (_log is null && Environment.GetCommandLineArgs().Contains("--setup")) Setup.Installer.LogError("FirstChance", e.Exception);
            if (e.Exception is System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80040111) } c && seenActivation.TryAdd(c.Message, 0))
                _log?.LogWarning("Ativação COM falhou: {Message}\n{Stack}", c.Message, Environment.StackTrace);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log?.LogError(e.Exception, "Task não observada");
            if (_log is null) Setup.Installer.LogError("Task", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        // Instalador / desinstalador (mesmo executável, janela própria no visual do Dock).
        if (argv.Contains("--setup") || argv.Contains("--uninstall"))
        {
            Setup.SetupWindow.Run(argv.Contains("--uninstall"));
            return;
        }

        _mutex = new Mutex(true, MutexName, out bool isFirst);
        // Reinício (troca de idioma): espera a instância anterior fechar.
        for (int i = 0; !isFirst && argv.Contains("--restart") && i < 50; i++)
        {
            _mutex.Dispose();
            Thread.Sleep(200);
            _mutex = new Mutex(true, MutexName, out isFirst);
        }
        if (!isFirst)
        {
            // Já existe um Dock rodando: concede foco a ele, pede para abrir as configurações e encerra.
            int me = Environment.ProcessId;
            foreach (var other in Process.GetProcessesByName("NovaTaskbar"))
            {
                if (other.Id != me) NovaTaskbar.Windows.Interop.NativeMethods.AllowSetForegroundWindow(other.Id);
                other.Dispose();
            }
            try { using var ev = EventWaitHandle.OpenExisting(ShowSettingsEventName); ev.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Exit();
            return;
        }

        var sw = Stopwatch.StartNew();
        _ui = DispatcherQueue.GetForCurrentThread();
        _services = BuildServices();
        _log = _services.GetRequiredService<ILogger<App>>();
        _log.LogInformation("CleanWinVista {Version} iniciando (.NET {Runtime}, {OS})",
            typeof(App).Assembly.GetName().Version, Environment.Version, Environment.OSVersion.VersionString);

        // 1-2. Configuração carregada e validada (recupera de arquivo corrompido).
        var settings = _services.GetRequiredService<SettingsService>();
        settings.Load();
        // Idioma da interface (inglês por padrão; os outros 19 vêm de i18n\*.json).
        L.Load(settings.Current.Behavior.Language, Path.Combine(AppContext.BaseDirectory, "i18n"));
        if (!settings.Current.DefaultsSeeded)
        {
            settings.Update(s =>
            {
                if (s.PinnedApps.Count == 0) s.PinnedApps.AddRange(DefaultApps.Discover());
                s.DefaultsSeeded = true;
            });
            settings.SaveNow();
        }

        // 3-6. Dock.
        _dock = new DockWindow(
            settings,
            _services.GetRequiredService<IconService>(),
            _services.GetRequiredService<AppLauncher>(),
            _services.GetRequiredService<WindowTracker>(),
            _services.GetRequiredService<AppInfoProvider>(),
            _services.GetRequiredService<NovaTaskbar.App.Widgets.WidgetHost>(),
            _services.GetRequiredService<ILogger<DockWindow>>(),
            OpenSettings,
            Shutdown);
        _dock.Start();

        // 7. Serviço opcional: sinal de "abrir configurações" vindo de uma segunda instância.
        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        var listener = new Thread(() =>
        {
            while (!_exiting && _showSettingsEvent.WaitOne())
                if (!_exiting) _ui.TryEnqueue(OpenSettings);
        }) { IsBackground = true, Name = "Nova single-instance listener" };
        listener.Start();

        _log.LogInformation("Inicialização concluída em {Ms} ms", sw.ElapsedMilliseconds);

        if (Environment.GetCommandLineArgs().Contains("--settings")) OpenSettings();
        else if (!_services!.GetRequiredService<SettingsService>().Current.Behavior.StartupAsked) AskStartupOnFirstRun();
    }

    /// <summary>1º uso: abre as Configurações e pergunta se o app deve iniciar com o Windows.</summary>
    private void AskStartupOnFirstRun()
    {
        OpenSettings(); // o cartão "Iniciar com o Windows?" aparece no topo até ser respondido
    }

    private ServiceProvider BuildServices()
    {
        var paths = new AppPaths();
        var sc = new ServiceCollection();
        sc.AddSingleton(paths);
        sc.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddProvider(new FileLoggerProvider(paths.LogDirectory));
            b.SetMinimumLevel(LogLevel.Information);
        });
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton<IconService>();
        sc.AddSingleton<AppLauncher>();
        sc.AddSingleton<WindowTracker>();
        sc.AddSingleton<AppInfoProvider>();
        sc.AddSingleton<NovaTaskbar.Windows.SystemInfo.AudioVolume>();
        sc.AddSingleton<NovaTaskbar.Windows.SystemInfo.MediaWatcher>();
        sc.AddSingleton<WidgetStore>();
        sc.AddSingleton<SecretStore>();
        sc.AddSingleton<NetGate>();
        sc.AddSingleton<NovaTaskbar.App.Widgets.WidgetHost>();
        return sc.BuildServiceProvider();
    }

    private void OpenSettings()
    {
        if (_exiting || _services is null || _dock is null) return;
        if (_settingsWindow is not null)
        {
            _settingsWindow.BringToFront();
            return;
        }
        var settings = _services.GetRequiredService<SettingsService>();
        var vm = new SettingsViewModel(settings, _dock.PickAndPinAsync);
        _settingsWindow = new SettingsWindow(vm, settings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.BringToFront();
    }

    /// <summary>Reinicia o app (ex.: depois de trocar o idioma).</summary>
    internal static void Restart()
    {
        if (Current is not App app) return;
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart --settings") { UseShellExecute = false }); }
        catch (System.ComponentModel.Win32Exception) { return; }
        app.Shutdown();
    }

    private void Shutdown()
    {
        if (_exiting) return;
        _exiting = true;
        _log?.LogInformation("Encerrando");
        try
        {
            _services?.GetService<SettingsService>()?.SaveNow();
            _settingsWindow?.Close();
            _dock?.Shutdown();
            _showSettingsEvent?.Set();
        }
        finally
        {
            _services?.Dispose();
            _mutex?.ReleaseMutex();
            Exit();
        }
    }
}
