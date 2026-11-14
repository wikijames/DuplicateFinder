namespace DuplicateFinder.Core;

public sealed class DuplicateGroup
{
    public DuplicateGroup(IEnumerable<string> filePaths, long sizeBytes)
    {
        FilePaths = filePaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SizeBytes = sizeBytes;
    }

    public IReadOnlyList<string> FilePaths { get; }

    public long SizeBytes { get; }
}
