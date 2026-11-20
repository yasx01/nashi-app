using Avalonia.Media.Imaging;
using Nashi.Core;
using System.Text;

namespace Nashi.Linux;

internal static class ClipboardImport
{
    internal static string Image(Bitmap image, string library)
    {
        ImageEditing.Canvas(image.PixelSize.Width, image.PixelSize.Height);
        using var tx = new ExportTransaction(Path.Combine(library, "Imports"));
        var path = Path.Combine(tx.StagingDirectory, "Pasted image.png"); image.Save(path);
        return tx.Publish([path], default).Single();
    }
    internal static string Text(string text, string library)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Önce bir dosya, görsel veya metin kopyala.");
        if (Encoding.UTF8.GetByteCount(text) > 16 * 1024 * 1024) throw new ArgumentException("Metin 16 MB sınırını aşıyor. Bir metin dosyası seç.");
        using var tx = new ExportTransaction(Path.Combine(library, "Imports"));
        var path = Path.Combine(tx.StagingDirectory, "Pasted text.txt"); File.WriteAllText(path, text, new UTF8Encoding(false));
        return tx.Publish([path], default).Single();
    }
}
