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
    private readonly DuplicateReviewWorkspace _workspace = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _cleanupInProgress;

    public MainWindow()
    {
        InitializeComponent();
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
        ClearCurrentResults("Folder selected. Choose extensions or scan all file types.");
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await TryScanCurrentRootAsync();
    }

    private void ScanSetup_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_workspace.Groups.Count > 0 && !_cleanupInProgress && _scanCancellation is null)
        {
            ClearCurrentResults("Scan setup changed. Scan again before reviewing removal.");
        }
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
        BrowseButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        ResultsList.IsEnabled = !busy;
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
            options = ScanSetup.CreateOptions(ExtensionsBox.Text);
        }
        catch (ArgumentException)
        {
            ClearCurrentResults("Use comma-separated extensions such as .txt, .jpg or .pdf. Leave blank to scan all file types.");
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
