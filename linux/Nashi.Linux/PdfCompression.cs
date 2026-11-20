using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Nashi.Linux;

internal static class PdfCompression
{
    internal static async Task<IReadOnlyList<string>> RunAsync(string[] inputs, string destination, int quality, long? targetBytes, double? reduction, string password, IProgress<OperationProgress> progress, CancellationToken token)
    {
        if (quality is < 1 or > 100 || targetBytes is <= 0 || reduction is { } value && (!double.IsFinite(value) || value <= 0 || value >= 100)) throw new ArgumentException("Geçerli kalite ve hedef boyut seç.");
        using var tx = new ExportTransaction(destination); var outputs = new List<string>();
        foreach (var input in inputs)
        {
            token.ThrowIfCancellationRequested(); var originalBytes = new FileInfo(input).Length;
            var target = targetBytes ?? (reduction is { } percent ? (long?)(originalBytes * (1 - percent / 100)) : null);
            if (target is <= 0) throw new ArgumentException("Daha büyük bir hedef boyut seç.");
            using var source = PdfReader.Open(input, password, PdfDocumentOpenMode.Import);
            var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(input) + " Compressed.pdf");
            var preserve = DocumentEngine.RequiresLosslessCompression(source) || !string.IsNullOrWhiteSpace(OfficeDocuments.PdfText(input, password));
            if (preserve)
            {
                var rewritten = DocumentEngine.Pdf("compress", [input], tx.StagingDirectory, token: token, password: password).Single(); if (rewritten != output) File.Move(rewritten, output);
                if (new FileInfo(output).Length > originalBytes) File.Copy(input, output, true);
                if (target is { } limit && new FileInfo(output).Length > limit) throw new IOException("Seçilebilir metin, form, imza ve bağlantılar korunarak bu boyuta ulaşılamadı. Daha büyük hedef seç.");
            }
            else
            {
                var dpi = 144; var jpegQuality = quality; var fits = false;
                for (var attempt = 0; attempt < 14; attempt++)
                {
                    token.ThrowIfCancellationRequested(); var pages = await LinuxPdf.RenderAsync(input, dpi, token, password);
                    using var pdf = new PdfDocument(); pdf.Info.Title = source.Info.Title; pdf.Info.Author = source.Info.Author; pdf.Info.Subject = source.Info.Subject; pdf.Info.Keywords = source.Info.Keywords; pdf.Info.Creator = source.Info.Creator;
                    for (var index = 0; index < pages.Length; index++)
                    {
                        token.ThrowIfCancellationRequested(); using var image = new MagickImage(pages[index]); image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove); image.Quality = (uint)jpegQuality;
                        using var jpeg = new MemoryStream(image.ToByteArray(MagickFormat.Jpeg)); using var picture = XImage.FromStream(jpeg);
                        var page = pdf.AddPage(); var original = source.Pages[index]; var turned = original.Rotate % 180 != 0;
                        page.Width = turned ? original.Height : original.Width; page.Height = turned ? original.Width : original.Height;
                        using var drawing = XGraphics.FromPdfPage(page); drawing.DrawImage(picture, 0, 0, page.Width.Point, page.Height.Point);
                        progress.Report(new(null, $"PDF sıkıştırılıyor · deneme {attempt + 1} · sayfa {index + 1}/{pages.Length}"));
                    }
                    pdf.Save(output);
                    if (target is null || new FileInfo(output).Length <= target) { fits = true; break; }
                    dpi = Math.Max(36, (int)(dpi * .8)); jpegQuality = Math.Max(5, (int)(jpegQuality * .75));
                }
                if (!fits && !(target is { } limit && originalBytes <= limit)) throw new IOException("Hedef PDF boyutuna ulaşılamadı. Daha büyük hedef seç.");
                if (!fits || new FileInfo(output).Length > originalBytes) File.Copy(input, output, true);
            }
            outputs.Add(output);
        }
        return tx.Publish(outputs, token);
    }
}
