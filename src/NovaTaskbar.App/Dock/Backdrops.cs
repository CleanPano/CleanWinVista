using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Windows.Interop;
using NovaTaskbar.Core.Configuration;
using Windows.UI;

namespace NovaTaskbar.App.Dock;

/// <summary>Garante um global::Windows.System.DispatcherQueue no UI thread (exigido por global::Windows.UI.Composition).</summary>
internal static class SystemDispatcherQueue
{
    private static IntPtr _controller;

    public static void Ensure()
    {
        if (global::Windows.System.DispatcherQueue.GetForCurrentThread() != null || _controller != IntPtr.Zero) return;
        var options = new NativeMethods.DispatcherQueueOptions
        {
            dwSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DispatcherQueueOptions>(),
            threadType = 2,    // DQTYPE_THREAD_CURRENT
            apartmentType = 2, // DQTAT_COM_STA
        };
        NativeMethods.CreateDispatcherQueueController(options, out _controller);
    }
}

/// <summary>
/// Fundo totalmente transparente para a janela dos ícones: o desktop aparece por trás.
/// Isto é transparência de janela (composição DWM), não desfoque.
/// </summary>
internal sealed class TransparentBackdrop : SystemBackdrop
{
    private static global::Windows.UI.Composition.Compositor? s_compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        SystemDispatcherQueue.Ensure();
        s_compositor ??= new global::Windows.UI.Composition.Compositor();
        connectedTarget.SystemBackdrop = s_compositor.CreateColorBrush(Color.FromArgb(0, 255, 255, 255));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        disconnectedTarget.SystemBackdrop = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }

    // A implementação base tenta reaplicar a configuração padrão ao trocar o tema e lança
    // ArgumentException ("The parameter is incorrect") para este pincel próprio. Nada a fazer: é transparente.
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot) { }
}

/// <summary>
/// Material de vidro configurável em tempo real: Desktop Acrylic (Base/Thin) ou Mica Alt,
/// usando os controladores oficiais do Windows App SDK. A janela é sempre tratada como "ativa"
/// para o Acrylic não cair para a cor sólida quando outro app tem o foco.
/// </summary>
internal sealed class GlassBackdrop : SystemBackdrop
{
    private readonly SystemBackdropConfiguration _config = new() { IsInputActive = true };
    private ICompositionSupportsSystemBackdrop? _target;
    private DesktopAcrylicController? _acrylic;
    private MicaController? _mica;

    private GlassMaterial _material = GlassMaterial.Acrylic;
    private Color? _tint;
    private float _tintOpacity = 0.15f, _luminosity = 0.35f;
    private bool _dark;

    public static bool IsAcrylicSupported => DesktopAcrylicController.IsSupported();
    public static bool IsMicaSupported => MicaController.IsSupported();

    public void Configure(GlassMaterial material, Color? tint, double tintOpacity, double luminosity, bool dark)
    {
        bool rebuild = KindOf(material) != KindOf(_material) || (_acrylic is null && _mica is null);
        _material = material;
        _tint = tint;
        _tintOpacity = (float)tintOpacity;
        _luminosity = (float)luminosity;
        _dark = dark;
        if (_target is null) return;
        if (rebuild) Rebuild();
        UpdateProperties();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _target = connectedTarget;
        Rebuild();
        UpdateProperties();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        Teardown();
        _target = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }

    // O tema/estado é controlado por Configure() (sempre "ativo"); ignorar o padrão do XAML evita a exceção
    // da implementação base ao trocar o tema e impede que o vidro volte para o tema errado.
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot) { }

    private static int KindOf(GlassMaterial m) => m == GlassMaterial.Mica ? 2 : 1;

    private void Rebuild()
    {
        Teardown();
        if (_target is null) return;
        if (_material == GlassMaterial.Mica)
        {
            _mica = new MicaController { Kind = MicaKind.BaseAlt };
            _mica.SetSystemBackdropConfiguration(_config);
            _mica.AddSystemBackdropTarget(_target);
        }
        else
        {
            _acrylic = new DesktopAcrylicController();
            _acrylic.SetSystemBackdropConfiguration(_config);
            _acrylic.AddSystemBackdropTarget(_target);
        }
    }

    private void UpdateProperties()
    {
        _config.Theme = _dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        // Cor automática explícita por tema: não depende da propagação do tema para o controlador.
        Color c = _tint ?? (_dark ? Color.FromArgb(255, 30, 30, 32) : Color.FromArgb(255, 243, 243, 245));
        if (_acrylic != null)
        {
            _acrylic.ResetProperties();
            _acrylic.Kind = _material == GlassMaterial.AcrylicThin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base;
            _acrylic.TintColor = c;
            _acrylic.FallbackColor = c;
            _acrylic.TintOpacity = _tintOpacity;
            _acrylic.LuminosityOpacity = _luminosity;
        }
        else if (_mica != null)
        {
            _mica.ResetProperties();
            _mica.Kind = MicaKind.BaseAlt;
            _mica.TintColor = c;
            _mica.FallbackColor = c;
            _mica.TintOpacity = _tintOpacity;
            _mica.LuminosityOpacity = _luminosity;
        }
    }

    private void Teardown()
    {
        if (_acrylic != null)
        {
            if (_target != null) _acrylic.RemoveSystemBackdropTarget(_target);
            _acrylic.Dispose();
            _acrylic = null;
        }
        if (_mica != null)
        {
            if (_target != null) _mica.RemoveSystemBackdropTarget(_target);
            _mica.Dispose();
            _mica = null;
        }
    }
}

internal static class ThemeHelper
{
    private static readonly global::Windows.UI.ViewManagement.UISettings s_ui = new();

    public static bool SystemIsDark
    {
        get
        {
            var bg = s_ui.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Background);
            return bg.R + bg.G + bg.B < 384;
        }
    }

    public static bool IsDark(ThemePreference pref) => pref switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => SystemIsDark,
    };

    public static Color? ParseTint(string hex) =>
        SettingsValidator.TryParseHexColor(hex, out var r, out var g, out var b) ? Color.FromArgb(255, r, g, b) : null;

    public static event EventHandler? SystemThemeChanged
    {
        add => s_handlers += value;
        remove => s_handlers -= value;
    }

    private static EventHandler? s_handlers;

    static ThemeHelper()
    {
        s_ui.ColorValuesChanged += (_, _) => s_handlers?.Invoke(null, EventArgs.Empty);
    }
}
