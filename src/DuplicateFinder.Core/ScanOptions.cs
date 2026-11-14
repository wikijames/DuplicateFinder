namespace DuplicateFinder.Core;

public sealed class ScanOptions
{
    public string SearchPattern { get; set; } = "*";

    public bool IncludeHidden { get; set; }

    public long? MinimumSizeBytes { get; set; }

    public long? MaximumSizeBytes { get; set; }

    public IEnumerable<string>? AllowedExtensions { get; set; }

    public IEnumerable<string>? ExcludedDirectoryNames { get; set; }

    public IEnumerable<string>? ExcludedPathSegments { get; set; } = OperatingSystem.IsWindows()
        ? new[] { "$RECYCLE.BIN", "System Volume Information", "Windows", "Program Files", "Program Files (x86)", "Temp", "tmp", "cache", "Cache", "AppData" }
        : new[] { "tmp", "temp", "cache", "Cache" };

    public IEnumerable<string>? ExcludedFileNames { get; set; } = OperatingSystem.IsWindows()
        ? new[] { "Thumbs.db", "desktop.ini" }
        : Array.Empty<string>();

    public void Validate()
    {
        if (MinimumSizeBytes is long minimumSize && minimumSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSizeBytes), "Minimum size cannot be negative.");
        }

        if (MaximumSizeBytes is long maximumSize && maximumSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSizeBytes), "Maximum size cannot be negative.");
        }

        if (MinimumSizeBytes.HasValue && MaximumSizeBytes.HasValue && MinimumSizeBytes.Value > MaximumSizeBytes.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSizeBytes), "Maximum size must be greater than or equal to minimum size.");
        }
    }
}
