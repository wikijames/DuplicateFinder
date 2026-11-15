namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class WindowsPathSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void LongPaths_SupportScanQuarantineAndRestoreWithOrdinaryLocalPathInputs()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = CreateDeepFolder("source");
        var destination = CreateDeepFolder("destination");
        var first = Path.Combine(source, "first.txt");
        var keep = Path.Combine(source, "keep.txt");
        File.WriteAllText(first, "synthetic long path contents");
        File.WriteAllText(keep, "synthetic long path contents");
        Assert.True(first.Length > 260);

        var result = new DuplicateDetector(new WindowsFileIdentityProvider()).Scan(source);
        Assert.True(result.Issues.IsComplete);
        var group = Assert.Single(result.Groups);
        var service = new QuarantineService();
        var batch = service.Quarantine(HardLinkSafetyTests.CreateCleanupOperation(group, keep, DuplicateAction.Quarantine), destination);
        Assert.False(File.Exists(first));
        Assert.True(File.Exists(keep));
        Assert.Equal(1, service.Restore(service.Inspect(batch.DirectoryPath)).RestoredCount);
        Assert.Equal("synthetic long path contents", File.ReadAllText(first));
    }

    [Fact]
    public void LongPaths_SupportGuardedPermanentDeletionOfOnlyTheReviewedSyntheticCopy()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = CreateDeepFolder("delete-source");
        var first = Path.Combine(source, "first.txt");
        var keep = Path.Combine(source, "keep.txt");
        File.WriteAllText(first, "synthetic delete fixture");
        File.WriteAllText(keep, "synthetic delete fixture");
        var group = Assert.Single(new DuplicateDetector(new WindowsFileIdentityProvider()).FindDuplicates(source));

        new WindowsCleanupService().Execute(HardLinkSafetyTests.CreateCleanupOperation(group, keep, DuplicateAction.Delete));

        Assert.False(File.Exists(first));
        Assert.Equal("synthetic delete fixture", File.ReadAllText(keep));
    }

    [Theory]
    [InlineData(@"relative\file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\?\C:\file.txt")]
    [InlineData(@"\\.\C:\file.txt")]
    [InlineData(@"C:\fixture\file.txt:stream")]
    [InlineData(@"C:\fixture\trailing.")]
    [InlineData(@"C:\fixture\trailing ")]
    public void UnsupportedPathForms_AreRejectedBeforeAnyFileAction(string path)
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Throws<IOException>(() => WindowsCleanupSession.GetSupportedPath(path));
    }

    private string CreateDeepFolder(string name)
    {
        var path = Path.Combine(_root, name);
        while (path.Length < 310) path = Path.Combine(path, "synthetic-segment-" + new string('x', 30));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
