using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Services;

/// <summary>
/// Traduz uma árvore XAML escrita em inglês (Text, Content, Header, PlaceholderText, OnContent/OffContent, dicas e
/// itens de ComboBox) para o idioma atual, e aplica direita→esquerda (árabe, urdu, persa).
/// </summary>
internal static class Localizer
{
    public static void Apply(DependencyObject root)
    {
        if (root is FrameworkElement fe && L.IsRightToLeft) fe.FlowDirection = FlowDirection.RightToLeft;
        if (L.Current.Code == "en") return;
        Walk(root, 0);
    }

    private static void Walk(DependencyObject d, int depth)
    {
        if (depth > 200) return;
        Translate(d);
        int n = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i), depth + 1);
        // Conteúdo que não está na árvore visual ainda (itens de ComboBox fechado)
        if (d is ItemsControl ic && d is not ComboBox)
            foreach (var item in ic.Items) if (item is DependencyObject o && VisualTreeHelper.GetParent(o) is null) Walk(o, depth + 1);
    }

    private static void Translate(DependencyObject d)
    {
        switch (d)
        {
            case TextBlock t when !string.IsNullOrEmpty(t.Text): t.Text = L.T(t.Text); break;
            case ComboBox c:
                if (c.Header is string ch) c.Header = L.T(ch);
                if (c.PlaceholderText is { Length: > 0 } cp) c.PlaceholderText = L.T(cp);
                foreach (var item in c.Items) if (item is ComboBoxItem { Content: string s } ci) ci.Content = L.T(s);
                break;
            case ToggleSwitch ts:
                if (ts.Header is string th) ts.Header = L.T(th);
                if (ts.OnContent is string on) ts.OnContent = L.T(on);
                if (ts.OffContent is string off) ts.OffContent = L.T(off);
                break;
            case Slider sl when sl.Header is string sh: sl.Header = L.T(sh); break;
            case TextBox tb:
                if (tb.Header is string tbh) tb.Header = L.T(tbh);
                if (tb.PlaceholderText is { Length: > 0 } tp) tb.PlaceholderText = L.T(tp);
                break;
            case NumberBox nb when nb.Header is string nh: nb.Header = L.T(nh); break;
            case RadioButtons rb when rb.Header is string rh: rb.Header = L.T(rh); break;
            case ContentControl cc when cc.Content is string cs: cc.Content = L.T(cs); break;
        }
        if (ToolTipService.GetToolTip(d) is string tip) ToolTipService.SetToolTip(d, L.T(tip));
    }
}
