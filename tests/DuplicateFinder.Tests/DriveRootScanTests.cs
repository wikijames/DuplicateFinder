using DuplicateFinder.Core;

namespace DuplicateFinder.Tests;

public sealed class DriveRootScanTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));

    public DriveRootScanTests() => Directory.CreateDirectory(_fixture);

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void HiddenSystemDriveRootScansVisibleContentAndPreservesFilters(bool includeHidden, int expectedFiles)
    {
        if (!OperatingSystem.IsWindows()) return;

        WriteFile("visible", "first.png");
        WriteFile("visible", "second.png");
        WriteFile("visible", "document.txt");
        WriteFile("hidden", "hidden.png");
        WriteFile("system", "system.png");
        WriteFile("linked", "linked.png");
        WriteFile(".DuplicateFinder-Quarantine", "quarantined.png");
        File.SetAttributes(Path.Combine(_fixture, "hidden"), FileAttributes.Directory | FileAttributes.Hidden);
        File.SetAttributes(Path.Combine(_fixture, "system"), FileAttributes.Directory | FileAttributes.System);

        var driveRoot = Path.GetPathRoot(_fixture)!;
        var scanner = CreateScanner(driveRoot);
        var files = scanner.EnumerateFiles(driveRoot, new ScanOptions
        {
            IncludeHidden = includeHidden,
            AllowedExtensions = [".png"],
            // Physical fixture paths are under Temp; no user directories are enumerated.
            ExcludedPathSegments = []
        }).ToArray();

        Assert.Equal(expectedFiles, files.Length);
        Assert.Contains(Path.Combine(_fixture, "visible", "first.png"), files);
        Assert.Contains(Path.Combine(_fixture, "visible", "second.png"), files);
        Assert.DoesNotContain(files, path => path.Contains("system.png") || path.Contains("linked.png") || path.Contains("quarantined.png"));
        Assert.All(files, path => Assert.EndsWith(".png", path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryHiddenSelectedFolderStillRequiresIncludeHidden(bool includeHidden)
    {
        var file = WriteFile("ordinary", "image.png");
        var folder = Path.GetDirectoryName(file)!;
        var scanner = new FileScanner(Directory.EnumerateFiles, Directory.EnumerateDirectories,
            path => path == folder ? FileAttributes.Directory | FileAttributes.Hidden : File.GetAttributes(path));

        var files = scanner.EnumerateFiles(folder, new ScanOptions { IncludeHidden = includeHidden }).ToArray();

        Assert.Equal(includeHidden ? 1 : 0, files.Length);
    }

    [Fact]
    public void ReparsePointDriveRootIsNeverTraversed()
    {
        if (!OperatingSystem.IsWindows()) return;
        WriteFile("visible", "image.png");
        var driveRoot = Path.GetPathRoot(_fixture)!;
        var files = CreateScanner(driveRoot, reparseRoot: true).EnumerateFiles(driveRoot).ToArray();
        Assert.Empty(files);
    }

    private FileScanner CreateScanner(string driveRoot, bool reparseRoot = false) => new(
        (path, pattern, options) => Directory.EnumerateFiles(MapFixturePath(path, driveRoot), pattern, options),
        (path, pattern, options) => Directory.EnumerateDirectories(MapFixturePath(path, driveRoot), pattern, options),
        path => path == driveRoot
            ? FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System | (reparseRoot ? FileAttributes.ReparsePoint : 0)
            : Path.GetFileName(path) == "linked" ? FileAttributes.Directory | FileAttributes.ReparsePoint : File.GetAttributes(MapFixturePath(path, driveRoot)));

    private string MapFixturePath(string path, string driveRoot)
    {
        if (path == driveRoot) return _fixture;
        if (path.StartsWith(_fixture + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return path;
        throw new InvalidOperationException("The regression test must only enumerate its generated fixture.");
    }

    private string WriteFile(string folder, string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_fixture, folder)).FullName;
        var file = Path.Combine(directory, name);
        File.WriteAllText(file, "synthetic identical content");
        return file;
    }

    public void Dispose()
    {
        foreach (var directory in Directory.EnumerateDirectories(_fixture))
            File.SetAttributes(directory, FileAttributes.Directory);
        Directory.Delete(_fixture, recursive: true);
    }
}
