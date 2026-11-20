using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Nashi.Core;
using System.Diagnostics;

namespace Nashi.Linux;

internal static class FileActions
{
    internal static async Task CopyToClipboard(Window owner, IEnumerable<string> paths)
    {
        if (owner.Clipboard is null) throw new IOException("Pano kullanılamıyor.");
        var items = new List<IStorageItem>();
        foreach (var path in paths)
        {
            IStorageItem? item = Directory.Exists(path) ? await owner.StorageProvider.TryGetFolderFromPathAsync(path) : await owner.StorageProvider.TryGetFileFromPathAsync(path);
            if (item is null) throw new FileNotFoundException("Dosya bulunamadı: " + path); items.Add(item);
        }
        await owner.Clipboard.SetFilesAsync(items);
    }
    internal static async Task<IReadOnlyList<string>> Transfer(string[] inputs, string destination, bool move, IProgress<OperationProgress> progress, CancellationToken token)
    {
        if (inputs.Length == 0) throw new ArgumentException("Önce dosya seç.");
        var dest = Path.GetFullPath(destination);
        var canonicalDest = (await ProcessRunner.RunAsync("/usr/bin/realpath", ["-z", "-m", "--", dest], token)).Output.TrimEnd('\0');
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            if (path == Path.GetPathRoot(path) || !File.Exists(path) && !Directory.Exists(path)) throw new ArgumentException("Var olan normal dosya veya klasörleri seç.");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Üst düzey sembolik bağlantı yerine dosyanın veya klasörün kendisini seç.");
            var physical = (await ProcessRunner.RunAsync("/usr/bin/realpath", ["-z", "-e", "--", path], token)).Output.TrimEnd('\0');
            if (Directory.Exists(path) && (physical == canonicalDest || canonicalDest.StartsWith(physical + '/', StringComparison.Ordinal))) throw new ArgumentException("Hedef klasör, kaynak klasörün dışında olmalı.");
            sources.TryAdd(physical, path);
        }
        var selected = sources.Where(pair => !sources.Any(parent => parent.Key != pair.Key && Directory.Exists(parent.Value) && pair.Key.StartsWith(parent.Key + '/', StringComparison.Ordinal))).Select(pair => pair.Value).ToArray();
        Directory.CreateDirectory(dest);
        if (!move)
        {
            using var tx = new ExportTransaction(dest); var staged = new List<string>();
            foreach (var path in selected)
            {
                var target = SafePaths.Unique(tx.StagingDirectory, Path.GetFileName(path));
                await ProcessRunner.RunAsync("/usr/bin/cp", ["--archive", "--reflink=auto", "--update=none-fail", "--no-target-directory", "--", path, target], token);
                staged.Add(target); progress.Report(new(staged.Count / (double)selected.Length, "Dosyalar kopyalanıyor…"));
            }
            return tx.Publish(staged, token, allowEmptyFiles: true);
        }
        var completed = new List<string>();
        foreach (var path in selected)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var parent = (await ProcessRunner.RunAsync("/usr/bin/realpath", ["-z", "-e", "--", Path.GetDirectoryName(path)!], token)).Output.TrimEnd('\0');
                if (parent == canonicalDest) { completed.Add(path); continue; }
                var target = SafePaths.Unique(dest, Path.GetFileName(path));
                await ProcessRunner.RunAsync("/usr/bin/mv", ["--update=none-fail", "--no-target-directory", "--", path, target], token);
                completed.Add(target); progress.Report(new(completed.Count / (double)selected.Length, "Dosyalar taşınıyor…"));
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                throw new IOException($"Taşıma durdu. Tamamlanan: {completed.Count}. Kaynak ve hedefte kalan dosyaları kontrol et. Hedef: {dest}\n{error.Message}", error);
            }
        }
        return completed;
    }
    internal static void Launch(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Sistem aracı başlatılamadı.");
    }
    internal static void Reveal(IEnumerable<string> paths)
    {
        foreach (var folder in paths.Select(path => Directory.Exists(path) ? path : Path.GetDirectoryName(path)!).Distinct(StringComparer.Ordinal)) Launch("/usr/bin/xdg-open", folder);
    }
    internal static void Terminal(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Dosyanın klasörü bulunamadı.");
        Launch("/usr/bin/konsole", "--workdir", folder);
    }
}
