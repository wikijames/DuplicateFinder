namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;

public class FileScannerTests
{
    [Fact]
    public void EnumerateFiles_ReturnsNestedFilesRecursively()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "alpha"));
        Directory.CreateDirectory(Path.Combine(root, "beta", "gamma"));

        var rootFile = Path.Combine(root, "root.txt");
        var nestedFile = Path.Combine(root, "alpha", "nested.txt");
        var deepFile = Path.Combine(root, "beta", "gamma", "deep.bin");

        File.WriteAllText(rootFile, "root");
        File.WriteAllText(nestedFile, "nested");
        File.WriteAllText(deepFile, "deep");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Equal(3, files.Count);
            Assert.Contains(rootFile, files);
            Assert.Contains(nestedFile, files);
            Assert.Contains(deepFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_RespectsPatternFilter()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var txtFileOne = Path.Combine(root, "one.txt");
        var txtFileTwo = Path.Combine(root, "two.txt");
        var mdFile = Path.Combine(root, "notes.md");

        File.WriteAllText(txtFileOne, "one");
        File.WriteAllText(txtFileTwo, "two");
        File.WriteAllText(mdFile, "notes");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Equal(2, files.Count);
            Assert.Contains(txtFileOne, files);
            Assert.Contains(txtFileTwo, files);
            Assert.All(files, file => Assert.EndsWith(".txt", file, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_RespectsSizeAndExtensionFilters()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var smallTxt = Path.Combine(root, "small.txt");
        var mediumTxt = Path.Combine(root, "medium.txt");
        var biggerMd = Path.Combine(root, "bigger.md");

        File.WriteAllText(smallTxt, "a");
        File.WriteAllText(mediumTxt, "abcdef");
        File.WriteAllText(biggerMd, "abcdefghijk");

        try
        {
            var scanner = new FileScanner();
            var options = new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" },
                MinimumSizeBytes = 2,
                MaximumSizeBytes = 10
            };

            var files = scanner.EnumerateFiles(root, options)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.Single(files);
            Assert.Contains(mediumTxt, files);
            Assert.DoesNotContain(smallTxt, files);
            Assert.DoesNotContain(biggerMd, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
