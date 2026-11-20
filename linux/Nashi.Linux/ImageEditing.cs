using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Text.RegularExpressions;

namespace Nashi.Linux;

public readonly record struct ImagePoint(double X, double Y);
public readonly record struct ImageArea(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public void Validate(int width, int height)
    {
        if (!new[] { X, Y, Width, Height }.All(double.IsFinite) || X < 0 || Y < 0 || Width < 1 || Height < 1 || Right > width || Bottom > height)
            throw new ArgumentException("Seçim alanı görselin içinde olmalı.");
    }
}
public sealed record ImageMark(string Kind, ImageArea Area, string Color = "#FF5200", double Stroke = 3,
    double FontSize = 24, string Text = "", ImagePoint[]? Points = null, string? ImagePath = null);
public sealed record BackgroundOptions(int Padding = 80, double Radius = 24, string Color = "#FFFFFF",
    string Kind = "solid", string EndColor = "#FFD85C", string? ImagePath = null, double Blur = 0,
    double Shadow = 24, double ShadowOpacity = .25, int? Width = null, int? Height = null,
    double Scale = 1, double X = .5, double Y = .5);
public sealed record CollageOptions(string Layout = "grid", int Columns = 2, int Width = 2048, int Height = 2048,
    int Spacing = 12, int Padding = 16, double Radius = 0, string Color = "#FFFFFF", bool Fill = true, int[]? Order = null);
public sealed record ImageEditOptions
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int Rotation { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public PhotoAdjustments Adjustments { get; init; } = new();
    public ImageArea? Crop { get; init; }
    public ImageMark[] Marks { get; init; } = [];
    public ImageArea[] Redactions { get; init; } = [];
    public string RedactionStyle { get; init; } = "solid";
    public BackgroundOptions Background { get; init; } = new();
    public CollageOptions Collage { get; init; } = new();
    public int Quality { get; init; } = 85;
    public long? TargetBytes { get; init; }
    public double? ReductionPercent { get; init; }
    public int[]? InputOrder { get; init; }
}

