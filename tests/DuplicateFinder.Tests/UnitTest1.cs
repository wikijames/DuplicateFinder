namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;

public class ReviewUxTests
{
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
    public void DuplicateCleanupPlan_RequiresAValidKeepTargetBeforeExecution()
    {
        var group = new DuplicateGroup(new[] { "keep.txt", "remove.txt" }, 64);
        var decision = new DuplicateReviewDecision(group, "remove.txt", DuplicateAction.Delete);
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
}
