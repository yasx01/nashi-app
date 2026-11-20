using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ImageMagick;
using Nashi.Core;
using Avalonia.Platform.Storage;
using Orientation = Avalonia.Layout.Orientation;

namespace Nashi.Linux;

internal sealed class ImageEditorWindow : Window
{
    private readonly MainWindow host;
    private readonly string action;
    private readonly string[] inputs;
    private readonly StackPanel settings = new() { Spacing = 8 };
    private readonly Dictionary<string, NumericUpDown> numbers = [];
    private readonly Dictionary<string, ComboBox> choices = [];
    private readonly Dictionary<string, TextBox> texts = [];
    private readonly TextBlock message = MainWindow.Label("", 12);
    private readonly ImageCanvas canvas;
    private readonly SemaphoreSlim previewGate = new(1, 1);
    private CancellationTokenSource? preview;
    private readonly List<int> order;
    private readonly ListBox inputList = new() { MaxHeight = 140 };
    private string? backgroundImage;
    private bool ready, closed;
    private CheckBox? flipHorizontal, flipVertical;
    internal ImageEditOptions? Result { get; private set; }
    internal ImageCanvas EditorCanvas => canvas;
    internal bool PreviewPending => preview is not null;

    public ImageEditorWindow(MainWindow host, string action, string[] inputs)
    {
        this.host = host; this.action = action; this.inputs = inputs; order = Enumerable.Range(0, inputs.Length).ToList();
        using var source = PhotoCodec.Read(inputs[0]); var sourceWidth = (int)source.Width; var sourceHeight = (int)source.Height;
        canvas = new ImageCanvas(sourceWidth, sourceHeight) { Mode = "view" };
        using (var thumb = source.Clone())
        {
            if (thumb.Width > 1600 || thumb.Height > 1200) thumb.Resize(new MagickGeometry(1600, 1200));
            canvas.SetImage(thumb.ToByteArray(MagickFormat.Png));
        }
        Title = MainWindow.ToolTitle(action) + " · Nashi"; Width = 1080; Height = 800; MinWidth = 800; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = host.Background; Foreground = host.Foreground; RequestedThemeVariant = host.RequestedThemeVariant;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var save = host.Button("Kullan", () => { Accept(); return Task.CompletedTask; }, true); DockPanel.SetDock(save, Dock.Right); heading.Children.Add(save);
        heading.Children.Add(MainWindow.Label(MainWindow.ToolTitle(action), 25)); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(message, Dock.Bottom); message.Margin = new Thickness(0, 12, 0, 0); root.Children.Add(message);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,320"), ColumnSpacing = 18 }; root.Children.Add(columns);
        columns.Children.Add(new Border { Child = canvas, ClipToBounds = true, CornerRadius = new CornerRadius(16) });
        var scroll = new ScrollViewer { Content = settings }; Grid.SetColumn(scroll, 1); columns.Children.Add(scroll);
        settings.Children.Add(MainWindow.Label(Path.GetFileName(inputs[0]), 12, true));

        switch (action)
        {
            case "cropImage":
                canvas.Mode = "crop";
                Choice("ratio", "En-boy oranı", ["Serbest", "1:1", "4:3", "3:2", "16:9", "9:16", "Özgün"]);
                choices["ratio"].SelectionChanged += (_, _) =>
                {
                    canvas.AspectRatio = choices["ratio"].SelectedIndex switch { 1 => 1, 2 => 4d / 3, 3 => 1.5, 4 => 16d / 9, 5 => 9d / 16, 6 => (double)canvas.ImageWidth / canvas.ImageHeight, _ => null };
                };
                settings.Children.Add(MainWindow.Label("Alan çizmek için sürükle. İçinden tutarak taşı; sağ alt köşeden boyutlandır. Shift oranı korur. Enter kaydeder.", 13, true));
                settings.Children.Add(host.Button("Seçimi temizle", () => { canvas.Crop = null; canvas.InvalidateVisual(); return Task.CompletedTask; }));
                break;
            case "annotateImage": case "redactImage":
                canvas.Mode = "rectangle";
                if (action == "annotateImage")
                {
                    Choice("drawing", "Çizim aracı", ["Dikdörtgen", "Elips", "Çizgi", "Ok", "Serbest çizim", "Vurgulayıcı", "Metin", "Görsel", "Seç / taşı"]);
                    choices["drawing"].SelectionChanged += (_, _) => canvas.Mode = new[] { "rectangle", "ellipse", "line", "arrow", "freehand", "highlight", "text", "image", "select" }[Math.Max(0, choices["drawing"].SelectedIndex)];
                    Choice("ink", "Mürekkep", ["#FF5200", "#E8BF59", "#FFFFFF", "#000000", "#3388FF", "#35B55A"]);
                    choices["ink"].SelectionChanged += (_, _) => { canvas.Ink = StringChoice("ink"); if (texts.TryGetValue("customInk", out var custom)) custom.Text = canvas.Ink; };
                    Text("customInk", "Özel renk (#RRGGBB)", "#FF5200").TextChanged += (_, _) =>
                    {
                        var value = texts["customInk"].Text ?? "";
                        if (System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")) canvas.Ink = value;
                        else message.Text = "Rengi #RRGGBB biçiminde gir.";
                    };
                    Number("stroke", "Çizgi kalınlığı", 3, .1m, 1000, .5m).ValueChanged += (_, _) => canvas.Stroke = (double)NumberValue("stroke");
                    Number("fontSize", "Yazı boyutu", 24, 1, 2000).ValueChanged += (_, _) => canvas.FontSize = (double)NumberValue("fontSize");
                    Text("note", "Metin", "Not").TextChanged += (_, _) => canvas.MarkText = texts["note"].Text ?? "";
                    settings.Children.Add(host.Button("Eklenecek görseli seç", async () =>
                    {
                        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Eklenecek görsel" });
                        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { using var image = PhotoCodec.Read(path); canvas.MarkImage = path; choices["drawing"].SelectedIndex = 7; }
                    }));
                }
                else
                {
                    Choice("redaction", "Redaksiyon", ["solid", "blur", "pixelate"]);
                    settings.Children.Add(host.Button("Alan seç / taşı", () => { canvas.Mode = canvas.Mode == "select" ? "rectangle" : "select"; return Task.CompletedTask; }));
                }
                settings.Children.Add(MainWindow.Label("Sürükleyerek çiz. Seç/taşı ile işareti taşı; Delete siler, Ctrl+Z geri alır.", 13, true));
                settings.Children.Add(host.Button("Geri al", () => { canvas.Undo(); return Task.CompletedTask; }));
                settings.Children.Add(host.Button("Seçileni sil", () => { canvas.Delete(); return Task.CompletedTask; }));
                settings.Children.Add(host.Button("Tümünü temizle", () => { canvas.Clear(); return Task.CompletedTask; }));
                break;
            case "frameImage":
                Choice("backdrop", "Arka plan", ["solid", "gradient", "image", "transparent"]);
                Text("color", "Başlangıç rengi (#RRGGBB)", "#FFFFFF"); Text("endColor", "Bitiş rengi (#RRGGBB)", "#FFD85C");
                settings.Children.Add(host.Button("Arka plan görselini seç", async () =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Arka plan görseli" });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { using var image = PhotoCodec.Read(path); backgroundImage = path; choices["backdrop"].SelectedIndex = 2; QueuePreview(); }
                }));
                Number("width", "Tuval genişliği", source.Width + 160, 1, 30000); Number("height", "Tuval yüksekliği", source.Height + 160, 1, 30000);
                Number("padding", "Kenar boşluğu", 80, 0, 4000); Number("radius", "Köşe yuvarlaklığı", 24, 0, 4000);
                Number("blur", "Arka plan bulanıklığı", 0, 0, 100); Number("shadow", "Gölge", 24, 0, 100); Number("opacity", "Gölge opaklığı (%)", 25, 0, 100);
                Number("scale", "Görsel ölçeği (%)", 100, 1, 1000); Number("x", "Yatay konum (%)", 50, 0, 100); Number("y", "Dikey konum (%)", 50, 0, 100);
                break;
            case "createCollage":
                Choice("layout", "Yerleşim", ["grid", "row", "column", "featured"]); Number("columns", "Sütun", 2, 1, 100);
                Number("width", "Genişlik", 2048, 1, 30000); Number("height", "Yükseklik", 2048, 1, 30000);
                Number("spacing", "Görseller arası boşluk", 12, 0, 4000); Number("padding", "Kenar boşluğu", 16, 0, 4000);
                Number("radius", "Köşe yuvarlaklığı", 0, 0, 4000); Text("color", "Arka plan (#RRGGBB)", "#FFFFFF");
                Choice("fit", "Sığdırma", ["Doldur", "Tamamını göster"]); BuildOrder(); break;
            case "createPDF": BuildOrder(); settings.Children.Add(MainWindow.Label("Her görsel, kendi boyutunda bir PDF sayfası olur.", 13, true)); break;
            case "editImage":
                Number("width", "Genişlik", source.Width, 1, 30000); Number("height", "Yükseklik", source.Height, 1, 30000);
                var locked = new CheckBox { Content = "En-boy oranını koru", IsChecked = true }; settings.Children.Add(locked); var resizing = false;
                numbers["width"].ValueChanged += (_, _) => { if (!resizing && locked.IsChecked == true) { resizing = true; numbers["height"].Value = Math.Max(1, Math.Round(NumberValue("width") * sourceHeight / sourceWidth)); resizing = false; } };
                numbers["height"].ValueChanged += (_, _) => { if (!resizing && locked.IsChecked == true) { resizing = true; numbers["width"].Value = Math.Max(1, Math.Round(NumberValue("height") * sourceWidth / sourceHeight)); resizing = false; } };
                Number("rotation", "Döndürme (derece)", 0, -359, 359);
                var rotate = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var (label, angle) in new[] { ("Sola 90°", -90), ("Sağa 90°", 90) }) rotate.Children.Add(host.Button(label, () => { resizing = true; (numbers["width"].Value, numbers["height"].Value) = (numbers["height"].Value, numbers["width"].Value); numbers["rotation"].Value = (NumberValue("rotation") + angle) % 360; resizing = false; QueuePreview(); return Task.CompletedTask; }));
                settings.Children.Add(rotate);
                flipHorizontal = new CheckBox { Content = "Yatay çevir" }; flipVertical = new CheckBox { Content = "Dikey çevir" }; settings.Children.Add(flipHorizontal); settings.Children.Add(flipVertical);
                flipHorizontal.IsCheckedChanged += (_, _) => QueuePreview(); flipVertical.IsCheckedChanged += (_, _) => QueuePreview();
                foreach (var property in typeof(PhotoAdjustments).GetProperties())
                {
                    var neutral = property.Name is "Contrast" or "Gamma" or "Saturation" ? 1 : 0;
                    var min = property.Name is "Gamma" ? .1m : property.Name is "Contrast" or "Saturation" or "Blur" or "Sharpen" or "NoiseReduction" or "Vignette" or "Grain" ? 0 : property.Name == "Hue" ? -180 : -5;
                    var max = property.Name == "Hue" ? 180 : property.Name == "Blur" ? 50 : 5;
                    Number("photo:" + property.Name, PhotoLabel(property.Name), neutral, min, max, .1m);
                }
                break;
            case "compress":
                Number("target", "Hedef boyut (KB, 0: kapalı)", 0, 0, 200000); Number("reduction", "Azaltma (%, 0: kapalı)", 0, 0, 99); break;
        }
        Number("quality", "Çıktı kalitesi (%)", 85, 1, 100);
        canvas.Changed += QueuePreview;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Enter && e.Source is not TextBox) { Accept(); e.Handled = true; }
        };
        Closed += (_, _) => { closed = true; preview?.Cancel(); canvas.Dispose(); };
        ready = true; QueuePreview();
    }
    private NumericUpDown Number(string key, string label, decimal value, decimal minimum, decimal maximum, decimal step = 1)
    {
        settings.Children.Add(MainWindow.Label(label, 12)); var field = new NumericUpDown { Value = value, Minimum = minimum, Maximum = maximum, Increment = step };
        AutomationProperties.SetName(field, label); numbers.Add(key, field); settings.Children.Add(field); field.ValueChanged += (_, _) => QueuePreview(); return field;
    }
    private ComboBox Choice(string key, string label, string[] options)
    {
        settings.Children.Add(MainWindow.Label(label, 12)); var field = new ComboBox { ItemsSource = options, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(field, label); choices.Add(key, field); settings.Children.Add(field); field.SelectionChanged += (_, _) => QueuePreview(); return field;
    }
    private TextBox Text(string key, string label, string value)
    {
        settings.Children.Add(MainWindow.Label(label, 12)); var field = new TextBox { Text = value }; AutomationProperties.SetName(field, label);
        texts.Add(key, field); settings.Children.Add(field); field.TextChanged += (_, _) => QueuePreview(); return field;
    }
    private decimal NumberValue(string key, decimal fallback = 0) => numbers.TryGetValue(key, out var field) ? field.Value ?? throw new ArgumentException("Sayısal alan boş bırakılamaz.") : fallback;
    private string StringChoice(string key) => choices[key].SelectedItem as string ?? "";
    private void BuildOrder()
    {
        settings.Children.Add(MainWindow.Label("Dosya sırası", 13)); UpdateOrder(); settings.Children.Add(inputList);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (title, offset) in new[] { ("Yukarı", -1), ("Aşağı", 1) }) buttons.Children.Add(host.Button(title, () =>
        {
            var index = inputList.SelectedIndex; var next = index + offset;
            if (index >= 0 && next >= 0 && next < order.Count) { (order[index], order[next]) = (order[next], order[index]); UpdateOrder(); inputList.SelectedIndex = next; QueuePreview(); }
            return Task.CompletedTask;
        }));
        settings.Children.Add(buttons);
    }
    private void UpdateOrder() => inputList.ItemsSource = order.Select(i => Path.GetFileName(inputs[i])).ToArray();
    internal ImageEditOptions Options()
    {
        var adjustments = new PhotoAdjustments();
        foreach (var property in typeof(PhotoAdjustments).GetProperties()) if (numbers.ContainsKey("photo:" + property.Name)) property.SetValue(adjustments, (double)NumberValue("photo:" + property.Name));
        return new ImageEditOptions
        {
            Width = action == "editImage" ? (int)NumberValue("width") : null, Height = action == "editImage" ? (int)NumberValue("height") : null,
            Rotation = (int)NumberValue("rotation"), FlipHorizontal = flipHorizontal?.IsChecked == true, FlipVertical = flipVertical?.IsChecked == true,
            Adjustments = adjustments, Crop = canvas.Crop, Marks = canvas.Marks.ToArray(), Redactions = canvas.Marks.Select(m => m.Area).ToArray(),
            RedactionStyle = choices.ContainsKey("redaction") ? StringChoice("redaction") : "solid", Quality = (int)NumberValue("quality", 85),
            TargetBytes = NumberValue("target") > 0 ? (long)NumberValue("target") * 1024 : null,
            ReductionPercent = NumberValue("reduction") > 0 ? (double)NumberValue("reduction") : null,
            InputOrder = order.ToArray(),
            Background = action == "frameImage" ? new((int)NumberValue("padding"), (double)NumberValue("radius"), texts["color"].Text ?? "", StringChoice("backdrop"), texts["endColor"].Text ?? "", backgroundImage,
                (double)NumberValue("blur"), (double)NumberValue("shadow"), (double)NumberValue("opacity") / 100, (int)NumberValue("width"), (int)NumberValue("height"), (double)NumberValue("scale") / 100, (double)NumberValue("x") / 100, (double)NumberValue("y") / 100) : new(),
            Collage = action == "createCollage" ? new(StringChoice("layout"), (int)NumberValue("columns"), (int)NumberValue("width"), (int)NumberValue("height"), (int)NumberValue("spacing"), (int)NumberValue("padding"), (double)NumberValue("radius"), texts["color"].Text ?? "", choices["fit"].SelectedIndex == 0, order.ToArray()) : new()
        };
    }
    private void Accept()
    {
        try
        {
            var options = Options();
            if (texts.TryGetValue("customInk", out var color) && !System.Text.RegularExpressions.Regex.IsMatch(color.Text ?? "", "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Rengi #RRGGBB biçiminde gir.");
            if (action == "cropImage") (options.Crop ?? throw new ArgumentException("Sürükleyerek bir kırpma alanı seç.")).Validate(canvas.ImageWidth, canvas.ImageHeight);
            if (action is "annotateImage" or "redactImage" && canvas.Marks.Count == 0) throw new ArgumentException("Önce bir işaret veya alan çiz.");
            if (options.Marks.Any(m => m.Kind == "image" && m.ImagePath is null)) throw new ArgumentException("Eklenecek görseli seç.");
            Result = options; Close(options);
        }
        catch (ArgumentException error) { message.Text = error.Message; }
    }
    private async void QueuePreview()
    {
        if (!ready || closed) return;
        preview?.Cancel(); var pending = new CancellationTokenSource(); preview = pending;
        try
        {
            await Task.Delay(180, pending.Token); var options = Options();
            await previewGate.WaitAsync(pending.Token);
            try
            {
                pending.Token.ThrowIfCancellationRequested();
                var png = await Task.Run(() =>
                {
                    using var image = action == "createCollage" ? ImageEditing.Collage(inputs, options.Collage, pending.Token) :
                        action == "createPDF" || action == "cropImage" || (action is "annotateImage" or "redactImage" && options.Marks.Length == 0) ? PhotoCodec.Read(inputs[0]) : ImageEditing.Transform(inputs[0], action, options);
                    if (image.Width > 1600 || image.Height > 1200) image.Resize(new MagickGeometry(1600, 1200));
                    return image.ToByteArray(MagickFormat.Png);
                }, pending.Token);
                if (!closed && preview == pending) { canvas.SetImage(png); message.Text = canvas.Crop is { } c ? $"{c.X:0}, {c.Y:0} · {c.Width:0} × {c.Height:0} piksel" : "Önizleme hazır. Orijinaller korunur."; }
            }
            finally { previewGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closed && preview == pending) message.Text = error.Message; }
        finally { if (preview == pending) preview = null; pending.Dispose(); }
    }
    private static string PhotoLabel(string property) => property switch
    {
        "Exposure" => "Pozlama", "Brightness" => "Parlaklık", "Contrast" => "Kontrast", "Highlights" => "Parlak alanlar", "Shadows" => "Gölgeler",
        "Whites" => "Beyazlar", "Blacks" => "Siyahlar", "Gamma" => "Gama", "Temperature" => "Sıcaklık", "Tint" => "Ton", "Saturation" => "Doygunluk",
        "Vibrance" => "Canlılık", "Hue" => "Renk tonu", "Clarity" => "Berraklık", "Texture" => "Doku", "Fade" => "Solukluk", "Blur" => "Bulanıklık",
        "Sharpen" => "Keskinlik", "Dehaze" => "Pus giderme", "Grain" => "Gren", "NoiseReduction" => "Gürültü azaltma", "Vignette" => "Vinyet", _ => property
    };
}
