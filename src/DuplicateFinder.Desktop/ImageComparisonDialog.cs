using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DuplicateFinder.Desktop;

internal sealed class ImageComparisonDialog : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _keepPath;
    private readonly IReadOnlyList<string> _copies;
    private readonly Image _keepImage = new() { Stretch = Stretch.Uniform, Height = 260 };
    private readonly Image _copyImage = new() { Stretch = Stretch.Uniform, Height = 260 };
    private readonly TextBlock _keepMetadata = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _copyMetadata = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _copyPath = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 92 };
    private readonly Button _previous = new() { Content = "Previous copy" };
    private readonly Button _next = new() { Content = "Next copy" };
    private readonly TextBlock _position = new() { VerticalAlignment = VerticalAlignment.Center };
    private Task _loadTask = Task.CompletedTask;
    private int _index;
    private Bitmap? _keepBitmap;
    private Bitmap? _copyBitmap;

    internal ImageComparisonDialog(string keepPath, IReadOnlyList<string> copies, string selectedPath, bool permanentDelete)
    {
        _keepPath = keepPath;
        _copies = copies;
        for (var index = 0; index < copies.Count; index++)
            if (string.Equals(copies[index], selectedPath, StringComparison.OrdinalIgnoreCase)) _index = index;
        Title = "Compare duplicate images";
        Width = 1000; Height = 650; MinWidth = 480; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var keepPathBox = new TextBox { Text = keepPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 92 };
        NameControl(keepPathBox, "ImageKeepPath", "Kept image file path");
        NameControl(_copyPath, "ImageCopyPath", "Extra copy image file path");
        NameControl(_keepImage, "KeptImage", "Preview of the protected kept image");
        NameControl(_copyImage, "ExtraCopyImage", "Preview of the extra copy");
        AutomationProperties.SetAutomationId(_keepMetadata, "KeptImageMetadata");
        AutomationProperties.SetAutomationId(_copyMetadata, "CopyImageMetadata");
        NameControl(_previous, "PreviousImageCopy", "Preview previous extra copy");
        NameControl(_next, "NextImageCopy", "Preview next extra copy");
        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 20, RowSpacing = 20 };
        var keepPane = Pane("Keep · protected file", keepPathBox, _keepMetadata, _keepImage);
        var copyPane = Pane(permanentDelete ? "Extra copy · marked for permanent deletion" : "Extra copy · marked for quarantine", _copyPath, _copyMetadata, _copyImage);
        Grid.SetColumn(copyPane, 1);
        panes.Children.Add(keepPane); panes.Children.Add(copyPane);
        panes.SizeChanged += (_, _) =>
        {
            var narrow = panes.Bounds.Width < 640;
            panes.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
            panes.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            Grid.SetColumn(copyPane, narrow ? 0 : 1); Grid.SetRow(copyPane, narrow ? 1 : 0);
        };
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        NameControl(close, "CloseImageComparison", "Close image comparison");
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 14 };
        root.Children.Add(new TextBlock { Text = "Exact duplicate image previews", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "Read-only comparison. Keep choices stay unchanged. Images show current file contents, not a new duplicate verification. PNG/JPEG only; 16 MiB, 12 megapixels and 8192 pixels per side maximum.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(panes);
        var navigation = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { _previous, _position, _next }) { control.Margin = new Thickness(0, 0, 12, 8); navigation.Children.Add(control); }
        body.Children.Add(navigation);
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(close, 2); root.Children.Add(close); Content = root;
        close.Click += (_, _) => Close();
        _previous.Click += (_, _) => Navigate(-1);
        _next.Click += (_, _) => Navigate(1);
        Opened += (_, _) => { close.Focus(); _loadTask = LoadAsync(includeKeep: true); };
        Closed += (_, _) => _cancellation.Cancel();
    }

    internal async Task ShowComparisonAsync(Window owner)
    {
        try { await ShowDialog(owner); }
        finally
        {
            _cancellation.Cancel();
            await _loadTask;
            _keepImage.Source = null; _copyImage.Source = null;
            _keepBitmap?.Dispose(); _copyBitmap?.Dispose();
            _cancellation.Dispose();
        }
    }

    private void Navigate(int offset)
    {
        if (!_loadTask.IsCompleted) return;
        _index = Math.Clamp(_index + offset, 0, _copies.Count - 1);
        _loadTask = LoadAsync(includeKeep: false);
    }

    private async Task LoadAsync(bool includeKeep)
    {
        _previous.IsEnabled = _next.IsEnabled = false;
        _copyPath.Text = _copies[_index];
        _position.Text = $"Extra copy {_index + 1} of {_copies.Count}";
        _copyImage.Source = null; _copyBitmap?.Dispose(); _copyBitmap = null;
        _copyMetadata.Text = "Loading image preview...";
        if (includeKeep) _keepMetadata.Text = "Loading image preview...";
        try
        {
            if (includeKeep)
            {
                var kept = await Task.Run(() => ImageFilePreview.Read(_keepPath, _cancellation.Token));
                _cancellation.Token.ThrowIfCancellationRequested();
                _keepBitmap = Apply(kept, _keepMetadata, _keepImage);
            }
            var copy = await Task.Run(() => ImageFilePreview.Read(_copies[_index], _cancellation.Token));
            _cancellation.Token.ThrowIfCancellationRequested();
            _copyBitmap = Apply(copy, _copyMetadata, _copyImage);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            _copyMetadata.Text = "Preview unavailable. Close the comparison and rescan if files have changed.";
        }
        finally
        {
            if (!_cancellation.IsCancellationRequested)
            {
                _previous.IsEnabled = _index > 0;
                _next.IsEnabled = _index + 1 < _copies.Count;
            }
        }
    }

    private static Bitmap? Apply(ImagePreviewResult result, TextBlock metadata, Image image)
    {
        metadata.Text = result.Width > 0 ? $"{result.Width} × {result.Height} px · {DisplayFileSize.Format(result.Bytes)}\n{result.Message}" : result.Message;
        if (result.Thumbnail is null) return null;
        using var stream = new MemoryStream(result.Thumbnail, writable: false);
        var bitmap = new Bitmap(stream);
        image.Source = bitmap;
        return bitmap;
    }

    private static StackPanel Pane(string title, TextBox path, TextBlock metadata, Image image) =>
        new() { Spacing = 10, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, path, metadata, image } };

    private static void NameControl(Control control, string id, string name)
    {
        AutomationProperties.SetAutomationId(control, id);
        AutomationProperties.SetName(control, name);
    }
}
