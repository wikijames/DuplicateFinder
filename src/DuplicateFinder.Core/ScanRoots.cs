namespace DuplicateFinder.Core;

public static class ScanRoots
{
    public const int MaximumRootCount = 64;

    public static IReadOnlyList<string> Normalize(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var roots = new List<string>();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Every scan folder needs a path.", nameof(paths));
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (roots.Any(parent => Covers(parent, normalized, comparison))) continue;
            roots.RemoveAll(child => Covers(normalized, child, comparison));
            roots.Add(normalized);
        }
        if (roots.Count == 0) throw new ArgumentException("Select at least one scan folder.", nameof(paths));
        if (roots.Count > MaximumRootCount) throw new ArgumentException($"Select at most {MaximumRootCount} non-overlapping scan folders.", nameof(paths));
        return roots.AsReadOnly();
    }

    private static bool Covers(string parent, string child, StringComparison comparison) =>
        string.Equals(parent, child, comparison) || child.StartsWith(
            Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, comparison);
}
