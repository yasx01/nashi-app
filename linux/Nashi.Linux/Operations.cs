using ImageMagick;
using Nashi.Core;

namespace Nashi.Linux;

// The Linux UI advertises only routes implemented here, never the entire upstream catalog.
public sealed class Operations(Toolchain tools)
{
    private static readonly string[] ImageFormats = ["jpg", "png", "webp", "tiff", "avif", "bmp", "gif", "heic", "pdf"];
    public static string[] Formats(IReadOnlyList<string> inputs) => FeatureCatalog.Formats(inputs)
        .Where(format => inputs.All(input => !FeatureCatalog.IsImage(input) || ImageFormats.Contains(format))).ToArray();

    public static string[] Tools(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 0) return [];
        return FeatureCatalog.Tools(inputs).Where(id => id == "readQRCode" ||
            (inputs.All(FeatureCatalog.IsImage) ? true :
            inputs.All(p => FeatureCatalog.IsAudio(p) || FeatureCatalog.IsVideo(p)) ?
                id is "compress" or "normalizeAudio" or "muteVideo" or "trimVideo" or "trimAudio" or "changeVideoSpeed" or "joinVideos" or "videoSnapshots" or "removeMetadata" or "audioChannels" or "splitVideo" or "audioToVideo" :
            inputs.All(p => FeatureCatalog.Extension(p) == "pdf") ? id is "mergePDF" or "splitPDF" or "organizePDF" or "compress" or "removeMetadata" :
            inputs.All(FeatureCatalog.IsArchive) && id == "extractArchive")).ToArray();
    }

    public async Task<IReadOnlyList<string>> RunAsync(string action, string[] inputs, string destination,
        string format, int quality, int width, int height, int x, int y, MediaOptions media,
        IProgress<OperationProgress> progress, CancellationToken token, PhotoAdjustments? adjustments = null, ImageEditOptions? imageOptions = null, DocumentOptions? documentOptions = null, PdfEditOptions? pdfOptions = null)
    {
        if (inputs.Length == 0 || inputs.Any(p => !File.Exists(p) && !(Directory.Exists(p) && FeatureCatalog.Extension(p) is "pages" or "rtfd"))) throw new ArgumentException("Var olan dosyaları seçin.");
        if (action == "convert" ? !Formats(inputs).Contains(format) : !Tools(inputs).Contains(action))
            throw new ArgumentException("Bu işlem seçilen dosyalar için desteklenmiyor.");
        if (quality is < 1 or > 100) throw new ArgumentException("Kalite 1–100 arasında olmalı.");
        if (action == "readQRCode") throw new ArgumentException("QR sonuçları dışa aktarma yerine QR okuma ekranında gösterilir.");
        if (inputs.All(p => FeatureCatalog.IsAudio(p) || FeatureCatalog.IsVideo(p)))
        {
            var engine = new MediaEngine(tools.Executable("ffmpeg"), tools.Executable("ffprobe"));
            return action == "convert"
                ? await engine.ConvertAsync(inputs, format, destination, media, progress, token)
                : await engine.ToolAsync(action, inputs, destination, media, progress, token);
        }
        if (inputs.All(FeatureCatalog.IsImage))
        {
            var options = imageOptions ?? new ImageEditOptions
            {
                Width = action == "editImage" ? width : null, Height = action == "editImage" ? height : null,
                Crop = action == "cropImage" ? new ImageArea(x, y, width, height) : null,
                Quality = quality, Adjustments = adjustments ?? new()
            };
            var needsEncoder = action == "convert" ? format == "heic" : action is not ("frameImage" or "createPDF" or "createCollage") && inputs.Any(p => FeatureCatalog.Extension(p) == "heic");
            var encoder = needsEncoder ? tools.Executable("ffmpeg") : null;
            return await Task.Run(() => ImageEditing.Export(action, inputs, destination, format, options, encoder, progress, token), token);
        }
        var document = documentOptions ?? new DocumentOptions();
        if (action == "convert") return await new DocumentConversion(tools).ConvertAsync(inputs, format, destination, document, progress, token);
        return await Task.Run<IReadOnlyList<string>>(() =>
        {
            if (inputs.All(p => FeatureCatalog.Extension(p) == "pdf"))
            {
                var pdf = pdfOptions ?? new PdfEditOptions();
                if (pdf.Pages is { Length: > 10000 } || pdf.PageRotations is { } rotations && (pdf.Pages is null || rotations.Length != pdf.Pages.Length || rotations.Any(r => r % 90 != 0)))
                    throw new ArgumentException("Geçerli sayfa sırası ve döndürme değerleri seç.");
                return DocumentEngine.Pdf(action == "mergePDF" ? "mergePDFs" : action, inputs, destination, pages: pdf.Pages, token: token, password: document.Password,
                    pageRotations: pdf.PageRotations, uniformWidth: pdf.UniformWidth, pagesPerFile: pdf.PagesPerFile, pageRanges: pdf.PageRanges);
            }
            if (action != "extractArchive") throw new ArgumentException("Desteklenmeyen dosya aracı.");
            using var tx = new ExportTransaction(destination);
            var outputs = inputs.SelectMany(input => ArchiveEngine.Extract(input, tx.StagingDirectory, document.Password, progress, token)).ToArray();
            return tx.Publish(outputs, token);
        }, token);
    }

    public static string[] ReadQr(string input)
    {
        using var image = PhotoCodec.Read(input);
        return DecodeQr(image);
    }
    public static string[] ReadQr(byte[] png)
    {
        using var image = new MagickImage(png, new MagickReadSettings { Format = MagickFormat.Png }); return DecodeQr(image);
    }
    private static string[] DecodeQr(MagickImage image)
    {
        // Bound the decoding buffer as well as the source image's existing 100 MP limit.
        if (image.Width > 2400 || image.Height > 2400) image.Resize(new MagickGeometry(2400, 2400));
        var reader = new ZXing.BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions { TryHarder = true, PossibleFormats = [ZXing.BarcodeFormat.QR_CODE] }
        };
        using var pixels = image.GetPixels();
        return reader.DecodeMultiple(pixels.ToByteArray(PixelMapping.BGRA)!, (int)image.Width, (int)image.Height,
            ZXing.RGBLuminanceSource.BitmapFormat.BGRA32)?.Select(r => r.Text).Distinct().ToArray() ?? [];
    }
}
