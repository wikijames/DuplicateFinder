using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public partial class MainWindow
{
    private readonly ScanFolderSelection _scanFolders = new();
    private ScanFilterSettings _scanFilters = new();
    private bool _hasScanResults;
    private bool _setupDialogOpen;

    private bool SetupIsBusy => _scanCancellation is not null || _cleanupInProgress || _setupDialogOpen;

    private void AddScanFolders(IEnumerable<string> paths)
    {
        _scanFolders.Add(paths);
        InvalidateScanSetup();
        RootPathBox.Text = string.Empty;
        UpdateFolderCount();
        StatusText.Text = $"{_scanFolders.Folders.Count} folder(s) selected. Overlapping folders are scanned once; filters apply to all folders.";
    }

    private void UpdateFolderCount()
    {
        ManageFoldersButton.Content = $"Folders ({_scanFolders.Folders.Count})";
        ToolTip.SetTip(ManageFoldersButton, string.Join(Environment.NewLine, _scanFolders.Folders));
    }

    private void AddFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SetupIsBusy) return;
        try { AddScanFolders(new[] { RootPathBox.Text?.Trim() ?? string.Empty }); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Add an existing local folder. The previous folder selection is unchanged.";
        }
    }

    private void ConfigureFolderDrop()
    {
        DragDrop.SetAllowDrop(ScanSetupPanel, true);
        ScanSetupPanel.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = !SetupIsBusy && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        ScanSetupPanel.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            e.Handled = true;
            e.DragEffects = DragDropEffects.None;
            if (SetupIsBusy) return;
            try
            {
                AddScanFolders(FolderDropInput.GetPaths(e.DataTransfer));
                e.DragEffects = DragDropEffects.Copy;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                StatusText.Text = "Drop existing local folders only. Nothing was added or scanned.";
            }
        });
    }

    private async void ManageFoldersButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SetupIsBusy) return;
        _setupDialogOpen = true;
        SetScanInProgress(false);
        try
        {
            var list = new ListBox { ItemsSource = _scanFolders.Folders };
            AutomationProperties.SetAutomationId(list, "ScanFoldersList");
            AutomationProperties.SetName(list, "Selected scan folders");
            var remove = new Button { Content = "Remove from scan", IsEnabled = false };
            var clear = new Button { Content = "Clear selection" };
            var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { remove, clear, close } };
            var content = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
            content.Children.Add(new TextBlock { Text = "These folders are scanned together. Parent folders cover selected subfolders; scan filters still apply. Removing an entry does not delete anything.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            Grid.SetRow(list, 1);
            Grid.SetRow(actions, 2);
            content.Children.Add(list);
            content.Children.Add(actions);
            var dialog = new Window { Title = "Selected scan folders", Width = 740, Height = 420, MinWidth = 480, MinHeight = 320, Content = content, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            list.SelectionChanged += (_, _) => remove.IsEnabled = list.SelectedItem is string;
            remove.Click += (_, _) =>
            {
                if (list.SelectedItem is string path) _scanFolders.Remove(path);
                list.ItemsSource = _scanFolders.Folders;
                InvalidateScanSetup();
                UpdateFolderCount();
            };
            clear.Click += (_, _) =>
            {
                _scanFolders.Clear();
                RootPathBox.Text = string.Empty;
                list.ItemsSource = _scanFolders.Folders;
                InvalidateScanSetup();
                UpdateFolderCount();
            };
            close.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);
        }
        finally
        {
            _setupDialogOpen = false;
            SetScanInProgress(false);
            ManageFoldersButton.Focus();
        }
    }

    private async void ScanSettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SetupIsBusy) return;
        _setupDialogOpen = true;
        SetScanInProgress(false);
        try
        {
            var minimum = new TextBox { Text = _scanFilters.MinimumSizeMiB, PlaceholderText = "No minimum" };
            var maximum = new TextBox { Text = _scanFilters.MaximumSizeMiB, PlaceholderText = "No maximum" };
            var hidden = new CheckBox { Content = "Include hidden files and folders", IsChecked = _scanFilters.IncludeHidden };
            var exclusions = new TextBox { Text = _scanFilters.ExcludedFolderNames, PlaceholderText = "For example: node_modules, .git, build output" };
            AutomationProperties.SetAutomationId(minimum, "MinimumSizeBox");
            AutomationProperties.SetName(minimum, "Minimum size in MiB");
            AutomationProperties.SetAutomationId(maximum, "MaximumSizeBox");
            AutomationProperties.SetName(maximum, "Maximum size in MiB");
            AutomationProperties.SetAutomationId(hidden, "IncludeHiddenBox");
            AutomationProperties.SetAutomationId(exclusions, "ExcludedFoldersBox");
            AutomationProperties.SetName(exclusions, "Additional excluded folder names");
            var validation = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            AutomationProperties.SetName(validation, "Scan settings validation");
            AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite);
            var apply = new Button { Content = "Apply settings" };
            var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true };
            var reset = new Button { Content = "Reset defaults" };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { reset, cancel, apply } };
            var content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Minimum size (MiB; 1 MiB = 1,048,576 bytes)" }, minimum,
                    new TextBlock { Text = "Maximum size (MiB)" }, maximum, hidden,
                    new TextBlock { Text = "Additional excluded subfolder names (comma-separated)" }, exclusions,
                    new TextBlock { Text = "Protected defaults and quarantine exclusions remain active. Options apply to every selected folder.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    validation
                }
            };
            content.Margin = new Thickness(0);
            var dialog = CreateReviewDialog("Scan settings", content, actions, cancel);
            ScanFilterSettings? reviewedSettings = null;
            apply.Click += (_, _) =>
            {
                var settings = new ScanFilterSettings(minimum.Text ?? "", maximum.Text ?? "", hidden.IsChecked == true, exclusions.Text ?? "");
                try
                {
                    ScanSetup.ApplyFilters(new ScanOptions(), settings);
                    reviewedSettings = settings;
                    dialog.Close(true);
                }
                catch (ArgumentException)
                {
                    validation.Text = "Use non-negative MiB sizes, with minimum no larger than maximum. Exclusions must be folder names, not paths or wildcards.";
                }
            };
            cancel.Click += (_, _) => dialog.Close(false);
            reset.Click += (_, _) => { minimum.Text = ""; maximum.Text = ""; hidden.IsChecked = false; exclusions.Text = ""; validation.Text = ""; };
            if (await dialog.ShowDialog<bool>(this) && reviewedSettings is not null)
            {
                _scanFilters = reviewedSettings;
                InvalidateScanSetup();
                ScanSettingsButton.Content = _scanFilters == new ScanFilterSettings() ? "Scan settings" : "Scan settings •";
                StatusText.Text = "Scan settings updated. Scan again to apply them.";
            }
        }
        finally
        {
            _setupDialogOpen = false;
            SetScanInProgress(false);
            ScanSettingsButton.Focus();
        }
    }
}
