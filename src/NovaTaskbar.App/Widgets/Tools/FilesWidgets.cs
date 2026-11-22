using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NovaTaskbar.Core.Tools;
using NovaTaskbar.Core.Widgets;
using NovaTaskbar.Windows.Apps;
using NovaTaskbar.Windows.SystemInfo;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using NovaTaskbar.Core.Localization;

namespace NovaTaskbar.App.Widgets;

// ============================================================================================================
// Lote 3 — arquivos e acesso rápido: downloads, prateleira, favoritos, busca, alternador de apps, fotos.
// ============================================================================================================

/// <summary>Últimos arquivos da pasta Downloads; clique abre, clique direito mostra na pasta.</summary>
internal sealed class DownloadsWidget(double iconSize, double width) : PanelWidget(WidgetKind.Downloads, iconSize, width, Glyphs.Download)
{
    public override string Tooltip => L.T("Recent downloads");
    public override void Update(WidgetData d) { }

    protected override FrameworkElement BuildPanel()
    {
        string folder = ScreenTools.DownloadsFolder();
        var p = Panel(L.T("Downloads"), 340);
        var list = new StackPanel { Spacing = 2 };
        try
        {
            var files = new DirectoryInfo(folder).EnumerateFiles().Where(f => !f.Attributes.HasFlag(System.IO.FileAttributes.Hidden) && !f.Name.EndsWith(".crdownload") && !f.Name.EndsWith(".part"))
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(12).ToList();
            if (files.Count == 0) p.Children.Add(Hint(L.T("No files in Downloads.")));
            foreach (var f in files) list.Children.Add(FileRow(f.FullName, $"{f.LastWriteTime.ToString("g", L.Culture)} · {Size(f.Length)}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { p.Children.Add(Hint(L.T("Could not read the Downloads folder."))); }
        p.Children.Add(Scroll(list, 340));
        p.Children.Add(Btn(L.T("Open folder"), () => WidgetActions.OpenFile(folder), ""));
        return p;
    }

    internal static Button FileRow(string path, string detail)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis });
        sp.Children.Add(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.65 });
        var b = new Button { Content = sp, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
        b.Click += (_, _) => WidgetActions.OpenFile(path);
        b.RightTapped += (_, e) => { WidgetActions.Reveal(path); e.Handled = true; };
        // Arrastar o arquivo para fora (para um e-mail, outra pasta…).
        b.CanDrag = true;
        b.DragStarting += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                if (File.Exists(path)) e.Data.SetStorageItems([await StorageFile.GetFileFromPathAsync(path)]);
                e.Data.RequestedOperation = DataPackageOperation.Copy;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
            finally { deferral.Complete(); }
        };
        return b;
    }

    internal static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.0} MB",
        _ => $"{bytes / 1073741824.0:0.0} GB",
    };
}

/// <summary>Prateleira: solte arquivos no ícone para guardá-los à mão; depois arraste-os para onde quiser.</summary>
internal sealed class FileShelfWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.FileShelf, iconSize, width, Glyphs.Shelf)
{
    private IReadOnlyList<string> Items => ctx.Store.Data.Shelf.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();

    public override string Tooltip => Items.Count == 0 ? L.T("File shelf") + "\n" + L.T("Drop files here to keep them") : L.F("Shelf: {0} item(s)", Items.Count);

    public override void Update(WidgetData d) => SetBadge(Items.Count > 0 ? Items.Count.ToString() : null);

    public override bool AcceptsDrops => true;

