using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

namespace DuplicateFinder.Tests;

public sealed class ScanProgressTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder-Progress-" + Guid.NewGuid().ToString("N"));
    public ScanProgressTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ProgressCountsActualWorkAcrossAllPasses()
    {
        File.WriteAllBytes(Path.Combine(_root, "first.txt"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_root, "second.txt"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_root, "unique.txt"), [1, 2, 3, 4]);
        var observer = new RecordingProgress();
        var summary = new DuplicateScanCoordinator().Scan(_root, progress: observer);
        Assert.Single(summary.Groups);
        Assert.Equal(new ScanProgress(ScanStage.Completed, 3, 2, 2, 1, 18), observer.Values[^1]);
        Assert.Equal(ScanStage.Discovering, observer.Values[0].Stage);
        Assert.Contains(observer.Values, value => value.Stage == ScanStage.Sampling);
        Assert.Contains(observer.Values, value => value.Stage == ScanStage.Hashing);
        Assert.Contains(observer.Values, value => value.Stage == ScanStage.Verifying);
        for (var index = 1; index < observer.Values.Count; index++)
        {
            Assert.True(observer.Values[index].BytesRead >= observer.Values[index - 1].BytesRead);
            Assert.True(observer.Values[index].DiscoveredFiles >= observer.Values[index - 1].DiscoveredFiles);
        }
    }

    [Fact]
    public void EmptyScanCompletesWithZeroCounters()
    {
        var observer = new RecordingProgress();
        new DuplicateScanCoordinator().CreateReport(new[] { _root }, progress: observer);
        Assert.Equal(new ScanProgress(ScanStage.Completed, 0, 0, 0, 0, 0), observer.Values[^1]);
    }

    [Fact]
    public void EmptyDuplicatesHaveNoBytesButAreVerified()
    {
        File.WriteAllBytes(Path.Combine(_root, "first"), []);
        File.WriteAllBytes(Path.Combine(_root, "second"), []);
        var observer = new RecordingProgress();
        new DuplicateDetector().Scan(_root, progress: observer);
        Assert.Equal(new ScanProgress(ScanStage.Completed, 2, 2, 2, 1, 0), observer.Values[^1]);
    }

    [Fact]
    public void CancelledScanDoesNotReportCompletion()
    {
        File.WriteAllBytes(Path.Combine(_root, "first"), [1]);
        File.WriteAllBytes(Path.Combine(_root, "second"), [1]);
        using var cancellation = new CancellationTokenSource();
        var observer = new RecordingProgress(value => { if (value.Stage == ScanStage.Hashing) cancellation.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => new DuplicateDetector().Scan(_root, cancellationToken: cancellation.Token, progress: observer));
        Assert.DoesNotContain(observer.Values, value => value.Stage == ScanStage.Completed);
    }

    [Fact]
    public void ObserverDoesNotChangeDuplicateResults()
    {
        File.WriteAllBytes(Path.Combine(_root, "first"), [1, 2]);
        File.WriteAllBytes(Path.Combine(_root, "second"), [1, 2]);
        var detector = new DuplicateDetector();
        var without = detector.Scan(_root);
        var with = detector.Scan(_root, progress: new RecordingProgress());
        Assert.Equal(without.Groups[0].FilePaths, with.Groups[0].FilePaths);
        Assert.Equal(without.Groups[0].ContentHash, with.Groups[0].ContentHash);
        Assert.Equal(without.Issues, with.Issues);
    }

    [Fact]
    public void UiMailboxRetainsOnlyLatestSnapshot()
    {
        var mailbox = new LatestScanProgress();
        for (var index = 0; index < 10000; index++) mailbox.Report(new ScanProgress(ScanStage.Discovering, index, 0, 0, 0, 0));
        Assert.Equal(9999, mailbox.TakeLatest()!.DiscoveredFiles);
        Assert.Null(mailbox.TakeLatest());
    }

    private sealed class RecordingProgress(Action<ScanProgress>? callback = null) : IProgress<ScanProgress>
    {
        public List<ScanProgress> Values { get; } = [];
        public void Report(ScanProgress value) { Values.Add(value); callback?.Invoke(value); }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
