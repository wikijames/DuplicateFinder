using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public partial class MainWindow : Window
{
    private readonly DuplicateReviewWorkspace _workspace = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _cleanupInProgress;
    private string? _lastBatchDirectory;
    private string _customExtensions = string.Empty;
    private string? _scanCompletionStatus;

    public MainWindow()
    {
        InitializeComponent();
        KeepFileComboBox.SelectionChanged += KeepFileComboBox_SelectionChanged;
        FileTypeBox.ItemsSource = FileTypePreset.CommonTypes;
        FileTypeBox.SelectedIndex = 0;
        ConfigureFolderDrop();
        _diagnosticsTimer.Tick += (_, _) => UpdateDiagnosticsButton();
        ResultsSortBox.ItemsSource = new[] { "Name (A–Z)", "Extra-copy size (largest)", "File size (largest)", "Copy count (most)" };
        ResultsSortBox.SelectedIndex = 0;
        AppearanceBox.ItemsSource = new[] { "System", "Light", "Dark" };
        AppearanceBox.SelectedIndex = 0;
        GroupFilesList.SelectionChanged += (_, _) => PreviewFileButton.IsEnabled = !SetupIsBusy && GroupFilesList.SelectedItem is DuplicateFileItem;
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape && _scanCancellation is not null)
            {
                _scanCancellation.Cancel();
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.F && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && !SetupIsBusy)
            {
                ResultsSearchBox.Focus();
                ResultsSearchBox.SelectAll();
                e.Handled = true;
            }
        };
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SetupIsBusy) return;
        try
        {
            await BrowseForRootAsync();
        }
        catch (Exception exception)
        {
            ReportOperationFailure("Folder selection", exception);
        }
    }

    private async Task BrowseForRootAsync()
    {
        var pickerOptions = new FolderPickerOpenOptions
        {
            Title = "Select folders to scan",
            AllowMultiple = true
        };

        var startFolder = RootPathBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
        {
            pickerOptions.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(startFolder)));
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(pickerOptions);
        if (folders.Count > 0) AddScanFolders(folders.Select(folder => folder.TryGetLocalPath() ?? throw new ArgumentException("Only local folders are supported.")));
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await TryScanCurrentRootAsync();
    }

    private void ScanSetup_Changed(object? sender, TextChangedEventArgs e)
    {
        InvalidateScanSetup();
    }

    private void InvalidateScanSetup()
    {
        if (_hasScanResults && !_cleanupInProgress && _scanCancellation is null)
        {
            ClearCurrentResults("Scan setup changed. Scan again before reviewing removal.");
        }
    }

    private void FileTypeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: FileTypePreset preset }) return;
        if (e.RemovedItems.OfType<FileTypePreset>().Any(previous => previous.IsCustom))
        {
            _customExtensions = ExtensionsBox.Text ?? string.Empty;
        }
        ExtensionsBox.IsReadOnly = !preset.IsCustom;
        ExtensionsBox.PlaceholderText = preset.IsCustom ? ".jpg, .pdf, .txt" : "All file extensions";
        ExtensionsBox.Text = preset.IsCustom ? _customExtensions : preset.Extensions;
        InvalidateScanSetup();
    }

    private void CancelScanButton_Click(object sender, RoutedEventArgs e)
    {
        _scanCancellation?.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        _scanCancellation?.Cancel();
        _previewCancellation?.Cancel();
        base.OnClosed(e);
    }

    private async void CleanupButton_Click(object sender, RoutedEventArgs e)
    {
        var operation = _workspace.CleanupOperation;
        if (operation is null || SetupIsBusy)
        {
            StatusText.Text = "Select a duplicate group and keep file before reviewing removal.";
            return;
        }

        _cleanupInProgress = true;
        SetScanInProgress(false);
        try
        {
            var (confirmed, destination) = await ConfirmCleanupAsync(operation);
            if (!confirmed)
            {
                StatusText.Text = "Cleanup cancelled by the user.";
                return;
            }

            StatusText.Text = "Checking files and cleaning up...";
            _diagnostics?.Record(operation.Action == DuplicateAction.Quarantine ? DiagnosticOperation.Quarantine : DiagnosticOperation.PermanentDelete, DiagnosticOutcome.Started);
            if (operation.Action == DuplicateAction.Quarantine)
            {
                var batch = await Task.Run(() => new QuarantineService().Quarantine(operation, destination!));
                RememberBatch(batch.DirectoryPath);
                _diagnostics?.Record(DiagnosticOperation.Quarantine, DiagnosticOutcome.Completed, files: batch.Entries.Count);
                ClearCurrentResults($"Quarantined {batch.Entries.Count} file(s). Use Restore batch to recover them. Disk usage is unchanged.");
            }
            else
            {
                await Task.Run(() => new WindowsCleanupService().Execute(operation));
                _diagnostics?.Record(DiagnosticOperation.PermanentDelete, DiagnosticOutcome.Completed, files: operation.PathsToRemove.Count);
                ClearCurrentResults("Cleanup completed. Rescan to refresh results.");
            }
        }
        catch (QuarantineInterruptedException exception)
        {
            ReportRecoveryInterruption(exception);
        }
        catch (Exception exception)
        {
            ReportOperationFailure("Cleanup", exception);
        }
        finally
        {
            _cleanupInProgress = false;
            SetScanInProgress(false);
        }
    }

    private void KeepFileComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_workspace.Selection is null)
        {
            return;
        }

        if (KeepFileComboBox.SelectedItem is not KeepFileChoice choice)
        {
            return;
        }

        _workspace.SelectKeepFile(choice.FullPath);
        RefreshReview();
    }

    private void ResultsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = ResultsList.SelectedItem as DuplicateGroupItem;
        _workspace.SelectGroup(item?.Group);
        KeepFileComboBox.ItemsSource = Array.Empty<KeepFileChoice>();
        if (_workspace.Selection is { } selection)
        {
            var choices = selection.Group.FilePaths.Select(path => new KeepFileChoice(path)).ToList();
            KeepFileComboBox.ItemsSource = choices;
            KeepFileComboBox.SelectedItem = choices.First(choice => choice.FullPath == selection.KeepFilePath);
        }
        RefreshReview();
    }

    private async Task TryScanCurrentRootAsync()
    {
        if (_scanCancellation is not null || _cleanupInProgress || _setupDialogOpen)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        SetScanInProgress(true);
        try
        {
            await ScanCurrentRootAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            ClearCurrentResults("Scan cancelled.");
            Trace.TraceInformation("Scan cancelled.");
            _diagnostics?.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Cancelled);
        }
        catch (Exception exception)
        {
            ReportOperationFailure("Scan", exception);
        }
        finally
        {
            _scanCancellation = null;
            SetScanInProgress(false);
        }
    }

    private void SetScanInProgress(bool scanning)
    {
        var busy = scanning || _cleanupInProgress || _setupDialogOpen;
        RootPathBox.IsEnabled = !busy;
        AddFolderButton.IsEnabled = !busy;
        ManageFoldersButton.IsEnabled = !busy;
        ScanSettingsButton.IsEnabled = !busy;
        AppearanceBox.IsEnabled = !busy;
        DiagnosticsButton.IsEnabled = !busy;
        ExtensionsBox.IsEnabled = !busy;
        FileTypeBox.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        ResultsList.IsEnabled = !busy;
        ResultsSearchBox.IsEnabled = !busy;
        ResultsSortBox.IsEnabled = !busy;
        GroupFilesList.IsEnabled = !busy;
        PreviewFileButton.IsEnabled = !busy && GroupFilesList.SelectedItem is DuplicateFileItem;
        CleanupActionBox.IsEnabled = !busy;
        RestoreBatchButton.IsEnabled = !busy;
        CleanupButton.IsEnabled = !busy && _workspace.CleanupOperation is not null;
        KeepFileComboBox.IsEnabled = !busy && _workspace.Selection is not null;
        CancelScanButton.IsEnabled = scanning;
        ScanProgressIndicator.IsVisible = busy;
    }

    private async Task ScanCurrentRootAsync(CancellationToken cancellationToken)
    {
        string[] roots;
        ScanOptions options;
        try
        {
            if (!string.IsNullOrWhiteSpace(RootPathBox.Text)) AddScanFolders(new[] { RootPathBox.Text.Trim() });
            if (_scanFolders.Folders.Count == 0) throw new ArgumentException("Select at least one folder.");
            roots = _scanFolders.Folders.ToArray();
            options = ScanSetup.CreateOptions((FileTypePreset)FileTypeBox.SelectedItem!, ExtensionsBox.Text, _scanFilters);
            if (roots.Any(root => !Directory.Exists(root))) throw new DirectoryNotFoundException();
        }
        catch (DirectoryNotFoundException)
        {
            ClearCurrentResults("A selected scan folder was not found. Update the folder selection.");
            return;
        }
        catch (ArgumentException)
        {
            ClearCurrentResults("Check selected folders, extensions and scan settings. Custom extensions cannot be blank.");
            return;
        }

        var coordinator = new DuplicateScanCoordinator(OperatingSystem.IsWindows() ? new WindowsFileIdentityProvider() : null);
        ClearCurrentResults("Scanning...");
        _diagnostics?.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Started);
        var progress = new LatestScanProgress();
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            if (progress.TakeLatest() is not { } snapshot) return;
            StatusText.Text = $"{snapshot.Stage}: {snapshot.DiscoveredFiles} candidates · {snapshot.SampledFiles} sampled · " +
                $"{snapshot.HashedFiles} hashed · {snapshot.VerifiedPairs} pairs checked · {DisplayFileSize.Format(snapshot.BytesRead)} read";
        };
        DuplicateScanReport report;
        timer.Start();
        try
        {
            report = await Task.Run(() => coordinator.CreateReport(roots, options, cancellationToken, progress), cancellationToken);
        }
        finally { timer.Stop(); }
        cancellationToken.ThrowIfCancellationRequested();
        var summary = report.Summary;
        _diagnostics?.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Completed, groups: summary.TotalDuplicateGroups,
            files: summary.TotalDuplicateFiles, skippedFiles: summary.Issues.SkippedFileCount, skippedFolders: summary.Issues.SkippedDirectoryCount);

        GroupCountText.Text = summary.TotalDuplicateGroups.ToString();
        DuplicateFileCountText.Text = summary.TotalDuplicateFiles.ToString();
        ReclaimableText.Text = DisplayFileSize.Format(summary.TotalReclaimableBytes);
        StatusText.Text = report.StatusMessage;

        if (!summary.HasDuplicates)
        {
            ClearCurrentResults(report.StatusMessage);
            ShowScanIssues(report);
            _hasScanResults = true;
            return;
        }

        _workspace.Load(summary.Groups);
        _hasScanResults = true;
        _scanCompletionStatus = report.StatusMessage;
        ShowScanIssues(report);
        RefreshResultGroups();
        ResultsList.SelectedIndex = 0;
    }

    private void ReportOperationFailure(string operation, Exception exception)
    {
        var diagnostic = $"{operation} failed ({exception.GetType().Name}).";
        Trace.TraceError(diagnostic);
        _diagnostics?.Record(operation switch
        {
            "Scan" => DiagnosticOperation.Scan,
            "Cleanup" => _workspace.CleanupAction == DuplicateAction.Quarantine ? DiagnosticOperation.Quarantine : DiagnosticOperation.PermanentDelete,
            "Folder selection" => DiagnosticOperation.FolderSelection,
            _ => DiagnosticOperation.Session
        }, DiagnosticOutcome.Failed, SessionDiagnostics.Classify(exception));
        ClearCurrentResults(operation == "Cleanup"
            ? $"{diagnostic} Cleanup may be incomplete. Rescan and review files before retrying."
            : diagnostic);
    }

    private void ClearCurrentResults(string statusMessage)
    {
        _scanCompletionStatus = null;
        _hasScanResults = false;
        ScanWarningText.IsVisible = false;
        ScanWarningText.Text = string.Empty;
        GroupCountText.Text = "0";
        DuplicateFileCountText.Text = "0";
        ReclaimableText.Text = "0 B";
        StatusText.Text = statusMessage;
        _workspace.Clear();
        VisibleGroupsText.Text = "0 of 0 groups";
        ResultsList.ItemsSource = Array.Empty<DuplicateGroupItem>();
        KeepFileComboBox.ItemsSource = Array.Empty<KeepFileChoice>();
        GroupFilesList.ItemsSource = Array.Empty<DuplicateFileItem>();
        SelectedGroupTitle.Text = "Choose a duplicate group";
        EmptyResultsText.Text = statusMessage;
        EmptyResultsText.IsVisible = true;
        CleanupButton.IsEnabled = false;
        KeepFileComboBox.IsEnabled = false;
        ReviewText.Text = "No review selected.";
    }

    private void ShowScanIssues(DuplicateScanReport report)
    {
        var showNotice = !report.Summary.IsComplete || report.Summary.Issues.ExcludedHardLinkPathCount > 0;
        ScanWarningText.Text = showNotice ? report.StatusMessage : string.Empty;
        ScanWarningText.IsVisible = showNotice;
    }

    private void RefreshReview()
    {
        var selection = _workspace.Selection;
        if (selection is null)
        {
            GroupFilesList.ItemsSource = Array.Empty<DuplicateFileItem>();
            SelectedGroupTitle.Text = "Choose a duplicate group";
            ReviewText.Text = "No review selected.";
            CleanupButton.IsEnabled = false;
            KeepFileComboBox.IsEnabled = false;
            return;
        }

        var busy = _scanCancellation is not null || _cleanupInProgress;
        SelectedGroupTitle.Text = Path.GetFileName(selection.Group.FilePaths[0]);
        GroupFilesList.ItemsSource = selection.Group.FilePaths.Select(path => new DuplicateFileItem(path, selection.Group.SizeBytes,
            string.Equals(path, selection.KeepFilePath, StringComparison.OrdinalIgnoreCase))).ToList();
        CleanupButton.IsEnabled = !busy && _workspace.CleanupOperation is not null;
        KeepFileComboBox.IsEnabled = !busy;
        ReviewText.Text = _workspace.CleanupOperation is null
            ? _workspace.CleanupUnavailableReason ?? "This selection is review-only."
            : $"This group: keep 1 file, remove {selection.RemoveFilePaths.Count}. Other groups are unaffected.";
        StatusText.Text = _scanCompletionStatus ?? "Review the selected group. Nothing is removed until you confirm.";
    }

    private async Task<(bool Confirmed, string? Destination)> ConfirmCleanupAsync(DuplicateCleanupOperation operation)
    {
        var quarantine = operation.Action == DuplicateAction.Quarantine;
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, IsDefault = true };
        var confirmButton = new Button { Content = quarantine ? "Move to quarantine" : "Permanently delete selected" };
        var destinationBox = new TextBox { Text = Path.GetDirectoryName(operation.KeepPath) };
        AutomationProperties.SetAutomationId(destinationBox, "QuarantineDestinationBox");
        AutomationProperties.SetName(destinationBox, "Quarantine destination folder");
        var validationText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(new TextBlock
        {
            Text = quarantine
                ? $"Move {operation.PathsToRemove.Count} file(s) into recoverable quarantine? Disk space will not be freed.\nKeep: {operation.KeepPath}"
                : $"Permanently delete {operation.PathsToRemove.Count} duplicate file(s)? This cannot be undone.\nKeep: {operation.KeepPath}",
            TextWrapping = TextWrapping.Wrap
        });
        if (quarantine)
        {
            header.Children.Add(new TextBlock { Text = "Destination folder (same drive). Keep the manifest; restore requires this Windows user profile.", TextWrapping = TextWrapping.Wrap });
            header.Children.Add(CreateFolderEntry(destinationBox, validationText, "Select a quarantine destination on the same drive"));
            header.Children.Add(validationText);
        }

        var targets = new ScrollViewer
        {
            Content = new TextBlock { Text = string.Join(Environment.NewLine, operation.PathsToRemove), TextWrapping = TextWrapping.Wrap }
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancelButton, confirmButton }
        };
        var content = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 16 };
        Grid.SetRow(targets, 1);
        Grid.SetRow(buttons, 2);
        content.Children.Add(header);
        content.Children.Add(targets);
        content.Children.Add(buttons);
        var dialog = new Window
        {
            Title = "Confirm duplicate cleanup",
            Width = 640,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        confirmButton.Click += (_, _) =>
        {
            if (quarantine && !Directory.Exists(destinationBox.Text?.Trim()))
            {
                validationText.Text = "Enter an existing folder on the same drive as the reviewed files.";
                return;
            }
            dialog.Close(true);
        };
        var confirmed = await dialog.ShowDialog<bool>(this);
        return (confirmed, quarantine ? destinationBox.Text?.Trim() : null);
    }

    private void CleanupActionBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox actionBox) return;
        _workspace.SetCleanupAction(actionBox.SelectedIndex == 1 ? DuplicateAction.Delete : DuplicateAction.Quarantine);
        if (_workspace.Selection is not null) RefreshReview();
    }

    private void RememberBatch(string directory)
    {
        _lastBatchDirectory = directory;
        RecoveryText.Text = "Recovery batch: " + directory;
        ToolTip.SetTip(RecoveryText, directory);
        RecoveryText.IsVisible = true;
    }

    private void ReportRecoveryInterruption(QuarantineInterruptedException exception, DiagnosticOperation operation = DiagnosticOperation.Quarantine)
    {
        Trace.TraceError("Recovery operation interrupted ({0}).", exception.GetType().Name);
        _diagnostics?.Record(operation, DiagnosticOutcome.Interrupted, DiagnosticFailure.InputOutput, files: exception.CompletedCount);
        RememberBatch(exception.BatchDirectory);
        ClearCurrentResults($"Recovery operation stopped after {exception.CompletedCount} move(s). Keep the batch; use Restore batch after resolving conflicts.");
    }

    private async void RestoreBatchButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SetupIsBusy) return;
        _cleanupInProgress = true;
        SetScanInProgress(false);
        try
        {
            var directory = await PromptForBatchAsync();
            if (directory is null)
            {
                StatusText.Text = "Restore cancelled. No files moved.";
                return;
            }
            var service = new QuarantineService();
            StatusText.Text = "Reading recovery manifest...";
            var batch = await Task.Run(() => service.Inspect(directory));
            if (!await ConfirmRestoreAsync(batch))
            {
                StatusText.Text = "Restore cancelled. No files moved.";
                return;
            }
            RememberBatch(batch.DirectoryPath);
            StatusText.Text = "Checking recovery files and restoring...";
            _diagnostics?.Record(DiagnosticOperation.Restore, DiagnosticOutcome.Started);
            var result = await Task.Run(() => service.Restore(batch));
            _diagnostics?.Record(DiagnosticOperation.Restore, DiagnosticOutcome.Completed, files: result.RestoredCount);
            RememberBatch(batch.DirectoryPath);
            ClearCurrentResults($"Restored {result.RestoredCount} file(s); {result.AlreadyOriginalCount} already at their original locations. Rescan to refresh results.");
        }
        catch (QuarantineInterruptedException exception)
        {
            ReportRecoveryInterruption(exception, DiagnosticOperation.Restore);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Restore failed ({0}).", exception.GetType().Name);
            _diagnostics?.Record(DiagnosticOperation.Restore, DiagnosticOutcome.Failed, SessionDiagnostics.Classify(exception));
            ClearCurrentResults("Restore stopped. Check the batch, original folders and file conflicts. No existing file is overwritten.");
        }
        finally
        {
            _cleanupInProgress = false;
            SetScanInProgress(false);
        }
    }

    private async Task<string?> PromptForBatchAsync()
    {
        var pathBox = new TextBox { Text = _lastBatchDirectory, PlaceholderText = "Paste the batch folder path" };
        AutomationProperties.SetAutomationId(pathBox, "RestoreBatchPathBox");
        AutomationProperties.SetName(pathBox, "Recovery batch folder");
        var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = true };
        var review = new Button { Content = "Review restore" };
        var validationText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var dialog = new Window
        {
            Title = "Select recovery batch", Width = 640, Height = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Select a batch folder inside .DuplicateFinder-Quarantine. The protected manifest requires the original Windows account.", TextWrapping = TextWrapping.Wrap },
                    CreateFolderEntry(pathBox, validationText, "Select a quarantine batch folder"),
                    validationText,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, review } }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        review.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this) ? pathBox.Text?.Trim() : null;
    }

    private Grid CreateFolderEntry(TextBox pathBox, TextBlock validationText, string title)
    {
        var browse = new Button { Content = "Browse" };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(browse, 1);
        row.Children.Add(pathBox);
        row.Children.Add(browse);
        browse.Click += async (_, _) =>
        {
            try
            {
                var provider = TopLevel.GetTopLevel(pathBox)?.StorageProvider;
                if (provider is null) return;
                var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
                var selectedPath = folders.FirstOrDefault()?.Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(selectedPath)) pathBox.Text = selectedPath;
            }
            catch (Exception exception)
            {
                Trace.TraceError("Recovery folder selection failed ({0}).", exception.GetType().Name);
                validationText.Text = "Folder selection failed. Enter or paste the folder path instead.";
            }
        };
        return row;
    }

    private async Task<bool> ConfirmRestoreAsync(QuarantineBatch batch)
    {
        var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = true };
        var restore = new Button { Content = "Restore files" };
        var targets = new ScrollViewer
        {
            Content = new TextBlock { Text = string.Join(Environment.NewLine, batch.Entries.Select(entry => entry.OriginalPath)), TextWrapping = TextWrapping.Wrap }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, restore } };
        var content = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 16 };
        content.Children.Add(new TextBlock { Text = $"Review {batch.Entries.Count} recovery entry/entries. Files still in quarantine will return to these original paths. Existing files are never overwritten; matching files already restored are left alone.", TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(targets, 1);
        Grid.SetRow(buttons, 2);
        content.Children.Add(targets);
        content.Children.Add(buttons);
        var dialog = new Window { Title = "Confirm batch restore", Width = 640, Height = 420, Content = content, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        cancel.Click += (_, _) => dialog.Close(false);
        restore.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }
}
