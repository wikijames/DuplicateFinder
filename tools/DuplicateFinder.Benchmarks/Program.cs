using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DuplicateFinder.Core;

if (args.Length is < 1 or > 2 ||
    args[0] is not ("small-duplicates" or "mostly-unique" or "large-duplicates" or "cancellation") ||
    (args.Length == 2 && args[1] is not ("smoke" or "standard")))
{
    Console.Error.WriteLine("Usage: DuplicateFinder.Benchmarks <small-duplicates|mostly-unique|large-duplicates|cancellation> [smoke|standard]");
    return 2;
}

var scenario = args[0];
var scale = args.Length == 2 ? args[1] : "smoke";
var small = scenario is "small-duplicates" or "mostly-unique";
var fileCount = small ? (scale == "smoke" ? 200 : 2000) : 3;
var fileSize = small ? 4096 : (scale == "smoke" ? 4 * 1024 * 1024 : 32 * 1024 * 1024);
var fixtureRoot = Path.Combine(Path.GetTempPath(), "DuplicateFinder-Benchmark-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtureRoot);
try
{
    // Everything created and removed by this tool is inside this newly generated directory.
    var warmup = Path.Combine(fixtureRoot, "warmup");
    Directory.CreateDirectory(warmup);
    File.WriteAllBytes(Path.Combine(warmup, "first.bin"), new byte[1024]);
    File.WriteAllBytes(Path.Combine(warmup, "second.bin"), new byte[1024]);
    var coordinator = new DuplicateScanCoordinator();
    coordinator.Scan(warmup);

    var data = Path.Combine(fixtureRoot, "data");
    Directory.CreateDirectory(data);
    var buffer = new byte[64 * 1024];
    for (var index = 0; index < fileCount; index++)
    {
        // Seed per file only for the unique scenario. Others contain identical deterministic bytes.
        new Random(scenario == "mostly-unique" ? index : 42).NextBytes(buffer);
        using var file = new FileStream(Path.Combine(data, $"fixture-{index:D5}.bin"), FileMode.CreateNew, FileAccess.Write);
        for (var remaining = fileSize; remaining > 0; remaining -= Math.Min(buffer.Length, remaining))
            file.Write(buffer.AsSpan(0, Math.Min(buffer.Length, remaining)));
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    using var cancellation = new CancellationTokenSource();
    long cancellationRequested = 0;
    await using var cancelTimer = new Timer(_ =>
    {
        Interlocked.Exchange(ref cancellationRequested, Stopwatch.GetTimestamp());
        cancellation.Cancel();
    }, null, Timeout.Infinite, Timeout.Infinite);
    var observer = new BenchmarkProgress(() => cancelTimer.Change(1, Timeout.Infinite), scenario == "cancellation");
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var collectionsBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
    var started = Stopwatch.GetTimestamp();
    DuplicateScanSummary? summary = null;
    var cancelled = false;
    try { summary = coordinator.Scan(data, cancellationToken: cancellation.Token, progress: observer); }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
    var finished = Stopwatch.GetTimestamp();
    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var elapsed = Stopwatch.GetElapsedTime(started, finished);
    await cancelTimer.DisposeAsync();
    var requested = Interlocked.Read(ref cancellationRequested);
    var expectedGroups = scenario == "mostly-unique" ? 0 : 1;
    var valid = scenario == "cancellation" ? cancelled :
        summary is { IsComplete: true } && summary.TotalDuplicateGroups == expectedGroups &&
        summary.TotalReclaimableBytes == (expectedGroups == 0 ? 0 : (long)(fileCount - 1) * fileSize);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Scenario = scenario, Scale = scale, Runtime = RuntimeInformation.FrameworkDescription,
        Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        FileCount = fileCount, FileSizeBytes = fileSize, DatasetBytes = (long)fileCount * fileSize,
        ElapsedMilliseconds = Math.Round(elapsed.TotalMilliseconds, 3),
        DatasetMiBPerSecond = cancelled ? (double?)null : Math.Round((long)fileCount * fileSize / 1048576d / elapsed.TotalSeconds, 3),
        ScanAllocatedBytes = allocatedBytes,
        ScanGcCollections = Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - collectionsBefore[generation]).ToArray(),
        ProcessLifetimePeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
        Cancelled = cancelled,
        CancellationObservationMilliseconds = cancelled && requested != 0 ? (double?)Math.Round(Stopwatch.GetElapsedTime(requested, finished).TotalMilliseconds, 3) : null,
        DuplicateGroups = summary?.TotalDuplicateGroups,
        LastReportedProgress = observer.Latest,
        ValidationPassed = valid
    }));
    return valid ? 0 : 1;
}
catch (Exception exception)
{
    // Exception messages and stack traces can contain user paths; do not export them.
    Console.Error.WriteLine($"Benchmark failed ({exception.GetType().Name}).");
    return 1;
}
finally
{
    var resolved = Path.GetFullPath(fixtureRoot);
    var parent = Path.GetFullPath(Path.GetTempPath());
    if (string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(parent),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
        Path.GetFileName(resolved).StartsWith("DuplicateFinder-Benchmark-", StringComparison.Ordinal))
    {
        try { Directory.Delete(resolved, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Synthetic fixture cleanup failed ({exception.GetType().Name}).");
            Environment.Exit(1);
        }
    }
}

sealed class BenchmarkProgress(Action requestCancellation, bool cancel) : IProgress<ScanProgress>
{
    private bool _armed;
    public ScanProgress? Latest { get; private set; }
    public void Report(ScanProgress value)
    {
        Latest = value;
        if (cancel && !_armed && value.Stage == ScanStage.Hashing)
        {
            _armed = true;
            requestCancellation();
        }
    }
}
