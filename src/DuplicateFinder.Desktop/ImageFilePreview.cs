using System;
using System.IO;
using System.Threading;
using SkiaSharp;

namespace DuplicateFinder.Desktop;

internal sealed record ImagePreviewResult(string Message, byte[]? Thumbnail = null, int Width = 0, int Height = 0, long Bytes = 0);

internal static class ImageFilePreview
{
    internal const int MaximumBytes = 16 * 1024 * 1024;
    internal const long MaximumPixels = 12_000_000;
    internal const int MaximumDimension = 8192;
    internal const int ThumbnailDimension = 768;

    internal static bool Supports(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

    // Called on a worker. Only a bounded thumbnail crosses to the UI; no full-size bitmap is retained.
    internal static ImagePreviewResult Read(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Supports(path)) return new("Metadata only: image previews support PNG and JPEG files.");
        if (!OperatingSystem.IsWindows()) return new("Image preview is currently supported on Windows only.");
        try
        {
            var bytes = ImagePreviewFileReader.Read(path, cancellationToken);
            using var input = new MemoryStream(bytes, writable: false);
            using var codec = SKCodec.Create(input);
            if (codec is null || codec.EncodedFormat is not SKEncodedImageFormat.Png and not SKEncodedImageFormat.Jpeg)
                return new("Preview unavailable: this is not a supported PNG or JPEG image.");
            var info = codec.Info;
            if (!DimensionsAllowed(info.Width, info.Height))
                return new("Metadata only: image preview is limited to 12 megapixels and 8192 pixels per side.", Width: info.Width, Height: info.Height, Bytes: bytes.Length);
            cancellationToken.ThrowIfCancellationRequested();
            using var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
                return new("Preview unavailable: the image is incomplete or damaged.");
            cancellationToken.ThrowIfCancellationRequested();
            var swapped = (int)codec.EncodedOrigin >= 5;
            var width = swapped ? info.Height : info.Width;
            var height = swapped ? info.Width : info.Height;
            var scale = Math.Min(1d, ThumbnailDimension / (double)Math.Max(width, height));
            using var thumbnail = new SKBitmap(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
            using (var canvas = new SKCanvas(thumbnail))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(thumbnail.Width / (float)width, thumbnail.Height / (float)height);
                canvas.Concat(Orientation(codec.EncodedOrigin, info.Width, info.Height));
                canvas.DrawBitmap(decoded, 0, 0);
            }
            using var encoded = thumbnail.Encode(SKEncodedImageFormat.Png, 100);
            cancellationToken.ThrowIfCancellationRequested();
            return new("Current file preview; rescan if files have changed. Preview does not verify duplicates.", encoded.ToArray(), width, height, bytes.Length);
        }
        catch (ImagePreviewLimitException) { return new("Metadata only: image preview is limited to files of 16 MiB or less."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return new("Preview unavailable: the image is damaged, inaccessible, linked, changed, or uses an unsupported path.");
        }
    }

    internal static bool DimensionsAllowed(int width, int height) => width > 0 && height > 0 &&
        width <= MaximumDimension && height <= MaximumDimension && (long)width * height <= MaximumPixels;

    private static SKMatrix Orientation(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => Matrix(-1, 0, width, 0, 1, 0),
        SKEncodedOrigin.BottomRight => Matrix(-1, 0, width, 0, -1, height),
        SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, height),
        SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
        SKEncodedOrigin.RightTop => Matrix(0, -1, height, 1, 0, 0),
        SKEncodedOrigin.RightBottom => Matrix(0, -1, height, -1, 0, width),
        SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, width),
        _ => SKMatrix.Identity
    };

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY) =>
        new() { ScaleX = scaleX, SkewX = skewX, TransX = transX, SkewY = skewY, ScaleY = scaleY, TransY = transY, Persp2 = 1 };
}
