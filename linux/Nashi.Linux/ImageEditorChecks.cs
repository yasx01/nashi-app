using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Diagnostics;

namespace Nashi.Linux;

internal static class ImageEditorChecks
{
    internal static void Run(string root, string preview)
    {
        var input = Path.Combine(root, "image-editing", "white.png");
        var host = new MainWindow([input], Path.Combine(root, "image-editor-library")); host.Show(); Dispatcher.UIThread.RunJobs();
        void Check(bool value, string label) { if (!value) throw new Exception("Image editor: " + label); }
        void Preview(ImageEditorWindow window)
        {
            var deadline = Stopwatch.StartNew();
            while (window.PreviewPending && deadline.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            Check(!window.PreviewPending, "preview completes");
        }
        void Drag(ImageEditorWindow window, ImagePoint from, ImagePoint to, RawInputModifiers modifier = RawInputModifiers.None)
        {
            var canvas = window.EditorCanvas;
            var a = canvas.TranslatePoint(canvas.View(from), window)!.Value; var b = canvas.TranslatePoint(canvas.View(to), window)!.Value;
            window.MouseDown(a, MouseButton.Left, modifier); window.MouseMove(b, modifier | RawInputModifiers.LeftMouseButton); window.MouseUp(b, MouseButton.Left, modifier); Dispatcher.UIThread.RunJobs();
        }
        var crop = new ImageEditorWindow(host, "cropImage", [input]); _ = crop.ShowDialog(host); Dispatcher.UIThread.RunJobs();
        using (crop.CaptureRenderedFrame()) { }
        Drag(crop, new(10, 10), new(50, 40));
        Check(crop.EditorCanvas.Crop is { } first && Math.Abs(first.Width - 40) < .1 && Math.Abs(first.Height - 30) < .1, "drag creates exact source-pixel crop");
        Drag(crop, new(20, 20), new(30, 25));
        Check(crop.EditorCanvas.Crop is { } moved && Math.Abs(moved.X - 20) < .1 && Math.Abs(moved.Y - 15) < .1, "drag inside moves selection");
        Drag(crop, new(60, 45), new(80, 65), RawInputModifiers.Shift);
        Check(crop.EditorCanvas.Crop is { } resized && Math.Abs(resized.Width / resized.Height - 4d / 3) < .01, "Shift resize preserves existing aspect");
        using (var frame = crop.CaptureRenderedFrame() ?? throw new Exception("Crop render missing")) frame.Save(Path.ChangeExtension(preview, ".crop.png"));
        crop.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); host.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Check(crop.Result?.Crop is not null, "Enter accepts crop");

        var annotation = new ImageEditorWindow(host, "annotateImage", [input]); _ = annotation.ShowDialog(host); Dispatcher.UIThread.RunJobs();
        using (annotation.CaptureRenderedFrame()) { }
        Drag(annotation, new(10, 10), new(50, 40));
        Check(annotation.EditorCanvas.Marks.Count == 1 && annotation.EditorCanvas.Marks[0].Kind == "rectangle", "one stroke creates one mark");
        var tools = annotation.GetLogicalDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Çizim aracı"); tools.SelectedIndex = 8;
        Drag(annotation, new(20, 20), new(30, 25));
        Check(Math.Abs(annotation.EditorCanvas.Marks[0].Area.X - 20) < .1, "selected mark moves");
        annotation.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); annotation.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control); Dispatcher.UIThread.RunJobs();
        Check(Math.Abs(annotation.EditorCanvas.Marks[0].Area.X - 10) < .1, "Ctrl+Z restores mark position");
        Drag(annotation, new(20, 20), new(20, 20));
        annotation.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None); annotation.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Check(annotation.EditorCanvas.Marks.Count == 0, "Delete removes selected mark");
        annotation.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Geri al").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(annotation.EditorCanvas.Marks.Count == 1, "Undo restores deleted mark");
        Preview(annotation);
        using (var frame = annotation.CaptureRenderedFrame() ?? throw new Exception("Annotation render missing")) frame.Save(Path.ChangeExtension(preview, ".annotate.png"));
        annotation.Close(); host.Close();
        Console.WriteLine("PASS: real pointer crop creation/move/Shift-resize, Enter, annotation move/delete and keyboard undo.");
    }
}
