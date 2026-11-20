using Nashi.Core;

namespace Nashi.Linux;

internal static class FileActionChecks
{
    private sealed class CancelCopy(CancellationTokenSource cancellation) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => cancellation.Cancel();
    }
    internal static async Task Run(string root)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux file actions test");
        void Check(bool value, string label) { if (!value) throw new Exception("File actions: " + label); }
        var source = Path.Combine(root, "file-actions-source"); Directory.CreateDirectory(source);
        var file = Path.Combine(source, "Türkçe ' $()\n.txt"); await File.WriteAllTextAsync(file, "source bytes"); File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var empty = Path.Combine(source, "empty.txt"); await File.WriteAllTextAsync(empty, "");
        var folder = Path.Combine(source, "folder"); Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, "child.txt"), "child");
        var outside = Path.Combine(root, "file-actions-outside"); Directory.CreateDirectory(outside); await File.WriteAllTextAsync(Path.Combine(outside, "keep.txt"), "keep");
        Directory.CreateSymbolicLink(Path.Combine(folder, "link"), outside);
        var destination = Path.Combine(root, "file-actions-copy"); var progress = new Progress<OperationProgress>();
        var copied = await FileActions.Transfer([file, empty, folder], destination, false, progress, default);
        Check(copied.Count == 3 && await File.ReadAllTextAsync(copied[0]) == "source bytes" && new FileInfo(copied[1]).Length == 0, "copy preserves text and empty files");
        Check(File.GetUnixFileMode(copied[0]) == File.GetUnixFileMode(file), "copy preserves Unix permissions");
        Check(new DirectoryInfo(Path.Combine(copied[2], "link")).LinkTarget == outside, "nested symlink is copied without following it");
        var again = await FileActions.Transfer([file], destination, false, progress, default); Check(again[0] != copied[0] && File.ReadAllText(copied[0]) == "source bytes", "copy collision preserves existing output");
        var alias = Path.Combine(root, "file-actions-alias"); Directory.CreateSymbolicLink(alias, folder);
        try { await FileActions.Transfer([folder], Path.Combine(alias, "inside"), false, progress, default); throw new Exception("Destination in source accepted."); } catch (ArgumentException) { }
        Check(!Directory.Exists(Path.Combine(folder, "inside")), "physical descendant destination rejected before mutation");
        var cancelled = Path.Combine(root, "file-actions-cancelled"); using var cancellation = new CancellationTokenSource();
        try { await FileActions.Transfer([file, empty], cancelled, false, new CancelCopy(cancellation), cancellation.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
        Check(!Directory.EnumerateFileSystemEntries(cancelled).Any() && File.Exists(file) && File.Exists(empty) && File.Exists(Path.Combine(outside, "keep.txt")), "canceled copy rolls back and preserves source/external link target");
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute); File.SetUnixFileMode(outside, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        using (var readOnlyCancel = new CancellationTokenSource())
        {
            var readOnlyDestination = Path.Combine(root, "file-actions-readonly-cancel");
            try { await FileActions.Transfer([folder], readOnlyDestination, false, new CancelCopy(readOnlyCancel), readOnlyCancel.Token); throw new Exception("Read-only copy cancellation ignored."); } catch (OperationCanceledException) { }
            Check(!Directory.EnumerateFileSystemEntries(readOnlyDestination).Any(), "read-only copied directory rolls back");
            Check(File.GetUnixFileMode(folder) == (UnixFileMode.UserRead | UnixFileMode.UserExecute) && File.GetUnixFileMode(outside) == (UnixFileMode.UserRead | UnixFileMode.UserExecute), "rollback does not chmod originals or symlink targets");
        }
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); File.SetUnixFileMode(outside, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var moved = await FileActions.Transfer([file], destination, true, progress, default);
        Check(!File.Exists(file) && await File.ReadAllTextAsync(moved.Single()) == "source bytes" && File.Exists(copied[0]), "move collision creates a new name and retains old destination");
        var unchanged = await FileActions.Transfer(moved.ToArray(), destination, true, progress, default); Check(unchanged.Single() == moved.Single(), "move into same physical directory is a no-op");
        var nested = await FileActions.Transfer([folder, Path.Combine(folder, "child.txt")], Path.Combine(root, "file-actions-moved-folder"), true, progress, default);
        Check(nested.Count == 1 && !Directory.Exists(folder) && File.Exists(Path.Combine(nested[0], "child.txt")), "nested selection moves its ancestor once");
        Check(File.Exists(Path.Combine(outside, "keep.txt")), "moves never modify symlink targets");
        Console.WriteLine("PASS: file copy/move, empty files, permissions, collisions, newline/Unicode paths, symlinks, nested selections, physical destination validation and copy rollback.");
    }
}
