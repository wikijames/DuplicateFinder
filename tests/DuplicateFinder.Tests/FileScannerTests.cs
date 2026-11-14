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

    [Fact]
    public void EnumerateFiles_StopsWhenCancellationIsRequested()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        for (var i = 0; i < 200; i++)
        {
            File.WriteAllText(Path.Combine(root, $"file-{i:D3}.txt"), new string('x', 32));
        }

        try
        {
            var scanner = new FileScanner();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var exception = Assert.ThrowsAny<OperationCanceledException>(() =>
            {
                foreach (var _ in scanner.EnumerateFiles(root, new ScanOptions(), cts.Token))
                {
                }
            });

            Assert.NotNull(exception);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_IgnoresReparsePointDirectories()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        var realDirectory = Path.Combine(root, "real");
        Directory.CreateDirectory(realDirectory);

        var linkedDirectory = Path.Combine(root, "linked");

        try
        {
            Directory.CreateSymbolicLink(linkedDirectory, realDirectory);

            var safeFile = Path.Combine(realDirectory, "safe.txt");
            File.WriteAllText(safeFile, "safe");

            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                IncludeHidden = false
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(safeFile, files);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        finally
        {
            if (Directory.Exists(linkedDirectory))
            {
                Directory.Delete(linkedDirectory);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EnumerateFiles_IgnoresConfiguredDirectoryNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var keepDirectory = Path.Combine(root, "keep");
        var ignoredDirectory = Path.Combine(root, "node_modules");
        Directory.CreateDirectory(keepDirectory);
        Directory.CreateDirectory(ignoredDirectory);

        var keepFile = Path.Combine(keepDirectory, "kept.txt");
        var ignoredFile = Path.Combine(ignoredDirectory, "ignored.txt");

        File.WriteAllText(keepFile, "keep");
        File.WriteAllText(ignoredFile, "ignore");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                ExcludedDirectoryNames = new[] { "node_modules" }
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(keepFile, files);
            Assert.DoesNotContain(ignoredFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_IgnoresExcludedPathSegmentsAnywhereInTheTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        var keepDirectory = Path.Combine(root, "project");
        var ignoredDirectory = Path.Combine(keepDirectory, "bin");
        Directory.CreateDirectory(ignoredDirectory);
        Directory.CreateDirectory(keepDirectory);

        var keepFile = Path.Combine(keepDirectory, "keep.txt");
        var ignoredFile = Path.Combine(ignoredDirectory, "ignored.txt");

        File.WriteAllText(keepFile, "keep");
        File.WriteAllText(ignoredFile, "ignore");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                ExcludedPathSegments = new[] { "bin" }
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(keepFile, files);
            Assert.DoesNotContain(ignoredFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_RespectsExtensionFilterAfterExclusionChecks()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        var keepDirectory = Path.Combine(root, "project");
        var ignoredDirectory = Path.Combine(keepDirectory, "bin");
        Directory.CreateDirectory(ignoredDirectory);
        Directory.CreateDirectory(keepDirectory);

        var keepFile = Path.Combine(keepDirectory, "keep.txt");
        var ignoredFile = Path.Combine(ignoredDirectory, "keep.txt");
        var otherFile = Path.Combine(keepDirectory, "notes.md");

        File.WriteAllText(keepFile, "keep");
        File.WriteAllText(ignoredFile, "ignore");
        File.WriteAllText(otherFile, "notes");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" },
                ExcludedPathSegments = new[] { "bin" }
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(keepFile, files);
            Assert.DoesNotContain(ignoredFile, files);
            Assert.DoesNotContain(otherFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_SkipsCommonWindowsSystemDirectoriesByDefault()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        var safeDirectory = Path.Combine(root, "project");
        var systemDirectory = Path.Combine(root, "$RECYCLE.BIN");
        Directory.CreateDirectory(safeDirectory);
        Directory.CreateDirectory(systemDirectory);

        var safeFile = Path.Combine(safeDirectory, "safe.txt");
        var systemFile = Path.Combine(systemDirectory, "system.txt");
        File.WriteAllText(safeFile, "safe");
        File.WriteAllText(systemFile, "system");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(safeFile, files);
            Assert.DoesNotContain(systemFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_RejectsEmptyOrWhitespaceRootPath()
    {
        var scanner = new FileScanner();

        Assert.Throws<ArgumentException>(() => scanner.EnumerateFiles("   ").ToList());
        Assert.Throws<ArgumentException>(() => scanner.EnumerateFiles(string.Empty).ToList());
    }

    [Fact]
    public void EnumerateFiles_SkipsSystemProtectedDirectoriesEvenWhenNamedDifferently()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        var safeDirectory = Path.Combine(root, "project");
        var protectedDirectory = Path.Combine(root, "Protected Files");
        Directory.CreateDirectory(safeDirectory);
        Directory.CreateDirectory(protectedDirectory);

        var safeFile = Path.Combine(safeDirectory, "safe.txt");
        var protectedFile = Path.Combine(protectedDirectory, "protected.txt");
        File.WriteAllText(safeFile, "safe");
        File.WriteAllText(protectedFile, "protected");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                ExcludedPathSegments = new[] { "Protected Files" }
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(safeFile, files);
            Assert.DoesNotContain(protectedFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_IgnoresConfiguredFileNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var keepFile = Path.Combine(root, "keep.txt");
        var excludedFile = Path.Combine(root, "Thumbs.db");
        var hiddenFile = Path.Combine(root, "desktop.ini");

        File.WriteAllText(keepFile, "keep");
        File.WriteAllText(excludedFile, "ignore");
        File.WriteAllText(hiddenFile, "ignore");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                ExcludedFileNames = new[] { "Thumbs.db", "desktop.ini" }
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(keepFile, files);
            Assert.DoesNotContain(excludedFile, files);
            Assert.DoesNotContain(hiddenFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_SkipsTemporaryAndCacheDirectoriesByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "project");
        var cacheDirectory = Path.Combine(projectDirectory, "temp");
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(projectDirectory);

        var keepFile = Path.Combine(projectDirectory, "keep.txt");
        var cachedFile = Path.Combine(cacheDirectory, "cached.txt");

        File.WriteAllText(keepFile, "keep");
        File.WriteAllText(cachedFile, "ignore");

        try
        {
            var scanner = new FileScanner();
            var files = scanner.EnumerateFiles(root, new ScanOptions
            {
                ExcludedPathSegments = new[] { "temp" }
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Single(files);
            Assert.Contains(keepFile, files);
            Assert.DoesNotContain(cachedFile, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
