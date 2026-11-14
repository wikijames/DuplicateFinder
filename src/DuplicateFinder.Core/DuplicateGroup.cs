namespace DuplicateFinder.Core;

public sealed class DuplicateGroup
{
    public DuplicateGroup(IEnumerable<string> filePaths, long sizeBytes)
    {
        FilePaths = filePaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SizeBytes = sizeBytes;
        ReclaimableBytes = Math.Max(0, sizeBytes * Math.Max(0, FilePaths.Count - 1));
    }

    public IReadOnlyList<string> FilePaths { get; }

    public long SizeBytes { get; }

    public long ReclaimableBytes { get; }
}

public enum DuplicateAction
{
    Keep,
    Delete,
    MoveToRecycleBin,
    Ignore
}

public sealed class DuplicateReviewDecision
{
    public DuplicateReviewDecision(DuplicateGroup group, string selectedFilePath, DuplicateAction action)
    {
        if (group is null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        if (string.IsNullOrWhiteSpace(selectedFilePath))
        {
            throw new ArgumentException("A selected file path is required.", nameof(selectedFilePath));
        }

        if (!group.FilePaths.Contains(selectedFilePath, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The selected file path must belong to the duplicate group.", nameof(selectedFilePath));
        }

        Group = group;
        SelectedFilePath = selectedFilePath;
        Action = action;
    }

    public DuplicateGroup Group { get; }

    public string SelectedFilePath { get; }

    public DuplicateAction Action { get; }
}

public sealed class DuplicateCleanupPlan
{
    public DuplicateCleanupPlan(DuplicateReviewDecision decision)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        Action = decision.Action;
        SelectedFilePath = decision.SelectedFilePath;

        if (decision.Group.FilePaths.Count <= 1)
        {
            KeepPath = decision.SelectedFilePath;
            PathsToRemove = Array.Empty<string>();
            CanExecute = false;
            return;
        }

        if (decision.Action == DuplicateAction.Ignore)
        {
            KeepPath = decision.SelectedFilePath;
            PathsToRemove = Array.Empty<string>();
            CanExecute = false;
            return;
        }

        var selectedPath = decision.SelectedFilePath;
        var keepPath = selectedPath;
        var removals = decision.Group.FilePaths
            .Where(path => !string.Equals(path, keepPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (decision.Action == DuplicateAction.Delete || decision.Action == DuplicateAction.MoveToRecycleBin)
        {
            var keepCandidate = decision.Group.FilePaths
                .FirstOrDefault(path => !string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(keepCandidate))
            {
                KeepPath = decision.SelectedFilePath;
                PathsToRemove = Array.Empty<string>();
                CanExecute = false;
                return;
            }

            KeepPath = keepCandidate;
            PathsToRemove = new[] { selectedPath };
            CanExecute = true;
            return;
        }

        KeepPath = keepPath;
        PathsToRemove = removals;
        CanExecute = removals.Length > 0;
    }

    public DuplicateAction Action { get; }

    public string SelectedFilePath { get; }

    public string KeepPath { get; }

    public IReadOnlyList<string> PathsToRemove { get; }

    public bool CanExecute { get; }
}

public sealed class DuplicateCleanupExecution
{
    public DuplicateCleanupExecution(DuplicateCleanupPlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (!plan.CanExecute)
        {
            throw new InvalidOperationException("This cleanup plan cannot be executed.");
        }

        Action = plan.Action;
        KeepPath = plan.KeepPath;
        PathsToRemove = plan.PathsToRemove.ToArray();
        CanExecute = true;
    }

    public DuplicateAction Action { get; }

    public string KeepPath { get; }

    public IReadOnlyList<string> PathsToRemove { get; }

    public bool CanExecute { get; }
}

public sealed class DuplicateCleanupExecutor
{
    public DuplicateCleanupExecution CreateExecution(DuplicateCleanupPlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        return new DuplicateCleanupExecution(plan);
    }
}

public sealed class DuplicateCleanupOperation
{
    public DuplicateCleanupOperation(DuplicateCleanupExecution execution)
    {
        if (execution is null)
        {
            throw new ArgumentNullException(nameof(execution));
        }

        if (!execution.CanExecute)
        {
            throw new InvalidOperationException("This execution cannot be performed.");
        }

        Action = execution.Action;
        KeepPath = execution.KeepPath;
        PathsToRemove = execution.PathsToRemove.ToArray();
        CanExecute = true;
    }

    public DuplicateAction Action { get; }

    public string KeepPath { get; }

    public IReadOnlyList<string> PathsToRemove { get; }

    public bool CanExecute { get; }
}

public sealed class DuplicateCleanupAdapter
{
    public DuplicateCleanupOperation CreateOperation(DuplicateCleanupExecution execution)
    {
        if (execution is null)
        {
            throw new ArgumentNullException(nameof(execution));
        }

        return new DuplicateCleanupOperation(execution);
    }
}

public sealed class WindowsCleanupAction
{
    public WindowsCleanupAction(string operationName, string keepPath)
    {
        OperationName = string.IsNullOrWhiteSpace(operationName)
            ? throw new ArgumentException("Operation name is required.", nameof(operationName))
            : operationName;

        KeepPath = string.IsNullOrWhiteSpace(keepPath)
            ? throw new ArgumentException("A keep path is required.", nameof(keepPath))
            : keepPath;
    }

    public string OperationName { get; }

    public string KeepPath { get; }
}

public sealed class WindowsCleanupStrategy
{
    public WindowsCleanupAction CreateAction(DuplicateCleanupOperation operation)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        var operationName = operation.Action switch
        {
            DuplicateAction.Delete => "Delete",
            DuplicateAction.MoveToRecycleBin => "RecycleBin",
            _ => "Review"
        };

        return new WindowsCleanupAction(operationName, operation.KeepPath);
    }
}

public sealed class DuplicateScanSummary
{
    public DuplicateScanSummary(string rootPath, IReadOnlyList<DuplicateGroup> groups)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A root path is required.", nameof(rootPath));
        }

        if (groups is null)
        {
            throw new ArgumentNullException(nameof(groups));
        }

        RootPath = rootPath;
        Groups = groups.OrderBy(group => group.FilePaths.FirstOrDefault() ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
        TotalDuplicateGroups = Groups.Count;
        TotalDuplicateFiles = Groups.Sum(group => group.FilePaths.Count);
        TotalReclaimableBytes = Groups.Sum(group => group.ReclaimableBytes);
    }

    public string RootPath { get; }

    public IReadOnlyList<DuplicateGroup> Groups { get; }

    public int TotalDuplicateGroups { get; }

    public int TotalDuplicateFiles { get; }

    public long TotalReclaimableBytes { get; }

    public bool HasDuplicates => TotalDuplicateGroups > 0;
}

public sealed class DuplicateScanReport
{
    public DuplicateScanReport(string rootPath, DuplicateScanSummary summary)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A root path is required.", nameof(rootPath));
        }

