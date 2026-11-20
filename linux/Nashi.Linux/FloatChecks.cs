using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Nashi.Core;
using System.Diagnostics;

namespace Nashi.Linux;

internal static class FloatChecks
{
    internal static void Run(string root, string preview)
    {
        void Check(bool result, string label) { if (!result) throw new Exception("Float: " + label); }
        void Await(Task task)
        {
            var timeout = Stopwatch.StartNew(); while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            Check(task.IsCompleted, "async request completes"); task.GetAwaiter().GetResult();
        }
        var a = Path.Combine(root, "Nashi artwork.png"); var b = Path.Combine(root, "organizer.pdf");
        var host = new MainWindow(dataDirectory: Path.Combine(root, "float-library")); host.Show(); Dispatcher.UIThread.RunJobs();
        var shelf = new FloatShelf(host, [a, b, a], new Toolchain(Path.Combine(root, "float-tools"), "/usr/bin")); shelf.Show(); Dispatcher.UIThread.RunJobs();
        Check(shelf.Paths.Count == 2 && !shelf.Expanded, "compact shelf deduplicates file paths");
        using (var frame = shelf.CaptureRenderedFrame() ?? throw new Exception("Compact Float render missing")) frame.Save(Path.ChangeExtension(preview, ".float-compact.png"));
        shelf.SetExpanded(true); Dispatcher.UIThread.RunJobs();
        var list = shelf.GetLogicalDescendants().OfType<ListBox>().Single();
        Check(shelf.Expanded && list.IsEffectivelyVisible && list.ItemCount == 2, "expanded shelf displays actual files");
        using (var frame = shelf.CaptureRenderedFrame() ?? throw new Exception("Expanded Float render missing")) frame.Save(Path.ChangeExtension(preview, ".float.png"));
        list.SelectedIndex = 0; shelf.RemoveSelected(); Check(shelf.Paths.SequenceEqual([b]) && File.Exists(a), "remove selected reference preserves original file");
        shelf.Add([a]); Check(shelf.Paths.Count == 2, "incoming files join shelf");
        host.UseFiles([b]); Check(host.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text == Path.GetFileName(b)), "reuse returns paths to Nashi");
        Await(FileActions.CopyToClipboard(host, [a, b]));
        var clipboard = host.Clipboard ?? throw new Exception("Clipboard unavailable.");
        var copied = clipboard.TryGetFilesAsync(); Await(copied);
        var values = copied.GetAwaiter().GetResult();
        Check(values is { Length: 2 } && values.All(item => item.TryGetLocalPath() is { } path && (path == a || path == b)), "file clipboard carries real local files");
        Await(clipboard.ClearAsync());
        var area = new PixelRect(-1920, 0, 1920, 1080); var size = new PixelSize(160, 224);
        Check(FloatShelf.Snap(area, size, new(-1910, 700)) == new PixelPoint(-1920, 700), "snap supports negative monitor coordinates");
        Check(FloatShelf.Snap(area, size, new(-166, 20)) == new PixelPoint(-160, 0), "snap preserves right/top edges");
        Check(FloatShelf.Snap(area, size, new(1000, 2000)) == new PixelPoint(-160, 856), "snap clamps off-screen position");
        shelf.SetExpanded(false); Check(!shelf.Expanded && shelf.Width == 160, "shelf collapses"); shelf.Close(); host.Close(); Dispatcher.UIThread.RunJobs();
        Check(File.Exists(a) && File.Exists(b), "closing Float never removes files");
        Console.WriteLine("PASS: Float compact/expanded render, selection/removal/reuse, real file clipboard, negative-monitor edge geometry and original files.");
        Console.WriteLine("SKIP: native cross-application drag and compositor edge placement require desktop integration validation.");
    }
}
