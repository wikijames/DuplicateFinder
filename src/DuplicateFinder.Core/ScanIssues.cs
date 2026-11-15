namespace DuplicateFinder.Core;

using System.Security;

public sealed record ScanIssueSummary(int SkippedFileCount, int SkippedDirectoryCount, int ChangedFileCount, int ExcludedHardLinkPathCount = 0)
{
    public bool IsComplete => SkippedFileCount == 0 && SkippedDirectoryCount == 0 && ChangedFileCount == 0;
}

public sealed record DuplicateDetectionResult(IReadOnlyList<DuplicateGroup> Groups, ScanIssueSummary Issues);

internal sealed class ScanDiagnostics
{
    private readonly HashSet<string> _files = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _changed = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _hardLinks = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public void FileFailed(string path, bool changed = false)
    {
        _files.Add(path);
        if (changed) _changed.Add(path);
    }

    public void DirectoryFailed(string path) => _directories.Add(path);
    public void HardLinkExcluded(string path) => _hardLinks.Add(path);
    public ScanIssueSummary Snapshot() => new(_files.Count, _directories.Count, _changed.Count, _hardLinks.Count);
    public static bool IsRecoverable(Exception exception) => exception is IOException or UnauthorizedAccessException or SecurityException;
}

internal sealed class ScanFileChangedException() : IOException("File changed during scanning.");
