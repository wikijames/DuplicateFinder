namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;

public class ScanSafetyTests
{
    [Fact]
    public void ScanOptions_RejectsInvalidSizeRange()
    {
        var options = new ScanOptions
        {
            MinimumSizeBytes = 100,
            MaximumSizeBytes = 50
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("MaximumSizeBytes", exception.ParamName);
    }

    [Fact]
    public void FileScanner_IgnoresHiddenEntriesWhenDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var visibleFile = Path.Combine(root, "visible.txt");
        var hiddenDir = Path.Combine(root, ".hidden");
        Directory.CreateDirectory(hiddenDir);
        var hiddenFile = Path.Combine(hiddenDir, "hidden.txt");

        File.WriteAllText(visibleFile, "visible");
        File.WriteAllText(hiddenFile, "hidden");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                IncludeHidden = false
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(visibleFile, files);
            Assert.DoesNotContain(hiddenFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileScanner_RespectsMinimumSizeFilter()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var small = Path.Combine(root, "small.txt");
        var large = Path.Combine(root, "large.txt");

        File.WriteAllText(small, "a");
        File.WriteAllText(large, "abcdefghij");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                MinimumSizeBytes = 5
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(large, files);
            Assert.DoesNotContain(small, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