internal static class ImageEditing
{
    private static readonly Lazy<string> AnnotationFont = new(() =>
    {
        // Magick's generic font alias can resolve to a decorative font instead of the desktop's sans face.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var path = ProcessRunner.RunAsync("/usr/bin/fc-match", ["--format=%{file}", "sans-serif"], timeout.Token).GetAwaiter().GetResult().Output;
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new FileNotFoundException("Çizim için sistem yazı tipi bulunamadı. fontconfig paketini kontrol et.");
        return path;
    });
    internal static void Canvas(int width, int height)
    {
        if (width < 1 || height < 1 || width > 30000 || height > 30000 || (long)width * height > 100_000_000)
            throw new ArgumentException("Boyutlar 1–30.000 piksel ve toplam en fazla 100 megapiksel olmalı.");
    }
    private static MagickColor Color(string value)
    {
        if (!Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Renk #RRGGBB biçiminde olmalı.");
        return new MagickColor(value);
    }
    internal static IReadOnlyList<string> Export(string action, string[] inputs, string destination, string format,
        ImageEditOptions options, string? encoder, IProgress<OperationProgress> progress, CancellationToken token)
    {
        if (options.Quality is < 1 or > 100) throw new ArgumentException("Kalite 1–100 arasında olmalı.");
        using var tx = new ExportTransaction(destination);
        if (action == "createCollage")
        {
            using var collage = Collage(inputs, options.Collage, token);
            var output = Path.Combine(tx.StagingDirectory, "Collage.png"); collage.Write(output, MagickFormat.Png);
            return tx.Publish([output], token);
        }
        if (action == "createPDF")
        {
            if (options.InputOrder is { } order)
            {
                if (!order.Order().SequenceEqual(Enumerable.Range(0, inputs.Length))) throw new ArgumentException("PDF sırası her görseli bir kez içermeli.");
                inputs = order.Select(i => inputs[i]).ToArray();
            }
            using var pdf = new PdfDocument();
            for (var index = 0; index < inputs.Length; index++)
            {
                token.ThrowIfCancellationRequested(); using var image = PhotoCodec.Read(inputs[index]);
                AddPdfPage(pdf, image); progress.Report(new((index + 1d) / inputs.Length, "PDF sayfaları hazırlanıyor…"));
            }
            var output = Path.Combine(tx.StagingDirectory, "Images.pdf"); pdf.Save(output); return tx.Publish([output], token);
        }
        var outputs = new List<string>();
        foreach (var input in inputs)
        {
            token.ThrowIfCancellationRequested();
            using var image = Transform(input, action, options);
            var ext = action == "convert" ? format : action == "frameImage" || FeatureCatalog.Extension(input) == "svg" ? "png" : FeatureCatalog.Extension(input);
            if (action == "compress" && ext is not ("jpg" or "png" or "heic" or "webp" or "avif")) ext = "jpg";
            var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(input) + " Nashi." + ext);
            if (action == "compress") Compress(image, input, output, ext, options, encoder, progress, token);
            else Save(image, output, ext, options.Quality, encoder, token);
            outputs.Add(output); progress.Report(new((double)outputs.Count / inputs.Length, Path.GetFileName(input)));
        }
        return tx.Publish(outputs, token);
    }
    internal static MagickImage Transform(string input, string action, ImageEditOptions options)
    {
        var image = action == "editImage" ? new MagickImage(PhotoCodec.Adjust(PhotoCodec.Png(input), options.Adjustments)) : PhotoCodec.Read(input);
        try
        {
            switch (action)
            {
                case "editImage":
                    if (options.FlipHorizontal) image.Flop();
                    if (options.FlipVertical) image.Flip();
                    image.BackgroundColor = MagickColors.Transparent;
                    if (options.Rotation % 360 != 0)
                    {
                        var radians = options.Rotation * Math.PI / 180;
                        Canvas((int)Math.Ceiling(image.Width * Math.Abs(Math.Cos(radians)) + image.Height * Math.Abs(Math.Sin(radians))),
                            (int)Math.Ceiling(image.Width * Math.Abs(Math.Sin(radians)) + image.Height * Math.Abs(Math.Cos(radians))));
                        image.Rotate(options.Rotation); image.ResetPage();
                    }
                    if (options.Width is not null || options.Height is not null)
                    {
                        var width = options.Width ?? Math.Max(1, (int)Math.Round(image.Width * (double)options.Height!.Value / image.Height));
                        var height = options.Height ?? Math.Max(1, (int)Math.Round(image.Height * (double)width / image.Width));
                        Canvas(width, height); image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
                    }
                    break;
                case "cropImage":
                    var crop = options.Crop ?? throw new ArgumentException("Kırpılacak alanı seç."); crop.Validate((int)image.Width, (int)image.Height);
                    image.Crop(new MagickGeometry((int)crop.X, (int)crop.Y, (uint)crop.Width, (uint)crop.Height)); image.ResetPage(); break;
                case "annotateImage": Annotate(image, options.Marks); break;
                case "redactImage": Redact(image, options.Redactions, options.RedactionStyle); break;
                case "frameImage":
                    var framed = Frame(image, options.Background); image.Dispose(); return framed;
                case "removeMetadata": image.Strip(); break;
                case "convert": case "compress": break;
                default: throw new ArgumentException("Desteklenmeyen görsel aracı.");
            }
            return image;
        }
        catch { image.Dispose(); throw; }
    }
    internal static void Annotate(MagickImage image, ImageMark[] marks)
    {
        if (marks.Length == 0 || marks.Length > 1000) throw new ArgumentException("1–1.000 işaret ekle.");
        foreach (var mark in marks)
        {
            mark.Area.Validate((int)image.Width, (int)image.Height);
            if (!double.IsFinite(mark.Stroke) || mark.Stroke is < .1 or > 1000 || !double.IsFinite(mark.FontSize) || mark.FontSize is < 1 or > 2000)
                throw new ArgumentException("Geçerli çizgi ve yazı boyutu seç.");
            var r = mark.Area; var draw = new Drawables().FillColor(MagickColors.Transparent).StrokeColor(Color(mark.Color)).StrokeWidth(mark.Stroke);
            switch (mark.Kind)
            {
                case "rectangle": draw.Rectangle(r.X, r.Y, r.Right, r.Bottom); break;
                case "ellipse": draw.Ellipse(r.X + r.Width / 2, r.Y + r.Height / 2, r.Width / 2, r.Height / 2, 0, 360); break;
                case "text":
                    if (string.IsNullOrWhiteSpace(mark.Text) || mark.Text.Length > 4096) throw new ArgumentException("1–4.096 karakterlik metin gir.");
                    draw.StrokeColor(MagickColors.Transparent).FillColor(Color(mark.Color)).Font(AnnotationFont.Value).FontPointSize(mark.FontSize).Text(r.X, r.Y + Math.Min(mark.FontSize, r.Height), mark.Text); break;
                case "image":
                    using (var picture = PhotoCodec.Read(mark.ImagePath ?? throw new ArgumentException("Eklenecek görseli seç.")))
                    { picture.Resize(new MagickGeometry((uint)r.Width, (uint)r.Height) { IgnoreAspectRatio = true }); image.Composite(picture, (int)r.X, (int)r.Y, CompositeOperator.Over); }
                    continue;
                case "line": case "arrow": case "freehand": case "highlight":
                    var points = mark.Points ?? [];
                    if (points.Length < 2 || points.Length > 10000 || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.Y < 0 || p.X > image.Width || p.Y > image.Height))
                        throw new ArgumentException("Çizgi noktaları görselin içinde olmalı.");
                    if (mark.Kind == "highlight") draw.StrokeOpacity(new Percentage(35));
                    draw.Polyline(points.Select(p => new PointD(p.X, p.Y)));
                    if (mark.Kind == "arrow")
                    {
                        var end = points[^1]; var angle = Math.Atan2(end.Y - points[0].Y, end.X - points[0].X); var length = Math.Max(10, mark.Stroke * 4);
                        draw.Line(end.X, end.Y, end.X - length * Math.Cos(angle - .5), end.Y - length * Math.Sin(angle - .5));
                        draw.Line(end.X, end.Y, end.X - length * Math.Cos(angle + .5), end.Y - length * Math.Sin(angle + .5));
                    }
                    break;
                default: throw new ArgumentException("Desteklenmeyen çizim aracı.");
            }
            draw.Draw(image);
        }
    }
    internal static void Redact(MagickImage image, ImageArea[] regions, string style)
    {
        if (regions.Length == 0 || regions.Length > 1000 || style is not ("solid" or "blur" or "pixelate")) throw new ArgumentException("Geçerli bir redaksiyon alanı ve biçimi seç.");
        foreach (var r in regions)
        {
            r.Validate((int)image.Width, (int)image.Height);
            if (style == "solid") new Drawables().FillColor(MagickColors.Black).StrokeColor(MagickColors.Black).Rectangle(r.X, r.Y, r.Right, r.Bottom).Draw(image);
            else
            {
                using var patch = image.Clone(); patch.Crop(new MagickGeometry((int)r.X, (int)r.Y, (uint)r.Width, (uint)r.Height)); patch.ResetPage();
                if (style == "blur") patch.Blur(0, 24);
                else { patch.Scale(Math.Max(1, (uint)r.Width / 16), Math.Max(1, (uint)r.Height / 16)); patch.Scale((uint)r.Width, (uint)r.Height); }
                image.Composite(patch, (int)r.X, (int)r.Y, CompositeOperator.Over);
            }
        }
        image.Strip();
    }
    internal static MagickImage Frame(MagickImage source, BackgroundOptions o)
    {
        if (o.Padding is < 0 or > 4000 || !new[] { o.Radius, o.Blur, o.Shadow, o.ShadowOpacity, o.Scale, o.X, o.Y }.All(double.IsFinite) ||
            o.Radius < 0 || o.Blur is < 0 or > 100 || o.Shadow is < 0 or > 100 || o.ShadowOpacity is < 0 or > 1 || o.Scale is <= 0 or > 10 || o.X is < 0 or > 1 || o.Y is < 0 or > 1)
            throw new ArgumentException("Geçerli arka plan boyutları seç.");
        var w = o.Width ?? checked((int)source.Width + 2 * o.Padding); var h = o.Height ?? checked((int)source.Height + 2 * o.Padding); Canvas(w, h);
        var canvas = new MagickImage(o.Kind == "transparent" ? MagickColors.Transparent : Color(o.Color), (uint)w, (uint)h);
        try
        {
            if (o.Kind == "gradient") canvas.Read($"gradient:{Color(o.Color)}-{Color(o.EndColor)}", new MagickReadSettings { Width = (uint)w, Height = (uint)h });
            else if (o.Kind == "image")
            {
                using var back = PhotoCodec.Read(o.ImagePath ?? throw new ArgumentException("Arka plan görselini seç."));
                if (o.Blur > 0) back.Blur(0, o.Blur); Fit(back, w, h, true); canvas.Composite(back, CompositeOperator.Over);
            }
            else if (o.Kind is not ("solid" or "transparent")) throw new ArgumentException("Geçerli arka plan türü seç.");
            using var front = source.Clone();
            var scale = Math.Min(Math.Max(1, w - 2 * o.Padding) / (double)source.Width, Math.Max(1, h - 2 * o.Padding) / (double)source.Height) * o.Scale;
            var fw = Math.Max(1, (int)Math.Round(source.Width * scale)); var fh = Math.Max(1, (int)Math.Round(source.Height * scale)); Canvas(fw, fh);
            front.Resize((uint)fw, (uint)fh); Round(front, o.Radius);
            var x = (int)((w - fw) * o.X); var y = (int)((h - fh) * o.Y);
            if (o.Shadow > 0 && o.ShadowOpacity > 0)
            {
                using var shadow = front.Clone(); shadow.Shadow(0, 0, o.Shadow, new Percentage(o.ShadowOpacity * 100), MagickColors.Black);
                canvas.Composite(shadow, x - ((int)shadow.Width - fw) / 2, y - ((int)shadow.Height - fh) / 2, CompositeOperator.Over);
            }
            canvas.Composite(front, x, y, CompositeOperator.Over); return canvas;
        }
        catch { canvas.Dispose(); throw; }
    }
    internal static MagickImage Collage(string[] inputs, CollageOptions o, CancellationToken token)
    {
        if (inputs.Length is < 2 or > 100 || o.Columns is < 1 or > 100 || o.Padding < 0 || o.Spacing < 0 || !double.IsFinite(o.Radius) || o.Radius < 0 || o.Layout is not ("grid" or "row" or "column" or "featured"))
            throw new ArgumentException("Kolaj için 2–100 görsel ve geçerli yerleşim seç.");
        Canvas(o.Width, o.Height);
        var order = o.Order ?? Enumerable.Range(0, inputs.Length).ToArray();
        if (!order.Order().SequenceEqual(Enumerable.Range(0, inputs.Length))) throw new ArgumentException("Görsel sırası her görseli bir kez içermeli.");
        var columns = o.Layout == "row" ? inputs.Length : o.Layout == "column" ? 1 : Math.Min(o.Columns, inputs.Length);
        var rows = (int)Math.Ceiling(inputs.Length / (double)columns);
        var cw = (o.Width - 2d * o.Padding - (columns - 1d) * o.Spacing) / columns;
        var ch = (o.Height - 2d * o.Padding - (rows - 1d) * o.Spacing) / rows;
        if (cw < 1 || ch < 1) throw new ArgumentException("Görsellerin sığması için boşlukları azalt.");
        var canvas = new MagickImage(Color(o.Color), (uint)o.Width, (uint)o.Height);
        try
        {
            for (var n = 0; n < order.Length; n++)
            {
                token.ThrowIfCancellationRequested();
                var x = o.Padding + n % columns * (cw + o.Spacing); var y = o.Padding + n / columns * (ch + o.Spacing); var w = cw; var h = ch;
                if (o.Layout == "featured")
                {
                    var availableW = o.Width - 2d * o.Padding; var availableH = o.Height - 2d * o.Padding; var large = (availableW - o.Spacing) * .62;
                    var small = (availableH - (order.Length - 2d) * o.Spacing) / (order.Length - 1);
                    x = n == 0 ? o.Padding : o.Padding + large + o.Spacing; y = n == 0 ? o.Padding : o.Padding + (n - 1) * (small + o.Spacing);
                    w = n == 0 ? large : availableW - large - o.Spacing; h = n == 0 ? availableH : small;
                }
                if (w < 1 || h < 1) throw new ArgumentException("Kolaj hücreleri pozitif boyutta olmalı.");
                using var photo = PhotoCodec.Read(inputs[order[n]]); Fit(photo, (int)w, (int)h, o.Fill); Round(photo, o.Radius); canvas.Composite(photo, (int)x, (int)y, CompositeOperator.Over);
            }
            return canvas;
        }
        catch { canvas.Dispose(); throw; }
    }
    private static void Fit(IMagickImage<byte> image, int width, int height, bool fill)
    {
        Canvas(width, height);
        var scale = fill ? Math.Max(width / (double)image.Width, height / (double)image.Height) : Math.Min(width / (double)image.Width, height / (double)image.Height);
        Canvas(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
        image.Resize((uint)Math.Max(1, (int)Math.Round(image.Width * scale)), (uint)Math.Max(1, (int)Math.Round(image.Height * scale)));
        image.BackgroundColor = MagickColors.Transparent; image.Extent((uint)width, (uint)height, Gravity.Center); image.ResetPage();
    }
    private static void Round(IMagickImage<byte> image, double radius)
    {
        if (radius <= 0) return;
        using var mask = new MagickImage(MagickColors.Transparent, image.Width, image.Height);
        new Drawables().FillColor(MagickColors.White).StrokeColor(MagickColors.Transparent).RoundRectangle(0, 0, image.Width - 1, image.Height - 1, radius, radius).Draw(mask);
        image.Alpha(AlphaAction.Set); image.Composite(mask, CompositeOperator.DstIn);
    }
    private static void AddPdfPage(PdfDocument pdf, IMagickImage<byte> image)
    {
        var page = pdf.AddPage(); page.Width = XUnit.FromPoint(image.Width * .75); page.Height = XUnit.FromPoint(image.Height * .75);
        // PDFsharp cannot decode every optimized 1/2-bit PNG that ImageMagick can emit.
        using var png = new MemoryStream(image.ToByteArray(MagickFormat.Png32)); using var picture = XImage.FromStream(png);
        using var graphics = XGraphics.FromPdfPage(page); graphics.DrawImage(picture, 0, 0, page.Width.Point, page.Height.Point);
    }
    private static void Save(IMagickImage<byte> image, string output, string format, int quality, string? encoder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); image.Strip(); image.Quality = (uint)quality;
        if (format == "pdf") { using var pdf = new PdfDocument(); AddPdfPage(pdf, image); pdf.Save(output); }
        else if (format == "heic") PhotoCodec.EncodePng(image.ToByteArray(MagickFormat.Png), output, format, quality, encoder, token);
        else
        {
            if (format == "jpg") { image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove); }
            image.Write(output, PhotoCodec.Format(format));
        }
    }
    private static void Compress(MagickImage image, string input, string output, string format, ImageEditOptions o, string? encoder, IProgress<OperationProgress> progress, CancellationToken token)
    {
        if (o.TargetBytes is <= 0 || o.ReductionPercent is { } p && (!double.IsFinite(p) || p <= 0 || p >= 100)) throw new ArgumentException("Pozitif hedef boyut veya %0–100 arasında azaltma seç.");
        var target = o.TargetBytes ?? (o.ReductionPercent is { } percent ? (long?)(new FileInfo(input).Length * (1 - percent / 100)) : null);
        if (target is <= 0) throw new ArgumentException("Daha büyük bir hedef boyut seç.");
        var quality = o.Quality;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            token.ThrowIfCancellationRequested(); progress.Report(new(null, $"Hedef boyuta sığdırılıyor · {attempt + 1}…"));
            Save(image, output, format, quality, encoder, token);
            if (target is null || new FileInfo(output).Length <= target) return;
            // This is a file owned by the current export transaction, never the original.
            File.Delete(output);
            if (quality > 15 && format != "png") quality = Math.Max(1, (int)(quality * .72));
            else image.Resize(Math.Max(1, (uint)(image.Width * .8)), Math.Max(1, (uint)(image.Height * .8)));
        }
        throw new IOException("Hedef boyuta ulaşılamadı. Daha büyük bir boyut seç.");
    }
}
