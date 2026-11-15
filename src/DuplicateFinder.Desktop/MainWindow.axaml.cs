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

    public MainWindow()
    {
        InitializeComponent();
        KeepFileComboBox.SelectionChanged += KeepFileComboBox_SelectionChanged;
        FileTypeBox.ItemsSource = FileTypePreset.CommonTypes;
        FileTypeBox.SelectedIndex = 0;
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
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
            Title = "Select a folder to scan",
            AllowMultiple = false
        };

        var startFolder = RootPathBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
        {
            pickerOptions.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(startFolder)));
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(pickerOptions);
        var selectedPath = folders.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        RootPathBox.Text = selectedPath;
        ClearCurrentResults("Folder selected. Choose extensions or scan all file types.");
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
        if (_workspace.Groups.Count > 0 && !_cleanupInProgress && _scanCancellation is null)
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
        base.OnClosed(e);
    }

    private async void CleanupButton_Click(object sender, RoutedEventArgs e)
    {
        var operation = _workspace.CleanupOperation;
        if (operation is null || _cleanupInProgress || _scanCancellation is not null)
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
            if (operation.Action == DuplicateAction.Quarantine)
            {
                var batch = await Task.Run(() => new QuarantineService().Quarantine(operation, destination!));
                RememberBatch(batch.DirectoryPath);
                ClearCurrentResults($"Quarantined {batch.Entries.Count} file(s). Use Restore batch to recover them. Disk usage is unchanged.");
            }
            else
            {
                await Task.Run(() => new WindowsCleanupService().Execute(operation));
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
        if (_scanCancellation is not null || _cleanupInProgress)
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
        var busy = scanning || _cleanupInProgress;
        RootPathBox.IsEnabled = !busy;
        ExtensionsBox.IsEnabled = !busy;
        FileTypeBox.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        ResultsList.IsEnabled = !busy;
        CleanupActionBox.IsEnabled = !busy;
        RestoreBatchButton.IsEnabled = !busy;
        CleanupButton.IsEnabled = !busy && _workspace.CleanupOperation is not null;
        KeepFileComboBox.IsEnabled = !busy && _workspace.Selection is not null;
        CancelScanButton.IsEnabled = scanning;
        ScanProgressIndicator.IsVisible = busy;
    }

    private async Task ScanCurrentRootAsync(CancellationToken cancellationToken)
    {
        var rootPath = RootPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            ClearCurrentResults("Enter a valid scan root directory first.");
            return;
        }

        if (!Directory.Exists(rootPath))
        {
            ClearCurrentResults("Scan directory was not found.");
            return;
        }

        ScanOptions options;
        try
        {
            options = ScanSetup.CreateOptions((FileTypePreset)FileTypeBox.SelectedItem!, ExtensionsBox.Text);
        }
        catch (ArgumentException)
        {
            ClearCurrentResults("Choose All files, or enter custom extensions such as .txt, .jpg or .pdf separated by commas.");
            return;
        }

        var coordinator = new DuplicateScanCoordinator();
        ClearCurrentResults("Scanning...");
        var report = await Task.Run(() => coordinator.CreateReport(rootPath, options, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var summary = report.Summary;

        GroupCountText.Text = summary.TotalDuplicateGroups.ToString();
        DuplicateFileCountText.Text = summary.TotalDuplicateFiles.ToString();
        ReclaimableText.Text = DisplayFileSize.Format(summary.TotalReclaimableBytes);
        StatusText.Text = report.StatusMessage;

        if (!summary.HasDuplicates)
        {
            ClearCurrentResults(report.StatusMessage);
            return;
        }

        _workspace.Load(summary.Groups);
        ResultsList.ItemsSource = summary.Groups.Select(group => new DuplicateGroupItem(group)).ToList();
        EmptyResultsText.IsVisible = false;
        ResultsList.SelectedIndex = 0;
    }

    private void ReportOperationFailure(string operation, Exception exception)
    {
        var diagnostic = $"{operation} failed ({exception.GetType().Name}).";
        Trace.TraceError(diagnostic);
        ClearCurrentResults(operation == "Cleanup"
            ? $"{diagnostic} Cleanup may be incomplete. Rescan and review files before retrying."
            : diagnostic);
    }

    private void ClearCurrentResults(string statusMessage)
    {
        GroupCountText.Text = "0";
        DuplicateFileCountText.Text = "0";
        ReclaimableText.Text = "0 B";
        StatusText.Text = statusMessage;
        _workspace.Clear();
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
            ? "Review only: cleanup requires a scan fingerprint and at most 128 removal targets."
            : $"This group: keep 1 file, remove {selection.RemoveFilePaths.Count}. Other groups are unaffected.";
        StatusText.Text = "Review the selected group. Nothing is removed until you confirm.";
    }

    private async Task<(bool Confirmed, string? Destination)> ConfirmCleanupAsync(DuplicateCleanupOperation operation)
    {
        var quarantine = operation.Action == DuplicateAction.Quarantine;
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, IsDefault = true };
        var confirmButton = new Button { Content = quarantine ? "Move to quarantine" : "Permanently delete selected" };
        var destinationBox = new TextBox { Text = RootPathBox.Text?.Trim() };
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

    private void ReportRecoveryInterruption(QuarantineInterruptedException exception)
    {
        Trace.TraceError("Recovery operation interrupted ({0}).", exception.GetType().Name);
        RememberBatch(exception.BatchDirectory);
        ClearCurrentResults($"Recovery operation stopped after {exception.CompletedCount} move(s). Keep the batch; use Restore batch after resolving conflicts.");
    }

    private async void RestoreBatchButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_cleanupInProgress || _scanCancellation is not null) return;
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
            var result = await Task.Run(() => service.Restore(batch));
            RememberBatch(batch.DirectoryPath);
            ClearCurrentResults($"Restored {result.RestoredCount} file(s); {result.AlreadyOriginalCount} already at their original locations. Rescan to refresh results.");
        }
        catch (QuarantineInterruptedException exception)
        {
            ReportRecoveryInterruption(exception);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Restore failed ({0}).", exception.GetType().Name);
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
