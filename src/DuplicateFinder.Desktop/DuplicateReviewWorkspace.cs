using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

/// <summary>Keeps group navigation and the reviewed cleanup operation in sync.</summary>
public sealed class DuplicateReviewWorkspace
{
    private readonly Dictionary<DuplicateGroup, string> _keepChoices = new();

    public IReadOnlyList<DuplicateGroup> Groups { get; private set; } = Array.Empty<DuplicateGroup>();
    public DuplicateReviewSelection? Selection { get; private set; }
    public DuplicateCleanupOperation? CleanupOperation { get; private set; }
    public DuplicateAction CleanupAction { get; private set; } = DuplicateAction.Quarantine;
    public string? CleanupUnavailableReason { get; private set; }

    public void SetCleanupAction(DuplicateAction action)
    {
        if (action is not DuplicateAction.Delete and not DuplicateAction.Quarantine)
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        CleanupAction = action;
        CleanupOperation = null;
        CleanupUnavailableReason = null;
        if (Selection is not null) UpdateOperation();
    }

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
        CleanupUnavailableReason = null;
        if (group is null) return;
        if (!Groups.Contains(group))
        {
            throw new ArgumentException("Select a group from the current scan.", nameof(group));
        }

        var keepPath = GetKeepFilePath(group);
        Selection = new DuplicateReviewSelection(group, keepPath);
        UpdateOperation();
    }

    internal string GetKeepFilePath(DuplicateGroup group) =>
        _keepChoices.TryGetValue(group, out var previousChoice) ? previousChoice : group.FilePaths.First();

    public void SelectKeepFile(string path)
    {
        var group = Selection?.Group ?? throw new InvalidOperationException("Select a duplicate group first.");
        Selection = null;
        CleanupOperation = null;
        CleanupUnavailableReason = null;
        Selection = new DuplicateReviewSelection(group, path);
        _keepChoices[group] = path;
        UpdateOperation();
    }

    private void UpdateOperation()
    {
        var selection = Selection!;
        CleanupUnavailableReason = null;
        // Mirror the bounded Windows cleanup limit; larger groups remain reviewable.
        if (selection.RemoveFilePaths.Count > 128 || string.IsNullOrWhiteSpace(selection.Group.ContentHash))
        {
            CleanupUnavailableReason = "Review only: cleanup requires a scan fingerprint and at most 128 removal targets.";
            return;
        }
        if (CleanupAction == DuplicateAction.Quarantine && selection.Group.FilePaths.Select(Path.GetPathRoot)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() > 1)
        {
            CleanupUnavailableReason = "Review only in quarantine mode: all files in this group must be on the same drive.";
            return;
        }
        var plan = new DuplicateCleanupPlan(selection, CleanupAction);
        CleanupOperation = new DuplicateCleanupOperation(new DuplicateCleanupExecution(plan));
    }

    public void Clear()
    {
        Groups = Array.Empty<DuplicateGroup>();
        Selection = null;
        CleanupOperation = null;
        _keepChoices.Clear();
        CleanupUnavailableReason = null;
    }
}
