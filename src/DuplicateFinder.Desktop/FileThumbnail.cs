using System;
using System.IO;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DuplicateFinder.Desktop;

internal sealed class FileThumbnail : Button
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _placeholder = new()
    {
        FontSize = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    };
    private CancellationTokenSource? _request;
    private Bitmap? _bitmap;
    private ThumbnailLoader? _loader;

    public FileThumbnail()
    {
        Width = Height = MinHeight = 64;
        Padding = new Avalonia.Thickness(2);
        Content = new Grid { Children = { _image, _placeholder } };
        AutomationProperties.SetAutomationId(this, "FileThumbnail");
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not MainWindow owner) return;
            _loader = owner.Thumbnails;
            _loader.Resumed += Load;
            Load();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_loader is not null) _loader.Resumed -= Load;
            _loader = null;
            Reset();
        };
        DataContextChanged += (_, _) => { if (_loader is not null) Load(); };
    }

    private void Reset()
    {
        _request?.Cancel();
        _request = null;
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _placeholder.IsVisible = true;
    }

    private async void Load()
    {
        Reset();
        if (DataContext is not FileReviewRow row || _loader is not { } loader) return;
        _placeholder.Text = row.CanCompareImage ? "IMAGE" : "FILE";
        AutomationProperties.SetName(this, $"Preview {row.FileName}, {row.FullPath}");
        ToolTip.SetTip(this, row.CanCompareImage ? "Loading a small image preview..." : "Preview file; thumbnails support PNG/JPEG images.");
        if (!row.CanCompareImage) return;
        using var request = new CancellationTokenSource();
        _request = request;
        try
        {
            var result = await loader.LoadAsync(row.FullPath, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_request, request)) return;
            ToolTip.SetTip(this, result.Message);
            if (result.Thumbnail is null) return;
            using var stream = new MemoryStream(result.Thumbnail, writable: false);
            _bitmap = new Bitmap(stream);
            _image.Source = _bitmap;
            _placeholder.IsVisible = false;
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            if (ReferenceEquals(_request, request)) ToolTip.SetTip(this, "Preview unavailable. Open the image link for details.");
        }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
    }
}
