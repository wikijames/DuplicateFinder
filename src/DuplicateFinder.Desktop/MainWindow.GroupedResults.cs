using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public partial class MainWindow
{
    private GroupedReviewRows _groupedRows = new(Array.Empty<DuplicateGroup>(), group => group.FilePaths[0]);
    private bool _synchronizingResults;

    private void SelectResultGroup(DuplicateGroup? group, bool scrollToGroup)
    {
        if (_synchronizingResults) return;
        _synchronizingResults = true;
        try
        {
            _workspace.SelectGroup(group);
            if (group is not null && GroupFilesList.SelectedItem is ReviewResultRow current && !ReferenceEquals(current.Group, group))
                GroupFilesList.SelectedItem = _groupedRows.HeaderFor(group);
            ResultsList.SelectedItem = ResultsList.ItemsSource?.Cast<DuplicateGroupItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Group, group));
            KeepFileComboBox.ItemsSource = Array.Empty<KeepFileChoice>();
            if (_workspace.Selection is { } selection)
            {
                var choices = selection.Group.FilePaths.Select(path => new KeepFileChoice(path)).ToList();
                KeepFileComboBox.ItemsSource = choices;
                KeepFileComboBox.SelectedItem = choices.First(choice => choice.FullPath == selection.KeepFilePath);
                if (scrollToGroup && _groupedRows.HeaderFor(selection.Group) is { } header)
                {
                    GroupFilesList.SelectedItem = header;
                    GroupFilesList.ScrollIntoView(header);
                }
            }
            RefreshReview();
        }
        finally { _synchronizingResults = false; }
    }

    private void GroupedFiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingResults || SetupIsBusy) return;
        if (GroupFilesList.SelectedItem is ReviewResultRow row) SelectResultGroup(row.Group, scrollToGroup: false);
        UpdatePreviewAction();
    }

    private void UpdatePreviewAction()
    {
        var file = GroupFilesList.SelectedItem as FileReviewRow;
        PreviewFileButton.Content = "Preview file";
        PreviewFileButton.IsVisible = file is not null && !file.CanCompareImage;
        PreviewFileButton.IsEnabled = !SetupIsBusy && file is not null;
    }

    private void ReviewGroupButton_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (SetupIsBusy || sender is not Button { DataContext: ReviewGroupHeader header }) return;
        SelectResultGroup(header.Group, scrollToGroup: false);
    }

    private void KeepFileButton_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (SetupIsBusy || sender is not Button { DataContext: FileReviewRow file }) return;
        SelectResultGroup(file.Group, scrollToGroup: false);
        _workspace.SelectKeepFile(file.FullPath);
        _synchronizingResults = true;
        try
        {
            KeepFileComboBox.SelectedItem = KeepFileComboBox.ItemsSource!.Cast<KeepFileChoice>()
                .First(choice => choice.FullPath == file.FullPath);
            RefreshReview();
        }
        finally { _synchronizingResults = false; }
        // The Keep button becomes hidden; return keyboard focus to the file row.
        GroupFilesList.SelectedItem = file;
        GroupFilesList.Focus();
    }

    private async void FileThumbnail_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (SetupIsBusy || sender is not Control { DataContext: FileReviewRow file } source) return;
        SelectResultGroup(file.Group, scrollToGroup: false);
        GroupFilesList.SelectedItem = file;
        await PreviewFileAsync(file, source);
    }
}
