namespace DuplicateFinder.Tests;

using System.Globalization;
using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class ScanFilterSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));

    public ScanFilterSettingsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SizeRange_IsInclusiveAndAppliesToActualDiscovery()
    {
        foreach (var (name, count) in new[] { ("small.bin", 1), ("first.bin", 3), ("second.bin", 3), ("large.bin", 5) })
            File.WriteAllBytes(Path.Combine(_root, name), new byte[count]);
        var threeBytes = (3m / 1048576m).ToString(CultureInfo.CurrentCulture);
        var options = new ScanOptions();
        ScanSetup.ApplyFilters(options, new ScanFilterSettings(threeBytes, threeBytes));

        var group = Assert.Single(new DuplicateDetector().FindDuplicates(_root, options));

        Assert.Equal(3, group.SizeBytes);
        Assert.Equal(2, group.FilePaths.Count);
    }

    [Theory]
    [InlineData("-1", "")]
    [InlineData("abc", "")]
    [InlineData("2", "1")]
    [InlineData("", "-3")]
    [InlineData("8796093022208", "")]
    public void InvalidSizeSettings_AreRejectedBeforeScanning(string minimum, string maximum)
    {
        Assert.ThrowsAny<ArgumentException>(() => ScanSetup.ApplyFilters(new ScanOptions(), new ScanFilterSettings(minimum, maximum)));
    }

    [Fact]
    public void DecimalSizes_RespectCurrentCultureAndRoundMinimumUp()
    {
        var options = new ScanOptions();
        var text = (0.0000001m).ToString(CultureInfo.CurrentCulture);
        ScanSetup.ApplyFilters(options, new ScanFilterSettings(text));

        Assert.Equal(1, options.MinimumSizeBytes);
        Assert.Null(options.MaximumSizeBytes);
    }

    [Fact]
    public void UserExclusions_DoNotDisableTheReservedQuarantineExclusion()
    {
        foreach (var folder in new[] { "build output", QuarantineService.FolderName })
        {
            Directory.CreateDirectory(Path.Combine(_root, folder));
            File.WriteAllText(Path.Combine(_root, folder, "copy.txt"), "synthetic identical contents");
        }
        File.WriteAllText(Path.Combine(_root, "original.txt"), "synthetic identical contents");
        var options = new ScanOptions();
        ScanSetup.ApplyFilters(options, new ScanFilterSettings(ExcludedFolderNames: "build output"));

        Assert.Empty(new DuplicateDetector().FindDuplicates(_root, options));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("folder/child")]
    [InlineData("*")]
    [InlineData("..")]
    public void Exclusions_RequireFolderNamesRatherThanPathsOrWildcards(string exclusions)
    {
        Assert.Throws<ArgumentException>(() => ScanSetup.ApplyFilters(new ScanOptions(), new ScanFilterSettings(ExcludedFolderNames: exclusions)));
    }

    [Fact]
    public void Defaults_KeepNoSizeBoundsAndHideHiddenEntries()
    {
        var options = new ScanOptions();
        ScanSetup.ApplyFilters(options, new ScanFilterSettings());

        Assert.Null(options.MinimumSizeBytes);
        Assert.Null(options.MaximumSizeBytes);
        Assert.False(options.IncludeHidden);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
