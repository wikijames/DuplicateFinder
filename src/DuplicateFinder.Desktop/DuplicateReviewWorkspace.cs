using System;
using System.Collections.Generic;
using System.Linq;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

/// <summary>Keeps group navigation and the reviewed cleanup operation in sync.</summary>
public sealed class DuplicateReviewWorkspace
{
    private readonly Dictionary<DuplicateGroup, string> _keepChoices = new();

    public IReadOnlyList<DuplicateGroup> Groups { get; private set; } = Array.Empty<DuplicateGroup>();
    public DuplicateReviewSelection? Selection { get; private set; }
    public DuplicateCleanupOperation? CleanupOperation { get; private set; }

    public void Load(IReadOnlyList<DuplicateGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Clear();
        Groups = groups.ToList().AsReadOnly();
    }

    public void SelectGroup(DuplicateGroup? group)
    {
        Selection = null;
        CleanupOperation = null;
        if (group is null) return;
        if (!Groups.Contains(group))
        {
            throw new ArgumentException("Select a group from the current scan.", nameof(group));
        }

        var keepPath = _keepChoices.TryGetValue(group, out var previousChoice) ? previousChoice : group.FilePaths.First();
        Selection = new DuplicateReviewSelection(group, keepPath);
        UpdateOperation();
    }

    public void SelectKeepFile(string path)
    {
        var group = Selection?.Group ?? throw new InvalidOperationException("Select a duplicate group first.");
        Selection = null;
        CleanupOperation = null;
        Selection = new DuplicateReviewSelection(group, path);
        _keepChoices[group] = path;
        UpdateOperation();
    }

    private void UpdateOperation()
    {
        var selection = Selection!;
        // Mirror the bounded Windows cleanup limit; larger groups remain reviewable.
        if (selection.RemoveFilePaths.Count > 128 || string.IsNullOrWhiteSpace(selection.Group.ContentHash)) return;
        var plan = new DuplicateCleanupPlan(selection, DuplicateAction.Delete);
        CleanupOperation = new DuplicateCleanupOperation(new DuplicateCleanupExecution(plan));
    }

    public void Clear()
    {
        Groups = Array.Empty<DuplicateGroup>();
        Selection = null;
        CleanupOperation = null;
        _keepChoices.Clear();
    }
}
