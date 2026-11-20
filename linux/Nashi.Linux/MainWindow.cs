using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Nashi.Core;

namespace Nashi.Linux;

public sealed class MainWindow : Window
{
    private readonly List<string> files = [];
    private readonly LibraryStore library;
    private readonly Toolchain tools;
    private readonly StackPanel home = new() { Spacing = 10 }, libraryPage = new() { Spacing = 12 };
    private readonly StackPanel recent = new() { Spacing = 8 }, libraryRows = new() { Spacing = 8 }, selected = new() { Spacing = 4 };
    private readonly TextBlock selection = Label("Güzel bir şey bırak.", 23), status = Label("Dosyaların bilgisayarında kalır.", 12, true);
    private readonly TextBox search = new() { Watermark = "Dosya veya işlem ara" };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 4, IsVisible = false };
    private readonly Button cancel;
    private readonly Button wheel;
    private CancellationTokenSource? operation;
    private bool dark = true;
    private IBrush Surface => Brush(dark ? "#1C281F" : "#FFFEF7");
    private IBrush Line => Brush(dark ? "#344237" : "#DCDDD1");
    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
    internal static TextBlock Label(string text, double size = 14, bool muted = false) => new()
    {
        Text = text, FontSize = size, FontWeight = size >= 17 ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap, Opacity = muted ? .75 : 1, VerticalAlignment = VerticalAlignment.Center
    };

    public MainWindow(string[]? initial = null, string? dataDirectory = null)
    {
        library = new(dataDirectory);
        tools = new(Path.Combine(library.DirectoryPath, "Tools", "v1"), Environment.GetEnvironmentVariable("PATH"));
        dark = library.Appearance != "light";
        Title = "Nashi · Fedora"; Width = 820; Height = 780; MinWidth = 700; MinHeight = 580;
        using (var icon = AssetLoader.Open(new Uri("avares://Nashi.Linux/Assets/NashiIcon.png"))) Icon = new WindowIcon(icon);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ApplyTheme();
        var root = new DockPanel { Margin = new Thickness(28, 18) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var nav = Row();
        nav.Children.Add(Button("Araç kurulumu", Setup));
        nav.Children.Add(Button("Ana sayfa", () => { ShowLibrary(false); return Task.CompletedTask; }));
        nav.Children.Add(Button("Kitaplık", () => { ShowLibrary(true); return Task.CompletedTask; }));
        nav.Children.Add(Button("Görünüm", () => { dark = !dark; library.SetAppearance(dark ? "dark" : "light"); ApplyTheme(); return Task.CompletedTask; }));
        DockPanel.SetDock(nav, Dock.Right); header.Children.Add(nav);
        var brand = Row(); brand.Children.Add(Artwork("NashiMark", 40)); brand.Children.Add(Label("Nashi", 20)); header.Children.Add(brand);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        cancel = Button("İptal", () => { operation?.Cancel(); return Task.CompletedTask; }); cancel.IsVisible = false;
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        DockPanel.SetDock(progress, Dock.Bottom); root.Children.Add(progress);
        var pages = new Grid(); pages.Children.Add(home); pages.Children.Add(libraryPage);
        root.Children.Add(new ScrollViewer { Content = pages, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = root;

        home.Children.Add(Label("Dosyaların. Bir sonraki halleri.", 30));
        home.Children.Add(Label("Dönüştür, ince ayar yap ve yoluna gönder.", 14, true));
        var drop = new StackPanel { Spacing = 12 };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,166") };
        var caption = new StackPanel { Spacing = 12 };
        caption.Children.Add(new TextBlock { Text = "✦  BURADAN BAŞLA", Foreground = Brush("#E8BF59"), FontSize = 11 });
        caption.Children.Add(selection);
        caption.Children.Add(Label("Görseller, videolar, belgeler ve aralarındaki küçük şeyler.", 12, true));
        var picks = new WrapPanel { Orientation = Orientation.Horizontal };
        picks.Children.Add(Button("Dosya seç", Choose, true)); picks.Children.Add(Button("Klasör seç", ChooseFolder)); picks.Children.Add(Button("Yapıştır", Paste));
        wheel = Button("Çarkı aç", OpenWheel, true); picks.Children.Add(wheel); caption.Children.Add(picks);
        foreach (var control in picks.Children) control.Margin = new Thickness(0, 0, 6, 6);
        top.Children.Add(caption); var garden = Garden(); Grid.SetColumn(garden, 1); top.Children.Add(garden);
        drop.Children.Add(top); drop.Children.Add(new ScrollViewer { Content = selected, MaxHeight = 150 });
        drop.Children.Add(Label("Ctrl+O  Dosya seç     Ctrl+Space  Çark     Ctrl+V  Yapıştır", 11, true));
        home.Children.Add(Card(drop, new Thickness(0, 16, 0, 0)));
        var web = Button("↓   Bağlantı indir\n      Video, ses veya görsel. Doğrudan araçlarına.", Download);
        web.HorizontalAlignment = HorizontalAlignment.Stretch;
        web.HorizontalContentAlignment = HorizontalAlignment.Left; web.Padding = new Thickness(22); web.CornerRadius = new CornerRadius(24);
        home.Children.Add(web);
        var heading = new DockPanel { Margin = new Thickness(0, 20, 0, 2) };
        var all = Button("Tümünü gör ↗", () => { ShowLibrary(true); return Task.CompletedTask; }); DockPanel.SetDock(all, Dock.Right); heading.Children.Add(all);
        heading.Children.Add(Label("Yeni hazırlananlar", 20)); home.Children.Add(heading); home.Children.Add(recent);
        home.Children.Add(Label("Fedora sürümü · Dışa aktarmalar yeni dosya olarak kaydedilir.", 11, true));
        libraryPage.Children.Add(Label("Kitaplığın", 30)); libraryPage.Children.Add(Label("Yeni hazırlanmış, yeniden işlemeye hazır.", 14, true));
        libraryPage.Children.Add(search); libraryPage.Children.Add(libraryRows); search.TextChanged += (_, _) => RenderLibrary();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = operation is null && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        AddHandler(DragDrop.DropEvent, (_, e) => { if (operation is null) AddFiles(e.DataTransfer.TryGetFiles()?.Select(p => p.TryGetLocalPath()).OfType<string>() ?? []); e.Handled = true; });
        KeyDown += async (_, e) =>
        {
            if (operation is not null) return;
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O) { e.Handled = true; await Safely(Choose); }
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Space) { e.Handled = true; await Safely(OpenWheel); }
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.V && e.Source is not TextBox) { e.Handled = true; await Safely(Paste); }
            if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.D2 or Key.F) { e.Handled = true; ShowLibrary(true); search.Focus(); }
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.D1) { e.Handled = true; ShowLibrary(false); }
        };
        Closing += (_, e) => { if (operation is not null) { e.Cancel = true; status.Text = "Önce işlemi bitir veya İptal'e bas."; } };
        AddFiles(initial ?? []); ShowLibrary(false); RenderLibrary();
        if (library.RecoveryNotice is { } notice) status.Text = notice;
    }

    private void ApplyTheme()
    {
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Background = Brush(dark ? "#111A14" : "#F9F6ED"); Foreground = Brush(dark ? "#E7EBE6" : "#243B2B");
        Resources["NashiSurface"] = Surface; Resources["NashiLine"] = Line;
    }
    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    internal Button Button(string title, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = title, Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(13), FontSize = 12, BorderThickness = new Thickness(1) };
        button.Bind(BackgroundProperty, this.GetResourceObservable("NashiSurface")); button.Bind(BorderBrushProperty, this.GetResourceObservable("NashiLine"));
        if (primary) { button.Background = Brush("#E8BF59"); button.Foreground = Brush("#243B2B"); }
        button.Click += async (_, _) => { if (operation is null || button == cancel) await Safely(action); };
        return button;
    }
    private Border Card(Control content, Thickness? margin = null)
    {
        var border = new Border { Child = content, Padding = new Thickness(22), CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), Margin = margin ?? new Thickness(0) };
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable("NashiSurface")); border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("NashiLine")); return border;
    }
    private static Image Artwork(string name, double size)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Nashi.Linux/Assets/{name}.png"));
        return new Image { Source = new Bitmap(stream), Width = size, Height = size };
    }
    private Control Garden()
    {
        var garden = new Grid { Width = 166, Height = 150, IsHitTestVisible = false };
        garden.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 144, Height = 144, Fill = Brush("#3C3421") });
        foreach (var (name, angle, offset) in new[] { ("PDF", -16d, -32d), ("PNG", 7d, 30d) })
        {
            var text = new StackPanel { Spacing = 10 };
            text.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Left,
                Data = Geometry.Parse(name == "PDF" ? "M5,1 L22,1 L29,8 L29,29 L5,29 Z M22,1 L22,8 L29,8 M10,14 L24,14 M10,19 L24,19 M10,24 L20,24" : "M2,4 L30,4 L30,27 L2,27 Z M3,25 L12,14 L18,20 L23,14 L30,23 M11,10 A2,2 0 1 1 7,10 A2,2 0 1 1 11,10"),
                Stroke = Brush(name == "PDF" ? "#A9B5A8" : "#E8BF59"), StrokeThickness = 2
            });
            text.Children.Add(Label(name, 11));
            var card = Card(text); card.Width = 77; card.Height = 103; card.Padding = new Thickness(13); card.CornerRadius = new CornerRadius(15);
            var transforms = new TransformGroup(); transforms.Children.Add(new RotateTransform(angle)); transforms.Children.Add(new TranslateTransform(offset, -5)); card.RenderTransform = transforms; garden.Children.Add(card);
        }
        var pear = Artwork("NashiMarkHappy", 65); pear.RenderTransform = new TranslateTransform(0, 45); garden.Children.Add(pear); return garden;
    }
    private async Task Safely(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { status.Text = "İptal edildi. Orijinal dosyaların korundu."; }
        catch (Exception error)
        {
            status.Text = error.Message;
            var panel = new StackPanel { Spacing = 16 };
            panel.Children.Add(Label("İşlem tamamlanamadı", 22));
            panel.Children.Add(Label(error.Message));
            var dialog = Dialog("İşlem tamamlanamadı", panel);
            panel.Children.Add(Button("Tamam", () => { dialog.Close(); return Task.CompletedTask; }));
            await dialog.ShowDialog(OwnedWindows.LastOrDefault(w => w.IsVisible) ?? this);
        }
    }
    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath)) if (!files.Contains(path)) files.Add(path);
        selected.Children.Clear();
        foreach (var path in files)
        {
            var row = new DockPanel(); var remove = Button("Kaldır", () => { files.Remove(path); AddFiles([]); return Task.CompletedTask; });
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove); row.Children.Add(Label(Path.GetFileName(path), 12)); selected.Children.Add(row);
        }
        selection.Text = files.Count == 0 ? "Güzel bir şey bırak." : files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} dosya, hazır.";
        wheel.IsVisible = files.Count > 0;
    }
    private async Task Choose()
    {
        var chosen = await StorageProvider.OpenFilePickerAsync(new() { Title = "Nashi için dosya seç", AllowMultiple = true });
        AddFiles(chosen.Select(p => p.TryGetLocalPath()).OfType<string>());
    }
    private async Task ChooseFolder()
    {
        var chosen = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Klasör veya belge paketi seç", AllowMultiple = true });
        AddFiles(chosen.Select(p => p.TryGetLocalPath()).OfType<string>());
    }
    private async Task Paste()
    {
        if (Clipboard is null) return;
        var copied = await Clipboard.TryGetFilesAsync();
        if (copied is { Length: > 0 }) { AddFiles(copied.Select(p => p.TryGetLocalPath()).OfType<string>()); return; }
        var text = await Clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text)) return;
        var paths = text.Split('\n').Select(p => p.Trim()).Select(p => Uri.TryCreate(p, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : p).ToArray();
        if (paths.All(p => File.Exists(p) || Directory.Exists(p))) AddFiles(paths);
        else if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var link) && link.Scheme is "https" or "http") await Download(text.Trim());
        else
        {
            var folder = Path.Combine(library.DirectoryPath, "Clipboard"); Directory.CreateDirectory(folder);
            var path = SafePaths.Unique(folder, "Yapıştırılan metin.txt"); await File.WriteAllTextAsync(path, text); library.Record("Paste", [path]); AddFiles([path]); RenderLibrary();
        }
    }
    private void ShowLibrary(bool show) { home.IsVisible = !show; libraryPage.IsVisible = show; RenderLibrary(); }
    private void RenderLibrary()
    {
        recent.Children.Clear(); libraryRows.Children.Clear();
        foreach (var entry in library.Entries.Take(3)) recent.Children.Add(LibraryRow(entry));
        foreach (var entry in library.Search(search.Text ?? "", "tr")) libraryRows.Children.Add(LibraryRow(entry));
        if (recent.Children.Count == 0) recent.Children.Add(Card(Label("Bir sonraki yeni dosyan burada görünecek.", 13, true)));
        if (libraryRows.Children.Count == 0) libraryRows.Children.Add(Card(Label("Henüz bir dosya yok veya aramayla eşleşmedi.", 13, true)));
    }
    private Control LibraryRow(LibraryEntry entry)
    {
        var row = new DockPanel(); var reuse = Button("Yeniden kullan ↗", () => { files.Clear(); AddFiles(entry.AvailablePaths); ShowLibrary(false); return Task.CompletedTask; });
        reuse.IsEnabled = entry.AvailablePaths.Length > 0; DockPanel.SetDock(reuse, Dock.Right); row.Children.Add(reuse);
        var title = new StackPanel { Spacing = 5 }; title.Children.Add(Label(entry.Name, 13)); title.Children.Add(Label(entry.Action + " · " + entry.Created.ToLocalTime().ToString("g"), 11, true)); row.Children.Add(title);
        var card = Card(row); card.Padding = new Thickness(14); return card;
    }

    private Window Dialog(string title, Control content, double width = 560) => new()
    {
        Title = title + " · Nashi", Width = width, SizeToContent = SizeToContent.Height, MaxHeight = 720,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Foreground,
        RequestedThemeVariant = RequestedThemeVariant, Content = new ScrollViewer { Content = content, Margin = new Thickness(24) }
    };
    private async Task OpenWheel()
    {
        if (files.Count == 0) return;
        var choices = Operations.Tools(files); var formats = Operations.Formats(files);
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(Label("Dosyalarına yeni bir yön ver.", 23));
        var ids = (formats.Length > 0 ? new[] { "convert" } : []).Concat(choices).ToArray();
        if (ids.Length == 0) { status.Text = "Bu dosya türünün araçları henüz Fedora arayüzüne taşınmadı."; return; }
        var radius = Math.Max(170, ids.Length * 22);
        var canvas = new Canvas { Width = radius * 2 + 140, Height = radius * 2 + 60, HorizontalAlignment = HorizontalAlignment.Center };
        var center = Artwork("NashiMarkHappy", 70); Canvas.SetLeft(center, canvas.Width / 2 - 35); Canvas.SetTop(center, canvas.Height / 2 - 35); canvas.Children.Add(center);
        var dialog = Dialog("Araç çarkı", panel, canvas.Width + 48);
        var buttons = new List<Button>();
        for (var i = 0; i < ids.Length; i++)
        {
            var id = ids[i]; var title = ToolTitle(id);
            var button = Button(title, async () => { dialog.Close(); await Edit(id); }, id == "convert"); button.Width = 126;
            var angle = -Math.PI / 2 + i * Math.PI * 2 / ids.Length;
            Canvas.SetLeft(button, canvas.Width / 2 - 63 + Math.Cos(angle) * radius); Canvas.SetTop(button, canvas.Height / 2 - 20 + Math.Sin(angle) * radius); canvas.Children.Add(button); buttons.Add(button);
        }
        panel.Children.Add(canvas); panel.Children.Add(Label("Tab ile seç · Enter ile aç · Esc ile kapat", 12, true));
        dialog.Focusable = true; dialog.Opened += (_, _) => dialog.Focus();
        // The key-up that opened the wheel must not activate its first button.
        dialog.AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)) e.Handled = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) dialog.Close();
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                var current = buttons.FindIndex(b => b.IsKeyboardFocusWithin); var step = e.Key is Key.Left or Key.Up ? -1 : 1;
                buttons[current < 0 ? step > 0 ? 0 : buttons.Count - 1 : (current + step + buttons.Count) % buttons.Count].Focus(); e.Handled = true;
            }
        };
        await dialog.ShowDialog(this);
    }
    internal static string ToolTitle(string id) => id switch
    {
        "convert" => "Dönüştür", "compress" => "Sıkıştır", "editImage" => "Fotoğrafı düzenle", "cropImage" => "Kırp", "removeMetadata" => "Metaveriyi sil", "readQRCode" => "QR oku",
        "normalizeAudio" => "Sesi dengele", "muteVideo" => "Sesi kaldır", "trimVideo" or "trimAudio" => "Kes", "changeVideoSpeed" => "Hızı değiştir",
        "joinVideos" => "Birleştir", "videoSnapshots" => "Kare al", "mergePDF" => "PDF birleştir", "splitPDF" => "PDF ayır", "extractArchive" => "Arşivi aç",
        "audioChannels" => "Ses kanalları", "splitVideo" => "Videoyu böl", "audioToVideo" => "Ses görselleştir",
        "annotateImage" => "Görsele not ekle", "frameImage" => "Arka plan ekle", "createCollage" => "Kolaj oluştur", "redactImage" => "Görseli sansürle", "createPDF" => "PDF oluştur", "organizePDF" => "PDF sayfalarını düzenle", _ => id
    };
    private async Task Edit(string action)
    {
        if (action == "readQRCode") { await ReadQr(); return; }
        if (action == "organizePDF")
        {
            var input = files[0]; var pdfPassword = await AskPassword("PDF sayfalarını düzenle"); if (pdfPassword is null) return;
            byte[][]? images = null;
            await Run("PDF önizleme", async (_, token) => { images = await LinuxPdf.RenderAsync(input, 36, token, pdfPassword); return []; });
            if (images is null) return;
            var organizer = new PdfOrganizerWindow(this, images); await organizer.ShowDialog(this);
            if (organizer.Result is not { } result) return;
            var folder = await Folder(this); if (folder is null) return;
            await Run(ToolTitle(action), (p, t) => new Operations(tools).RunAsync(action, [input], folder, "", 85, 0, 0, 0, 0, new(), p, t, documentOptions: new(Password: pdfPassword), pdfOptions: result)); return;
        }
        if (files.All(FeatureCatalog.IsImage) && action is not ("convert" or "removeMetadata"))
        {
            var selected = files.ToArray(); var editor = new ImageEditorWindow(this, action, selected);
            await editor.ShowDialog(this);
            if (editor.Result is not { } edit) return;
            var folder = await Folder(this); if (folder is null) return;
            await Run(ToolTitle(action), (p, t) => new Operations(tools).RunAsync(action, selected, folder, "", edit.Quality, 0, 0, 0, 0, new(), p, t, imageOptions: edit));
            return;
        }
        var inputs = files.ToArray(); var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(Label(ToolTitle(action), 25));
        panel.Children.Add(Label(string.Join(", ", inputs.Select(Path.GetFileName)), 12, true));
        var formats = new ComboBox { ItemsSource = Operations.Formats(inputs), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (action == "convert") { panel.Children.Add(Label("Çıktı biçimi")); panel.Children.Add(formats); }
        NumericUpDown Number(string title, decimal value, decimal min, decimal max)
        {
            panel.Children.Add(Label(title, 12)); var input = new NumericUpDown { Value = value, Minimum = min, Maximum = max, Increment = 1 };
            AutomationProperties.SetName(input, title); panel.Children.Add(input); return input;
        }
        var quality = Number("Kalite (%)", 85, 1, 100);
        TextBox? password = null, pageRanges = null; NumericUpDown? dpi = null, pagesPerFile = null; ComboBox? layout = null, epubLayout = null;
        if (inputs.Any(p => FeatureCatalog.Extension(p) == "pdf" || FeatureCatalog.IsArchive(p)))
        {
            panel.Children.Add(Label("Dosya şifresi (varsa)", 12)); password = new TextBox { PasswordChar = '●' }; panel.Children.Add(password);
        }
        if (action == "convert" && !inputs.All(p => FeatureCatalog.IsImage(p) || FeatureCatalog.IsAudio(p) || FeatureCatalog.IsVideo(p) || FeatureCatalog.IsArchive(p)))
        {
            dpi = Number("PDF görüntü çözünürlüğü (DPI)", 144, 36, 600);
            panel.Children.Add(Label("PDF → belge yerleşimi", 12)); layout = new ComboBox { ItemsSource = new[] { "Düzenlenebilir metin", "Sayfa görünümünü koru" }, SelectedIndex = 0 }; panel.Children.Add(layout);
            panel.Children.Add(Label("EPUB yerleşimi", 12)); epubLayout = new ComboBox { ItemsSource = new[] { "Akışkan metin", "Sabit sayfalar" }, SelectedIndex = 0 }; panel.Children.Add(epubLayout);
        }
        if (action == "splitPDF")
        {
            pagesPerFile = Number("Her dosyadaki sayfa sayısı", 1, 1, 1000);
            panel.Children.Add(Label("Sayfa aralığı (boş: tümü; örnek: 1-3, 5)", 12)); pageRanges = new TextBox(); panel.Children.Add(pageRanges);
        }
        if (action is "mergePDF" or "joinVideos")
        {
            panel.Children.Add(Label("Birleştirme sırası", 12)); var list = new ListBox { ItemsSource = inputs.Select(Path.GetFileName).ToArray(), MaxHeight = 180 }; panel.Children.Add(list);
            var orderButtons = Row();
            foreach (var (title, step) in new[] { ("Yukarı", -1), ("Aşağı", 1) }) orderButtons.Children.Add(Button(title, () =>
            {
                var index = list.SelectedIndex; var next = index + step;
                if (index >= 0 && next >= 0 && next < inputs.Length) { (inputs[index], inputs[next]) = (inputs[next], inputs[index]); list.ItemsSource = inputs.Select(Path.GetFileName).ToArray(); list.SelectedIndex = next; }
                return Task.CompletedTask;
            }));
            panel.Children.Add(orderButtons);
        }
        NumericUpDown? start = null, end = null, speed = null;
        NumericUpDown? channels = null, clips = null, snapshot = null;
        if (action is "trimVideo" or "trimAudio") { start = Number("Başlangıç (saniye)", 0, 0, 864000); end = Number("Bitiş (saniye)", 10, 0, 864000); }
        if (action == "changeVideoSpeed") speed = Number("Hız çarpanı", 1, .25m, 4);
        if (action == "audioChannels") channels = Number("Kanal sayısı (1: mono, 2: stereo)", 2, 1, 2);
        if (action == "splitVideo") clips = Number("Eşit parça sayısı", 2, 2, 100);
        if (action == "videoSnapshots") snapshot = Number("Kare zamanı (saniye)", 0, 0, 864000);
        if (action == "audioToVideo") panel.Children.Add(Label("Ses dalgası içeren 1280 × 720 MP4 oluşturulur.", 12, true));
        panel.Children.Add(Label("Orijinaller korunur. Yeni dosyalar için bir klasör seçeceksin.", 12, true));
        var dialog = Dialog(ToolTitle(action), panel);
        panel.Children.Add(Button("Klasör seç ve kaydet", async () =>
        {
            if (start is not null && end?.Value <= start.Value) throw new ArgumentException("Bitiş, başlangıçtan sonra olmalı.");
            var destination = await Folder(dialog); if (destination is null) return;
            var format = formats.SelectedItem as string ?? "";
            var options = new MediaOptions
            {
                Quality = (double)(quality.Value ?? 85) / 100, Start = (double)(start?.Value ?? 0),
                End = end is null ? null : (double)(end.Value ?? 0), Speed = (double)(speed?.Value ?? 1),
                Channels = (int)(channels?.Value ?? 2), EqualClips = clips is null ? null : (int)(clips.Value ?? 2),
                SnapshotTimes = [(double)(snapshot?.Value ?? 0)]
            };
            var document = new DocumentOptions((int)(dpi?.Value ?? 144), layout?.SelectedIndex == 1 ? "preserve" : "editable", epubLayout?.SelectedIndex == 1 ? "fixed" : "reflowable", password?.Text ?? "");
            var pdf = new PdfEditOptions(PagesPerFile: (int)(pagesPerFile?.Value ?? 1), PageRanges: pageRanges?.Text ?? "");
            dialog.Close();
            await Run(ToolTitle(action), (p, t) => new Operations(tools).RunAsync(action, inputs, destination, format, (int)(quality.Value ?? 85),
                0, 0, 0, 0, options, p, t, documentOptions: document, pdfOptions: pdf));
        }, true));
        await dialog.ShowDialog(this);
    }
    private static async Task<string?> Folder(Window owner)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new() { Title = "Yeni dosyaların kaydedileceği klasör" });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task Run(string label, Func<IProgress<OperationProgress>, CancellationToken, Task<IReadOnlyList<string>>> work)
    {
        using var cancellation = new CancellationTokenSource(); operation = cancellation; cancel.IsVisible = progress.IsVisible = true;
        try
        {
            var outputs = await work(new Progress<OperationProgress>(p => { if (operation != cancellation) return; progress.IsIndeterminate = p.Fraction is null; progress.Value = (p.Fraction ?? 0) * 100; status.Text = p.Label; }), cancellation.Token);
            status.Text = outputs.Count == 0 ? "İşlem tamamlandı." : $"{outputs.Count} yeni dosya kaydedildi. Orijinaller korundu.";
            try { library.Record(label, outputs); } catch (Exception error) { status.Text += " Kitaplık kaydedilemedi: " + error.Message; }
            RenderLibrary();
        }
        finally { operation = null; cancel.IsVisible = progress.IsVisible = false; }
    }
    private async Task Setup()
    {
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(Label("Medya araçları", 25));
        panel.Children.Add(Label("FFmpeg, yt-dlp ve Deno eksikse özgün kaynaklarından indirilir ve sağlama toplamları doğrulanır. İndirme birkaç yüz MB olabilir. Sistem paketlerin değiştirilmez.", 14));
        var dialog = Dialog("Araç kurulumu", panel);
        panel.Children.Add(Button("Eksik araçları indir", async () => { dialog.Close(); await Run("Kurulum", async (p, t) => { await tools.InstallAsync(p, t); return []; }); status.Text = "Medya araçları hazır."; }, true));
        await dialog.ShowDialog(this);
    }
    private Task Download() => Download("");
    private async Task<string?> AskPassword(string title)
    {
        var content = new StackPanel { Spacing = 12 }; content.Children.Add(Label(title, 24)); content.Children.Add(Label("PDF şifresi (varsa)", 13));
        var field = new TextBox { PasswordChar = '●' }; content.Children.Add(field); var dialog = Dialog(title, content);
        content.Children.Add(Button("Devam", () => { dialog.Close(field.Text ?? ""); return Task.CompletedTask; }, true));
        dialog.Opened += (_, _) => field.Focus(); dialog.KeyDown += (_, e) => { if (e.Key == Key.Enter) { dialog.Close(field.Text ?? ""); e.Handled = true; } };
        return await dialog.ShowDialog<string?>(this);
    }
    private async Task ReadQr(string? password = null)
    {
        if (password is null && files.Any(p => FeatureCatalog.Extension(p) == "pdf"))
        {
            var value = await AskPassword("PDF içinden QR oku"); if (value is not null) await ReadQr(value); return;
        }
        var results = new List<string>();
        await Run("QR oku", async (progress, token) =>
        {
            foreach (var file in files.ToArray())
            {
                token.ThrowIfCancellationRequested();
                if (FeatureCatalog.Extension(file) == "pdf")
                {
                    var pages = await LinuxPdf.RenderAsync(file, 144, token, password ?? "");
                    for (var index = 0; index < pages.Length; index++)
                    {
                        token.ThrowIfCancellationRequested(); var codes = await Task.Run(() => Operations.ReadQr(pages[index]), token);
                        results.Add(Path.GetFileName(file) + $" · Sayfa {index + 1}:\n" + (codes.Length == 0 ? "QR kod bulunamadı." : string.Join("\n", codes)));
                        progress.Report(new((index + 1d) / pages.Length, "QR kodlar taranıyor…"));
                    }
                }
                else
                {
                    var codes = await Task.Run(() => Operations.ReadQr(file), token);
                    results.Add(Path.GetFileName(file) + ":\n" + (codes.Length == 0 ? "QR kod bulunamadı." : string.Join("\n", codes)));
                }
            }
            return [];
        });
        var panel = new StackPanel { Spacing = 16 }; panel.Children.Add(Label("QR sonuçları", 24));
        panel.Children.Add(new TextBox { Text = string.Join("\n\n", results), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap });
        var dialog = Dialog("QR sonuçları", panel);
        panel.Children.Add(Button("Kapat", () => { dialog.Close(); return Task.CompletedTask; })); await dialog.ShowDialog(this);
        status.Text = "QR taraması tamamlandı. Bağlantılar otomatik açılmaz.";
    }
    private async Task Download(string initial)
    {
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(Label("Web'den indir", 25)); panel.Children.Add(Label("Herkese açık bir medya bağlantısı", 13));
        var link = new TextBox { Text = initial, Watermark = "https://…" }; panel.Children.Add(link);
        var kind = new ComboBox { ItemsSource = new[] { "video", "audio", "thumbnail" }, SelectedIndex = 0 }; panel.Children.Add(kind);
        var dialog = Dialog("Bağlantı indir", panel);
        panel.Children.Add(Button("İndir", async () => { var url = WebDownload.Validate(link.Text ?? "").AbsoluteUri; var folder = await Folder(dialog); if (folder is null) return; dialog.Close(); await Run("Download", (p, t) => new WebDownload(tools).DownloadAsync(url, (string)kind.SelectedItem!, folder, p, t)); }, true));
        await dialog.ShowDialog(this);
    }
}
