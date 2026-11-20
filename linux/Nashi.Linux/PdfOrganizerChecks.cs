using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Nashi.Linux;

internal static class PdfOrganizerChecks
{
    internal static async Task<byte[][]> Prepare(string root)
    {
        var file = Path.Combine(root, "organizer.pdf");
        using (var pdf = new PdfDocument())
        {
            pdf.Info.Title = "Original PDF metadata";
            var a = pdf.AddPage(); a.Width = XUnit.FromPoint(100); a.Height = XUnit.FromPoint(200);
            var b = pdf.AddPage(); b.Width = XUnit.FromPoint(300); b.Height = XUnit.FromPoint(200); pdf.Save(file);
        }
        var original = File.ReadAllBytes(file); var progress = new Progress<OperationProgress>();
        var engine = new Operations(new Toolchain(Path.Combine(root, "pdf-tools")));
        var options = new PdfEditOptions([2, 1, 2], [90, 0, 180]);
        var output = (await engine.RunAsync("organizePDF", [file], root, "", 85, 0, 0, 0, 0, new(), progress, default, pdfOptions: options)).Single();
        using (var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import))
        {
            Check(pdf.PageCount == 3 && pdf.Pages[0].Width.Point == 300 && pdf.Pages[1].Width.Point == 100, "page order and duplicate");
            Check(pdf.Pages[0].Rotate == 90 && pdf.Pages[2].Rotate == 180, "independent duplicate rotations");
            Check(pdf.Info.Title == "Original PDF metadata", "metadata retained");
        }
        var uniform = (await engine.RunAsync("organizePDF", [file], root, "", 85, 0, 0, 0, 0, new(), progress, default, pdfOptions: new([1, 2], [0, 0], true))).Single();
        using (var pdf = PdfReader.Open(uniform, PdfDocumentOpenMode.Import)) Check(Math.Abs(pdf.Pages[0].Width.Point - pdf.Pages[1].Width.Point) < .01, "uniform page widths");
        var split = await engine.RunAsync("splitPDF", [file], root, "", 85, 0, 0, 0, 0, new(), progress, default, pdfOptions: new(PagesPerFile: 2, PageRanges: "2,1"));
        using (var pdf = PdfReader.Open(split.Single(), PdfDocumentOpenMode.Import)) Check(pdf.PageCount == 2 && pdf.Pages[0].Width.Point == 300, "split range and group size");
        var encrypted = Path.Combine(root, "encrypted.pdf");
        using (var pdf = PdfReader.Open(file, PdfDocumentOpenMode.Modify)) { pdf.SecuritySettings.UserPassword = "reader"; pdf.SecuritySettings.OwnerPassword = "owner"; pdf.Save(encrypted); }
        Check((await LinuxPdf.RenderAsync(encrypted, 36, default, "reader")).Length == 2, "user password rendering");
        Check((await LinuxPdf.RenderAsync(encrypted, 36, default, "owner")).Length == 2, "owner password rendering");
        try { await LinuxPdf.RenderAsync(encrypted, 36, default, "wrong"); throw new Exception("Incorrect password accepted"); } catch (PdfReaderException) { }
        var huge = Path.Combine(root, "huge.pdf");
        using (var pdf = new PdfDocument()) { var page = pdf.AddPage(); page.Width = XUnit.FromPoint(1_000_000); page.Height = XUnit.FromPoint(1_000_000); pdf.Save(huge); }
        try { await LinuxPdf.RenderAsync(huge); throw new Exception("Unbounded PDF rendered"); } catch (InvalidDataException) { }
        Check(File.ReadAllBytes(file).SequenceEqual(original), "source byte integrity");
        Console.WriteLine("PASS: PDF page order/duplicates/independent rotations/uniform widths/split ranges, user and owner passwords, oversized page rejection and source integrity.");
        return await LinuxPdf.RenderAsync(file, 36);
    }
    internal static void Interact(byte[][] images, string root, string preview)
    {
        var host = new MainWindow(dataDirectory: Path.Combine(root, "pdf-editor-library")); host.Show(); Dispatcher.UIThread.RunJobs();
        var editor = new PdfOrganizerWindow(host, images); _ = editor.ShowDialog(host); Dispatcher.UIThread.RunJobs();
        using (editor.CaptureRenderedFrame()) { }
        void Click(string title)
        {
            editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == title).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        editor.Gallery.SelectedIndex = 1; Click("Sola taşı"); Click("Çoğalt"); Click("Döndür");
        Check(editor.Gallery.ItemCount == 3, $"UI duplicate (items={editor.Gallery.ItemCount}, selected={editor.Gallery.SelectedItems?.Count})");
        using (var frame = editor.CaptureRenderedFrame() ?? throw new Exception("PDF editor frame missing")) frame.Save(Path.ChangeExtension(preview, ".pdf.png"));
        Click("Kullan"); Dispatcher.UIThread.RunJobs();
        Check(editor.Result is { } options && options.Pages!.SequenceEqual(new[] { 2, 2, 1 }) && options.PageRotations!.SequenceEqual(new[] { 90, 0, 0 }), "UI order and independent rotation");
        host.Close(); Console.WriteLine("PASS: PDF organizer UI moves, duplicates and independently rotates page selections.");
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("PDF organizer: " + label); }
}
