using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NovaTaskbar.App.Services;
using NovaTaskbar.Core.Widgets;
using Windows.UI;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

/// <summary>
/// Notas rápidas (ideias do app Sticky Notes do GNOME, código próprio): várias notas, lista com busca e prévia,
/// editor com negrito/itálico/sublinhado/tachado e lista, excluir com "desfazer". Salva sozinho.
/// </summary>
internal sealed class NotesWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.Notes, iconSize, width, Glyphs.Note)
{
    private const int MaxNotes = 200, MaxChars = 100_000;

    private static readonly (string Id, string Name, Color Tint)[] Colors =
    [
        ("yellow", L.T("Yellow"), Color.FromArgb(255, 245, 204, 92)),
        ("green", L.T("Green"), Color.FromArgb(255, 126, 200, 140)),
        ("blue", L.T("Blue"), Color.FromArgb(255, 110, 168, 230)),
        ("purple", L.T("Purple"), Color.FromArgb(255, 176, 140, 230)),
        ("pink", L.T("Pink"), Color.FromArgb(255, 236, 140, 176)),
        ("gray", L.T("Gray"), Color.FromArgb(255, 150, 150, 156)),
    ];

    private StackPanel? _host;
    private string _query = "";
    private NoteItem? _deleted;          // última nota excluída (para desfazer)
    private int _deletedIndex;

    private List<NoteItem> Notes
    {
        get
        {
            var d = ctx.Store.Data;
            if (d.NoteList.Count == 0 && d.Notes.Trim().Length > 0) // migra a nota única antiga
                ctx.Store.Update(x => { x.NoteList.Add(new NoteItem { Text = x.Notes, Rtf = "" }); x.Notes = ""; });
            return d.NoteList;
        }
    }

    public override string Tooltip => Notes.Count switch
    {
        0 => L.T("Quick notes") + "\n" + L.T("Click to create a note"),
        1 => L.T("Notes: ") + Title(Notes[0]),
        var n => L.F("{0} notes · last: {1}", n, Title(Notes.OrderByDescending(x => x.Updated).First())),
    };

    public override void Update(WidgetData d) => SetBadge(Notes.Count > 1 ? Notes.Count.ToString(CultureInfo.InvariantCulture) : null);

    protected override FrameworkElement BuildPanel()
    {
        _host = new StackPanel { Width = 360, Padding = new Thickness(4) };
        _query = "";
        ShowList();
        return _host;
    }

    // ============================================================ lista

