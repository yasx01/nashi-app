using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Nashi.Core;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Nashi.Linux;

internal static class MediaEditorChecks
{
    internal static void Run((string File, MediaInfo Info) fixture, string root, string preview)
    {
        var original = SHA256.HashData(File.ReadAllBytes(fixture.File));
        var tools = new Toolchain(Environment.GetEnvironmentVariable("NASHI_VERIFY_TOOLS_DIRECTORY") ?? Path.Combine(root, "media-editor-tools"), Environment.GetEnvironmentVariable("PATH"));
        var host = new MainWindow(dataDirectory: Path.Combine(root, "media-editor-library")); host.Show();
        void Check(bool result, string label) { if (!result) throw new Exception("Media editor: " + label); }
        void Ready(MediaEditorWindow window)
        {
            var timeout = Stopwatch.StartNew();
            do { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); } while (window.Busy && timeout.Elapsed < TimeSpan.FromSeconds(45));
            Check(!window.Busy, "background preview finishes");
        }
        NumericUpDown Field(MediaEditorWindow window, string name) => window.GetLogicalDescendants().OfType<NumericUpDown>().Single(f => AutomationProperties.GetName(f) == name);
        void Click(MediaEditorWindow window, string text) => window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Drag(MediaEditorWindow window)
        {
            using (window.CaptureRenderedFrame()) { }
            var canvas = window.EditorCanvas;
            var a = canvas.TranslatePoint(canvas.View(new(8, 8)), window)!.Value;
            var b = canvas.TranslatePoint(canvas.View(new(40, 32)), window)!.Value;
            window.MouseDown(a, MouseButton.Left); window.MouseMove(b, RawInputModifiers.LeftMouseButton); window.MouseUp(b, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        }
        var redact = new MediaEditorWindow(host, tools, "redactVideo", [fixture.File], fixture.Info, silentPlayback: true);
        _ = redact.ShowDialog(host); Dispatcher.UIThread.RunJobs(); Ready(redact); Drag(redact);
        Field(redact, "Başlangıç (saniye)").Value = .5m; Field(redact, "Bitiş (saniye)").Value = 1.5m;
        Click(redact, "Seçili aralığı / alanı ekle");
        var region = redact.Options().Regions.Single();
        Check(Math.Abs(region.X - 8) < .1 && Math.Abs(region.Width - 32) < .1 && region.Start == .5 && region.End == 1.5, "pointer region retains source coordinates and timing");
        Click(redact, "İşlenmiş çıktıyı önizle"); Ready(redact);
        Check(redact.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("İşlenmiş çıktı ·") == true), "processed preview opens real export");
        using (var frame = redact.CaptureRenderedFrame() ?? throw new Exception("Media render missing")) frame.Save(Path.ChangeExtension(preview, ".media.png"));
        redact.Close();
        var crop = new MediaEditorWindow(host, tools, "cropVideo", [fixture.File], fixture.Info, silentPlayback: true);
        _ = crop.ShowDialog(host); Dispatcher.UIThread.RunJobs(); Ready(crop); Drag(crop);
        var options = crop.Options(); Check(options.Regions.Length == 1 && Math.Abs(options.Regions[0].Height - 24) < .1, "crop uses exact source coordinates");
        Click(crop, "Kullan"); Check(crop.Result is not null, "accept returns complete media settings");
        var export = Task.Run(async () =>
        {
            var files = await new Operations(tools).RunAsync("cropVideo", [fixture.File], Path.Combine(root, "media-crop"), "", 85, 0, 0, 0, 0, options, new Progress<OperationProgress>(), default);
            return await new MediaEngine(tools.Executable("ffmpeg"), tools.Executable("ffprobe")).InspectAsync(files.Single());
        }).GetAwaiter().GetResult();
        Check(export.Video is { Width: 32, Height: 24 }, "pointer crop reaches exported video");
        var audioFile = Path.ChangeExtension(fixture.File, ".wav");
        var audioInfo = Task.Run(() => new MediaEngine(tools.Executable("ffmpeg"), tools.Executable("ffprobe")).InspectAsync(audioFile)).GetAwaiter().GetResult();
        var audio = new MediaEditorWindow(host, tools, "redactAudio", [audioFile], audioInfo, silentPlayback: true);
        _ = audio.ShowDialog(host); Dispatcher.UIThread.RunJobs(); Ready(audio);
        Field(audio, "Başlangıç (saniye)").Value = .2m; Field(audio, "Bitiş (saniye)").Value = .7m;
        Click(audio, "Seçili aralığı / alanı ekle");
        var rangePlayback = audio.PlayRange(); var seekTimeout = Stopwatch.StartNew();
        while (!rangePlayback.IsCompleted && seekTimeout.Elapsed < TimeSpan.FromSeconds(5)) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        rangePlayback.GetAwaiter().GetResult();
        Field(audio, "Başlangıç (saniye)").Value = 1m; Field(audio, "Bitiş (saniye)").Value = 1.5m;
        Click(audio, "Seçili aralığı / alanı ekle");
        var mode = audio.GetLogicalDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Sansür türü"); mode.SelectedIndex = 1;
        Check(audio.Options().AudioRedactions.Length == 2 && audio.Options().RedactionMode == "mute", "multiple audio ranges and mute mode");
        Click(audio, "İşlenmiş çıktıyı önizle"); Ready(audio);
        Check(audio.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("İşlenmiş çıktı ·") == true), "audio waveform and real processed playback source");
        audio.Close();
        var trim = new MediaEditorWindow(host, tools, "trimVideo", [fixture.File], fixture.Info, silentPlayback: true);
        _ = trim.ShowDialog(host); Dispatcher.UIThread.RunJobs(); Ready(trim);
        Field(trim, "Başlangıç (saniye)").Value = 2m; Field(trim, "Bitiş (saniye)").Value = 1m;
        try { trim.Options(); throw new Exception("Media editor accepted reversed range."); } catch (ArgumentException) { }
        trim.Close();
        Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture.File))), "source bytes stay unchanged"); host.Close();
        Console.WriteLine("PASS: media editor pointer crop/timed redaction, real processed video/audio export previews, multiple audio redactions, reversed range rejection and unchanged original.");
    }
}