    public override bool AcceptDrop(IReadOnlyList<string> paths)
    {
        var ok = paths.Where(p => System.IO.Path.IsPathFullyQualified(p) && (File.Exists(p) || Directory.Exists(p))).ToList();
        if (ok.Count == 0) return false;
        ctx.Store.Update(x => { foreach (var p in ok) if (!x.Shelf.Contains(p, StringComparer.OrdinalIgnoreCase)) x.Shelf.Add(p); if (x.Shelf.Count > 50) x.Shelf.RemoveRange(0, x.Shelf.Count - 50); });
        ctx.Refresh();
        return true;
    }

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Shelf"), 340);
        var list = new StackPanel { Spacing = 2 };
        foreach (var path in Items)
        {
            string item = path;
            var row = DownloadsWidget.FileRow(item, System.IO.Path.GetDirectoryName(item) ?? "");
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(row);
            var del = Btn("", () => { ctx.Store.Update(x => x.Shelf.RemoveAll(s => string.Equals(s, item, StringComparison.OrdinalIgnoreCase))); ctx.Refresh(); list.Children.Remove(g); }, "");
            Grid.SetColumn(del, 1);
            g.Children.Add(del);
            list.Children.Add(g);
        }
        p.Children.Add(Items.Count == 0 ? Hint(L.T("Drag files from File Explorer onto the shelf icon. Then drag them from here wherever you want.")) : Scroll(list, 320));
        if (Items.Count > 0) p.Children.Add(Btn(L.T("Empty shelf"), () => { ctx.Store.Update(x => x.Shelf.Clear()); ctx.Refresh(); list.Children.Clear(); }, ""));
        return p;
    }
}

/// <summary>Favoritos de links (abre no navegador padrão; só http/https).</summary>
internal sealed class BookmarksWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.Bookmarks, iconSize, width, Glyphs.Star)
{
    public override string Tooltip => L.F("Bookmarks · {0} link(s)", ctx.Store.Data.Bookmarks.Count);
    public override void Update(WidgetData d) { }

    public override void AddMenuItems(MenuFlyout menu)
    {
        foreach (var b in ctx.Store.Data.Bookmarks.Take(15))
        {
            var bm = b;
            menu.Items.Add(Item(bm.Title, "", () => WidgetActions.OpenUrl(bm.Url)));
        }
    }

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Bookmarks"), 340);
        var list = new StackPanel { Spacing = 2 };
        void Render()
        {
            list.Children.Clear();
            foreach (var b in ctx.Store.Data.Bookmarks.ToList())
            {
                var bm = b;
                var open = new HyperlinkButton { Content = bm.Title, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                ToolTipService.SetToolTip(open, bm.Url);
                open.Click += (_, _) => WidgetActions.OpenUrl(bm.Url);
                var del = Btn("", () => { ctx.Store.Update(x => x.Bookmarks.Remove(bm)); Render(); }, "");
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(open);
                Grid.SetColumn(del, 1);
                g.Children.Add(del);
                list.Children.Add(g);
            }
        }
        var title = new TextBox { PlaceholderText = L.T("Name (optional)") };
        var url = new TextBox { PlaceholderText = "https://…" };
        var error = Hint("");
        void Add()
        {
            string u = UrlRules.Normalize(url.Text);
            if (!UrlRules.IsSafeWebUrl(u)) { error.Text = L.T("Invalid address (use http or https)."); return; }
            string t = string.IsNullOrWhiteSpace(title.Text) ? new Uri(u).Host : title.Text.Trim();
            ctx.Store.Update(x => x.Bookmarks.Add(new Services.BookmarkItem { Title = t.Length > 80 ? t[..80] : t, Url = u }));
            title.Text = url.Text = error.Text = "";
            Render();
        }
        url.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Enter) Add(); };
        Render();
        p.Children.Add(Scroll(list, 260));
        p.Children.Add(title); p.Children.Add(url);
        p.Children.Add(Row(Btn(L.T("Add"), Add, "", accent: true)));
        p.Children.Add(error);
        return p;
    }
}

/// <summary>Busca: apps do menu Iniciar (abre direto) e arquivos (abre a busca do Explorador).</summary>
internal sealed class SearchWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.Search, iconSize, width, Glyphs.Search)
{
    private static IReadOnlyList<(string Name, string Path)>? s_apps;

