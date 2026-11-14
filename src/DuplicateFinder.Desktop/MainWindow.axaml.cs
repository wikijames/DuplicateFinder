using System;
using System.IO;
using System.Linq;
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

    public MainWindow()
    {
        InitializeComponent();
        RootPathBox.Text = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Desktop", "demo");
        KeepFileComboBox.SelectionChanged += KeepFileComboBox_SelectionChanged;
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
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
        TryScanCurrentRoot();
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        TryScanCurrentRoot();
    }

    private async void CleanupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingOperation is null)
        {
            ResultsList.ItemsSource = new[] { "No duplicate group is currently selected for cleanup." };
            return;
        }

        var confirmed = await ConfirmCleanupAsync(_pendingOperation.KeepPath);
        if (!confirmed)
        {
            ResultsList.ItemsSource = new[] { "Cleanup cancelled by the user." };
            return;
        }

        var service = new WindowsCleanupService();
        service.Execute(_pendingOperation);
        ResultsList.ItemsSource = new[] { "Cleanup executed successfully." };
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

    private void TryScanCurrentRoot()
    {
        var rootPath = RootPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            ResultsList.ItemsSource = new[] { "Enter a valid scan root directory first." };
            return;
        }

        if (!Directory.Exists(rootPath))
        {
            ResultsList.ItemsSource = new[] { $"Directory not found: {rootPath}" };
            return;
        }

        var coordinator = new DuplicateScanCoordinator();
        var report = coordinator.CreateReport(rootPath, new ScanOptions
        {
            AllowedExtensions = new[] { ".txt" }
        });
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

        var decision = selection.ToDecision(DuplicateAction.Delete, removeTarget);
        var plan = new DuplicateCleanupPlan(decision);
        if (!plan.CanExecute)
        {
            _pendingOperation = null;
            ReviewText.Text = "The selected review cannot produce a safe cleanup target.";
            return;
        }

        var execution = new DuplicateCleanupExecutor().CreateExecution(plan);
        _pendingOperation = new DuplicateCleanupAdapter().CreateOperation(execution);
        ReviewText.Text = $"Keep: {selection.KeepFilePath}; Remove: {string.Join(", ", selection.RemoveFilePaths)}";
        StatusText.Text = $"Reviewing duplicate group: keep '{selection.KeepFilePath}' and remove '{removeTarget}'.";
    }

    private async Task<bool> ConfirmCleanupAsync(string keepPath)
    {
        var cancelButton = new Button { Content = "Cancel", IsCancel = true };
        var deleteButton = new Button { Content = "Delete selected", IsDefault = true };
        var dialog = new Window
        {
            Title = "Confirm duplicate cleanup",
            Width = 440,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"This will delete the selected duplicate file(s) while keeping '{keepPath}'. Do you want to continue?",
                        TextWrapping = TextWrapping.Wrap
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