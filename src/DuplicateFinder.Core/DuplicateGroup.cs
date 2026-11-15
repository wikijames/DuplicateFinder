namespace DuplicateFinder.Core;

public sealed class DuplicateGroup
{
    public DuplicateGroup(IEnumerable<string> filePaths, long sizeBytes, string? contentHash = null)
    {
        FilePaths = filePaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList().AsReadOnly();

        SizeBytes = sizeBytes;
        ContentHash = contentHash;
        ReclaimableBytes = Math.Max(0, sizeBytes * Math.Max(0, FilePaths.Count - 1));
    }

    public IReadOnlyList<string> FilePaths { get; }

    public long SizeBytes { get; }

    public string? ContentHash { get; }

    public long ReclaimableBytes { get; }
}

public enum DuplicateAction
{
    Keep,
    Delete,
    MoveToRecycleBin,
    Ignore,
    Quarantine
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
    public DuplicateCleanupPlan(DuplicateReviewSelection selection, DuplicateAction action)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (action is not DuplicateAction.Delete and not DuplicateAction.MoveToRecycleBin and not DuplicateAction.Quarantine)
        {
            throw new ArgumentOutOfRangeException(nameof(action), "A cleanup plan requires a delete, recycle-bin or quarantine action.");
        }

        Action = action;
        ContentHash = selection.Group.ContentHash;
        SizeBytes = selection.Group.SizeBytes;
        SelectedFilePath = selection.KeepFilePath;
        KeepPath = selection.KeepFilePath;
        PathsToRemove = selection.RemoveFilePaths.ToArray();
        CanExecute = PathsToRemove.Count > 0;
    }

    public DuplicateCleanupPlan(DuplicateReviewDecision decision)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        Action = decision.Action;
        ContentHash = decision.Group.ContentHash;
        SizeBytes = decision.Group.SizeBytes;
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

        if (decision.Action is DuplicateAction.Delete or DuplicateAction.MoveToRecycleBin or DuplicateAction.Quarantine)
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

    public string? ContentHash { get; }

    public long SizeBytes { get; }
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
        ContentHash = plan.ContentHash;
        SizeBytes = plan.SizeBytes;
        KeepPath = plan.KeepPath;
        PathsToRemove = plan.PathsToRemove.ToArray();
        CanExecute = true;
    }

    public DuplicateAction Action { get; }

    public string KeepPath { get; }

    public IReadOnlyList<string> PathsToRemove { get; }

    public bool CanExecute { get; }

    public string? ContentHash { get; }

    public long SizeBytes { get; }
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
        ContentHash = execution.ContentHash;
        SizeBytes = execution.SizeBytes;
        KeepPath = execution.KeepPath;
        PathsToRemove = execution.PathsToRemove.ToArray();
        CanExecute = true;
    }

    public DuplicateAction Action { get; }

    public string KeepPath { get; }

    public IReadOnlyList<string> PathsToRemove { get; }

    public bool CanExecute { get; }

    public string? ContentHash { get; }

    public long SizeBytes { get; }
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
            DuplicateAction.Quarantine => "Quarantine",
            _ => "Review"
        };

        return new WindowsCleanupAction(operationName, operation.KeepPath);
    }
}

public sealed class DuplicateScanSummary
{
    public DuplicateScanSummary(string rootPath, IReadOnlyList<DuplicateGroup> groups, ScanIssueSummary? issues = null)
        : this(new[] { rootPath }, groups, issues) { }

    public DuplicateScanSummary(IReadOnlyList<string> rootPaths, IReadOnlyList<DuplicateGroup> groups, ScanIssueSummary? issues = null)
    {
        if (rootPaths is null || rootPaths.Count == 0 || rootPaths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one root path is required.", nameof(rootPaths));
        }

        if (groups is null)
        {
            throw new ArgumentNullException(nameof(groups));
        }

        RootPaths = rootPaths.ToList().AsReadOnly();
        RootPath = RootPaths[0];
        Issues = issues ?? new ScanIssueSummary(0, 0, 0);
        Groups = groups.OrderBy(group => group.FilePaths.FirstOrDefault() ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
        TotalDuplicateGroups = Groups.Count;
        TotalDuplicateFiles = Groups.Sum(group => group.FilePaths.Count);
        TotalReclaimableBytes = Groups.Sum(group => group.ReclaimableBytes);
    }

    public string RootPath { get; }

    public IReadOnlyList<string> RootPaths { get; }

    public IReadOnlyList<DuplicateGroup> Groups { get; }

    public int TotalDuplicateGroups { get; }

    public int TotalDuplicateFiles { get; }

    public long TotalReclaimableBytes { get; }

    public bool HasDuplicates => TotalDuplicateGroups > 0;
    public ScanIssueSummary Issues { get; }
    public bool IsComplete => Issues.IsComplete;
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
        if (!summary.IsComplete)
        {
            StatusMessage = $"Incomplete scan: {summary.TotalDuplicateGroups} verified duplicate group(s). " +
                $"Skipped {summary.Issues.SkippedFileCount} file(s) and {summary.Issues.SkippedDirectoryCount} folder(s); " +
                $"{summary.Issues.ChangedFileCount} file(s) changed during scanning.";
        }
        if (summary.Issues.ExcludedHardLinkPathCount > 0)
        {
            StatusMessage += $" Excluded {summary.Issues.ExcludedHardLinkPathCount} hard-linked path(s) from matching and extra-copy totals.";
        }
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
    private readonly IFileIdentityProvider? _identityProvider;
    public DuplicateScanCoordinator() { }
    public DuplicateScanCoordinator(IFileIdentityProvider? identityProvider) => _identityProvider = identityProvider;

    public DuplicateScanSummary Scan(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default, IProgress<ScanProgress>? progress = null)
        => Scan(new[] { rootPath }, options, cancellationToken, progress);

    public DuplicateScanSummary Scan(IEnumerable<string> rootPaths, ScanOptions? options = null, CancellationToken cancellationToken = default, IProgress<ScanProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roots = ScanRoots.Normalize(rootPaths);
        var detector = new DuplicateDetector(_identityProvider);
        var result = detector.Scan(roots, options ?? new ScanOptions(), cancellationToken, progress);
        return new DuplicateScanSummary(roots, result.Groups, result.Issues);
    }

    public DuplicateScanReport CreateReport(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default, IProgress<ScanProgress>? progress = null)
        => CreateReport(new[] { rootPath }, options, cancellationToken, progress);

    public DuplicateScanReport CreateReport(IEnumerable<string> rootPaths, ScanOptions? options = null, CancellationToken cancellationToken = default, IProgress<ScanProgress>? progress = null)
    {
        var summary = Scan(rootPaths, options, cancellationToken, progress);
        return new DuplicateScanReport(summary.RootPath, summary);
    }
}
