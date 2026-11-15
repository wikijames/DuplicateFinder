using System;
using System.Threading;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

/// <summary>Retains one snapshot instead of queueing work on the UI thread.</summary>
internal sealed class LatestScanProgress : IProgress<ScanProgress>
{
    private ScanProgress? _latest;
    public void Report(ScanProgress value) => Interlocked.Exchange(ref _latest, value);
    public ScanProgress? TakeLatest() => Interlocked.Exchange(ref _latest, null);
}
