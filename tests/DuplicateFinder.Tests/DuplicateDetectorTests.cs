namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;

public class DuplicateDetectorTests
{
    [Fact]
    public void FindDuplicates_GroupsExactDuplicateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var original = Path.Combine(root, "original.txt");
        var copy = Path.Combine(root, "copy.txt");
        var differentContent = Path.Combine(root, "different.txt");

        File.WriteAllText(original, "same-content");
        File.WriteAllText(copy, "same-content");
        File.WriteAllText(differentContent, "different-content");

        try
        {
            var detector = new DuplicateDetector();
            var groups = detector.FindDuplicates(root, new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" }
            });

            Assert.Single(groups);
            Assert.Equal(2, groups[0].FilePaths.Count);
            Assert.Contains(original, groups[0].FilePaths);
            Assert.Contains(copy, groups[0].FilePaths);
            Assert.Equal(12, groups[0].SizeBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindDuplicates_UsesSizeAndExtensionFilters()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var duplicateOne = Path.Combine(root, "one.txt");
        var duplicateTwo = Path.Combine(root, "two.txt");
        var outOfRange = Path.Combine(root, "large.md");

        File.WriteAllText(duplicateOne, "alpha");
        File.WriteAllText(duplicateTwo, "alpha");
        File.WriteAllText(outOfRange, "very-large-content");

        try
        {
            var detector = new DuplicateDetector();
            var groups = detector.FindDuplicates(root, new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" },
                MinimumSizeBytes = 2,
                MaximumSizeBytes = 20
            });

            Assert.Single(groups);
            Assert.Equal(2, groups[0].FilePaths.Count);
            Assert.Contains(duplicateOne, groups[0].FilePaths);
            Assert.Contains(duplicateTwo, groups[0].FilePaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DuplicateGroup_ReportsHumanReadableMetadata()
    {
        var group = new DuplicateGroup(new[] { "a.txt", "b.txt" }, 32);

        Assert.Equal(2, group.FilePaths.Count);
        Assert.Equal(32, group.SizeBytes);
        Assert.Contains("a.txt", group.FilePaths);
        Assert.Contains("b.txt", group.FilePaths);
    }
}
