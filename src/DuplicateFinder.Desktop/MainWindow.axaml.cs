using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
        var startFolder = string.IsNullOrWhiteSpace(RootPathBox.Text)
            ? null
            : RootPathBox.Text.Trim();

        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a folder to scan",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder is null ? null : await StorageProvider.TryGetFolderFromPathAsync(new Uri(startFolder))
        });

        var selectedPath = folder.FirstOrDefault()?.Path.LocalPath;
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

    private void CleanupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingOperation is null)
        {
            ResultsList.ItemsSource = new[] { "No duplicate group is currently selected for cleanup." };
            return;
        }

        var confirmed = ConfirmCleanup(_pendingOperation.KeepPath);
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

        var decision = new DuplicateReviewDecision(_currentDuplicateGroup, removeTarget, DuplicateAction.Delete);
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

    private static bool ConfirmCleanup(string keepPath)
    {
        return true;
    }
}
