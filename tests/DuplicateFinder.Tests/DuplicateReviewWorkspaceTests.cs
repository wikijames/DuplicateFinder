namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class DuplicateReviewWorkspaceTests
{
    private readonly DuplicateGroup _documents = new(["draft.txt", "saved.txt", "working.txt"], 64, "document-fingerprint");
    private readonly DuplicateGroup _images = new(["first.jpg", "second.jpg"], 128, "image-fingerprint");

    [Fact]
    public void SelectGroup_ScopesCleanupToTheSelectedGroup()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_images);

        Assert.Same(_images, workspace.Selection!.Group);
        Assert.Equal("image-fingerprint", workspace.CleanupOperation!.ContentHash);
        Assert.Equal("first.jpg", workspace.CleanupOperation.KeepPath);
        Assert.Equal(["second.jpg"], workspace.CleanupOperation.PathsToRemove);
        Assert.DoesNotContain(workspace.CleanupOperation.PathsToRemove, path => _documents.FilePaths.Contains(path));
    }

    [Fact]
    public void GroupNavigation_RemembersTheKeepChoiceWithinTheCurrentScan()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_documents);
        workspace.SelectKeepFile("working.txt");
        workspace.SelectGroup(_images);
        workspace.SelectKeepFile("second.jpg");
        workspace.SelectGroup(_documents);

        Assert.Equal("working.txt", workspace.CleanupOperation!.KeepPath);
        Assert.Equal(["draft.txt", "saved.txt"], workspace.CleanupOperation.PathsToRemove);
        workspace.SelectGroup(_images);
        Assert.Equal("second.jpg", workspace.CleanupOperation!.KeepPath);
    }

    [Fact]
    public void LoadNewScan_ClearsSelectionsAndPreviouslyRememberedChoices()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_documents);
        workspace.SelectKeepFile("working.txt");
        workspace.Load([_documents]);

        Assert.Null(workspace.Selection);
        Assert.Null(workspace.CleanupOperation);
        workspace.SelectGroup(_documents);
        Assert.Equal("draft.txt", workspace.CleanupOperation!.KeepPath);
    }

    [Fact]
    public void Clear_RemovesAllResultsAndCleanupEligibility()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_documents);
        workspace.Clear();

        Assert.Empty(workspace.Groups);
        Assert.Null(workspace.Selection);
        Assert.Null(workspace.CleanupOperation);
    }

    [Fact]
    public void SelectUnknownGroup_InvalidatesThePreviousOperation()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_documents);
        var unknownGroup = new DuplicateGroup(["other.txt", "copy.txt"], 64, "other-fingerprint");

        Assert.Throws<ArgumentException>(() => workspace.SelectGroup(unknownGroup));
        Assert.Null(workspace.Selection);
        Assert.Null(workspace.CleanupOperation);
    }

    [Fact]
    public void SelectKeepFileFromAnotherGroup_InvalidatesThePreviousOperation()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_documents);

        Assert.Throws<ArgumentException>(() => workspace.SelectKeepFile("second.jpg"));
        Assert.Null(workspace.Selection);
        Assert.Null(workspace.CleanupOperation);
    }

    [Fact]
    public void DeselectGroup_DisablesCleanupWithoutDiscardingTheScan()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_documents);
        workspace.SelectGroup(null);

        Assert.Equal(2, workspace.Groups.Count);
        Assert.Null(workspace.Selection);
        Assert.Null(workspace.CleanupOperation);
    }

    [Fact]
    public void LargeGroup_IsReviewableButCannotCreateAnOversizedCleanup()
    {
        var largeGroup = new DuplicateGroup(Enumerable.Range(0, 130).Select(index => $"copy-{index}.txt"), 64, "fingerprint");
        var workspace = new DuplicateReviewWorkspace();
        workspace.Load([largeGroup]);
        workspace.SelectGroup(largeGroup);

        Assert.NotNull(workspace.Selection);
        Assert.Null(workspace.CleanupOperation);
    }

    [Fact]
    public void GroupWithoutScanFingerprint_IsReviewOnly()
    {
        var group = new DuplicateGroup(["draft.txt", "saved.txt"], 64);
        var workspace = new DuplicateReviewWorkspace();
        workspace.Load([group]);
        workspace.SelectGroup(group);

        Assert.NotNull(workspace.Selection);
        Assert.Null(workspace.CleanupOperation);
    }

    private DuplicateReviewWorkspace CreateWorkspace()
    {
        var workspace = new DuplicateReviewWorkspace();
        workspace.Load([_documents, _images]);
        return workspace;
    }

    [Fact]
    public void Cleanup_DefaultsToRecoverableQuarantine()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_documents);

        Assert.Equal(DuplicateAction.Quarantine, workspace.CleanupOperation!.Action);
    }

    [Fact]
    public void ChangingAction_PreservesTheReviewedKeepFileAndGroupScope()
    {
        var workspace = CreateWorkspace();
        workspace.SelectGroup(_documents);
        workspace.SelectKeepFile("working.txt");
        workspace.SetCleanupAction(DuplicateAction.Delete);

        Assert.Equal(DuplicateAction.Delete, workspace.CleanupOperation!.Action);
        Assert.Equal("working.txt", workspace.CleanupOperation.KeepPath);
        Assert.Equal(["draft.txt", "saved.txt"], workspace.CleanupOperation.PathsToRemove);
        workspace.SetCleanupAction(DuplicateAction.Quarantine);
        Assert.Equal(DuplicateAction.Quarantine, workspace.CleanupOperation!.Action);
    }
}
