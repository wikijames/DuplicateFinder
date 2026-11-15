namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;

public class CleanupReviewTests
{
    [Theory]
    [InlineData("first.txt", DuplicateAction.Delete)]
    [InlineData("second.txt", DuplicateAction.Delete)]
    [InlineData("third.txt", DuplicateAction.Delete)]
    [InlineData("third.txt", DuplicateAction.MoveToRecycleBin)]
    [InlineData("third.txt", DuplicateAction.Quarantine)]
    public void ReviewPlan_PreservesChosenKeepFileAndAllDisplayedRemovalTargets(string keepPath, DuplicateAction action)
    {
        var group = new DuplicateGroup(new[] { "first.txt", "second.txt", "third.txt" }, 32, "scan-fingerprint");
        var selection = new DuplicateReviewSelection(group, keepPath);
        var plan = new DuplicateCleanupPlan(selection, action);
        var operation = new DuplicateCleanupAdapter().CreateOperation(new DuplicateCleanupExecutor().CreateExecution(plan));

        Assert.True(operation.CanExecute);
        Assert.Equal(action, operation.Action);
        Assert.Equal("scan-fingerprint", operation.ContentHash);
        Assert.Equal(32, operation.SizeBytes);
        Assert.Equal(keepPath, operation.KeepPath);
        Assert.Equal(selection.RemoveFilePaths, operation.PathsToRemove);
        Assert.Equal(2, operation.PathsToRemove.Count);
        Assert.DoesNotContain(keepPath, operation.PathsToRemove);
    }

    [Theory]
    [InlineData(DuplicateAction.Keep)]
    [InlineData(DuplicateAction.Ignore)]
    public void ReviewPlan_RejectsActionsThatDoNotRemoveFiles(DuplicateAction action)
    {
        var selection = new DuplicateReviewSelection(new DuplicateGroup(new[] { "first.txt", "second.txt" }, 32), "first.txt");

        Assert.Throws<ArgumentOutOfRangeException>(() => new DuplicateCleanupPlan(selection, action));
    }
}
