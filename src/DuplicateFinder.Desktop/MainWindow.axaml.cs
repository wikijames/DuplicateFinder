using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public partial class MainWindow : Window
{
    private DuplicateCleanupOperation? _pendingOperation;
    private DuplicateGroup? _currentDuplicateGroup;
    private CancellationTokenSource? _scanCancellation;
    private bool _cleanupInProgress;

    public MainWindow()
    {
        InitializeComponent();
        RootPathBox.Text = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Desktop", "demo");
        KeepFileComboBox.SelectionChanged += KeepFileComboBox_SelectionChanged;
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
        await TryScanCurrentRootAsync();
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await TryScanCurrentRootAsync();
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
        if (_pendingOperation is null || _cleanupInProgress || _scanCancellation is not null)
        {
            ResultsList.ItemsSource = new[] { "No duplicate group is currently selected for cleanup." };
            return;
        }

        var operation = _pendingOperation;
        _cleanupInProgress = true;
        SetScanInProgress(false);
        try
        {
            var confirmed = await ConfirmCleanupAsync(operation);
            if (!confirmed)
            {
                StatusText.Text = "Cleanup cancelled by the user.";
                return;
            }

            StatusText.Text = "Checking files and cleaning up...";
            await Task.Run(() => new WindowsCleanupService().Execute(operation));
            ClearCurrentResults("Cleanup completed. Rescan to refresh results.");
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
        if (_currentDuplicateGroup is null)
        {
            return;
        }

        if (KeepFileComboBox.SelectedItem is not string keepPath)
        {
            return;
        }

        UpdateReviewSelection(keepPath);
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
        BrowseButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        CleanupButton.IsEnabled = !busy && _pendingOperation is not null;
        KeepFileComboBox.IsEnabled = !busy;
        CancelScanButton.IsEnabled = scanning;
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

        var coordinator = new DuplicateScanCoordinator();
        ClearCurrentResults("Scanning...");
        var report = await Task.Run(() => coordinator.CreateReport(rootPath, new ScanOptions
        {
            AllowedExtensions = new[] { ".txt" }
        }, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var summary = report.Summary;

        GroupCountText.Text = summary.TotalDuplicateGroups.ToString();
        DuplicateFileCountText.Text = summary.TotalDuplicateFiles.ToString();
        ReclaimableText.Text = $"{summary.TotalReclaimableBytes} bytes";
        StatusText.Text = report.StatusMessage;

        if (!summary.HasDuplicates)
        {
            ResultsList.ItemsSource = new[] { report.StatusMessage };
            KeepFileComboBox.ItemsSource = Array.Empty<string>();
            _currentDuplicateGroup = null;
            _pendingOperation = null;
            ReviewText.Text = "No review selected.";
            return;
        }

        var duplicateGroup = summary.Groups.FirstOrDefault();
        if (duplicateGroup is null)
        {
            KeepFileComboBox.ItemsSource = Array.Empty<string>();
            _currentDuplicateGroup = null;
            _pendingOperation = null;
            ReviewText.Text = "No review selected.";
            return;
        }

        _currentDuplicateGroup = duplicateGroup;
        KeepFileComboBox.ItemsSource = duplicateGroup.FilePaths;
        KeepFileComboBox.SelectedIndex = 0;

        ResultsList.ItemsSource = summary.Groups
            .Select(group => $"{group.SizeBytes} bytes: {string.Join(", ", group.FilePaths)}")
            .ToList();

        UpdateReviewSelection(duplicateGroup.FilePaths.First());
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
        ReclaimableText.Text = "0 bytes";
        StatusText.Text = statusMessage;
        ResultsList.ItemsSource = new[] { statusMessage };
        KeepFileComboBox.ItemsSource = Array.Empty<string>();
        _currentDuplicateGroup = null;
        _pendingOperation = null;
        CleanupButton.IsEnabled = false;
        ReviewText.Text = "No review selected.";
    }

    private void UpdateReviewSelection(string keepPath)
    {
        if (_currentDuplicateGroup is null)
        {
            return;
        }

        var selection = new DuplicateReviewSelection(_currentDuplicateGroup, keepPath);
        var removeTarget = selection.RemoveFilePaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(removeTarget))
        {
            _pendingOperation = null;
            ReviewText.Text = "No safe removal target is available for review.";
            return;
        }

        var plan = new DuplicateCleanupPlan(selection, DuplicateAction.Delete);
        if (!plan.CanExecute)
        {
            _pendingOperation = null;
            ReviewText.Text = "The selected review cannot produce a safe cleanup target.";
            return;
        }

        var execution = new DuplicateCleanupExecutor().CreateExecution(plan);
        _pendingOperation = new DuplicateCleanupAdapter().CreateOperation(execution);
        CleanupButton.IsEnabled = _scanCancellation is null && !_cleanupInProgress;
        ReviewText.Text = $"Keep: {selection.KeepFilePath}; Remove: {string.Join(", ", selection.RemoveFilePaths)}";
        StatusText.Text = $"Reviewing duplicate group: keep '{selection.KeepFilePath}' and remove {plan.PathsToRemove.Count} duplicate file(s).";
    }

    private async Task<bool> ConfirmCleanupAsync(DuplicateCleanupOperation operation)
    {
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, IsDefault = true };
        var deleteButton = new Button { Content = "Permanently delete selected" };
        var dialog = new Window
        {
            Title = "Confirm duplicate cleanup",
            Width = 600,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Permanently delete {operation.PathsToRemove.Count} duplicate file(s)? This cannot be undone.\nKeep: {operation.KeepPath}",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new ScrollViewer
                    {
                        MaxHeight = 150,
                        Content = new TextBlock
                        {
                            Text = string.Join(Environment.NewLine, operation.PathsToRemove),
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, deleteButton }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        deleteButton.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }
}
