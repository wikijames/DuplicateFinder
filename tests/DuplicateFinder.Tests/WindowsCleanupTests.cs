namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class WindowsCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
    private readonly string[] _paths;
    private readonly DuplicateCleanupOperation _operation;

    public WindowsCleanupTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new[] { "first.txt", "second.txt", "keep.txt" }.Select(name => Path.Combine(_root, name)).ToArray();
        foreach (var path in _paths) File.WriteAllText(path, "synthetic original content");
        var group = Assert.Single(new DuplicateDetector().FindDuplicates(_root));
        _operation = CreateOperation(group);
    }

    private DuplicateCleanupOperation CreateOperation(DuplicateGroup group, DuplicateAction action = DuplicateAction.Delete) =>
        new(new DuplicateCleanupExecution(new DuplicateCleanupPlan(new DuplicateReviewSelection(group, _paths[2]), action)));

    [Fact]
    public void Execute_RemovesOnlyReviewedSyntheticCopiesAndPreservesChosenKeepFile()
    {
        if (!OperatingSystem.IsWindows()) return;

        new WindowsCleanupService().Execute(_operation);

        Assert.False(File.Exists(_paths[0]));
        Assert.False(File.Exists(_paths[1]));
        Assert.Equal("synthetic original content", File.ReadAllText(_paths[2]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Execute_RejectsChangedFileBeforeAnyRemoval(int changedIndex)
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(_paths[changedIndex], "synthetic modified content");

        Assert.Throws<IOException>(() => new WindowsCleanupService().Execute(_operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Execute_RejectsMissingFileBeforeAnyRemoval(int missingIndex)
    {
        if (!OperatingSystem.IsWindows()) return;
        File.Delete(_paths[missingIndex]);

        Assert.Throws<IOException>(() => new WindowsCleanupService().Execute(_operation));
        Assert.All(_paths.Where((_, index) => index != missingIndex), path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Execute_RejectsIdenticallyChangedFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var path in _paths) File.WriteAllText(path, "synthetic modified content");

        Assert.Throws<IOException>(() => new WindowsCleanupService().Execute(_operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Prepare_HoldsFilesAndParentsAgainstModificationAndReplacementUntilDisposed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (WindowsCleanupSession.Prepare(_operation))
        {
            Assert.All(_paths, path => Assert.Throws<IOException>(() => File.WriteAllText(path, "changed")));
            Assert.All(_paths, path => Assert.Throws<IOException>(() => File.Move(path, path + ".moved")));
            Assert.Throws<IOException>(() => Directory.Move(_root, _root + ".moved"));
        }

        Assert.All(_paths, path => Assert.Equal("synthetic original content", File.ReadAllText(path)));
        File.WriteAllText(_paths[0], "locks released");
    }

    [Fact]
    public void Execute_RejectsMissingScanFingerprint()
    {
        if (!OperatingSystem.IsWindows()) return;
        var operation = CreateOperation(new DuplicateGroup(_paths, new FileInfo(_paths[0]).Length));

        Assert.Throws<IOException>(() => new WindowsCleanupService().Execute(operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Prepare_ReleasesEarlierLocksWhenLaterFileCannotBeLocked()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (var writer = File.Open(_paths[1], FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            Assert.Throws<IOException>(() => WindowsCleanupSession.Prepare(_operation));
        }

        Assert.All(_paths, path => File.WriteAllText(path, "locks released after preflight failure"));
    }

    [Fact]
    public void Execute_RejectsGroupsAboveHandleLimitBeforeRemovingAnything()
    {
        if (!OperatingSystem.IsWindows()) return;
        var paths = Enumerable.Range(0, 130).Select(index => Path.Combine(_root, $"copy-{index}.txt")).ToArray();
        var operation = new DuplicateCleanupOperation(new DuplicateCleanupExecution(new DuplicateCleanupPlan(
            new DuplicateReviewSelection(new DuplicateGroup(paths, 1, "fingerprint"), paths[0]), DuplicateAction.Delete)));

        Assert.Throws<InvalidOperationException>(() => new WindowsCleanupService().Execute(operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Execute_RejectsUnsupportedRecycleActionWithoutRemovingFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var operation = CreateOperation(Assert.Single(new DuplicateDetector().FindDuplicates(_root)), DuplicateAction.MoveToRecycleBin);

        Assert.Throws<InvalidOperationException>(() => new WindowsCleanupService().Execute(operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
