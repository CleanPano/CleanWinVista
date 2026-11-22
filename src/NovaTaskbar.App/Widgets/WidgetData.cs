using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.SystemInfo;

namespace NovaTaskbar.App.Widgets;

/// <summary>Dados mais recentes coletados pelo <see cref="WidgetHost"/>; null = indisponível.</summary>
internal sealed class WidgetData
{
    /// <summary>Amostras guardadas por métrica (a cada 2 s → 80 s de histórico).</summary>
    public const int HistoryLength = 40;

    public DateTime Now;
    public double? Cpu;
    public MemoryInfo? Memory;
    public (double Down, double Up)? Network;
    public BatteryInfo Battery;
    public AudioVolume.State Volume;
    public MediaState Media = MediaState.None;
    public ImageSource? MediaArt;
    public IReadOnlyList<KeyboardLanguage> Keyboards = [];
    public IntPtr KeyboardCurrent;
    public IReadOnlyList<PeripheralBattery> Devices = [];

    public History CpuHistory { get; } = new(HistoryLength);
    public History RamHistory { get; } = new(HistoryLength);
    public History DownHistory { get; } = new(HistoryLength);
    public History UpHistory { get; } = new(HistoryLength);
}
