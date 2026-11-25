using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

/// <summary>Nomes e ícones (Segoe Fluent) mostrados na interface para widgets e estilos.</summary>
internal static class WidgetLabels
{
    public static string Name(WidgetKind kind) => kind switch
    {
        WidgetKind.Media => L.T("Music (only while playing)"),
        WidgetKind.System => L.T("CPU and RAM"),
        WidgetKind.Network => L.T("Network"),
        WidgetKind.Volume => L.T("Volume"),
        WidgetKind.Battery => L.T("Battery (laptops only)"),
        WidgetKind.Focus => L.T("Focus timer"),
        WidgetKind.Clock => L.T("Clock and calendar"),
        WidgetKind.Keyboard => L.T("Keyboard language (2+ languages)"),
        WidgetKind.Devices => L.T("Peripherals and battery (Bluetooth)"),
        WidgetKind.Clipboard => L.T("Clipboard (history)"),
        WidgetKind.Screenshot => L.T("Screenshot"),
        WidgetKind.ColorPicker => L.T("Color picker (screen color)"),
        WidgetKind.Emoji => L.T("Emoji"),
        WidgetKind.Converter => L.T("Unit and currency converter"),
        WidgetKind.Calculator => L.T("Calculator"),
        WidgetKind.Stopwatch => L.T("Stopwatch"),
        WidgetKind.Timer => L.T("Timer"),
        WidgetKind.Notes => L.T("Quick notes"),
        WidgetKind.Todos => L.T("To-dos"),
        WidgetKind.Water => L.T("Water reminder"),
        WidgetKind.Posture => L.T("Posture reminder"),
        WidgetKind.Progress => L.T("Day/week/month/year progress"),
        WidgetKind.AnalogClock => L.T("Analog clock"),
        WidgetKind.WorldClock => L.T("World clock"),
        WidgetKind.Downloads => L.T("Recent downloads"),
        WidgetKind.FileShelf => L.T("File shelf"),
        WidgetKind.Bookmarks => L.T("Bookmarks (links)"),
        WidgetKind.Search => L.T("Search apps and files"),
        WidgetKind.AppSwitcher => L.T("Open windows"),
        WidgetKind.Photos => L.T("Recent photos"),
        WidgetKind.QuickToggles => L.T("Quick toggles (Wi-Fi, BT, theme, mic)"),
        WidgetKind.VoiceMemo => L.T("Voice recorder"),
        WidgetKind.Weather => L.T("Weather (internet)"),
        WidgetKind.Calendar => L.T("iCal agenda (internet)"),
        WidgetKind.Services => L.T("Services: GitHub, Stripe… (internet)"),
        WidgetKind.Power => L.T("Power (lock, sleep, restart, shut down)"),
        _ => kind.ToString(),
    };

    public static string Glyph(WidgetKind kind) => kind switch
    {
        WidgetKind.Media => "",
        WidgetKind.System => "",
        WidgetKind.Network => "",
        WidgetKind.Volume => "",
        WidgetKind.Battery => "",
        WidgetKind.Focus => "",
        WidgetKind.Keyboard => "\uE765",
        WidgetKind.Devices => "\uE702",
        WidgetKind.Clipboard => Glyphs.Clipboard,
        WidgetKind.Screenshot => Glyphs.Camera,
        WidgetKind.ColorPicker => Glyphs.Eyedropper,
        WidgetKind.Emoji => Glyphs.Emoji,
        WidgetKind.Converter => Glyphs.Convert,
        WidgetKind.Calculator => Glyphs.Calculator,
        WidgetKind.Stopwatch => Glyphs.Stopwatch,
        WidgetKind.Timer => Glyphs.Timer,
        WidgetKind.Notes => Glyphs.Note,
        WidgetKind.Todos => Glyphs.Checklist,
        WidgetKind.Water => Glyphs.Cup,
        WidgetKind.Posture => Glyphs.Walk,
        WidgetKind.Progress => Glyphs.Progress,
        WidgetKind.AnalogClock => Glyphs.AnalogClock,
        WidgetKind.WorldClock => Glyphs.WorldClock,
        WidgetKind.Downloads => Glyphs.Download,
        WidgetKind.FileShelf => Glyphs.Shelf,
        WidgetKind.Bookmarks => Glyphs.Star,
        WidgetKind.Search => Glyphs.Search,
        WidgetKind.AppSwitcher => Glyphs.AppWindows,
        WidgetKind.Photos => Glyphs.Photo,
        WidgetKind.QuickToggles => Glyphs.Toggles,
        WidgetKind.VoiceMemo => Glyphs.Mic,
        WidgetKind.Weather => Glyphs.Weather,
        WidgetKind.Calendar => Glyphs.Calendar,
        WidgetKind.Services => Glyphs.Services,
        WidgetKind.Power => Glyphs.Power,
        _ => "",
    };

    public static string Style(WidgetStyle style) => style switch
    {
        WidgetStyle.Ring => L.T("Rings"),
        WidgetStyle.Line => L.T("Line chart"),
        WidgetStyle.Bars => L.T("Bar chart"),
        WidgetStyle.Gauge => L.T("Gauge"),
        _ => L.T("Numbers only"),
    };
}
