namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;

public class ScanReportingTests
{
    [Fact]
    public void CreateReport_PropagatesCancellationInsteadOfReportingNoDuplicates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var coordinator = new DuplicateScanCoordinator();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            coordinator.CreateReport("unused-synthetic-root", cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CreateReport_PropagatesMissingDirectoryInsteadOfReportingNoDuplicates()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        var coordinator = new DuplicateScanCoordinator();

        Assert.Throws<DirectoryNotFoundException>(() => coordinator.CreateReport(missingRoot));
    }
}
