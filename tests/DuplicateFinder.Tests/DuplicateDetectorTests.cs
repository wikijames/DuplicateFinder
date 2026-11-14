namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;

public class DuplicateDetectorTests
{
    [Fact]
    public void FindDuplicates_GroupsExactDuplicateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var original = Path.Combine(root, "original.txt");
        var copy = Path.Combine(root, "copy.txt");
        var differentContent = Path.Combine(root, "different.txt");

        File.WriteAllText(original, "same-content");
        File.WriteAllText(copy, "same-content");
        File.WriteAllText(differentContent, "different-content");

        try
        {
            var detector = new DuplicateDetector();
            var groups = detector.FindDuplicates(root, new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" }
            });

            Assert.Single(groups);
            Assert.Equal(2, groups[0].FilePaths.Count);
            Assert.Contains(original, groups[0].FilePaths);
            Assert.Contains(copy, groups[0].FilePaths);
            Assert.Equal(12, groups[0].SizeBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindDuplicates_UsesSizeAndExtensionFilters()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var duplicateOne = Path.Combine(root, "one.txt");
        var duplicateTwo = Path.Combine(root, "two.txt");
        var outOfRange = Path.Combine(root, "large.md");

        File.WriteAllText(duplicateOne, "alpha");
        File.WriteAllText(duplicateTwo, "alpha");
        File.WriteAllText(outOfRange, "very-large-content");

        try
        {
            var detector = new DuplicateDetector();
            var groups = detector.FindDuplicates(root, new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" },
                MinimumSizeBytes = 2,
                MaximumSizeBytes = 20
            });

            Assert.Single(groups);
            Assert.Equal(2, groups[0].FilePaths.Count);
            Assert.Contains(duplicateOne, groups[0].FilePaths);
            Assert.Contains(duplicateTwo, groups[0].FilePaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindDuplicates_DoesNotGroupFilesWithSamePrefixButDifferentContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var first = Path.Combine(root, "first.txt");
        var second = Path.Combine(root, "second.txt");

        File.WriteAllText(first, "same-prefix-1234567890-unique-a");
        File.WriteAllText(second, "same-prefix-1234567890-unique-b");

        try
        {
            var detector = new DuplicateDetector();
            var groups = detector.FindDuplicates(root, new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" }
            });

            Assert.Empty(groups);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FindDuplicates_StopsWhenCancellationIsRequestedDuringHashing()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var duplicateOne = Path.Combine(root, "one.bin");
        var duplicateTwo = Path.Combine(root, "two.bin");
        var bytes = new byte[128 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(duplicateOne, bytes);
        await File.WriteAllBytesAsync(duplicateTwo, bytes);

        using var cts = new CancellationTokenSource();
        var cancellationTask = Task.Run(() =>
        {
            Thread.Sleep(20);
            cts.Cancel();
        });

        try
        {
            var detector = new DuplicateDetector();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() =>
                detector.FindDuplicates(root, new ScanOptions { AllowedExtensions = new[] { ".bin" } }, cts.Token), cts.Token));
        }
        finally
        {
            await cancellationTask;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DuplicateGroup_ReportsHumanReadableMetadata()
    {
        var group = new DuplicateGroup(new[] { "a.txt", "b.txt" }, 32);

        Assert.Equal(2, group.FilePaths.Count);
        Assert.Equal(32, group.SizeBytes);
        Assert.Equal(32, group.ReclaimableBytes);
        Assert.Contains("a.txt", group.FilePaths);
        Assert.Contains("b.txt", group.FilePaths);
    }

    [Fact]
    public void DuplicateReviewDecision_CapturesUserIntentWithoutMutatingFiles()
    {
        var group = new DuplicateGroup(new[] { "keep.txt", "remove.txt" }, 64);
        var decision = new DuplicateReviewDecision(group, "keep.txt", DuplicateAction.Keep);

        Assert.Equal(group, decision.Group);
        Assert.Equal("keep.txt", decision.SelectedFilePath);
        Assert.Equal(DuplicateAction.Keep, decision.Action);
        Assert.Equal(64, decision.Group.SizeBytes);
    }

    [Fact]
    public void DuplicateCleanupPlan_PreparesSafeRemovalTargets()
    {
        var group = new DuplicateGroup(new[] { "keep.txt", "remove.txt" }, 64);
        var decision = new DuplicateReviewDecision(group, "keep.txt", DuplicateAction.Keep);

        var plan = new DuplicateCleanupPlan(decision);

        Assert.True(plan.CanExecute);
        Assert.Equal("keep.txt", plan.KeepPath);
        Assert.Single(plan.PathsToRemove);
        Assert.Contains("remove.txt", plan.PathsToRemove);
    }

    [Fact]
    public void DuplicateCleanupPlan_RejectsSingleFileGroupsForDestructiveCleanup()
    {
        var group = new DuplicateGroup(new[] { "only.txt" }, 64);
        var decision = new DuplicateReviewDecision(group, "only.txt", DuplicateAction.Delete);

        var plan = new DuplicateCleanupPlan(decision);

        Assert.False(plan.CanExecute);
        Assert.Equal("only.txt", plan.KeepPath);
        Assert.Empty(plan.PathsToRemove);
    }

    [Fact]
    public void DuplicateCleanupExecutor_PreparesExecutionRequest()
    {
        var group = new DuplicateGroup(new[] { "keep.txt", "remove.txt" }, 64);
        var decision = new DuplicateReviewDecision(group, "remove.txt", DuplicateAction.Delete);
        var plan = new DuplicateCleanupPlan(decision);

        var executor = new DuplicateCleanupExecutor();
        var execution = executor.CreateExecution(plan);

        Assert.Equal(DuplicateAction.Delete, execution.Action);
        Assert.Equal("keep.txt", execution.KeepPath);
        Assert.Single(execution.PathsToRemove);
        Assert.Contains("remove.txt", execution.PathsToRemove);
        Assert.True(execution.CanExecute);
    }

    [Fact]
    public void DuplicateCleanupAdapter_MapsDeleteAndRecycleIntentSafely()
    {
        var group = new DuplicateGroup(new[] { "keep.txt", "remove.txt" }, 64);
        var decision = new DuplicateReviewDecision(group, "remove.txt", DuplicateAction.MoveToRecycleBin);
        var plan = new DuplicateCleanupPlan(decision);
        var execution = new DuplicateCleanupExecutor().CreateExecution(plan);

        var adapter = new DuplicateCleanupAdapter();
        var operation = adapter.CreateOperation(execution);

        Assert.Equal(DuplicateAction.MoveToRecycleBin, operation.Action);
        Assert.Equal("keep.txt", operation.KeepPath);
        Assert.Contains("remove.txt", operation.PathsToRemove);
        Assert.True(operation.CanExecute);
    }

    [Fact]
    public void WindowsCleanupStrategy_MapsDeleteAndRecycleOperations()
    {
        var strategy = new WindowsCleanupStrategy();

        var deleteOperation = strategy.CreateAction(new DuplicateCleanupOperation(
            new DuplicateCleanupExecution(new DuplicateCleanupPlan(
                new DuplicateReviewDecision(new DuplicateGroup(new[] { "keep.txt", "remove.txt" }, 64), "remove.txt", DuplicateAction.Delete)))));

        var recycleOperation = strategy.CreateAction(new DuplicateCleanupOperation(
            new DuplicateCleanupExecution(new DuplicateCleanupPlan(
                new DuplicateReviewDecision(new DuplicateGroup(new[] { "keep.txt", "remove.txt" }, 64), "remove.txt", DuplicateAction.MoveToRecycleBin)))));

        Assert.Equal("Delete", deleteOperation.OperationName);
        Assert.Equal("RecycleBin", recycleOperation.OperationName);
        Assert.Equal("keep.txt", deleteOperation.KeepPath);
        Assert.Equal("keep.txt", recycleOperation.KeepPath);
    }

    [Fact]
    public void DuplicateScanCoordinator_CreatesPortableSummaryForDesktopDisplays()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var original = Path.Combine(root, "original.txt");
        var duplicate = Path.Combine(root, "duplicate.txt");
        var unique = Path.Combine(root, "unique.txt");

        File.WriteAllText(original, "same-content");
        File.WriteAllText(duplicate, "same-content");
        File.WriteAllText(unique, "different");

        try
        {
            var coordinator = new DuplicateScanCoordinator();
            var summary = coordinator.Scan(root, new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" }
            });

            Assert.Equal(root, summary.RootPath);
            Assert.Equal(1, summary.TotalDuplicateGroups);
            Assert.Equal(2, summary.TotalDuplicateFiles);
            Assert.Equal(12, summary.TotalReclaimableBytes);
            Assert.Single(summary.Groups);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DuplicateScanCoordinator_ReportsEmptyResultsWhenNoDuplicatesExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var uniqueOne = Path.Combine(root, "one.txt");
        var uniqueTwo = Path.Combine(root, "two.txt");

        File.WriteAllText(uniqueOne, "first");
        File.WriteAllText(uniqueTwo, "second");

        try
        {
            var coordinator = new DuplicateScanCoordinator();
            var summary = coordinator.Scan(root, new ScanOptions
            {
                AllowedExtensions = new[] { ".txt" }
            });

            Assert.Equal(root, summary.RootPath);
            Assert.Equal(0, summary.TotalDuplicateGroups);
            Assert.Equal(0, summary.TotalDuplicateFiles);
            Assert.Equal(0, summary.TotalReclaimableBytes);
            Assert.Empty(summary.Groups);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DuplicateScanCoordinator_CreatesClearReportStatusForEmptyAndDuplicateScans()
    {
        var emptyRoot = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);
        File.WriteAllText(Path.Combine(emptyRoot, "one.txt"), "first");

        var duplicateRoot = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(duplicateRoot);
        var original = Path.Combine(duplicateRoot, "original.txt");
        var copy = Path.Combine(duplicateRoot, "copy.txt");
        File.WriteAllText(original, "same-content");
        File.WriteAllText(copy, "same-content");

        try
        {
            var coordinator = new DuplicateScanCoordinator();
            var emptyReport = coordinator.CreateReport(emptyRoot, new ScanOptions { AllowedExtensions = new[] { ".txt" } });
            var duplicateReport = coordinator.CreateReport(duplicateRoot, new ScanOptions { AllowedExtensions = new[] { ".txt" } });

            Assert.Equal("No duplicate files found in this folder.", emptyReport.StatusMessage);
            Assert.Equal("Found 1 duplicate group(s).", duplicateReport.StatusMessage);
            Assert.True(duplicateReport.Summary.HasDuplicates);
            Assert.False(emptyReport.Summary.HasDuplicates);
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
            Directory.Delete(duplicateRoot, recursive: true);
        }
    }

    [Fact]
    public void DuplicateReviewSelection_UsesSelectedKeepPathAndExcludesItFromRemovalTargets()
    {
        var group = new DuplicateGroup(new[] { "keep.txt", "remove-one.txt", "remove-two.txt" }, 64);
        var selection = new DuplicateReviewSelection(group, "keep.txt");

        Assert.Equal("keep.txt", selection.KeepFilePath);
        Assert.Equal(2, selection.RemoveFilePaths.Count);
        Assert.Contains("remove-one.txt", selection.RemoveFilePaths);
        Assert.Contains("remove-two.txt", selection.RemoveFilePaths);
        Assert.DoesNotContain("keep.txt", selection.RemoveFilePaths);
    }

    [Fact]
    public void DuplicateReviewSelection_CreatesCleanupDecisionFromSelectedKeepPath()
    {
        var group = new DuplicateGroup(new[] { "keep.txt", "remove-one.txt", "remove-two.txt" }, 64);
        var selection = new DuplicateReviewSelection(group, "keep.txt");

        var decision = selection.ToDecision(DuplicateAction.Delete);

        Assert.Equal(DuplicateAction.Delete, decision.Action);
        Assert.Equal("remove-one.txt", decision.SelectedFilePath);
        Assert.Contains("remove-one.txt", selection.RemoveFilePaths);
        Assert.Contains("remove-two.txt", selection.RemoveFilePaths);

        var plan = new DuplicateCleanupPlan(decision);
        Assert.Equal("keep.txt", plan.KeepPath);
        Assert.Single(plan.PathsToRemove);
        Assert.Contains("remove-one.txt", plan.PathsToRemove);
    }

    [Fact]
    public void DuplicateCleanupExecution_RequiresExplicitConfirmationBeforeDestructiveAction()
    {
        var group = new DuplicateGroup(new[] { "keep.txt", "remove.txt" }, 64);
        var decision = new DuplicateReviewDecision(group, "keep.txt", DuplicateAction.Delete);
        var plan = new DuplicateCleanupPlan(decision);
        var execution = new DuplicateCleanupExecutor().CreateExecution(plan);

        Assert.True(execution.CanExecute);
        Assert.Equal(DuplicateAction.Delete, execution.Action);
        Assert.Single(execution.PathsToRemove);
        Assert.Contains("keep.txt", execution.PathsToRemove);
        Assert.Equal("remove.txt", execution.KeepPath);
    }
}
