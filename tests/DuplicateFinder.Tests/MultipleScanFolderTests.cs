namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class MultipleScanFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _first;
    private readonly string _second;

    public MultipleScanFolderTests()
    {
        _first = Path.Combine(_root, "first");
        _second = Path.Combine(_root, "second");
        Directory.CreateDirectory(_first);
        Directory.CreateDirectory(_second);
        File.WriteAllText(Path.Combine(_first, "original.bin"), "same");
        File.WriteAllText(Path.Combine(_second, "copy.bin"), "same");
    }

    [Fact]
    public void CombinedScan_FindsDuplicatesThatAreUniqueWithinEachFolder()
    {
        var report = new DuplicateScanCoordinator().CreateReport(new[] { _first, _second });

        Assert.Equal(2, report.Summary.RootPaths.Count);
        Assert.Equal(2, Assert.Single(report.Summary.Groups).FilePaths.Count);
        Assert.Equal(4, report.Summary.TotalReclaimableBytes);
        Assert.True(report.Summary.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParentChildAndDuplicateRoots_AreScannedOnceRegardlessOfOrder(bool parentLast)
    {
        var nested = Path.Combine(_first, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "copy.bin"), "same");
        var roots = parentLast ? new[] { nested, _first, _first + Path.DirectorySeparatorChar } : new[] { _first, nested, _first };

        var report = new DuplicateScanCoordinator().CreateReport(roots);

        Assert.Equal(new[] { _first }, report.Summary.RootPaths);
        Assert.Equal(2, Assert.Single(report.Summary.Groups).FilePaths.Count);
        Assert.Equal(4, report.Summary.TotalReclaimableBytes);
    }

    [Fact]
    public void SimilarFolderPrefixes_AreNotTreatedAsOverlapping()
    {
        var roots = ScanRoots.Normalize(new[] { _first, _first + "-other" });

        Assert.Equal(2, roots.Count);
    }

    [Fact]
    public void RootCaseHandling_FollowsPlatformPathComparison()
    {
        var roots = ScanRoots.Normalize(new[] { _first, _first.ToUpperInvariant() });

        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, roots.Count);
    }

    [Fact]
    public void TooManyIndependentRoots_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => ScanRoots.Normalize(Enumerable.Range(0, ScanRoots.MaximumRootCount + 1)
            .Select(index => Path.Combine(_root, $"folder-{index}"))));
    }

    [Fact]
    public void ParentRootCanCollapseManyRedundantInputsBeforeLimitIsApplied()
    {
        var roots = ScanRoots.Normalize(Enumerable.Range(0, 70).Select(index => Path.Combine(_first, $"child-{index}")).Append(_first));

        Assert.Equal(new[] { _first }, roots);
    }

    [Fact]
    public void RootDisappearingAfterValidation_MarksPartialScanAndPreservesHealthyGroup()
    {
        File.WriteAllText(Path.Combine(_first, "local-copy.bin"), "same");
        IEnumerable<string> EnumerateThenRemoveOtherRoot(string path, string pattern, EnumerationOptions options)
        {
            foreach (var file in Directory.EnumerateFiles(path, pattern, options)) yield return file;
            if (path == _first) Directory.Delete(_second, recursive: true);
        }
        var detector = new DuplicateDetector(new FileScanner(EnumerateThenRemoveOtherRoot));

        var result = detector.Scan(new[] { _first, _second });

        Assert.False(result.Issues.IsComplete);
        Assert.Equal(1, result.Issues.SkippedDirectoryCount);
        Assert.Equal(2, Assert.Single(result.Groups).FilePaths.Count);
    }

    [Fact]
    public void RepeatedDiscoveryPaths_CannotCreateDuplicatesOfOneFile()
    {
        var original = Path.Combine(_first, "original.bin");
        var detector = new DuplicateDetector(new FileScanner((_, _, _) => new[] { original, original }));

        Assert.Empty(detector.Scan(_first).Groups);
    }

    [Fact]
    public void InvalidFolderBatch_DoesNotModifyTheExistingSelection()
    {
        var selection = new ScanFolderSelection();
        selection.Add(new[] { _first });

        Assert.Throws<DirectoryNotFoundException>(() => selection.Add(new[] { _second, Path.Combine(_root, "missing") }));
        Assert.Equal(new[] { _first }, selection.Folders);
    }

    [Fact]
    public void FileInput_IsRejectedAndDoesNotBecomeAScanRoot()
    {
        var selection = new ScanFolderSelection();

        Assert.Throws<DirectoryNotFoundException>(() => selection.Add(new[] { Path.Combine(_first, "original.bin") }));
        Assert.Empty(selection.Folders);
    }

    [Fact]
    public void QuarantineDirectories_CannotBeAddedAsDesktopScanRoots()
    {
        var vault = Path.Combine(_root, QuarantineService.FolderName);
        Directory.CreateDirectory(vault);
        var selection = new ScanFolderSelection();

        Assert.Throws<ArgumentException>(() => selection.Add(new[] { vault }));
        Assert.Empty(selection.Folders);
    }

    [Fact]
    public void RemovingOrClearingSelection_DoesNotRemoveFoldersOrFiles()
    {
        var selection = new ScanFolderSelection();
        selection.Add(new[] { _first, _second });
        selection.Remove(_first);
        Assert.Equal(new[] { _second }, selection.Folders);
        selection.Clear();

        Assert.Empty(selection.Folders);
        Assert.True(File.Exists(Path.Combine(_first, "original.bin")));
        Assert.True(File.Exists(Path.Combine(_second, "copy.bin")));
    }

    [Fact]
    public void MultiRootCancellation_PropagatesInsteadOfReturningSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new DuplicateDetector().Scan(new[] { _first, _second }, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CrossDriveQuarantine_IsReviewOnlyAndDoesNotForcePermanentDeletion()
    {
        if (!OperatingSystem.IsWindows()) return;
        var group = new DuplicateGroup(new[] { @"C:\synthetic\keep.txt", @"D:\synthetic\copy.txt" }, 4, "scan-fingerprint");
        var workspace = new DuplicateReviewWorkspace();
        workspace.Load(new[] { group });
        workspace.SelectGroup(group);

        Assert.Null(workspace.CleanupOperation);
        Assert.Contains("same drive", workspace.CleanupUnavailableReason!);
        Assert.Equal(DuplicateAction.Quarantine, workspace.CleanupAction);
        workspace.SetCleanupAction(DuplicateAction.Delete);
        Assert.Equal(DuplicateAction.Delete, workspace.CleanupOperation!.Action);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
