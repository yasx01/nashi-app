using ImageMagick;
using Nashi.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Nashi.Linux;

internal static class PdfCompressionChecks
{
    internal static async Task Run(string root)
    {
        void Check(bool value, string label) { if (!value) throw new Exception("PDF compression: " + label); }
        var folder = Path.Combine(root, "pdf-compression"); Directory.CreateDirectory(folder);
        var ops = new Operations(new Toolchain(Path.Combine(root, "pdf-compression-tools"))); var progress = new Progress<OperationProgress>();
        var input = Path.Combine(folder, "noise.png");
        var random = new Random(23); var pixels = new byte[512 * 512 * 3]; random.NextBytes(pixels);
        using (var image = new MagickImage()) { image.ReadPixels(pixels, new PixelReadSettings(512, 512, StorageType.Char, PixelMapping.RGB)); image.Write(input); }
        var pdfFile = (await ops.RunAsync("convert", [input], folder, "pdf", 85, 0, 0, 0, 0, new(), progress, default)).Single();
        using (var pdf = PdfReader.Open(pdfFile, PdfDocumentOpenMode.Modify)) { pdf.Info.Title = "Korunan başlık"; pdf.Save(pdfFile); }
        var original = await File.ReadAllBytesAsync(pdfFile);
        var output = (await ops.RunAsync("compress", [pdfFile], folder, "", 75, 0, 0, 0, 0, new() { TargetBytes = 150000 }, progress, default)).Single();
        Check(new FileInfo(output).Length <= 150000 && new FileInfo(output).Length < original.Length, "real scanned PDF meets byte target");
        using (var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import)) Check(pdf.PageCount == 1 && pdf.Info.Title == "Korunan başlık", "page count and properties retained");
        Check((await LinuxPdf.RenderAsync(output, 36)).Length == 1, "compressed PDF renders");
        var formFile = Path.Combine(folder, "form.pdf");
        using (var pdf = new PdfDocument())
        {
            pdf.AddPage(); var form = new PdfDictionary(pdf); var field = new PdfDictionary(pdf); field.Elements.SetString("/T", "Name"); pdf.Internals.AddObject(field);
            form.Elements["/Fields"] = new PdfArray(pdf, field.Reference!); pdf.Internals.AddObject(form); pdf.Internals.Catalog.Elements.SetReference("/AcroForm", form); pdf.Save(formFile);
        }
        var formBytes = await File.ReadAllBytesAsync(formFile);
        var kept = (await ops.RunAsync("compress", [formFile], folder, "", 85, 0, 0, 0, 0, new(), progress, default)).Single();
        Check(formBytes.SequenceEqual(File.ReadAllBytes(kept)), "interactive forms remain byte-identical");
        var failureDirectory = Path.Combine(folder, "failed");
        try { await ops.RunAsync("compress", [formFile], failureDirectory, "", 85, 0, 0, 0, 0, new() { TargetBytes = 1 }, progress, default); throw new Exception("Impossible target accepted."); } catch (IOException) { }
        Check(!Directory.Exists(failureDirectory) || !Directory.EnumerateFiles(failureDirectory, "*", SearchOption.AllDirectories).Any(), "impossible target publishes no partial file");
        Check(original.SequenceEqual(File.ReadAllBytes(pdfFile)) && formBytes.SequenceEqual(File.ReadAllBytes(formFile)), "originals remain unchanged");
        Console.WriteLine("PASS: scanned PDF target size/render/properties, byte-identical forms, failed-target rollback and unchanged originals.");
    }
}
