using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nashi.Core;

namespace Nashi.Linux;

internal sealed class FloatShelf : Window
{
    private readonly MainWindow host;
    private readonly Toolchain tools;
    private readonly List<string> paths = [];
    private readonly ListBox files = new() { SelectionMode = SelectionMode.Multiple, Height = 170 };
    private readonly StackPanel body = new() { Spacing = 8, IsVisible = false };
    private readonly TextBlock count = MainWindow.Label("", 12), status = MainWindow.Label("", 11, true);
    private readonly Button expand;
    private readonly DispatcherTimer hover = new();
    private readonly Bitmap artwork;
    private PointerPressedEventArgs? pressed;
    private Point origin;
    private bool dragging;
    internal bool Expanded { get; private set; }
    internal IReadOnlyList<string> Paths => paths;

    internal FloatShelf(MainWindow host, IEnumerable<string> initial, Toolchain tools)
    {
        this.host = host; this.tools = tools;
        Title = "Float · Nashi"; Width = 160; Height = 224; MinWidth = 160; MinHeight = 224;
        CanResize = false; Topmost = true; ShowInTaskbar = false; SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; RequestedThemeVariant = host.RequestedThemeVariant;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        Content = new Border { Background = host.Background, BorderBrush = Brush.Parse("#647259"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Child = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        var header = new DockPanel(); var close = host.Button("×", () => { Close(); return Task.CompletedTask; }); close.Padding = new Thickness(8, 2); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var grip = MainWindow.Label("⋮⋮  Float", 13); AutomationProperties.SetName(grip, "Float taşıma tutamacı"); header.Children.Add(grip); root.Children.Add(header);
        grip.PointerPressed += (_, e) => { if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        using (var stream = AssetLoader.Open(new Uri("avares://Nashi.Linux/Assets/NashiMarkHappy.png"))) artwork = new Bitmap(stream);
        var stack = new Image { Source = artwork, Height = 70, Stretch = Stretch.Uniform };
        root.Children.Add(stack); expand = host.Button("Rafı aç", () => { SetExpanded(!Expanded); return Task.CompletedTask; }); root.Children.Add(expand); root.Children.Add(count); root.Children.Add(body);
        body.Children.Add(files);
        var actions = new WrapPanel();
        actions.Children.Add(host.Button("Önizle", Preview));
        actions.Children.Add(host.Button("Eylemler", () => host.ShowFileActions(Selection(), this)));
        actions.Children.Add(host.Button("Nashi’de kullan", () => { host.UseFiles(Selection()); return Task.CompletedTask; }));
        actions.Children.Add(host.Button("Raftan kaldır", () => { RemoveSelected(); return Task.CompletedTask; })); body.Children.Add(actions); body.Children.Add(status);
        stack.PointerPressed += (_, e) => { pressed = e; origin = e.GetPosition(stack); };
        stack.PointerMoved += async (_, e) => { if (pressed is not null && e.GetCurrentPoint(stack).Properties.IsLeftButtonPressed && Moved(e.GetPosition(stack))) await Carry(pressed); };
        files.AddHandler(PointerPressedEvent, (_, e) => { pressed = e; origin = e.GetPosition(files); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        files.PointerMoved += async (_, e) => { if (pressed is not null && e.GetCurrentPoint(files).Properties.IsLeftButtonPressed && Moved(e.GetPosition(files))) await Carry(pressed); };
        PointerReleased += (_, _) => { pressed = null; SnapToEdge(); };
        files.DoubleTapped += async (_, _) => await Preview();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        AddHandler(DragDrop.DropEvent, (_, e) => { Add(e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>() ?? []); e.Handled = true; });
        PointerEntered += (_, _) => Schedule(true); PointerExited += (_, _) => Schedule(false);
        KeyDown += async (_, e) =>
        {
            try
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                if (e.Key == Key.Space) { await Preview(); e.Handled = true; }
                if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
                if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control) { await FileActions.CopyToClipboard(this, Selection()); e.Handled = true; }
                if (e.Key == Key.C && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && Clipboard is not null) { await Clipboard.SetTextAsync(string.Join('\n', Selection())); e.Handled = true; }
            }
            catch (Exception error) { status.Text = error.Message; }
        };
        PositionChanged += (_, _) => { hover.Stop(); };
        Closed += (_, _) => { hover.Stop(); artwork.Dispose(); };
        Add(initial);
    }
    internal void Add(IEnumerable<string> incoming)
    {
        foreach (var path in incoming.Where(path => File.Exists(path) || Directory.Exists(path)).Select(Path.GetFullPath)) if (!paths.Contains(path, StringComparer.Ordinal)) paths.Add(path);
        Reload();
    }
    private void Reload()
    {
        files.SelectedItems?.Clear(); files.ItemsSource = null; files.ItemsSource = paths.Select(path => new ListBoxItem { Content = Path.GetFileName(path), Tag = path }).ToArray();
        count.Text = $"{paths.Count} dosya"; status.Text = paths.Any(path => !File.Exists(path) && !Directory.Exists(path)) ? "Bazı dosyalar artık bu konumda değil." : "Dosyaları buraya veya raftan dışarı sürükleyebilirsin.";
    }
    private string[] Selection() => files.SelectedItems is { Count: > 0 } selected ? selected.OfType<ListBoxItem>().Select(item => (string)item.Tag!).ToArray() : paths.ToArray();
    internal void RemoveSelected()
    {
        var selected = Selection().ToHashSet(StringComparer.Ordinal); paths.RemoveAll(selected.Contains); Reload();
    }
    internal void SetExpanded(bool value)
    {
        if (dragging) return;
        Expanded = value; body.IsVisible = value; expand.Content = value ? "Rafı kapat" : "Rafı aç";
        var area = Screens.ScreenFromWindow(this)?.WorkingArea; var previousRight = Position.X + Width * RenderScaling;
        Width = value ? 420 : 160; Height = value ? Math.Min(460, Math.Max(224, (area?.Height ?? 1000) / RenderScaling - 16)) : 224;
        if (area is { } screen && Math.Abs(previousRight - screen.Right) <= 24) Position = new PixelPoint(screen.Right - (int)Math.Ceiling(Width * RenderScaling), Position.Y);
        SnapToEdge();
    }
    internal static PixelPoint Snap(PixelRect area, PixelSize size, PixelPoint point)
    {
        var maxX = Math.Max(area.X, area.Right - size.Width); var maxY = Math.Max(area.Y, area.Bottom - size.Height);
        var x = Math.Clamp(point.X, area.X, maxX); var y = Math.Clamp(point.Y, area.Y, maxY);
        if (Math.Abs(x - area.X) <= 24) x = area.X; else if (Math.Abs(maxX - x) <= 24) x = maxX;
        if (Math.Abs(y - area.Y) <= 24) y = area.Y; else if (Math.Abs(maxY - y) <= 24) y = maxY;
        return new(x, y);
    }
    private void SnapToEdge()
    {
        if (Screens.ScreenFromWindow(this) is { } screen) Position = Snap(screen.WorkingArea, new PixelSize((int)Math.Ceiling(Width * RenderScaling), (int)Math.Ceiling(Height * RenderScaling)), Position);
    }
    private bool Moved(Point point) => Math.Pow(point.X - origin.X, 2) + Math.Pow(point.Y - origin.Y, 2) > 64;
    private void Schedule(bool expand)
    {
        hover.Stop(); hover.Interval = TimeSpan.FromMilliseconds(expand ? 250 : 600);
        hover.Tick -= Hover; hover.Tick += Hover; hover.Start();
    }
    private void Hover(object? sender, EventArgs e) { hover.Stop(); if (!dragging) SetExpanded(IsPointerOver); }
    private async Task Preview()
    {
        hover.Stop(); try { await new FilePreviewWindow(host, Selection(), tools).ShowDialog(this); } catch (Exception error) { status.Text = error.Message; }
    }
    private async Task Carry(PointerPressedEventArgs pointer)
    {
        if (dragging) return; dragging = true; pressed = null; hover.Stop();
        try
        {
            using var data = new DataTransfer();
            foreach (var path in Selection())
            {
                IStorageItem? item = Directory.Exists(path) ? await StorageProvider.TryGetFolderFromPathAsync(path) : await StorageProvider.TryGetFileFromPathAsync(path);
                if (item is not null) data.Add(DataTransferItem.CreateFile(item));
            }
            if (data.Items.Count > 0) await DragDrop.DoDragDropAsync(pointer, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception error) { status.Text = error.Message; }
        finally { dragging = false; Reload(); }
    }
}
