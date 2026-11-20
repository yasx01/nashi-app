using ImageMagick;
using Nashi.Core;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;
using System.Globalization;
using System.Text;

namespace Nashi.Linux;

internal static class LinuxPdf
{
    public static async Task<byte[][]> RenderAsync(string input, int dpi = 144, CancellationToken token = default, string password = "")
    {
        var dimensions = await Dimensions(input, password, dpi, token);
        var pages = await Render(input, dimensions, Enumerable.Range(1, dimensions.Length).ToArray(), dpi, password, token);
        return pages.OrderBy(p => p.Key).Select(p => p.Value).ToArray();
    }
    public static async Task<Dictionary<int, byte[]>> RenderPagesAsync(string input, IReadOnlyCollection<int> pages, int dpi = 144, CancellationToken token = default, string password = "") =>
        await Render(input, await Dimensions(input, password, dpi, token), pages, dpi, password, token);

    private static Task<(int Width, int Height)[]> Dimensions(string input, string password, int dpi, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(input) || new FileInfo(input).Length > 200_000_000) throw new InvalidDataException("PDF dosyası bulunamadı veya 200 MB sınırını aşıyor.");
        if (dpi is < 36 or > 600) throw new ArgumentException("PDF çözünürlüğü 36–600 DPI olmalı.");
        using var pdf = PdfReader.Open(input, password, PdfDocumentOpenMode.Import);
        if (pdf.PageCount > 1000) throw new InvalidDataException("Önizleme en fazla 1.000 sayfa destekliyor. PDF'yi önce böl.");
        return pdf.Pages.Cast<PdfPage>().Select(page =>
        {
            token.ThrowIfCancellationRequested();
            var width = page.Width.Point * dpi / 72; var height = page.Height.Point * dpi / 72;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1 || width > 30000 || height > 30000 || width * height > 100_000_000)
                throw new InvalidDataException("PDF sayfası güvenli görüntüleme boyutunu aşıyor. Daha düşük DPI seç.");
            var w = (int)Math.Ceiling(width); var h = (int)Math.Ceiling(height);
            return Math.Abs(page.Rotate % 180) == 90 ? (h, w) : (w, h);
        }).ToArray();
    }, token);

    private static async Task<Dictionary<int, byte[]>> Render(string input, (int Width, int Height)[] dimensions,
        IReadOnlyCollection<int> pages, int dpi, string password, CancellationToken token)
    {
        if (!File.Exists("/usr/bin/pdftoppm")) throw new FileNotFoundException("PDF önizlemesi için Fedora'nın poppler-utils paketi kurulmalı.");
        var directory = Path.Combine(Path.GetTempPath(), "nashi-pdf-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var result = new Dictionary<int, byte[]>(); long bytes = 0;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            foreach (var number in pages.Distinct())
            {
                limit.Token.ThrowIfCancellationRequested();
                if (number < 1 || number > dimensions.Length) throw new ArgumentException("PDF sayfa numarası dosyanın dışında.");
                var size = dimensions[number - 1]; var output = Path.Combine(directory, "page");
                var args = new List<string> { "-f", number.ToString(CultureInfo.InvariantCulture), "-singlefile", "-png", "-r", dpi.ToString(CultureInfo.InvariantCulture),
                    "-scale-to-x", size.Width.ToString(CultureInfo.InvariantCulture), "-scale-to-y", size.Height.ToString(CultureInfo.InvariantCulture) };
                if (password.Length > 0) args.AddRange(["-upw", password, "-opw", password]);
                args.AddRange([Path.GetFullPath(input), output]);
                await ProcessRunner.RunAsync("/usr/bin/pdftoppm", args, limit.Token);
                var file = output + ".png"; var length = new FileInfo(file).Length; bytes += length;
                if (length < 1 || bytes > 512_000_000) throw new InvalidDataException("Görüntülenen sayfalar 512 MB sınırını aşıyor. PDF'yi böl veya daha düşük DPI seç.");
                result.Add(number, await File.ReadAllBytesAsync(file, limit.Token)); File.Delete(file);
            }
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("PDF görüntüleme beş dakika içinde tamamlanamadı."); }
        finally { Directory.Delete(directory, true); }
    }
    public static async Task<string> RecognizeAsync(byte[] png, CancellationToken token, bool boxes = false)
    {
        if (!File.Exists("/usr/bin/tesseract")) throw new FileNotFoundException("Taranmış sayfalar için tesseract ve tesseract-langpack-eng paketleri kurulmalı.");
        var directory = Path.Combine(Path.GetTempPath(), "nashi-ocr-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            using var image = new MagickImage(png, new MagickReadSettings { Format = MagickFormat.Png });
            if (image.Width > 2400 || image.Height > 2400) image.Resize(new MagickGeometry(2400, 2400));
            var path = Path.Combine(directory, "page.png"); image.Write(path, MagickFormat.Png);
            var language = File.Exists("/usr/share/tesseract/tessdata/tur.traineddata") ? "eng+tur" : "eng";
            var args = new List<string> { path, "stdout", "-l", language, "--psm", "3" }; if (boxes) args.Add("tsv");
            return (await ProcessRunner.RunAsync("/usr/bin/tesseract", args, limit.Token)).Output;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("OCR iki dakika içinde tamamlanamadı."); }
        finally { Directory.Delete(directory, true); }
    }
    public static async Task<string> TextAsync(string input, string password, CancellationToken token)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(input, new() { Password = password }); var result = new List<string>(); long characters = 0;
        foreach (var page in pdf.GetPages())
        {
            token.ThrowIfCancellationRequested(); var text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
            if (string.IsNullOrWhiteSpace(text))
            {
                var rendered = await RenderPagesAsync(input, [page.Number], 180, token, password);
                text = await RecognizeAsync(rendered[page.Number], token);
            }
            characters += text.Length; if (characters > 20_000_000) throw new InvalidDataException("Çıkarılan PDF metni 20 milyon karakter sınırını aşıyor.");
            result.Add(text);
        }
        var output = string.Join("\n\n\f\n\n", result);
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidDataException("PDF'den metin okunamadı."); return output;
    }
    public static async Task<string> SlidesAsync(string input, string work, int dpi, string password, CancellationToken token)
    {
        var pages = await RenderAsync(input, dpi, token, password);
        if (pages.Length == 0) throw new InvalidDataException("PDF boş.");
        var first = OfficeDocuments.PngSize(pages[0]) ?? throw new InvalidDataException("Geçersiz sayfa görüntüsü.");
        var width = first.Width * 72d / dpi; var height = first.Height * 72d / dpi; var slides = new StringBuilder();
        string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
        for (var n = 0; n < pages.Length; n++)
        {
            token.ThrowIfCancellationRequested(); var image = OfficeDocuments.PngSize(pages[n]) ?? throw new InvalidDataException("Geçersiz sayfa görüntüsü.");
            var scale = Math.Min(width / image.Width, height / image.Height); var w = image.Width * scale; var h = image.Height * scale;
            slides.Append($"<draw:page draw:name=\"Page {n + 1}\" draw:master-page-name=\"PDFMaster\"><draw:frame svg:x=\"{N((width - w) / 2)}pt\" svg:y=\"{N((height - h) / 2)}pt\" svg:width=\"{N(w)}pt\" svg:height=\"{N(h)}pt\"><draw:image><office:binary-data>{Convert.ToBase64String(pages[n])}</office:binary-data></draw:image></draw:frame></draw:page>");
        }
        var xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.presentation\"><office:styles/><office:automatic-styles><style:page-layout style:name=\"PDFCanvas\"><style:page-layout-properties fo:page-width=\"{N(width)}pt\" fo:page-height=\"{N(height)}pt\" fo:margin=\"0pt\"/></style:page-layout></office:automatic-styles><office:master-styles><style:master-page style:name=\"PDFMaster\" style:page-layout-name=\"PDFCanvas\"/></office:master-styles><office:body><office:presentation>{slides}</office:presentation></office:body></office:document>";
        var file = Path.Combine(work, "PDF slides.fodp"); await File.WriteAllTextAsync(file, xml, token); return file;
    }
}
