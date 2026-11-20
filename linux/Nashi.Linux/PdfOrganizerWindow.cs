using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Nashi.Linux;

public sealed record PdfEditOptions(int[]? Pages = null, int[]? PageRotations = null, bool UniformWidth = false,
    int PagesPerFile = 1, string PageRanges = "");

internal sealed class PdfOrganizerWindow : Window
{
    private sealed class Page(int number, byte[] image)
    {
        public int Number { get; } = number;
        public byte[] Image { get; } = image;
        public int Rotation { get; set; }
        public Bitmap? Bitmap { get; set; }
    }
    private readonly List<Page> pages = [];
    internal readonly ListBox Gallery = new() { SelectionMode = SelectionMode.Multiple };
    private readonly CheckBox uniform = new() { Content = "Bütün sayfaları eşit genişliğe getir" };
    private readonly TextBlock message = MainWindow.Label("Sayfaları seç; sırala, döndür veya çoğalt.", 12, true);
    private readonly HashSet<Bitmap> bitmaps = [];
    internal PdfEditOptions? Result { get; private set; }

    internal PdfOrganizerWindow(MainWindow host, byte[][] images)
    {
        if (images.Length == 0) throw new ArgumentException("PDF boş.");
        Title = "PDF sayfalarını düzenle · Nashi"; Width = 1000; Height = 760; MinWidth = 700; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = host.Background; Foreground = host.Foreground; RequestedThemeVariant = host.RequestedThemeVariant;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 16) }; var save = host.Button("Kullan", () => { Accept(); return Task.CompletedTask; }, true);
        DockPanel.SetDock(save, Dock.Right); heading.Children.Add(save); heading.Children.Add(MainWindow.Label("PDF sayfalarını düzenle", 26)); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (title, action) in new (string, Action)[] { ("Sola taşı", () => Move(-1)), ("Sağa taşı", () => Move(1)), ("Döndür", Rotate), ("Çoğalt", Duplicate), ("Kaldır", Remove) })
        {
            var button = host.Button(title, () => { action(); return Task.CompletedTask; }); button.Margin = new Thickness(0, 0, 6, 6); tools.Children.Add(button);
        }
        tools.Children.Add(uniform); DockPanel.SetDock(tools, Dock.Bottom); root.Children.Add(tools);
        DockPanel.SetDock(message, Dock.Bottom); message.Margin = new Thickness(0, 10, 0, 12); root.Children.Add(message);
        Gallery.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel());
        Gallery.ItemTemplate = new FuncDataTemplate<Page>((page, _) =>
        {
            if (page is null) return new TextBlock();
            if (page.Bitmap is null) { using var stream = new MemoryStream(page.Image); page.Bitmap = new Bitmap(stream); bitmaps.Add(page.Bitmap); }
            var content = new StackPanel { Spacing = 8, Margin = new Thickness(12), Width = 160 };
            content.Children.Add(new Image { Source = page.Bitmap, Width = 150, Height = 180, Stretch = Stretch.Uniform, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(page.Rotation) });
            content.Children.Add(MainWindow.Label($"Sayfa {page.Number} · {page.Rotation}°", 12)); return content;
        });
        for (var index = 0; index < images.Length; index++) pages.Add(new Page(index + 1, images[index]));
        Reload([]); root.Children.Add(Gallery);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Delete) { Remove(); e.Handled = true; }
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        };
        Closed += (_, _) => { foreach (var bitmap in bitmaps) bitmap.Dispose(); };
    }
    private Page[] Selected() => Gallery.SelectedItems?.Cast<Page>().Distinct().ToArray() ?? [];
    private void Reload(Page[] selected)
    {
        Gallery.SelectedItems?.Clear(); Gallery.ItemsSource = null; Gallery.ItemsSource = pages.ToArray();
        foreach (var page in selected.Distinct()) Gallery.SelectedItems?.Add(page);
        message.Text = $"{pages.Count} çıktı sayfası. Ctrl ile birden fazla sayfa seçebilirsin.";
    }
    private void Move(int direction)
    {
        var selected = Selected();
        foreach (var page in direction < 0 ? selected : selected.Reverse())
        {
            var index = pages.IndexOf(page); var next = index + direction;
            if (next < 0 || next >= pages.Count || selected.Contains(pages[next])) continue;
            pages.RemoveAt(index); pages.Insert(next, page);
        }
        Reload(selected);
    }
    private void Rotate() { var selected = Selected(); foreach (var page in selected) page.Rotation = (page.Rotation + 90) % 360; Reload(selected); }
    private void Duplicate()
    {
        var selected = Selected(); if (pages.Count + selected.Length > 10000) { message.Text = "En fazla 10.000 çıktı sayfası seçebilirsin."; return; }
        foreach (var page in selected.Reverse()) pages.Insert(pages.IndexOf(page) + 1, new Page(page.Number, page.Image) { Rotation = page.Rotation, Bitmap = page.Bitmap });
        Reload(selected);
    }
    private void Remove()
    {
        var selected = Selected(); if (pages.Count - selected.Length < 1) { message.Text = "En az bir sayfa kalmalı."; return; }
        foreach (var page in selected) pages.Remove(page); Reload([]);
    }
    private void Accept()
    {
        Result = new PdfEditOptions(pages.Select(p => p.Number).ToArray(), pages.Select(p => p.Rotation).ToArray(), uniform.IsChecked == true); Close(Result);
    }
}
