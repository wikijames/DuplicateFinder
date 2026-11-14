namespace DuplicateFinder.Core;

public sealed class ScanOptions
{
    public string SearchPattern { get; set; } = "*";

    public bool IncludeHidden { get; set; }

    public long? MinimumSizeBytes { get; set; }

    public long? MaximumSizeBytes { get; set; }

    public IEnumerable<string>? AllowedExtensions { get; set; }
}
