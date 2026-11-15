using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

namespace DuplicateFinder.Tests;

public sealed class ResultGroupQueryTests
{
    private readonly DuplicateGroup _documents = new(["archive/report.txt", "work/final.txt"], 30, "documents");
    private readonly DuplicateGroup _images = new(["images/alpha.png", "images/beta.png", "images/gamma.png"], 20, "images");

    [Fact]
    public void SearchMatchesAnyPathAndKeepsEntireGroup()
    {
        var found = ResultGroupQuery.Apply([_documents, _images], " WORK/FINAL ", ResultGroupSort.Name);
        Assert.Same(_documents, Assert.Single(found));
        Assert.Equal(2, found[0].FilePaths.Count);
    }

    [Fact]
    public void SearchWithoutMatchesLeavesOriginalResultsAndKeepChoicesIntact()
    {
        var workspace = new DuplicateReviewWorkspace();
        workspace.Load([_documents, _images]);
        workspace.SelectGroup(_documents);
        workspace.SelectKeepFile("work/final.txt");
        Assert.Empty(ResultGroupQuery.Apply(workspace.Groups, "missing", ResultGroupSort.Name));
        workspace.SelectGroup(null);
        Assert.Null(workspace.CleanupOperation);
        Assert.Equal(2, workspace.Groups.Count);
        workspace.SelectGroup(_documents);
        Assert.Equal("work/final.txt", workspace.Selection!.KeepFilePath);
    }

    [Theory]
    [InlineData(ResultGroupSort.Name, true)]
    [InlineData(ResultGroupSort.ExtraCopySize, true)]
    [InlineData(ResultGroupSort.FileSize, false)]
    [InlineData(ResultGroupSort.CopyCount, true)]
    public void SortingUsesRequestedMetric(ResultGroupSort sort, bool imagesFirst)
    {
        var result = ResultGroupQuery.Apply([_documents, _images], null, sort);
        Assert.Same(imagesFirst ? _images : _documents, result[0]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void EqualMetricsHaveDeterministicPathOrder()
    {
        var later = new DuplicateGroup(["z/one", "z/two"], 10);
        var earlier = new DuplicateGroup(["a/one", "a/two"], 10);
        Assert.Equal([earlier, later], ResultGroupQuery.Apply([later, earlier], "", ResultGroupSort.FileSize));
    }
}
