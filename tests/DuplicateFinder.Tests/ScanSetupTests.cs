namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class ScanSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));

    public ScanSetupTests()
    {
        Directory.CreateDirectory(_root);
        foreach (var name in new[] { "photo.jpg", "archive.bin", "note.txt" })
        {
            File.WriteAllText(Path.Combine(_root, name), "synthetic byte-identical contents; not a decoded image");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultSetup_FindsExactDuplicatesAcrossAllExtensions(string? extensions)
    {
        var result = new DuplicateDetector().FindDuplicates(_root, ScanSetup.CreateOptions(extensions));

        Assert.Equal(3, Assert.Single(result).FilePaths.Count);
    }

    [Fact]
    public void ExtensionSetup_FiltersFilesAndNormalizesRepeatedUserInput()
    {
        var result = new DuplicateDetector().FindDuplicates(_root, ScanSetup.CreateOptions("jpg, .TXT; .jpg"));

        var group = Assert.Single(result);
        Assert.Equal(2, group.FilePaths.Count);
        Assert.Contains(Path.Combine(_root, "photo.jpg"), group.FilePaths);
        Assert.Contains(Path.Combine(_root, "note.txt"), group.FilePaths);
        Assert.DoesNotContain(Path.Combine(_root, "archive.bin"), group.FilePaths);
    }

    [Theory]
    [InlineData("*.txt")]
    [InlineData("../jpg")]
    [InlineData(".")]
    public void InvalidExtensionInput_IsRejectedBeforeScanning(string extensions)
    {
        Assert.Throws<ArgumentException>(() => ScanSetup.CreateOptions(extensions));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
