using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Nashi.Core;

namespace Nashi.Linux;

public sealed record MetadataEditOptions(bool RemoveAll = true, IReadOnlyDictionary<string, string>? Updates = null);

internal sealed class MetadataEditorWindow : Window
{
    private readonly CheckBox removeAll = new() { Content = "Tüm üst verileri temizle" };
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly IReadOnlyDictionary<string, string> original;
    internal MetadataEditOptions? Result { get; private set; }

    internal MetadataEditorWindow(MainWindow host, IReadOnlyDictionary<string, string> metadata, bool media, int fileCount = 1)
    {
        original = metadata;
        Title = "Üst verileri düzenle · Nashi"; Width = 820; Height = 760; MinWidth = 600; MinHeight = 450;
        Background = host.Background; Foreground = host.Foreground; RequestedThemeVariant = host.RequestedThemeVariant; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel { Spacing = 12 };
        header.Children.Add(MainWindow.Label("Üst verileri düzenle", 25));
        header.Children.Add(MainWindow.Label(fileCount == 1 ? "Alanları değiştir veya temizle. Özgün dosyan korunur." : $"{fileCount} dosya · Değiştirdiğin alanlar bütün seçime uygulanır. Özgün dosyalar korunur.", 13, true));
        header.Children.Add(removeAll); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var accept = host.Button("Kullan", () => { Result = Options(); Close(Result); return Task.CompletedTask; }, true);
        accept.Margin = new Thickness(0, 12, 0, 0); accept.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(accept, Dock.Bottom); root.Children.Add(accept);
        var stack = new StackPanel { Spacing = 8 }; root.Children.Add(new ScrollViewer { Content = stack });
        var source = metadata.GetValueOrDefault("SourceFile", "");
        var prefix = FeatureCatalog.Extension(source) == "pdf" ? "PDF:" : "";
        var editable = media ? MediaMetadata.Editable.Concat(metadata.Keys.Where(k =>
            (k.StartsWith("stream:", StringComparison.Ordinal) || k.StartsWith("chapter:", StringComparison.Ordinal)) &&
            new[] { ":title", ":language", ":handler_name", ":comment" }.Any(suffix => k.EndsWith(suffix, StringComparison.Ordinal)))).Distinct() :
            MetadataService.EditableTags.Where(k => prefix.Length > 0 ? k.StartsWith(prefix, StringComparison.Ordinal) : k.StartsWith("EXIF:", StringComparison.Ordinal) || k.StartsWith("XMP:", StringComparison.Ordinal));
        foreach (var key in editable)
        {
            stack.Children.Add(MainWindow.Label(key, 12)); var field = new TextBox { Text = metadata.GetValueOrDefault(key, ""), AcceptsReturn = true, MinHeight = 34, MaxHeight = 120 };
            AutomationProperties.SetName(field, key); fields.Add(key, field); stack.Children.Add(field);
        }
        var details = new StackPanel { Spacing = 6 };
        foreach (var pair in metadata.Where(p => !fields.ContainsKey(p.Key))) details.Children.Add(MainWindow.Label(pair.Key + " · " + pair.Value, 12, true));
        stack.Children.Add(new Expander { Header = "Tüm üst veriler", Content = details });
        removeAll.IsCheckedChanged += (_, _) => { foreach (var field in fields.Values) field.IsEnabled = removeAll.IsChecked != true; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    internal MetadataEditOptions Options() => new(removeAll.IsChecked == true, removeAll.IsChecked == true ? null : fields
        .Where(pair => (pair.Value.Text ?? "") != original.GetValueOrDefault(pair.Key, ""))
        .ToDictionary(pair => pair.Key, pair => pair.Value.Text ?? ""));
}
