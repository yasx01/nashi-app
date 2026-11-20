using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Globalization;

namespace Nashi.Linux;

internal sealed class ImageCanvas : Control, IDisposable
{
    private Bitmap? bitmap;
    private ImagePoint? start;
    private ImageArea? beforeCrop;
    private ImageMark? beforeMove;
    private readonly List<ImagePoint> trail = [];
    private readonly Stack<ImageMark[]> undo = [];
    private bool movingCrop, resizingCrop;
    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public string Mode { get; set; } = "rectangle";
    public string Ink { get; set; } = "#FF5200";
    public double Stroke { get; set; } = 3;
    public double FontSize { get; set; } = 24;
    public string MarkText { get; set; } = "Not";
    public string? MarkImage { get; set; }
    public double? AspectRatio { get; set; }
    public ImageArea? Crop { get; set; }
    public List<ImageMark> Marks { get; } = [];
    public int Selected { get; private set; } = -1;
    public event Action? Changed;

    public ImageCanvas(int width, int height)
    {
        ImageWidth = width; ImageHeight = height; MinHeight = 320; Focusable = true;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { Delete(); e.Handled = true; }
            if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control) { Undo(); e.Handled = true; }
        };
    }
    private Rect ImageBounds
    {
        get
        {
            var width = Mode == "view" && bitmap is not null ? bitmap.PixelSize.Width : ImageWidth;
            var height = Mode == "view" && bitmap is not null ? bitmap.PixelSize.Height : ImageHeight;
            var scale = Math.Min(Math.Max(1, Bounds.Width - 24) / width, Math.Max(1, Bounds.Height - 24) / height);
            return new Rect((Bounds.Width - width * scale) / 2, (Bounds.Height - height * scale) / 2, width * scale, height * scale);
        }
    }
    internal Point View(ImagePoint p) { var r = ImageBounds; return new Point(r.X + p.X * r.Width / ImageWidth, r.Y + p.Y * r.Height / ImageHeight); }
    private ImagePoint Pixel(Point point)
    {
        var r = ImageBounds;
        return new ImagePoint(Math.Clamp((point.X - r.X) * ImageWidth / r.Width, 0, ImageWidth), Math.Clamp((point.Y - r.Y) * ImageHeight / r.Height, 0, ImageHeight));
    }
    private Rect View(ImageArea r) => new(View(new ImagePoint(r.X, r.Y)), View(new ImagePoint(r.Right, r.Bottom)));
    public void SetImage(byte[] png)
    {
        using var stream = new MemoryStream(png); var next = new Bitmap(stream); var old = bitmap; bitmap = next; old?.Dispose(); InvalidateVisual();
    }
    public void Undo()
    {
        if (undo.TryPop(out var previous)) { Marks.Clear(); Marks.AddRange(previous); Selected = -1; Changed?.Invoke(); InvalidateVisual(); }
    }
    public void Delete()
    {
        if (Selected < 0 || Selected >= Marks.Count) return;
        undo.Push(Marks.ToArray()); Marks.RemoveAt(Selected); Selected = -1; Changed?.Invoke(); InvalidateVisual();
    }
    public void Clear()
    {
        undo.Push(Marks.ToArray()); Marks.Clear(); Crop = null; Selected = -1; Changed?.Invoke(); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Mode == "view" || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !ImageBounds.Contains(e.GetPosition(this))) return;
        Focus(); start = Pixel(e.GetPosition(this)); trail.Clear(); trail.Add(start.Value); e.Pointer.Capture(this);
        if (Mode == "crop")
        {
            beforeCrop = Crop;
            var tolerance = 12 * ImageWidth / ImageBounds.Width;
            resizingCrop = Crop is { } r && Math.Abs(start.Value.X - r.Right) <= tolerance && Math.Abs(start.Value.Y - r.Bottom) <= tolerance;
            movingCrop = !resizingCrop && Crop is { } c && Contains(c, start.Value);
            if (!movingCrop && !resizingCrop) Crop = null;
        }
        else if (Mode == "select")
        {
            Selected = Marks.FindLastIndex(mark => Contains(mark.Area, start.Value));
            beforeMove = Selected >= 0 ? Marks[Selected] : null;
            if (beforeMove is not null) undo.Push(Marks.ToArray());
        }
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); if (start is not { } first) return;
        var point = Pixel(e.GetPosition(this));
        if (Mode == "crop")
        {
            if (movingCrop && beforeCrop is { } original)
                Crop = original with { X = Math.Clamp(original.X + point.X - first.X, 0, ImageWidth - original.Width), Y = Math.Clamp(original.Y + point.Y - first.Y, 0, ImageHeight - original.Height) };
            else
            {
                var origin = resizingCrop && beforeCrop is { } previous ? new ImagePoint(previous.X, previous.Y) : first;
                var ratio = AspectRatio ?? (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? resizingCrop && beforeCrop is { } old ? old.Width / old.Height : 1 : (double?)null);
                Crop = Area(origin, point, ratio);
            }
        }
        else if (Mode == "select" && beforeMove is { } mark && Selected >= 0)
        {
            var dx = Math.Clamp(point.X - first.X, -mark.Area.X, ImageWidth - mark.Area.Right); var dy = Math.Clamp(point.Y - first.Y, -mark.Area.Y, ImageHeight - mark.Area.Bottom);
            Marks[Selected] = mark with { Area = mark.Area with { X = mark.Area.X + dx, Y = mark.Area.Y + dy }, Points = mark.Points?.Select(p => new ImagePoint(p.X + dx, p.Y + dy)).ToArray() };
        }
        else if (trail.Count < 10000)
        {
            if (Mode is "freehand" or "highlight") trail.Add(point);
            else { if (trail.Count == 1) trail.Add(point); else trail[^1] = point; }
        }
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); if (start is null) return;
        if (Mode is not ("crop" or "select") && trail.Count > 1)
        {
            var mark = Draft();
            if (mark.Area.Width >= 1 && mark.Area.Height >= 1 && (mark.Kind != "text" || !string.IsNullOrWhiteSpace(mark.Text)))
            { undo.Push(Marks.ToArray()); Marks.Add(mark); Selected = Marks.Count - 1; }
        }
        start = null; beforeMove = null; trail.Clear(); e.Pointer.Capture(null); Changed?.Invoke(); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e); start = null; trail.Clear(); InvalidateVisual();
    }
    private static bool Contains(ImageArea r, ImagePoint p) => p.X >= r.X && p.X <= r.Right && p.Y >= r.Y && p.Y <= r.Bottom;
    private ImageArea Area(ImagePoint a, ImagePoint b, double? ratio = null)
    {
        var x = Math.Min(a.X, b.X); var y = Math.Min(a.Y, b.Y); var width = Math.Abs(a.X - b.X); var height = Math.Abs(a.Y - b.Y);
        if (ratio is > 0)
        {
            height = width / ratio.Value;
            if (y + height > ImageHeight) { height = ImageHeight - y; width = height * ratio.Value; }
        }
        return new ImageArea(x, y, Math.Min(width, ImageWidth - x), Math.Min(height, ImageHeight - y));
    }
    private ImageMark Draft()
    {
        var area = new ImageArea(trail.Min(p => p.X), trail.Min(p => p.Y), Math.Max(1, trail.Max(p => p.X) - trail.Min(p => p.X)), Math.Max(1, trail.Max(p => p.Y) - trail.Min(p => p.Y)));
        area = area with { X = Math.Min(area.X, ImageWidth - area.Width), Y = Math.Min(area.Y, ImageHeight - area.Height) };
        return new ImageMark(Mode, area, Ink, Stroke, FontSize, MarkText, trail.ToArray(), MarkImage);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(new SolidColorBrush(Color.Parse("#151F18")), Bounds.WithX(0).WithY(0));
        if (bitmap is not null) context.DrawImage(bitmap, ImageBounds);
        if (Mode == "crop" && Crop is { Width: >= 1, Height: >= 1 } crop)
        {
            var image = ImageBounds; var selected = View(crop); var shade = new SolidColorBrush(Colors.Black, .5);
            context.FillRectangle(shade, new Rect(image.X, image.Y, image.Width, selected.Top - image.Top));
            context.FillRectangle(shade, new Rect(image.X, selected.Bottom, image.Width, image.Bottom - selected.Bottom));
            context.FillRectangle(shade, new Rect(image.X, selected.Top, selected.Left - image.Left, selected.Height));
            context.FillRectangle(shade, new Rect(selected.Right, selected.Top, image.Right - selected.Right, selected.Height));
            context.DrawRectangle(null, new Pen(Brushes.Gold, 2), selected);
            context.FillRectangle(Brushes.Gold, new Rect(selected.BottomRight - new Vector(5, 5), new Size(10, 10)));
        }
        if (Selected >= 0 && Selected < Marks.Count) context.DrawRectangle(null, new Pen(Brushes.Gold, 1), View(Marks[Selected].Area));
        if (start is not null && Mode is not ("select" or "crop") && trail.Count > 1)
        {
            var mark = Draft(); var pen = new Pen(Brush.Parse(mark.Color), mark.Stroke * ImageBounds.Width / ImageWidth); var r = View(mark.Area);
            if (mark.Kind == "ellipse") context.DrawEllipse(null, pen, r);
            else if (mark.Kind is "rectangle" or "image") context.DrawRectangle(null, pen, r);
            else if (mark.Kind == "text") context.DrawText(new FormattedText(mark.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, mark.FontSize * ImageBounds.Width / ImageWidth, pen.Brush!), r.TopLeft);
            else for (var i = 1; i < trail.Count; i++) context.DrawLine(pen, View(trail[i - 1]), View(trail[i]));
        }
    }
    public void Dispose() { bitmap?.Dispose(); bitmap = null; }
}