    private void ShowList()
    {
        if (_host is null) return;
        _host.Children.Clear();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = L.T("Notes"), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var add = IconButton("", L.T("New note"), NewNote);
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        _host.Children.Add(header);

        var search = new TextBox { PlaceholderText = L.T("Search notes"), Text = _query, Margin = new Thickness(0, 6, 0, 6) };
        var list = new StackPanel { Spacing = 6 };
        search.TextChanged += (_, _) => { _query = search.Text; FillList(list); };
        _host.Children.Add(search);

        if (_deleted != null)
        {
            var undo = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            undo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            undo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            undo.Children.Add(new TextBlock { Text = L.F("Note “{0}” deleted", Title(_deleted)), Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new HyperlinkButton { Content = L.T("Undo") };
            b.Click += (_, _) => Undo();
            Grid.SetColumn(b, 1);
            undo.Children.Add(b);
            _host.Children.Add(undo);
        }

        FillList(list);
        _host.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    private void FillList(StackPanel list)
    {
        list.Children.Clear();
        var notes = Notes.OrderByDescending(n => n.Updated)
            .Where(n => _query.Length == 0 || n.Text.Contains(_query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (Notes.Count == 0) { list.Children.Add(Hint(L.T("No notes yet. Click + to create one."))); return; }
        if (notes.Count == 0) { list.Children.Add(Hint(L.T("Nothing found."))); return; }
        foreach (var n in notes) list.Children.Add(Card(n));
    }

    private Button Card(NoteItem n)
    {
        var bar = new Border { Width = 0, Margin = new Thickness(0, 0, 6, 0) }; // visual minimalista: sem etiqueta de cor
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = Title(n), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string body = Body(n);
        if (body.Length > 0) text.Children.Add(new TextBlock { Text = body, Opacity = 0.75, FontSize = 12, MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = When(n.Updated), Opacity = 0.5, FontSize = 11 });
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(bar);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var del = IconButton("", L.T("Delete note"), () => Delete(n));
        del.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(del, 2);
        g.Children.Add(del);

        var card = new Button
        {
            Content = g, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 8, 4, 8),
        };
        card.Click += (_, _) => ShowEditor(n);
        return card;
    }

    // ============================================================ editor

    private void ShowEditor(NoteItem note)
    {
        if (_host is null) return;
        _host.Children.Clear();

        var editor = new RichEditBox { Height = 280, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = L.T("Write your note…") };
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        Load(editor, note);

        // Salvar com atraso curto (não grava o arquivo a cada tecla).
        var save = DispatcherQueue.GetForCurrentThread().CreateTimer();
        save.Interval = TimeSpan.FromMilliseconds(400);
        save.IsRepeating = false;
        save.Tick += (_, _) => Save(editor, note);
        editor.TextChanged += (_, _) => { save.Stop(); save.Start(); };
        editor.Unloaded += (_, _) => { if (save.IsRunning) { save.Stop(); Save(editor, note); } };

        // ---- barra superior: voltar · formatação · cor · excluir
        var top = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = IconButton("", L.T("All notes"), () => { save.Stop(); Save(editor, note); ShowList(); });
        top.Children.Add(back);

        var fmt = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        fmt.Children.Add(IconButton("", L.T("Bold (Ctrl+B)"), () => Toggle(editor, f => f.Bold = FormatEffect.Toggle)));
        fmt.Children.Add(IconButton("", L.T("Italic (Ctrl+I)"), () => Toggle(editor, f => f.Italic = FormatEffect.Toggle)));
        fmt.Children.Add(IconButton("", L.T("Underline (Ctrl+U)"), () => Toggle(editor, f => f.Underline = f.Underline == UnderlineType.None ? UnderlineType.Single : UnderlineType.None)));
        fmt.Children.Add(IconButton("", L.T("Strikethrough"), () => Toggle(editor, f => f.Strikethrough = FormatEffect.Toggle)));
        fmt.Children.Add(IconButton("", L.T("Bulleted list"), () => Bullets(editor)));
        Grid.SetColumn(fmt, 1);
        top.Children.Add(fmt);

        var del = IconButton("", L.T("Delete note"), () => { save.Stop(); Save(editor, note); Delete(note); });
        Grid.SetColumn(del, 2);
        top.Children.Add(del);

        var stamp = new TextBlock { Text = L.F("Edited {0}", When(note.Updated)), Opacity = 0.5, FontSize = 11, Margin = new Thickness(2, 6, 0, 0) };
        _host.Children.Add(top);
        _host.Children.Add(editor);
        _host.Children.Add(stamp);
        editor.Loaded += (_, _) =>
        {
            editor.Focus(FocusState.Programmatic);
            editor.Document.Selection.SetRange(int.MaxValue, int.MaxValue); // cursor no fim
        };
    }

    /// <summary>Botão com a cor atual; abre uma fileira de amostras (Border — Shapes saíam desbotadas no popup).</summary>
    private Button ColorButton(NoteItem note)
    {
        var dot = Swatch(Tint(note.Color), 14);
        var b = new Button { Content = dot, Padding = new Thickness(7), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
        ToolTipService.SetToolTip(b, L.T("Note color"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var flyout = new Flyout { Content = row };
        PanelStyle.Apply(flyout);
        foreach (var (id, name, tint) in Colors)
        {
            string c = id;
            var sw = new Button { Content = Swatch(tint, 20), Padding = new Thickness(3), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            ToolTipService.SetToolTip(sw, name);
            sw.Click += (_, _) =>
            {
                ctx.Store.Update(_ => { note.Color = c; note.Updated = DateTime.Now; });
                b.Content = Swatch(Tint(c), 14);
                flyout.Hide();
            };
            row.Children.Add(sw);
        }
        b.Flyout = flyout;
        return b;
    }

    private static Border Swatch(Color color, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = new SolidColorBrush(color),
    };

    private static void Toggle(RichEditBox editor, Action<Microsoft.UI.Text.ITextCharacterFormat> change)
    {
        var f = editor.Document.Selection.CharacterFormat;
        change(f);
        editor.Document.Selection.CharacterFormat = f;
        editor.Focus(FocusState.Programmatic);
    }

    private static void Bullets(RichEditBox editor)
    {
        var p = editor.Document.Selection.ParagraphFormat;
        p.ListType = p.ListType == MarkerType.Bullet ? MarkerType.None : MarkerType.Bullet;
        editor.Document.Selection.ParagraphFormat = p;
        editor.Focus(FocusState.Programmatic);
    }

    private static void Load(RichEditBox editor, NoteItem note)
    {
        var doc = editor.Document;
        if (note.Rtf.Length > 0) doc.SetText(TextSetOptions.FormatRtf, note.Rtf);
        else doc.SetText(TextSetOptions.None, note.Text);
        // O RTF salvo pode trazer a cor de texto do tema em que foi escrito: usa sempre a cor do tema atual.
        doc.GetText(TextGetOptions.None, out string all);
        doc.GetRange(0, all.Length).CharacterFormat.ForegroundColor = Palette.Theme == ElementTheme.Light ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        doc.ClearUndoRedoHistory();
    }

    private void Save(RichEditBox editor, NoteItem note)
    {
        editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
        editor.Document.GetText(TextGetOptions.None, out string text);
        text = text.TrimEnd('\r', '\n');
        if (text == note.Text && rtf == note.Rtf) return;
        if (text.Length > MaxChars) return; // muito grande: não grava (evita arquivo gigante)
        ctx.Store.Update(_ => { note.Rtf = rtf; note.Text = text; note.Updated = DateTime.Now; });
        ctx.Refresh();
    }

    // ============================================================ ações

    private void NewNote()
    {
        if (Notes.Count >= MaxNotes) return;
        var n = new NoteItem { Color = Colors[Notes.Count % Colors.Length].Id };
        ctx.Store.Update(x => x.NoteList.Add(n));
        ctx.Refresh();
        ShowEditor(n);
    }

    private void Delete(NoteItem n)
    {
        _deletedIndex = Notes.IndexOf(n);
        _deleted = n;
        ctx.Store.Update(x => x.NoteList.Remove(n));
        ctx.Refresh();
        ShowList();
    }

    private void Undo()
    {
        if (_deleted is null) return;
        var n = _deleted;
        _deleted = null;
        ctx.Store.Update(x => x.NoteList.Insert(Math.Clamp(_deletedIndex, 0, x.NoteList.Count), n));
        ctx.Refresh();
        ShowList();
    }

    // ============================================================ peças

    private static Button IconButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Padding = new Thickness(7),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static Color Tint(string id) => Colors.FirstOrDefault(c => c.Id == id).Tint is var t && t.A > 0 ? t : Colors[0].Tint;

    private static string Title(NoteItem n)
    {
        string first = n.Text.Split('\n', '\r').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return first.Length == 0 ? L.T("Empty note") : first.Length > 50 ? first[..50] + "…" : first;
    }

    private static string Body(NoteItem n)
    {
        var lines = n.Text.Split('\n', '\r').Select(l => l.Trim()).Where(l => l.Length > 0).Skip(1);
        return string.Join(" · ", lines);
    }

    private static string When(DateTime t)
    {
        var culture = L.Culture;
        if (t.Date == DateTime.Today) return L.F("Today, {0}", t.ToString("t", culture));
        if (t.Date == DateTime.Today.AddDays(-1)) return L.F("Yesterday, {0}", t.ToString("t", culture));
        return t.ToString("g", culture);
    }
}
