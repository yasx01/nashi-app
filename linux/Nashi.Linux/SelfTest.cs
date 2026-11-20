using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
using System.IO.Compression;

namespace Nashi.Linux;

internal static class SelfTest
{
    public static void Run(string preview)
    {
        var root = Path.Combine(Path.GetTempPath(), "nashi-linux-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Task.Run(() => CheckOperations(root)).GetAwaiter().GetResult();
            var window = new MainWindow(dataDirectory: Path.Combine(root, "library"));
            window.Show(); Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame."))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(preview))!);
                frame.Save(preview);
            }
            var labels = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Check(labels.Contains("Dosyaların. Bir sonraki halleri."), "Home rendered");
            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Check(window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Watermark == "Dosya veya işlem ara").IsEffectivelyVisible, "Library keyboard shortcut");
            window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Digit1, RawInputModifiers.Control);
            var theme = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Görünüm");
            theme.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Check(window.Background!.ToString() == "#fff9f6ed", "Light theme");
            using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("No light frame.")) frame.Save(Path.ChangeExtension(preview, ".light.png"));
            window.Close();
            var selectedWindow = new MainWindow([Path.Combine(root, "Görsel ' test.png")], Path.Combine(root, "wheel-library"));
            selectedWindow.Show(); Dispatcher.UIThread.RunJobs();
            selectedWindow.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control); selectedWindow.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            var wheel = selectedWindow.OwnedWindows.Single();
            Check(wheel.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "QR oku"), "Wheel advertises QR reader");
            using (var frame = wheel.CaptureRenderedFrame() ?? throw new Exception("No wheel frame.")) frame.Save(Path.ChangeExtension(preview, ".wheel.png"));
            wheel.Close(); selectedWindow.Close();
            Console.WriteLine("PASS: image/PDF/subtitle exports, originals, duplicate names, cancellation, invalid crop, Home render and Library shortcut; installed FFmpeg/LibreOffice checks included when available.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); }
    private static async Task CheckOperations(string root)
    {
        var input = Path.Combine(root, "Görsel ' test.png");
        using (var image = new MagickImage(MagickColors.Gold, 80, 60)) image.Write(input);
        var original = File.ReadAllBytes(input); var target = Path.Combine(root, "exports");
        var operations = new Operations(new Toolchain(Path.Combine(root, "tools")));
        var progress = new Progress<OperationProgress>();
        Task<IReadOnlyList<string>> Run(string action, string format, CancellationToken token = default, int width = 40, int height = 30, int x = 0) =>
            operations.RunAsync(action, [input], target, format, 85, width, height, x, 0, new MediaOptions(), progress, token);
        Check(Operations.Formats([input]).Contains("jpg"), "PNG to JPG available");
        Check(Operations.Formats(["example.pages"]).Length == 0, "Unavailable routes hidden");
        var jpg = (await Run("convert", "jpg"))[0];
        using (var image = PhotoCodec.Read(jpg)) Check(image.Width == 80 && image.Height == 60, "JPEG dimensions");
        var second = (await Run("convert", "jpg"))[0]; Check(jpg != second && File.Exists(jpg), "No overwrite");
        var pdf = (await Run("convert", "pdf"))[0]; Check(File.ReadAllBytes(pdf).Take(4).SequenceEqual("%PDF"u8.ToArray()), "PDF output");
        var merged = await operations.RunAsync("mergePDF", [pdf, pdf], target, "", 85, 0, 0, 0, 0, new(), progress, default);
        var split = await operations.RunAsync("splitPDF", merged.ToArray(), target, "", 85, 0, 0, 0, 0, new(), progress, default);
        Check(split.Count == 2 && split.All(File.Exists), "Merge and split PDF");
        var resized = (await Run("editImage", "png"))[0];
        using (var image = PhotoCodec.Read(resized)) Check(image.Width == 40 && image.Height == 30, "Resize");
        var adjusted = await operations.RunAsync("editImage", [input], target, "", 85, 80, 60, 0, 0, new(), progress, default, new PhotoAdjustments { Brightness = -.5 });
        using (var image = PhotoCodec.Read(adjusted[0]))
        using (var pixels = image.GetPixels()) Check(pixels.GetPixel(0, 0).GetChannel(0) < 200, "Photo adjustment changes pixels");
        try { await Run("cropImage", "png", x: 70); throw new Exception("Invalid crop accepted."); } catch (ArgumentException) { }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await Run("convert", "jpg", cancellation.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
        Check(original.SequenceEqual(File.ReadAllBytes(input)), "Original unchanged");
        Check(!Directory.EnumerateDirectories(target, ".nashi-*").Any(), "Staging cleaned");
        var subtitle = Path.Combine(root, "test.srt"); await File.WriteAllTextAsync(subtitle, "1\n00:00:01,000 --> 00:00:02,000\nMerhaba\n");
        var vtt = await operations.RunAsync("convert", [subtitle], target, "vtt", 85, 0, 0, 0, 0, new(), progress, default);
        Check(File.ReadAllText(vtt[0]).StartsWith("WEBVTT"), "Subtitle conversion");
        var zip = Path.Combine(root, "archive.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntryFromFile(input, "source.png");
        var extracted = await operations.RunAsync("extractArchive", [zip], target, "", 85, 0, 0, 0, 0, new(), progress, default);
        Check(File.ReadAllBytes(Path.Combine(extracted.Single(), "source.png")).SequenceEqual(original), "Archive extraction preserves bytes");
        var qr = new ZXing.BarcodeWriterPixelData { Format = ZXing.BarcodeFormat.QR_CODE, Options = new ZXing.Common.EncodingOptions { Width = 300, Height = 300, Margin = 4 } }.Write("https://example.com/nashi-fedora");
        var qrPath = Path.Combine(root, "qr.png");
        using (var image = new MagickImage())
        {
            image.ReadPixels(qr.Pixels, new PixelReadSettings((uint)qr.Width, (uint)qr.Height, StorageType.Char, PixelMapping.BGRA)); image.Write(qrPath);
        }
        Check(Operations.ReadQr(qrPath).Single() == "https://example.com/nashi-fedora", "QR payload decoded");
        if (File.Exists("/usr/bin/libreoffice"))
        {
            var text = Path.Combine(root, "Belge.txt"); await File.WriteAllTextAsync(text, "Nashi Fedora test document.");
            var document = await operations.RunAsync("convert", [text], target, "pdf", 85, 0, 0, 0, 0, new(), progress, default);
            Check(OfficeDocuments.PdfText(document[0]).Contains("Nashi Fedora"), "LibreOffice PDF contains source text");
        }
        if (File.Exists("/usr/bin/ffmpeg") && File.Exists("/usr/bin/ffprobe"))
        {
            var audio = Path.Combine(root, "audio.wav");
            await ProcessRunner.RunAsync("/usr/bin/ffmpeg", ["-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", audio]);
            var media = new Operations(new Toolchain(Path.Combine(root, "tools"), "/usr/bin"));
            var flac = await media.RunAsync("convert", [audio], target, "flac", 85, 0, 0, 0, 0, new(), progress, default);
            var info = await new MediaEngine("/usr/bin/ffmpeg", "/usr/bin/ffprobe").InspectAsync(flac[0]);
            Check(info.Duration > .9 && info.HasAudio, "Real audio export");
            var mono = await media.RunAsync("audioChannels", [audio], target, "", 85, 0, 0, 0, 0, new() { Channels = 1 }, progress, default);
            Check((await new MediaEngine("/usr/bin/ffmpeg", "/usr/bin/ffprobe").InspectAsync(mono[0])).Audio!.Channels == 1, "Mono export");
            if (Environment.GetEnvironmentVariable("NASHI_TEST_FULL_MEDIA") == "1")
            {
                var visual = await media.RunAsync("audioToVideo", [audio], target, "", 85, 0, 0, 0, 0, new(), progress, default);
                var engine = new MediaEngine("/usr/bin/ffmpeg", "/usr/bin/ffprobe");
                var video = await engine.InspectAsync(visual.Single());
                Check(video.Video is { Width: 1280, Height: 720 } && video.HasAudio, "Audio visualization video");
                var parts = await media.RunAsync("splitVideo", visual.ToArray(), target, "", 85, 0, 0, 0, 0, new() { EqualClips = 2 }, progress, default);
                Check(parts.Count == 2, "Two video clips");
                var frame = await media.RunAsync("videoSnapshots", visual.ToArray(), target, "", 85, 0, 0, 0, 0, new() { SnapshotTimes = [.5] }, progress, default);
                using var snapshot = PhotoCodec.Read(frame.Single());
                Check(snapshot.Width == 1280, "Snapshot at requested time");
                Console.WriteLine("PASS: full media visualization, split and snapshot checks.");
            }
        }
    }
}
