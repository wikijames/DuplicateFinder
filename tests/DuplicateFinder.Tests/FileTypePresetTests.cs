namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class FileTypePresetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));

    public FileTypePresetTests()
    {
        Directory.CreateDirectory(_root);
        foreach (var extension in new[] { "jpg", "mp3", "mp4", "pdf", "docx", "zip", "unknown" })
        {
            File.WriteAllText(Path.Combine(_root, "first." + extension), "synthetic byte-identical file");
            File.WriteAllText(Path.Combine(_root, "second." + extension), "synthetic byte-identical file");
        }
    }

    [Theory]
    [InlineData("Images", ".jpg")]
    [InlineData("Audio", ".mp3")]
    [InlineData("Videos", ".mp4")]
    [InlineData("PDF", ".pdf")]
    [InlineData("Documents", ".docx")]
    [InlineData("Archives", ".zip")]
    public void PresetScan_IncludesMatchingFilesAndExcludesOtherCategories(string name, string extension)
    {
        var preset = FileTypePreset.CommonTypes.Single(preset => preset.Name == name);
        var options = ScanSetup.CreateOptions(preset, ".unknown");
        var group = Assert.Single(new DuplicateDetector().FindDuplicates(_root, options));

        Assert.Equal(2, group.FilePaths.Count);
        Assert.All(group.FilePaths, path => Assert.Equal(extension, Path.GetExtension(path)));
    }

    [Fact]
    public void AllFiles_IncludesUnknownExtensionsAndIgnoresPreviousCustomText()
    {
        var preset = FileTypePreset.CommonTypes.Single(preset => preset.Name == "All files");
        var group = Assert.Single(new DuplicateDetector().FindDuplicates(_root, ScanSetup.CreateOptions(preset, ".jpg")));

        Assert.Equal(14, group.FilePaths.Count);
    }

    [Fact]
    public void CustomPreset_UsesTheEditedExtensionList()
    {
        var preset = FileTypePreset.CommonTypes.Single(preset => preset.IsCustom);
        var group = Assert.Single(new DuplicateDetector().FindDuplicates(_root, ScanSetup.CreateOptions(preset, "PDF, .unknown")));

        Assert.Equal(4, group.FilePaths.Count);
        Assert.All(group.FilePaths, path => Assert.Contains(Path.GetExtension(path), new[] { ".pdf", ".unknown" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyCustomPreset_DoesNotSilentlyScanEverything(string input)
    {
        var preset = FileTypePreset.CommonTypes.Single(preset => preset.IsCustom);

        Assert.Throws<ArgumentException>(() => ScanSetup.CreateOptions(preset, input));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
