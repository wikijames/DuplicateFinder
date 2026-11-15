using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace DuplicateFinder.Desktop;

public partial class MainWindow
{
    private void AppearanceBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox box || Application.Current is not { } application) return;
        application.RequestedThemeVariant = box.SelectedIndex switch
        {
            1 => Avalonia.Styling.ThemeVariant.Light,
            2 => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
    }

    private CancellationTokenSource? _previewCancellation;

    private void ResultsSearch_Changed(object? sender, TextChangedEventArgs e) => RefreshResultGroups();
    private void ResultsSort_Changed(object? sender, SelectionChangedEventArgs e) => RefreshResultGroups();

    private void RefreshResultGroups()
    {
        // XAML events can fire before the remaining named controls have been initialized.
        if (ResultsList is null || ResultsSortBox is null || VisibleGroupsText is null) return;
        var selected = _workspace.Selection?.Group;
        var groups = ResultGroupQuery.Apply(_workspace.Groups, ResultsSearchBox.Text,
            (ResultGroupSort)Math.Max(0, ResultsSortBox.SelectedIndex));
        ResultsList.ItemsSource = groups.Select(group => new DuplicateGroupItem(group)).ToList();
        VisibleGroupsText.Text = $"{groups.Count} of {_workspace.Groups.Count} groups";
        EmptyResultsText.IsVisible = groups.Count == 0;
        if (groups.Count == 0 && _workspace.Groups.Count > 0) EmptyResultsText.Text = "No matching groups. Clear the search to show all results.";
        // Hidden groups cannot retain an actionable cleanup selection.
        ResultsList.SelectedItem = ResultsList.ItemsSource.Cast<DuplicateGroupItem>().FirstOrDefault(item => ReferenceEquals(item.Group, selected));
    }

    private async void PreviewFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SetupIsBusy || GroupFilesList.SelectedItem is not DuplicateFileItem file) return;
        _setupDialogOpen = true;
        using var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        SetScanInProgress(false);
        try
        {
            StatusText.Text = "Reading bounded text preview...";
            var result = await Task.Run(() => TextFilePreview.ReadAsync(file.FullPath, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
            var content = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12 };
            content.Children.Add(new TextBlock { Text = file.FullPath, TextWrapping = TextWrapping.Wrap });
            var message = new TextBlock { Text = result.Message, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(message, 1);
            content.Children.Add(message);
            var text = new TextBox { Text = result.Text ?? string.Empty, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap };
            Avalonia.Automation.AutomationProperties.SetName(text, "Read-only file preview");
            Grid.SetRow(text, 2);
            content.Children.Add(text);
            Grid.SetRow(close, 3);
            content.Children.Add(close);
            var dialog = new Window { Title = "File preview", Width = 760, Height = 520, Content = content, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            close.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);
            StatusText.Text = _scanCompletionStatus ?? "Preview closed.";
        }
        catch (OperationCanceledException) { }
        finally
        {
            _previewCancellation = null;
            _setupDialogOpen = false;
            SetScanInProgress(false);
        }
    }
}
