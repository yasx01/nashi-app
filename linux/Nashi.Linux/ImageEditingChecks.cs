using ImageMagick;
using Nashi.Core;
using PdfSharp.Pdf.IO;

namespace Nashi.Linux;

internal static class ImageEditingChecks
{
    internal static async Task Run(string root)
    {
        var folder = Path.Combine(root, "image-editing"); Directory.CreateDirectory(folder);
        var white = Path.Combine(folder, "white.png"); var red = Path.Combine(folder, "red.png");
        using (var image = new MagickImage(MagickColors.White, 100, 80)) image.Write(white);
        using (var image = new MagickImage(MagickColors.Red, 80, 60)) image.Write(red);
        var original = File.ReadAllBytes(white); var progress = new Progress<OperationProgress>();
        void Check(bool condition, string message) { if (!condition) throw new Exception("Image editing: " + message); }
        var ops = new Operations(new Toolchain(Path.Combine(root, "tools")));
        Task<IReadOnlyList<string>> Export(string action, ImageEditOptions options, string[]? inputs = null) =>
            ops.RunAsync(action, inputs ?? [white], Path.Combine(folder, "outputs"), "", options.Quality, 0, 0, 0, 0, new(), progress, default, imageOptions: options);
        foreach (var kind in new[] { "rectangle", "ellipse", "line", "arrow", "freehand", "highlight", "text", "image" })
        {
            var options = new ImageEditOptions { Marks = [new(kind, new(5, 5, 60, 35), "#000000", 4, 16, "Türkçe ' % @", [new(5, 5), new(30, 20), new(65, 40)], red)] };
            var output = (await Export("annotateImage", options)).Single();
            using var image = PhotoCodec.Read(output); using var source = PhotoCodec.Read(white);
            Check(image.Width == 100 && image.Height == 80 && !image.ToByteArray(MagickFormat.Rgb).SequenceEqual(source.ToByteArray(MagickFormat.Rgb)), kind + " paints pixels");
        }
        var batch = await Export("cropImage", new() { Crop = new(5, 5, 20, 20) }, [white, red]);
        using (var image = PhotoCodec.Read(batch[1]))
        using (var pixels = image.GetPixels()) Check(pixels.GetPixel(10, 10).GetChannel(1) == 0, "batch keeps each original");
        var redacted = (await Export("redactImage", new() { Redactions = [new(10, 10, 20, 20)] })).Single();
        using (var image = PhotoCodec.Read(redacted))
        using (var pixels = image.GetPixels()) Check(pixels.GetPixel(20, 20).GetChannel(0) == 0 && pixels.GetPixel(80, 60).GetChannel(0) == 255, "solid redaction changes only its area");
        foreach (var style in new[] { "blur", "pixelate" }) await Export("redactImage", new() { Redactions = [new(2, 2, 30, 30)], RedactionStyle = style });
        var framed = (await Export("frameImage", new() { Background = new(Padding: 20, Color: "#0000FF", Shadow: 0, Radius: 0) })).Single();
        using (var image = PhotoCodec.Read(framed))
        using (var pixels = image.GetPixels()) Check(image.Width == 140 && image.Height == 120 && pixels.GetPixel(0, 0).GetChannel(2) == 255 && pixels.GetPixel(0, 0).GetChannel(0) == 0, "background size and color");
        foreach (var backdrop in new[] { "gradient", "image", "transparent" }) await Export("frameImage", new() { Background = new(Kind: backdrop, ImagePath: red, Blur: 2) });
        foreach (var layout in new[] { "grid", "row", "column", "featured" })
        {
            var collage = (await Export("createCollage", new() { Collage = new(Layout: layout, Width: 200, Height: 160, Padding: 0, Spacing: 0, Order: [1, 0]) }, [white, red])).Single();
            using var image = PhotoCodec.Read(collage); using var pixels = image.GetPixels();
            Check(image.Width == 200 && image.Height == 160 && pixels.GetPixel(10, 10).GetChannel(1) == 0, layout + " honors order");
        }
        var pdf = (await Export("createPDF", new() { InputOrder = [1, 0] }, [white, red])).Single();
        using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import)) Check(document.PageCount == 2 && document.Pages[0].Width.Point == 60, "multi-page PDF order");
        var crop = (await Export("cropImage", new() { Crop = new(10, 20, 30, 40) })).Single();
        using (var image = PhotoCodec.Read(crop)) Check(image.Width == 30 && image.Height == 40, "crop size");
        var rotated = (await Export("editImage", new() { Rotation = 90, FlipHorizontal = true })).Single();
        using (var image = PhotoCodec.Read(rotated)) Check(image.Width == 80 && image.Height == 100, "quarter turn dimensions");
        var noise = Path.Combine(folder, "noise.png");
        using (var image = new MagickImage(MagickColors.White, 300, 200)) { image.AddNoise(NoiseType.Random); image.Write(noise); }
        var compressed = (await Export("compress", new() { TargetBytes = 10000 }, [noise])).Single();
        Check(new FileInfo(compressed).Length <= 10000 && new FileInfo(noise).Length > 10000, "compression meets target on noisy input");
        try { await Export("redactImage", new() { Redactions = [new(double.NaN, 0, 20, 20)] }); throw new Exception("NaN accepted"); } catch (ArgumentException) { }
        try { await Export("createCollage", new() { Collage = new(Order: [0, 0]) }, [white, red]); throw new Exception("Duplicate order accepted"); } catch (ArgumentException) { }
        Check(File.ReadAllBytes(white).SequenceEqual(original), "original unchanged");
        Console.WriteLine("PASS: all image tools, eight annotation kinds, redaction styles, four collage layouts, backgrounds, PDF ordering, crop, rotation, compression and invalid geometry.");
    }
}
