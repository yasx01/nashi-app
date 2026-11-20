using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nashi.Core;
using System.Diagnostics;

namespace Nashi.Linux;

internal static class LibraryChecks
{
    internal static void Run(string root, string preview, (string File, MediaInfo Info)? media)
    {
        void Check(bool value, string label) { if (!value) throw new Exception("Library/preview: " + label); }
        void Wait(Func<bool> condition, string label)
        {
            var timer = Stopwatch.StartNew();
            do { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); } while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(20));
            Check(condition(), label);
        }
        void Await(Task task) { Wait(() => task.IsCompleted, "async request finishes"); task.GetAwaiter().GetResult(); }
        void Click(Window window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var data = Path.Combine(root, "clipboard-library"); var host = new MainWindow(dataDirectory: data); host.Show(); Dispatcher.UIThread.RunJobs();
        var clipboard = host.Clipboard ?? throw new Exception("Headless clipboard unavailable.");
        const string text = " Türkçe metin\nİkinci satır & <script>";
        Await(clipboard.SetTextAsync(text)); Click(host, "Yapıştır");
        Wait(() => new LibraryStore(data).Entries.Count == 1, "plain text pasted into library");
        var textFile = new LibraryStore(data).Entries[0].Paths.Single(); Check(File.ReadAllText(textFile) == text, "clipboard text stays exact");
        using (var bitmap = new Bitmap(Path.Combine(root, "Nashi artwork.png")))
        {
            Await(clipboard.SetBitmapAsync(bitmap)); Click(host, "Yapıştır"); Wait(() => new LibraryStore(data).Entries.Count == 2, "bitmap paste");
            Click(host, "Yapıştır"); Wait(() => new LibraryStore(data).Entries.Count == 3, "same bitmap can be pasted again");
            var entries = new LibraryStore(data).Entries; Check(entries[0].Paths[0] != entries[1].Paths[0], "repeated paste keeps both files");
        }
        Await(clipboard.ClearAsync());
        var before = Directory.GetFiles(Path.Combine(data, "Imports")).Length;
        try { ClipboardImport.Text(new string('ğ', 9 * 1024 * 1024), data); throw new Exception("Oversized clipboard accepted."); } catch (ArgumentException) { }
        Check(Directory.GetFiles(Path.Combine(data, "Imports")).Length == before, "oversized text leaves no partial import");
        host.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control); host.KeyReleaseQwerty(PhysicalKey.Digit2, RawInputModifiers.Control); Dispatcher.UIThread.RunJobs();
        Click(host, "Tümünü seç"); Dispatcher.UIThread.RunJobs();
        Check(host.GetLogicalDescendants().OfType<CheckBox>().Where(c => c.IsEffectivelyVisible).Count(c => c.IsChecked == true) == 3, "library multi-selection");
        using (var frame = host.CaptureRenderedFrame() ?? throw new Exception("Library render missing")) frame.Save(Path.ChangeExtension(preview, ".library.png"));
        Click(host, "Seçilenleri önizle"); Dispatcher.UIThread.RunJobs();
        var selectedPreview = host.OwnedWindows.OfType<FilePreviewWindow>().Single(); Wait(() => !selectedPreview.Loading, "multi-file preview finishes");
        Check(selectedPreview.Status == nameof(Image), "library opens an image preview");
        using (var frame = selectedPreview.CaptureRenderedFrame() ?? throw new Exception("Preview render missing")) frame.Save(Path.ChangeExtension(preview, ".preview.png"));
        selectedPreview.Close(); Dispatcher.UIThread.RunJobs(); Click(host, "Geçmişten kaldır");
        Check(new LibraryStore(data).Entries.Count == 0 && File.Exists(textFile), "forget history persists without deleting files");

        var upper = Path.Combine(root, "Case.txt"); var lower = Path.Combine(root, "case.txt"); File.WriteAllText(upper, "A"); File.WriteAllText(lower, "a");
        var history = new LibraryStore(Path.Combine(root, "case-library")); history.Record("Case", [upper, lower]); Check(history.Entries[0].Paths.Length == 2, "Linux paths keep case-distinct files");
        var missing = Path.Combine(root, "missing.pdf");
        var tools = new Toolchain(Environment.GetEnvironmentVariable("NASHI_VERIFY_TOOLS_DIRECTORY") ?? Path.Combine(root, "preview-tools"), "/usr/bin");
        var files = new FilePreviewWindow(host, [Path.Combine(root, "organizer.pdf"), missing, Path.Combine(root, "encrypted.pdf"), textFile], tools, silentPlayback: true);
        _ = files.ShowDialog(host); Dispatcher.UIThread.RunJobs(); Wait(() => !files.Loading, "PDF preview finishes");
        Check(files.Status == nameof(Image), "PDF page raster preview");
        Click(files, "Sonraki sayfa"); Wait(() => !files.Loading, "next PDF page finishes");
        Check(files.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Sayfa 2/2"), "PDF next-page navigation");
        using (var frame = files.CaptureRenderedFrame() ?? throw new Exception("PDF preview missing")) frame.Save(Path.ChangeExtension(preview, ".preview-pdf.png"));
        files.Navigate(1); Wait(() => !files.Loading, "missing file preview finishes"); Check(files.Status.Contains("bulunmuyor"), "missing file gets an explicit message");
        files.Navigate(1); Wait(() => !files.Loading, "encrypted PDF failure finishes");
        Check(files.Status.StartsWith("Önizleme hazırlanamadı:"), "encrypted PDF requests password");
        files.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Watermark == "PDF şifresi (varsa)").Text = "reader";
        Click(files, "Şifreyle aç"); Wait(() => !files.Loading, "password preview finishes"); Check(files.Status == nameof(Image), "correct password unlocks preview");
        files.Navigate(1); Wait(() => !files.Loading, "text preview finishes");
        Check(files.GetLogicalDescendants().OfType<TextBox>().Any(t => t.IsReadOnly && t.Text == text), "text is displayed without evaluating markup");
        files.Navigate(-1); files.Navigate(-1); files.Navigate(1); Wait(() => !files.Loading, "rapid navigation cancels old preview loads");
        Check(files.CurrentPath.EndsWith("encrypted.pdf") && files.Status.StartsWith("Önizleme hazırlanamadı:"), "old requests cannot replace current preview"); files.Close();
        if (media is { } fixture)
        {
            var video = new FilePreviewWindow(host, [fixture.File, missing], tools, silentPlayback: true); _ = video.ShowDialog(host); Dispatcher.UIThread.RunJobs(); Wait(() => !video.Loading, "media preview finishes");
            Check(video.Status == nameof(ImageCanvas), "media preview uses embedded player");
            video.Navigate(1); video.Navigate(-1); Wait(() => !video.Loading, "media navigation finishes");
            Check(video.CurrentPath == fixture.File && video.Status == nameof(ImageCanvas), "media reload replaces old player safely"); video.Close();
        }
        host.Close(); Dispatcher.UIThread.RunJobs();
        Console.WriteLine("PASS: clipboard text/image/repeated paste/bounds; library selection/history/case-sensitive paths; PDF/password/text/missing/media previews and canceled navigation.");
    }
}