    public override string Tooltip => L.T("Search apps and files");
    public override void Update(WidgetData d) { }

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Search"), 360);
        var box = new TextBox { PlaceholderText = L.T("App or file name…"), FontSize = 15 };
        var results = new StackPanel { Spacing = 2 };
        s_apps ??= StartMenuApps();
        Action? first = null;
        void Run()
        {
            results.Children.Clear();
            first = null;
            string q = box.Text.Trim();
            if (q.Length == 0) return;
            foreach (var (name, path) in s_apps.Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                         .OrderBy(a => a.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ThenBy(a => a.Name).Take(8))
            {
                var b = new Button { Content = name, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                string target = path, label = name;
                Action launch = () => ctx.Launcher.Launch(target, label);
                first ??= launch;
                b.Click += (_, _) => launch();
                results.Children.Add(b);
            }
            var files = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = L.F("Search “{0}” in files (File Explorer)", q) };
            Action search = () => WidgetActions.Open("search-ms:query=" + Uri.EscapeDataString(q) + "&crumb=location:" + Uri.EscapeDataString(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            first ??= search;
            files.Click += (_, _) => search();
            results.Children.Add(files);
        }
        box.TextChanged += (_, _) => Run();
        box.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Enter) first?.Invoke(); };
        box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        p.Children.Add(box);
        p.Children.Add(Scroll(results, 320));
        return p;
    }

    /// <summary>Atalhos (.lnk) do menu Iniciar do usuário e de todos os usuários.</summary>
    private static IReadOnlyList<(string, string)> StartMenuApps()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };
        var list = new List<(string, string)>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(lnk);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("desinstalar", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add((name, lnk));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list.DistinctBy(a => a.Item1, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>Alternador: lista as janelas abertas; clique traz para frente.</summary>
internal sealed class AppSwitcherWidget(double iconSize, double width, WidgetContext ctx) : PanelWidget(WidgetKind.AppSwitcher, iconSize, width, Glyphs.AppWindows)
{
    public override string Tooltip => L.F("Open windows: {0}", ctx.Windows().Count);
    public override void Update(WidgetData d) => SetBadge(ctx.Windows().Count is > 0 and var n ? n.ToString() : null);

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Open windows"), 360);
        var list = new StackPanel { Spacing = 2 };
        foreach (var w in ctx.Windows().OrderBy(w => w.Title))
        {
            var win = w;
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(win.Title) ? L.T("(untitled)") : win.Title, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = System.IO.Path.GetFileNameWithoutExtension(win.ExePath ?? win.Aumid ?? "") + (win.IsMinimized ? " · " + L.T("minimized") : ""), FontSize = 11, Opacity = 0.65 });
            var b = new Button { Content = sp, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            b.Click += (_, _) => ctx.Activate(win);
            list.Children.Add(b);
        }
        if (list.Children.Count == 0) p.Children.Add(Hint(L.T("No open windows.")));
        p.Children.Add(Scroll(list, 380));
        return p;
    }
}

/// <summary>Fotos recentes da pasta Imagens (miniaturas; clique abre no app de fotos).</summary>
internal sealed class PhotosWidget(double iconSize, double width) : PanelWidget(WidgetKind.Photos, iconSize, width, Glyphs.Photo)
{
    private static readonly string[] Exts = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".heic"];

    public override string Tooltip => L.T("Recent photos (Pictures folder)");
    public override void Update(WidgetData d) { }

    protected override FrameworkElement BuildPanel()
    {
        var p = Panel(L.T("Recent photos"), 360);
        string root = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 84, ItemHeight = 84, MaximumRowsOrColumns = 4 };
        try
        {
            var files = new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true })
                .Where(f => Exts.Contains(f.Extension.ToLowerInvariant())).OrderByDescending(f => f.LastWriteTimeUtc).Take(16).ToList();
            foreach (var f in files)
            {
                string path = f.FullName;
                var img = new Image { Stretch = Stretch.UniformToFill, Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 160 } };
                var b = new Button { Content = img, Padding = new Thickness(0), Width = 80, Height = 80, CornerRadius = new CornerRadius(8) };
                ToolTipService.SetToolTip(b, f.Name);
                b.Click += (_, _) => WidgetActions.OpenFile(path);
                grid.Children.Add(b);
            }
            if (files.Count == 0) p.Children.Add(Hint(L.T("No photos in the Pictures folder.")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { p.Children.Add(Hint(L.T("Could not read the Pictures folder."))); }
        p.Children.Add(Scroll(grid, 360));
        p.Children.Add(Btn(L.T("Open Pictures folder"), () => WidgetActions.OpenFile(root), ""));
        return p;
    }
}
