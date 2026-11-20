using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;

namespace Nashi.Linux;

internal static class MetadataChecks
{
    internal static async Task Run(string root)
    {
        var toolsPath = Environment.GetEnvironmentVariable("NASHI_VERIFY_TOOLS_DIRECTORY");
        if (toolsPath is null) { Console.WriteLine("SKIP: metadata roundtrip needs the verified tool directory."); return; }
        var metadata = new MetadataService(toolsPath + " Metadata");
        if (metadata.Executable is null) { Console.WriteLine("SKIP: metadata roundtrip needs verified ExifTool setup."); return; }
        void Check(bool result, string label) { if (!result) throw new Exception("Metadata: " + label); }
        var folder = Path.Combine(root, "metadata"); Directory.CreateDirectory(folder);
        var photo = Path.Combine(folder, "Türkçe ' kaynak.jpg"); using (var image = new MagickImage(MagickColors.Gold, 32, 24)) image.Write(photo);
        var artist = " Türkçe & ilk satır\nikinci satır";
        photo = (await metadata.WriteAsync([photo], folder, false, new Dictionary<string, string> { ["EXIF:Artist"] = artist })).Single();
        await ProcessRunner.RunAsync(metadata.Executable, ["-overwrite_original", "-Orientation#=6", "--", photo]);
        var original = await File.ReadAllBytesAsync(photo); byte[] pixels;
        using (var image = new MagickImage(photo)) pixels = image.ToByteArray(MagickFormat.Rgb);
        var ops = new Operations(new Toolchain(toolsPath, "/usr/bin")); var progress = new Progress<OperationProgress>();
        var edited = (await ops.RunAsync("removeMetadata", [photo], folder, "", 85, 0, 0, 0, 0, new(), progress, default,
            metadataOptions: new(false, new Dictionary<string, string> { ["EXIF:Copyright"] = "Nashi test" }))).Single();
        var values = await metadata.ReadAsync(edited);
        Check(values.GetValueOrDefault("EXIF:Artist") == artist && values.GetValueOrDefault("EXIF:Copyright") == "Nashi test", "field edit retains other tags and multiline Unicode");
        using (var image = new MagickImage(edited)) Check(image.ToByteArray(MagickFormat.Rgb).SequenceEqual(pixels), "metadata edit does not re-encode image pixels");
        var removed = (await ops.RunAsync("removeMetadata", [edited], folder, "", 85, 0, 0, 0, 0, new(), progress, default)).Single();
        var cleaned = await metadata.ReadAsync(removed);
        Check(!cleaned.ContainsKey("EXIF:Artist") && !cleaned.ContainsKey("EXIF:Copyright") && cleaned.GetValueOrDefault("EXIF:Orientation") == values.GetValueOrDefault("EXIF:Orientation"), "cleanup removes private tags and preserves orientation");
        var converted = (await ops.RunAsync("convert", [photo], folder, "png", 85, 0, 0, 0, 0, new(), progress, default, imageOptions: new() { PreserveMetadata = true })).Single();
        var kept = await metadata.ReadAsync(converted);
        using (var image = new MagickImage(converted)) Check(image.Width == 24 && image.Height == 32, "conversion applies source orientation");
        Check(kept.GetValueOrDefault("EXIF:Artist") == artist && kept.GetValueOrDefault("EXIF:Orientation") == "Horizontal (normal)", "conversion keeps tags without stale rotation");
        var stripped = (await ops.RunAsync("convert", [photo], folder, "png", 85, 0, 0, 0, 0, new(), progress, default)).Single();
        Check(!(await metadata.ReadAsync(stripped)).ContainsKey("EXIF:Artist"), "conversion can omit private metadata");
        var redacted = (await ops.RunAsync("redactImage", [photo], folder, "", 85, 0, 0, 0, 0, new(), progress, default,
            imageOptions: new() { PreserveMetadata = true, Redactions = [new(0, 0, 10, 10)] })).Single();
        Check(!(await metadata.ReadAsync(redacted)).ContainsKey("EXIF:Artist"), "redaction never reattaches private metadata");
        Check(original.SequenceEqual(File.ReadAllBytes(photo)), "source bytes remain unchanged");
        Console.WriteLine("PASS: metadata field edit/remove, Unicode/multiline roundtrip, unchanged decoded pixels, preserved conversion tags/orientation and redaction privacy.");
    }
    internal static void Interact(string root, string preview)
    {
        var host = new MainWindow(dataDirectory: Path.Combine(root, "metadata-editor-library")); host.Show();
        var source = new Dictionary<string, string> { ["SourceFile"] = "photo.jpg", ["EXIF:Artist"] = "Orijinal", ["EXIF:Copyright"] = "Korunan" };
        var editor = new MetadataEditorWindow(host, source, false); _ = editor.ShowDialog(host); Dispatcher.UIThread.RunJobs();
        var fields = editor.GetLogicalDescendants().OfType<TextBox>().Where(field => AutomationProperties.GetName(field) is not null).ToDictionary(field => AutomationProperties.GetName(field)!);
        fields["EXIF:Artist"].Text = " Türkçe\nYeni sanatçı";
        var changes = editor.Options(); if (changes.RemoveAll || changes.Updates?.Count != 1 || changes.Updates["EXIF:Artist"] != " Türkçe\nYeni sanatçı") throw new Exception("Metadata UI did not isolate changed fields.");
        using (var frame = editor.CaptureRenderedFrame() ?? throw new Exception("Metadata frame missing")) frame.Save(Path.ChangeExtension(preview, ".metadata.png"));
        editor.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
        if (!editor.Options().RemoveAll || fields.Values.Any(f => f.IsEnabled)) throw new Exception("Metadata cleanup UI leaves editing enabled.");
        editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Kullan").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (editor.Result?.RemoveAll != true) throw new Exception("Metadata UI did not return cleanup choice."); host.Close();
        Console.WriteLine("PASS: metadata UI shows source fields, edits only changed values and disables fields during full cleanup.");
    }
}
