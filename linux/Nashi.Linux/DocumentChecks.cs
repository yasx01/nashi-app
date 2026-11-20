using ImageMagick;
using Nashi.Core;
using System.IO.Compression;

namespace Nashi.Linux;

internal static class DocumentChecks
{
    internal static async Task Run(string root)
    {
        var folder = Path.Combine(root, "document-conversions"); Directory.CreateDirectory(folder);
        void Check(bool value, string name) { if (!value) throw new Exception("Documents: " + name); }
        if (!File.Exists("/usr/bin/libreoffice") || !File.Exists("/usr/bin/pdftoppm")) { Console.WriteLine("SKIP: document conversion checks need LibreOffice and poppler-utils."); return; }
        var toolDirectory = Environment.GetEnvironmentVariable("NASHI_VERIFY_TOOLS_DIRECTORY") ?? Path.Combine(root, "document-tools");
        var tools = new Toolchain(toolDirectory, Environment.GetEnvironmentVariable("PATH")); var convert = new DocumentConversion(tools);
        var progress = new Progress<OperationProgress>(); var target = Path.Combine(folder, "output");
        Task<IReadOnlyList<string>> Convert(string input, string format, DocumentOptions? options = null) => convert.ConvertAsync([input], format, target, options ?? new(), progress, default);
        var text = Path.Combine(folder, "Türkçe belge.txt"); await File.WriteAllTextAsync(text, "Merhaba Fedora 123\nSecond document line."); var original = File.ReadAllBytes(text);
        var pdf = (await Convert(text, "pdf")).Single();
        var rendered = await LinuxPdf.RenderAsync(pdf, 72);
        Check(rendered.Length == 1 && OfficeDocuments.PngSize(rendered[0]) is { Width: > 500, Height: > 700 }, "PDF raster dimensions");
        Check((await LinuxPdf.TextAsync(pdf, "", default)).Contains("Merhaba Fedora 123"), "PDF content extraction");
        foreach (var format in new[] { "png", "jpg", "docx", "pptx", "xlsx", "epub", "txt", "md" })
        {
            var outputs = await Convert(pdf, format); Check(outputs.Count == 1 && new FileInfo(outputs[0]).Length > 0, "PDF to " + format);
            if (format == "docx")
            {
                var roundtrip = (await Convert(outputs[0], "pdf")).Single();
                Check((await LinuxPdf.TextAsync(roundtrip, "", default)).Contains("Merhaba Fedora 123"), "editable document preserves text");
            }
        }
        var fixedEpub = (await Convert(pdf, "epub", new(EpubLayout: "fixed"))).Single();
        using (var zip = ZipFile.OpenRead(fixedEpub))
        {
            using var reader = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open());
            Check(reader.ReadToEnd().Contains("pre-paginated") && zip.GetEntry("OEBPS/page0.png") is not null, "fixed EPUB includes page image");
        }
        var preserved = (await Convert(pdf, "docx", new(Layout: "preserve"))).Single();
        using (var zip = ZipFile.OpenRead(preserved)) Check(zip.Entries.Any(e => e.FullName.StartsWith("word/media/")), "preserved layout embeds page image");
        var qrPdf = (await new Operations(tools).RunAsync("convert", [Path.Combine(root, "qr.png")], target, "pdf", 85, 0, 0, 0, 0, new(), progress, default)).Single();
        Check(Operations.ReadQr((await LinuxPdf.RenderAsync(qrPdf))[0]).Contains("https://example.com/nashi-fedora"), "QR decoded from PDF page");
        if (File.Exists("/usr/bin/tesseract"))
        {
            var scan = Path.Combine(folder, "scanned.png");
            using (var image = new MagickImage(MagickColors.White, 1000, 250))
            {
                ImageEditing.Annotate(image, [new("text", new(40, 20, 900, 200), "#000000", FontSize: 64, Text: "FEDORA OCR TEST 123")]); image.Write(scan);
            }
            var scannedPdf = (await new Operations(tools).RunAsync("convert", [scan], target, "pdf", 85, 0, 0, 0, 0, new(), progress, default)).Single();
            var recognized = await LinuxPdf.TextAsync(scannedPdf, "", default);
            Check(recognized.Contains("FEDORA", StringComparison.OrdinalIgnoreCase) && recognized.Contains("123"), "scanned PDF OCR text: " + recognized.Trim());
            Console.WriteLine("PASS: real scanned PDF OCR recognizes source words and numbers.");
        }
        else Console.WriteLine("SKIP: scanned PDF OCR check needs system Tesseract.");
        try { await LinuxPdf.RenderAsync(pdf, 5000); throw new Exception("Unbounded DPI accepted"); } catch (ArgumentException) { }
        var rtfd = Path.Combine(folder, "attached.rtfd"); Directory.CreateDirectory(rtfd); File.Copy(Path.Combine(root, "qr.png"), Path.Combine(rtfd, "image.png"));
        await File.WriteAllTextAsync(Path.Combine(rtfd, "TXT.rtf"), "{\\rtf1\\ansi Attached picture {\\NeXTGraphic image.png \\width100 \\height100}}", System.Text.Encoding.Latin1);
        var materialized = await RichTextAttachments.Materialize(rtfd, folder, default); Check((await File.ReadAllTextAsync(materialized)).Contains("\\pngblip"), "RTFD embeds attachment");
        var link = Path.Combine(rtfd, "escape"); Directory.CreateSymbolicLink(link, folder);
        await File.WriteAllTextAsync(Path.Combine(rtfd, "TXT.rtf"), "{\\rtf1{\\NeXTGraphic escape/Türkçe belge.txt}}", System.Text.Encoding.Latin1);
        try { await RichTextAttachments.Materialize(rtfd, folder, default); throw new Exception("RTFD symlink followed"); } catch (InvalidDataException) { }
        Directory.Delete(link);
        if (tools.Ready)
        {
            var pages = (await Convert(text, "pages")).Single(); Check(new FileInfo(pages).Length > 0, "Pages written");
            var read = (await Convert(pages, "pdf")).Single(); Check((await LinuxPdf.TextAsync(read, "", default)).Contains("Merhaba Fedora 123"), "Pages roundtrip text");
            var package = Path.Combine(folder, "package.pages"); ZipFile.ExtractToDirectory(pages, package);
            var unpacked = (await Convert(package, "txt")).Single(); Check((await File.ReadAllTextAsync(unpacked)).Contains("Merhaba Fedora 123"), "folder Pages package");
            var pdfPages = (await Convert(pdf, "pages")).Single();
            Check((await File.ReadAllTextAsync((await Convert(pdfPages, "txt")).Single())).Contains("Merhaba Fedora 123"), "PDF to Pages preserves text");
        }
        else Console.WriteLine("SKIP: Pages roundtrip needs the verified media/Deno toolchain.");
        Check(File.ReadAllBytes(text).SequenceEqual(original), "document original unchanged");
        Console.WriteLine("PASS: PDF rendering/QR/document destinations, editable/preserved layouts, fixed EPUB, RTFD attachment boundaries and document originals.");
    }
}