        if (summary is null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        RootPath = rootPath;
        Summary = summary;
        StatusMessage = summary.HasDuplicates
            ? $"Found {summary.TotalDuplicateGroups} duplicate group(s)."
            : "No duplicate files found in this folder.";
    }

    public string RootPath { get; }

    public DuplicateScanSummary Summary { get; }

    public string StatusMessage { get; }
}

public sealed class DuplicateReviewSelection
{
    public DuplicateReviewSelection(DuplicateGroup group, string keepFilePath)
    {
        if (group is null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        if (string.IsNullOrWhiteSpace(keepFilePath))
        {
            throw new ArgumentException("A keep file path is required.", nameof(keepFilePath));
        }

        if (group.FilePaths.Count <= 1)
        {
            throw new InvalidOperationException("A review selection requires at least two files in the duplicate group.");
        }

        if (!group.FilePaths.Contains(keepFilePath, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The keep file path must belong to the duplicate group.", nameof(keepFilePath));
        }

        Group = group;
        KeepFilePath = keepFilePath;
        RemoveFilePaths = group.FilePaths
            .Where(path => !string.Equals(path, keepFilePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DuplicateGroup Group { get; }

    public string KeepFilePath { get; }

    public IReadOnlyList<string> RemoveFilePaths { get; }

    public DuplicateReviewDecision ToDecision(DuplicateAction action, string? removeFilePath = null)
    {
        if (action == DuplicateAction.Ignore)
        {
            throw new InvalidOperationException("Ignore actions do not create a destructive cleanup decision.");
        }

        var targetPath = string.IsNullOrWhiteSpace(removeFilePath)
            ? RemoveFilePaths.FirstOrDefault()
            : RemoveFilePaths.FirstOrDefault(path => string.Equals(path, removeFilePath, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("A valid removal target is required for a cleanup decision.");
        }

        return new DuplicateReviewDecision(Group, targetPath, action);
    }
}

public sealed class DuplicateScanCoordinator
{
    public DuplicateScanSummary Scan(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A root path is required.", nameof(rootPath));
        }

        var detector = new DuplicateDetector();
        var groups = detector.FindDuplicates(rootPath, options ?? new ScanOptions(), cancellationToken);
        return new DuplicateScanSummary(rootPath, groups);
    }

    public DuplicateScanReport CreateReport(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        var summary = Scan(rootPath, options, cancellationToken);
        return new DuplicateScanReport(rootPath, summary);
    }
}
