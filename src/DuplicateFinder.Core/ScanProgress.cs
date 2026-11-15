namespace DuplicateFinder.Core;

public enum ScanStage { Discovering, Sampling, Hashing, Verifying, Completed }

/// <summary>Cumulative work performed, not a completion estimate. Bytes include repeated reads.</summary>
public sealed record ScanProgress(ScanStage Stage, long DiscoveredFiles, long SampledFiles, long HashedFiles,
    long VerifiedPairs, long BytesRead);

internal sealed class ScanProgressTracker(IProgress<ScanProgress>? observer)
{
    private ScanStage _stage;
    private long _discovered, _sampled, _hashed, _verified, _bytes;
    private long _lastReport;

    public void Stage(ScanStage stage) { _stage = stage; Publish(force: true); }
    public void Discovered() { _discovered++; Publish(); }
    public void Sampled() { _sampled++; Publish(); }
    public void Hashed() { _hashed++; Publish(); }
    public void Verified() { _verified++; Publish(); }
    public void Read(int count) { _bytes += count; Publish(); }

    private void Publish(bool force = false)
    {
        if (observer is null) return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!force && System.Diagnostics.Stopwatch.GetElapsedTime(_lastReport, now).TotalMilliseconds < 150) return;
        _lastReport = now;
        observer.Report(new ScanProgress(_stage, _discovered, _sampled, _hashed, _verified, _bytes));
    }
}
