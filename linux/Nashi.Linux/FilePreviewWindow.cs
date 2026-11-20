using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Orientation = Avalonia.Layout.Orientation;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
using PdfSharp.Pdf.IO;

namespace Nashi.Linux;

internal sealed class FilePreviewWindow : Window
{
    private readonly string[] paths;
    private readonly Toolchain tools;
    private readonly bool silentPlayback;
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "nashi-preview-" + Guid.NewGuid().ToString("N"));
    private readonly ContentControl preview = new();
    private readonly TextBlock caption = MainWindow.Label("", 15), pageCaption = MainWindow.Label("", 12);
    private readonly TextBox password = new() { Watermark = "PDF şifresi (varsa)", PasswordChar = '●', Width = 180 };
    private readonly StackPanel pdfControls = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly StackPanel mediaControls = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly Slider seek = new() { Minimum = 0, Maximum = 1, MinWidth = 240 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly HashSet<Task> loads = [];
    private readonly Dictionary<string, string> converted = [];
    private CancellationTokenSource request = new();
    private MediaPlayback? playback;
    private ImageCanvas? canvas;
    private Bitmap? image;
    private int index, page, pageCount;
    private bool updating, closed;
    internal bool Loading => loads.Any(t => !t.IsCompleted);
    internal string CurrentPath => paths[index];
    internal string Status => preview.Content is TextBlock text ? text.Text ?? "" : preview.Content?.GetType().Name ?? "";

    internal FilePreviewWindow(MainWindow host, IEnumerable<string> paths, Toolchain tools, bool silentPlayback = false)
    {
        this.paths = paths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray(); this.tools = tools; this.silentPlayback = silentPlayback;
        if (this.paths.Length == 0) throw new ArgumentException("Önizlenecek bir dosya seç.");
        Title = "Önizleme · Nashi"; Width = 940; Height = 760; MinWidth = 640; MinHeight = 450;
        Background = host.Background; Foreground = host.Foreground; RequestedThemeVariant = host.RequestedThemeVariant; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Directory.CreateDirectory(temporary);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        navigation.Children.Add(host.Button("← Önceki", () => { Navigate(-1); return Task.CompletedTask; }));
        navigation.Children.Add(host.Button("Sonraki →", () => { Navigate(1); return Task.CompletedTask; }));
        DockPanel.SetDock(navigation, Dock.Right); header.Children.Add(navigation); header.Children.Add(caption);
        var footer = new StackPanel { Spacing = 8 }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        pdfControls.Children.Add(host.Button("Önceki sayfa", () => { page = Math.Max(0, page - 1); Reload(); return Task.CompletedTask; }));
        pdfControls.Children.Add(host.Button("Sonraki sayfa", () => { page = Math.Min(pageCount - 1, page + 1); Reload(); return Task.CompletedTask; }));
        pdfControls.Children.Add(pageCaption); pdfControls.Children.Add(password);
        pdfControls.Children.Add(host.Button("Şifreyle aç", () => { Reload(); return Task.CompletedTask; })); footer.Children.Add(pdfControls);
        mediaControls.Children.Add(host.Button("Oynat / duraklat", () => { playback?.Toggle(); return Task.CompletedTask; })); mediaControls.Children.Add(seek); footer.Children.Add(mediaControls);
        footer.Children.Add(MainWindow.Label("←/→ dosya · Medyada ←/→ 1 saniye, Alt+←/→ dosya · Space oynat/duraklat · Esc kapat", 11, true)); root.Children.Add(preview);
        seek.PropertyChanged += (_, e) => { if (!updating && e.Property == Slider.ValueProperty && seek.Maximum > 0) playback?.Seek(seek.Value, seek.Maximum); };
        timer.Tick += (_, _) => { if (playback is null) return; updating = true; seek.Value = Math.Clamp(playback.Player.Time / 1000d, 0, seek.Maximum); updating = false; };
        KeyDown += (_, e) =>
        {
            if (e.Source is TextBox && e.Key != Key.Escape) return;
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Space) { playback?.Toggle(); e.Handled = true; }
            if (e.Key is Key.Left or Key.Right)
            {
                var delta = e.Key == Key.Left ? -1 : 1;
                if (playback is not null && e.KeyModifiers == KeyModifiers.None) playback.Seek(Math.Clamp(seek.Value + delta, 0, seek.Maximum), seek.Maximum);
                else Navigate(delta); e.Handled = true;
            }
        };
        Opened += (_, _) => { timer.Start(); Reload(); };
        Closed += async (_, _) =>
        {
            closed = true; request.Cancel(); timer.Stop(); Clear();
            await Task.WhenAll(loads.ToArray()); request.Dispose();
            try { Directory.Delete(temporary, true); } catch (IOException error) { Console.Error.WriteLine("Önizleme temizlenemedi: " + error.Message); }
        };
    }
    internal void Navigate(int delta)
    {
        if (closed || paths.Length < 2) return;
        index = (index + delta % paths.Length + paths.Length) % paths.Length; page = 0; password.Text = ""; Reload();
    }
    private void Clear()
    {
        preview.Content = null; canvas?.Dispose(); canvas = null; playback?.Dispose(); playback = null; image?.Dispose(); image = null;
        mediaControls.IsVisible = false; pdfControls.IsVisible = false; pageCaption.Text = "";
    }
    private async void Reload()
    {
        if (closed) return;
        request.Cancel(); request.Dispose(); request = new(); var token = request.Token;
        Clear(); caption.Text = $"{index + 1}/{paths.Length} · {Path.GetFileName(CurrentPath)}"; preview.Content = MainWindow.Label("Önizleme hazırlanıyor…", 14, true);
        var task = Load(CurrentPath, token); loads.Add(task);
        try { await task; } finally { loads.Remove(task); }
    }
    private async Task Load(string path, CancellationToken token)
    {
        try
        {
            if (Directory.Exists(path) && FeatureCatalog.Extension(path) is not ("pages" or "rtfd"))
            {
                var entries = Directory.EnumerateFileSystemEntries(path).Take(1001).ToArray(); token.ThrowIfCancellationRequested();
                preview.Content = new ListBox { ItemsSource = entries.Take(1000).Select(Path.GetFileName).Append(entries.Length > 1000 ? "İlk 1.000 öğe gösteriliyor." : "").ToArray() }; return;
            }
            if (!File.Exists(path) && !Directory.Exists(path)) { preview.Content = MainWindow.Label("Dosya artık bu konumda bulunmuyor.", 16); return; }
            if (FeatureCatalog.IsImage(path))
            {
                var png = await Task.Run(() => { using var source = PhotoCodec.Read(path); if (source.Width > 1600 || source.Height > 1200) source.Resize(new MagickGeometry(1600, 1200)); return source.ToByteArray(MagickFormat.Png); }, token);
                token.ThrowIfCancellationRequested(); ShowImage(png); return;
            }
            var ext = FeatureCatalog.Extension(path);
            if (new[] { "txt", "md", "csv", "tsv", "srt", "vtt", "json", "html", "xml", "log" }.Contains(ext))
            {
                await using var file = File.OpenRead(path); var bytes = new byte[2_000_001]; var count = await file.ReadAtLeastAsync(bytes, bytes.Length, false, token);
                if (count == bytes.Length) throw new InvalidDataException("Metin önizlemesi 2 MB sınırını aşıyor.");
                token.ThrowIfCancellationRequested(); preview.Content = new TextBox { Text = LegacyTextCodec.Decode(bytes[..count]), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }; return;
            }
            if (FeatureCatalog.IsVideo(path) || FeatureCatalog.IsAudio(path))
            {
                var info = await new MediaEngine(tools.Executable("ffmpeg"), tools.Executable("ffprobe")).InspectAsync(path, token);
                var png = await MediaPlayback.ArtworkAsync(tools, temporary, path, info, token); token.ThrowIfCancellationRequested();
                canvas = new(Math.Max(1, info.Video?.Width ?? 1200), Math.Max(1, info.Video?.Height ?? 140)) { Mode = "view" }; canvas.SetImage(png); preview.Content = canvas;
                var current = new MediaPlayback(info, silent: silentPlayback); playback = current;
                current.FrameUpdated += () => { if (!token.IsCancellationRequested && playback == current && current.Frame is { } frame) canvas?.SetFrame(frame); };
                current.Failed += text => { if (!token.IsCancellationRequested && playback == current) preview.Content = MainWindow.Label(text, 14); };
                updating = true; seek.Maximum = Math.Max(.001, info.Duration); seek.Value = 0; seek.IsEnabled = info.Duration > 0; updating = false;
                await current.OpenReadyAsync(path, token); token.ThrowIfCancellationRequested(); mediaControls.IsVisible = true; return;
            }
            var pdf = path;
            if (ext != "pdf" && Operations.Formats([path]).Contains("pdf"))
            {
                if (!converted.TryGetValue(path, out pdf))
                {
                    pdf = (await new DocumentConversion(tools).ConvertAsync([path], "pdf", temporary, new(), new Progress<OperationProgress>(), token)).Single();
                    token.ThrowIfCancellationRequested(); converted[path] = pdf;
                }
            }
            if (FeatureCatalog.Extension(pdf) == "pdf")
            {
                pdfControls.IsVisible = true; var secret = password.Text ?? "";
                using (var source = PdfReader.Open(pdf, secret, PdfDocumentOpenMode.Import)) pageCount = source.PageCount;
                if (pageCount == 0) throw new InvalidDataException("PDF içinde sayfa yok."); page = Math.Clamp(page, 0, pageCount - 1);
                var rendered = await LinuxPdf.RenderPagesAsync(pdf, [page + 1], 120, token, secret); token.ThrowIfCancellationRequested();
                ShowImage(rendered[page + 1]); pageCaption.Text = $"Sayfa {page + 1}/{pageCount}"; return;
            }
            token.ThrowIfCancellationRequested(); preview.Content = MainWindow.Label($"{Path.GetFileName(path)}\n{new FileInfo(path).Length:N0} bayt · Bu dosya için görsel önizleme yok.", 16);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested && !closed) preview.Content = MainWindow.Label("Önizleme hazırlanamadı: " + error.Message, 14); }
    }
    private void ShowImage(byte[] png)
    {
        using var stream = new MemoryStream(png); var next = new Bitmap(stream); image?.Dispose(); image = next; preview.Content = new Image { Source = next, Stretch = Stretch.Uniform };
    }
}
