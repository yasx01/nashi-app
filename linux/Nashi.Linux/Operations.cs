using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Nashi.Linux;

// The Linux UI advertises only routes implemented here, never the entire upstream catalog.
public sealed class Operations(Toolchain tools)
{
    private static readonly string[] ImageFormats = ["jpg", "png", "webp", "tiff", "avif", "bmp", "gif", "heic", "pdf"];
    private static readonly string[] OfficeFormats = ["doc", "docx", "odt", "rtf", "txt", "md", "html", "xls", "xlsx", "ods", "csv", "tsv", "ppt", "pptx", "odp", "epub", "pdf"];
    public static string[] Formats(IReadOnlyList<string> inputs) => FeatureCatalog.Formats(inputs)
        .Where(format => inputs.All(input => CanConvert(input, format))).ToArray();
    private static bool CanConvert(string input, string format)
    {
        if (FeatureCatalog.IsImage(input)) return ImageFormats.Contains(format);
        if (FeatureCatalog.IsAudio(input) || FeatureCatalog.IsVideo(input) || FeatureCatalog.IsArchive(input) || FeatureCatalog.Extension(input) is "srt" or "vtt") return true;
        if (FeatureCatalog.Extension(input) == "pdf") return format is "txt" or "md";
        return OfficeFormats.Contains(FeatureCatalog.Extension(input)) && OfficeFormats.Contains(format);
    }

    public static string[] Tools(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 0) return [];
        return FeatureCatalog.Tools(inputs).Where(id =>
            inputs.All(FeatureCatalog.IsImage) ? id is "compress" or "editImage" or "cropImage" or "removeMetadata" or "readQRCode" :
            inputs.All(p => FeatureCatalog.IsAudio(p) || FeatureCatalog.IsVideo(p)) ?
                id is "compress" or "normalizeAudio" or "muteVideo" or "trimVideo" or "trimAudio" or "changeVideoSpeed" or "joinVideos" or "videoSnapshots" or "removeMetadata" or "audioChannels" or "splitVideo" or "audioToVideo" :
            inputs.All(p => FeatureCatalog.Extension(p) == "pdf") ? id is "mergePDF" or "splitPDF" or "compress" or "removeMetadata" :
            inputs.All(FeatureCatalog.IsArchive) && id == "extractArchive").ToArray();
    }

    public async Task<IReadOnlyList<string>> RunAsync(string action, string[] inputs, string destination,
        string format, int quality, int width, int height, int x, int y, MediaOptions media,
        IProgress<OperationProgress> progress, CancellationToken token, PhotoAdjustments? adjustments = null)
    {
        if (inputs.Length == 0 || inputs.Any(p => !File.Exists(p))) throw new ArgumentException("Var olan dosyaları seçin.");
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
        return await Task.Run<IReadOnlyList<string>>(async () =>
        {
            if (action != "convert" && inputs.All(p => FeatureCatalog.Extension(p) == "pdf"))
                return DocumentEngine.Pdf(action == "mergePDF" ? "mergePDFs" : action, inputs, destination, token: token);
            using var tx = new ExportTransaction(destination);
            var outputs = new List<string>();
            foreach (var input in inputs)
            {
                token.ThrowIfCancellationRequested();
                progress.Report(new((double)outputs.Count / inputs.Length, Path.GetFileName(input)));
                if (FeatureCatalog.IsArchive(input))
                {
                    outputs.AddRange(action == "extractArchive" ? ArchiveEngine.Extract(input, tx.StagingDirectory, token: token)
                        : ArchiveEngine.Convert(input, format, tx.StagingDirectory, token: token));
                    continue;
                }
                var targetFormat = action == "convert" ? format : FeatureCatalog.Extension(input) == "svg" ? "png" : FeatureCatalog.Extension(input);
                var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(input) + " Nashi." + targetFormat);
                if (FeatureCatalog.IsImage(input))
                {
                    using var image = action == "editImage" && adjustments is not null
                        ? new MagickImage(PhotoCodec.Adjust(PhotoCodec.Png(input), adjustments)) : PhotoCodec.Read(input);
                    if (action is "editImage" or "cropImage")
                    {
                        if (width < 1 || height < 1 || (long)width * height > 100_000_000)
                            throw new ArgumentException("Boyutlar pozitif ve toplam en fazla 100 megapiksel olmalı.");
                        if (action == "editImage") image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = false });
                        else
                        {
                            if (x < 0 || y < 0 || (long)x + width > image.Width || (long)y + height > image.Height)
                                throw new ArgumentException("Kırpma alanı görselin içinde olmalı.");
                            image.Crop(new MagickGeometry(x, y, (uint)width, (uint)height));
                            image.ResetPage();
                        }
                    }
                    image.Quality = (uint)quality;
                    if (action == "removeMetadata") image.Strip();
                    if (targetFormat == "pdf")
                    {
                        using var pdf = new PdfDocument();
                        var page = pdf.AddPage();
                        page.Width = XUnit.FromPoint(image.Width * .75);
                        page.Height = XUnit.FromPoint(image.Height * .75);
                        using var png = new MemoryStream(image.ToByteArray(MagickFormat.Png));
                        using var picture = XImage.FromStream(png);
                        using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawImage(picture, 0, 0, page.Width.Point, page.Height.Point);
                        pdf.Save(output);
                    }
                    else if (targetFormat == "heic") PhotoCodec.EncodePng(image.ToByteArray(MagickFormat.Png), output, targetFormat, quality, tools.Executable("ffmpeg"), token);
                    else image.Write(output, PhotoCodec.Format(targetFormat));
                }
                else if (FeatureCatalog.Extension(input) == "pdf") File.WriteAllText(output, OfficeDocuments.PdfText(input));
                else if (FeatureCatalog.Extension(input) is "srt" or "vtt") File.WriteAllText(output, SubtitleCodec.Convert(LegacyTextCodec.Decode(File.ReadAllBytes(input)), format));
                else
                {
                    if (!File.Exists("/usr/bin/libreoffice")) throw new FileNotFoundException("Belge dönüşümü için Fedora'nın LibreOffice paketini kurmalısın.");
                    var work = Path.Combine(tx.StagingDirectory, "office-" + outputs.Count); Directory.CreateDirectory(work);
                    var source = input;
                    if (FeatureCatalog.Extension(input) is "txt" or "md" or "csv" or "tsv")
                    {
                        source = Path.Combine(work, Path.GetFileName(input));
                        await LegacyTextCodec.ToUtf8Async(input, source, token);
                    }
                    if (FeatureCatalog.Extension(input) == "html")
                    {
                        source = Path.Combine(work, "Safe.html");
                        await File.WriteAllTextAsync(source, OfficeDocuments.SanitizedHtml(await File.ReadAllTextAsync(input, token)), token);
                    }
                    var converted = await new OfficeDocuments("/usr/bin/libreoffice", "", AppContext.BaseDirectory).OfficeAsync(source, format, work, token);
                    File.Move(converted, output);
                }
                outputs.Add(output);
            }
            return tx.Publish(outputs, token);
        }, token);
    }

    public static string[] ReadQr(string input)
    {
        using var image = PhotoCodec.Read(input);
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
