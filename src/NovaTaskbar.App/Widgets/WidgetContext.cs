using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using NovaTaskbar.App.Services;
using NovaTaskbar.Core.Apps;
using NovaTaskbar.Windows.Apps;
using NovaTaskbar.Windows.SystemInfo;

namespace NovaTaskbar.App.Widgets;

/// <summary>Serviços que os widgets de ferramenta usam (montado pelo WidgetHost).</summary>
internal sealed class WidgetContext
{
    public required WidgetStore Store { get; init; }
    public required SecretStore Secrets { get; init; }
    public required NetGate Net { get; init; }
    public required AudioVolume Audio { get; init; }
    public required AppLauncher Launcher { get; init; }
    public required SettingsService Settings { get; init; }
    public required ILogger Log { get; init; }
    public required DispatcherQueue UI { get; init; }
    /// <summary>Janelas abertas (do rastreador do Dock).</summary>
    public Func<IReadOnlyList<WindowInfo>> Windows { get; set; } = () => [];
    /// <summary>Traz uma janela para frente (mesma lógica do clique no ícone).</summary>
    public Action<WindowInfo> Activate { get; set; } = _ => { };
    /// <summary>Pede ao host para atualizar os widgets agora.</summary>
    public required Action Refresh { get; init; }
}
