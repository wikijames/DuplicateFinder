using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

namespace DuplicateFinder.Tests;

public sealed class GroupedReviewRowsTests
{
    private static DuplicateGroup CreateGroup(string name) => new(
        [Path.Combine("original", name), Path.Combine("copy", name)], 42, name);

    [Fact]
    public void ProjectionIncludesEveryGroupAndFileInOrderWithoutChangingActiveCleanup()
    {
        var first = CreateGroup("first.png");
        var second = CreateGroup("second.txt");
        var workspace = new DuplicateReviewWorkspace();
        workspace.Load([first, second]);
        workspace.SelectGroup(first);
        var operation = workspace.CleanupOperation;
        var rows = new GroupedReviewRows([first, second], workspace.GetKeepFilePath);

        Assert.Equal(6, rows.Rows.Count);
        Assert.Same(first, Assert.IsType<ReviewGroupHeader>(rows.Rows[0]).Group);
        Assert.Same(second, Assert.IsType<ReviewGroupHeader>(rows.Rows[3]).Group);
        Assert.Equal(first.FilePaths, rows.Rows.OfType<FileReviewRow>().Where(row => row.Group == first).Select(row => row.FullPath));
        Assert.Same(operation, workspace.CleanupOperation);
        Assert.Same(first, workspace.Selection!.Group);
    }

    [Fact]
    public void KeepChangesUpdateOnlyTheirGroupAndSurviveSearchProjection()
    {
        var first = CreateGroup("first.png");
        var second = CreateGroup("second.png");
        var workspace = new DuplicateReviewWorkspace();
        workspace.Load([first, second]);
        var rows = new GroupedReviewRows([first, second], workspace.GetKeepFilePath);
        var originalRow = rows.FirstFileFor(first);
        workspace.SelectGroup(first);
        workspace.SelectKeepFile(first.FilePaths[1]);
        rows.UpdateKeep(first, workspace.Selection!.KeepFilePath);

        Assert.Same(originalRow, rows.FirstFileFor(first));
        Assert.False(originalRow!.IsKeep);
        Assert.True(rows.FirstFileFor(second)!.IsKeep);
        Assert.All(new[] { first, second }, group =>
            Assert.Single(rows.Rows.OfType<FileReviewRow>(), row => row.Group == group && row.IsKeep));
        var filtered = new GroupedReviewRows([first], workspace.GetKeepFilePath);
        Assert.Equal(first.FilePaths[1], Assert.Single(filtered.Rows.OfType<FileReviewRow>(), row => row.IsKeep).FullPath);
        Assert.Equal([first.FilePaths[0]], workspace.CleanupOperation!.PathsToRemove);
        Assert.Same(first, workspace.Selection.Group);
    }

    [Fact]
    public void LargeProjectionHasLightweightRowsAndHeaderNavigationForEveryGroup()
    {
        var groups = Enumerable.Range(0, 10000).Select(index => CreateGroup($"image-{index}.png")).ToArray();
        var rows = new GroupedReviewRows(groups, group => group.FilePaths[0]);
        Assert.Equal(30000, rows.Rows.Count);
        Assert.Same(groups[^1], rows.HeaderFor(groups[^1])!.Group);
        Assert.Equal(groups[^1].FilePaths[0], rows.FirstFileFor(groups[^1])!.FullPath);
    }
}
