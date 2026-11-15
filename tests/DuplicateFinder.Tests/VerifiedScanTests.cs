namespace DuplicateFinder.Tests;

using System.Security.Cryptography;
using System.Text;
using DuplicateFinder.Core;

public sealed class VerifiedScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
    private readonly string[] _paths;
    private readonly string _contents = new('a', 128 * 1024 + 17);

    public VerifiedScanTests()
    {
        Directory.CreateDirectory(_root);
        _paths = [Path.Combine(_root, "a.txt"), Path.Combine(_root, "b.txt"), Path.Combine(_root, "c.txt")];
        foreach (var path in _paths) File.WriteAllText(path, _contents);
    }

    [Fact]
    public void Scan_ReturnsVerifiedGroupsAndTheOriginalContentFingerprint()
    {
        var result = new DuplicateDetector().Scan(_root);

        Assert.True(result.Issues.IsComplete);
        var group = Assert.Single(result.Groups);
        Assert.Equal(3, group.FilePaths.Count);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_contents))), group.ContentHash);
    }

    [Fact]
    public void ForcedHashBucketCollision_ProducesSeparateByteIdenticalGroups()
    {
        File.WriteAllText(_paths[2], _contents[..^1] + "b");
        File.WriteAllText(Path.Combine(_root, "d.txt"), _contents[..^1] + "b");
        var detector = new DuplicateDetector(new FileScanner(), groupHash: _ => "forced grouping collision");

        var result = detector.Scan(_root);

        Assert.True(result.Issues.IsComplete);
        Assert.Equal(2, result.Groups.Count);
        Assert.All(result.Groups, group =>
        {
            Assert.Equal(2, group.FilePaths.Count);
            Assert.Equal(File.ReadAllBytes(group.FilePaths[0]), File.ReadAllBytes(group.FilePaths[1]));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(group.FilePaths[0]))), group.ContentHash);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ChangedContentWithRestoredTimestamp_IsSkippedWhileValidDuplicatesSurvive(int changedIndex)
    {
        var detector = new DuplicateDetector(new FileScanner(), beforeVerification: () =>
        {
            var previousTime = File.GetLastWriteTimeUtc(_paths[changedIndex]);
            File.WriteAllText(_paths[changedIndex], _contents[..^1] + "b");
            File.SetLastWriteTimeUtc(_paths[changedIndex], previousTime);
        });

        var result = detector.Scan(_root);

        Assert.False(result.Issues.IsComplete);
        Assert.Equal(1, result.Issues.SkippedFileCount);
        Assert.Equal(1, result.Issues.ChangedFileCount);
        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.FilePaths.Count);
        Assert.DoesNotContain(_paths[changedIndex], group.FilePaths);
    }

    [Fact]
    public void IdenticalChangesToAllCandidates_AreNotReportedUsingStaleHashes()
    {
        var detector = new DuplicateDetector(new FileScanner(), beforeVerification: () =>
        {
            foreach (var path in _paths) File.WriteAllText(path, _contents[..^1] + "b");
        });

        var result = detector.Scan(_root);

        Assert.Empty(result.Groups);
        Assert.False(result.Issues.IsComplete);
        Assert.True(result.Issues.ChangedFileCount > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DisappearingCandidateDuringVerification_DoesNotDiscardHealthyResults(int missingIndex)
    {
        var detector = new DuplicateDetector(new FileScanner(), beforeVerification: () => File.Delete(_paths[missingIndex]));

        var result = detector.Scan(_root);

        Assert.Equal(1, result.Issues.SkippedFileCount);
        Assert.Equal(0, result.Issues.ChangedFileCount);
        Assert.Equal(2, Assert.Single(result.Groups).FilePaths.Count);
    }

    [Fact]
    public void DisappearingFileAfterDiscovery_IsSkippedDuringHashing()
    {
        IEnumerable<string> DiscoverThenRemove()
        {
            foreach (var path in _paths) yield return path;
            File.Delete(_paths[0]);
        }
        var scanner = new FileScanner((_, _, _) => DiscoverThenRemove());

        var result = new DuplicateDetector(scanner).Scan(_root);

        Assert.Equal(1, result.Issues.SkippedFileCount);
        Assert.Equal(2, Assert.Single(result.Groups).FilePaths.Count);
    }

    [Fact]
    public void LazyEnumerationFailure_PreservesYieldedCandidatesAndReportsAnIncompleteFolder()
    {
        IEnumerable<string> FailingEnumeration()
        {
            yield return _paths[0];
            yield return _paths[1];
            throw new IOException("synthetic lazy enumeration failure");
        }
        var scanner = new FileScanner((_, _, _) => FailingEnumeration());

        var result = new DuplicateDetector(scanner).Scan(_root);

        Assert.Equal(1, result.Issues.SkippedDirectoryCount);
        Assert.False(result.Issues.IsComplete);
        Assert.Equal(2, Assert.Single(result.Groups).FilePaths.Count);
    }

    [Fact]
    public void InaccessibleDirectoryEnumeration_DoesNotSilentlyReportAnEmptyCompleteScan()
    {
        var scanner = new FileScanner((_, _, _) => throw new UnauthorizedAccessException("synthetic access denial"));

        var result = new DuplicateDetector(scanner).Scan(_root);

        Assert.Empty(result.Groups);
        Assert.False(result.Issues.IsComplete);
        Assert.Equal(1, result.Issues.SkippedDirectoryCount);
    }

    [Fact]
    public void MetadataFailureForAMissingEntry_IsCountedWithoutDiscardingOtherFiles()
    {
        var missing = Path.Combine(_root, "missing.txt");
        var scanner = new FileScanner((_, _, _) => _paths.Prepend(missing));

        var result = new DuplicateDetector(scanner).Scan(_root);

        Assert.Equal(1, result.Issues.SkippedFileCount);
        Assert.Equal(3, Assert.Single(result.Groups).FilePaths.Count);
    }

    [Fact]
    public void LockedFile_DoesNotAbortTheRemainingScan()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var lockedFile = File.Open(_paths[0], FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = new DuplicateDetector().Scan(_root);

        Assert.Equal(1, result.Issues.SkippedFileCount);
        Assert.Equal(2, Assert.Single(result.Groups).FilePaths.Count);
    }

    [Fact]
    public void CancellationBeforeVerification_PropagatesInsteadOfReturningPartialSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var detector = new DuplicateDetector(new FileScanner(), beforeVerification: cancellation.Cancel);

        Assert.ThrowsAny<OperationCanceledException>(() => detector.Scan(_root, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void IncompleteEmptyReport_UsesWarningCountsInsteadOfNoDuplicatesSuccess()
    {
        var summary = new DuplicateScanSummary(_root, [], new ScanIssueSummary(2, 1, 1));
        var report = new DuplicateScanReport(_root, summary);

        Assert.False(summary.IsComplete);
        Assert.StartsWith("Incomplete scan:", report.StatusMessage);
        Assert.Contains("Skipped 2 file(s) and 1 folder(s)", report.StatusMessage);
        Assert.DoesNotContain("No duplicate files found", report.StatusMessage);
        Assert.DoesNotContain(_root, report.StatusMessage);
    }

    [Fact]
    public void EmptyFiles_AreVerifiedAsExactDuplicates()
    {
        foreach (var path in _paths) File.WriteAllText(path, "");

        var result = new DuplicateDetector().Scan(_root);

        Assert.True(result.Issues.IsComplete);
        Assert.Equal(3, Assert.Single(result.Groups).FilePaths.Count);
    }

    [Fact]
    public void ChangedLengthDuringVerification_IsSkippedWithoutDiscardingHealthyDuplicates()
    {
        var detector = new DuplicateDetector(new FileScanner(), beforeVerification: () => File.AppendAllText(_paths[0], "changed"));

        var result = detector.Scan(_root);

        Assert.Equal(1, result.Issues.ChangedFileCount);
        Assert.Equal(2, Assert.Single(result.Groups).FilePaths.Count);
    }

    [Fact]
    public void IncludeHidden_ActuallyIncludesWindowsHiddenAttributeFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        File.SetAttributes(_paths[0], File.GetAttributes(_paths[0]) | FileAttributes.Hidden);

        var visibleOnly = new DuplicateDetector().Scan(_root);
        var includingHidden = new DuplicateDetector().Scan(_root, new ScanOptions { IncludeHidden = true });

        Assert.Equal(2, Assert.Single(visibleOnly.Groups).FilePaths.Count);
        Assert.Equal(3, Assert.Single(includingHidden.Groups).FilePaths.Count);
        Assert.True(visibleOnly.Issues.IsComplete);
        Assert.True(includingHidden.Issues.IsComplete);
    }

    [Fact]
    public void ChangedFileSummary_AlwaysMarksTheReportIncomplete()
    {
        var summary = new DuplicateScanSummary(_root, [], new ScanIssueSummary(0, 0, 1));

        Assert.False(summary.IsComplete);
        Assert.StartsWith("Incomplete scan:", new DuplicateScanReport(_root, summary).StatusMessage);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
