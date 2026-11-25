using System.Runtime.InteropServices;
using DuplicateFinder.Desktop;
using SkiaSharp;

namespace DuplicateFinder.Tests;

public sealed class ImageFilePreviewTests : IDisposable
{
    [Fact]
    public void ListThumbnailOutputIsBoundedToNinetySixPixels()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = WriteImage(".png", SKEncodedImageFormat.Png, 1200, 600);
        var result = ImageFilePreview.Read(path, thumbnailDimension: ThumbnailLoader.ThumbnailSize);
        Assert.NotNull(result.Thumbnail);
        using var decoded = SKBitmap.Decode(result.Thumbnail);
        Assert.Equal(96, decoded.Width);
        Assert.Equal(48, decoded.Height);
    }
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder-ImagePreview-" + Guid.NewGuid().ToString("N"));
    public ImageFilePreviewTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(".png", SKEncodedImageFormat.Png, 1200, 600, 768, 384)]
    [InlineData(".JPG", SKEncodedImageFormat.Jpeg, 600, 1200, 384, 768)]
    [InlineData(".jpeg", SKEncodedImageFormat.Jpeg, 80, 40, 80, 40)]
    public void ThumbnailIsBoundedAndSourceIsUnchanged(string extension, SKEncodedImageFormat format, int width, int height, int outputWidth, int outputHeight)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = WriteImage(extension, format, width, height);
        var original = File.ReadAllBytes(path);
        var result = ImageFilePreview.Read(path);
        Assert.NotNull(result.Thumbnail);
        using var thumbnail = SKBitmap.Decode(result.Thumbnail);
        Assert.Equal(outputWidth, thumbnail.Width);
        Assert.Equal(outputHeight, thumbnail.Height);
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(original.Length, result.Bytes);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(4000, 3000, true)]
    [InlineData(4001, 3000, false)]
    [InlineData(8192, 1, true)]
    [InlineData(8193, 1, false)]
    [InlineData(int.MaxValue, int.MaxValue, false)]
    [InlineData(0, 20, false)]
    public void PixelAndDimensionLimitsAreOverflowSafe(int width, int height, bool allowed) =>
        Assert.Equal(allowed, ImageFilePreview.DimensionsAllowed(width, height));

    [Theory]
    [InlineData(4001, 3000)]
    [InlineData(8193, 1)]
    public void OversizedImageUsesMetadataWithoutThumbnail(int width, int height)
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = ImageFilePreview.Read(WriteImage(".png", SKEncodedImageFormat.Png, width, height));
        Assert.Null(result.Thumbnail);
        Assert.Equal(width, result.Width);
        Assert.Contains("12 megapixels", result.Message);
    }

    [Fact]
    public void UnsupportedTypeDoesNotOpenAFile()
    {
        var result = ImageFilePreview.Read(Path.Combine(_root, "missing.gif"));
        Assert.Null(result.Thumbnail);
        Assert.Contains("PNG and JPEG", result.Message);
    }

    [Fact]
    public void CorruptTruncatedAndDisguisedImagesHaveNoThumbnail()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "damaged.png");
        File.WriteAllText(path, "Synthetic invalid image");
        Assert.Null(ImageFilePreview.Read(path).Thumbnail);
        var valid = File.ReadAllBytes(WriteImage(".png", SKEncodedImageFormat.Png, 400, 300));
        File.WriteAllBytes(path, valid[..(valid.Length / 2)]);
        Assert.Null(ImageFilePreview.Read(path).Thumbnail);
        using var bitmap = new SKBitmap(2, 2);
        using var gifAlternative = bitmap.Encode(SKEncodedImageFormat.Webp, 100);
        File.WriteAllBytes(path, gifAlternative.ToArray());
        Assert.Null(ImageFilePreview.Read(path).Thumbnail);
    }

    [Fact]
    public void EncodedFileSizeIsLimitedBeforeDecode()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "oversized.png");
        using (var stream = File.Create(path)) stream.SetLength(ImageFilePreview.MaximumBytes + 1L);
        var result = ImageFilePreview.Read(path);
        Assert.Null(result.Thumbnail);
        Assert.Contains("16 MiB", result.Message);
    }

    [Fact]
    public void MissingLockedAndHardLinkedImagesFailWithoutLeakingPaths()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "missing.png");
        Assert.DoesNotContain(_root, ImageFilePreview.Read(path).Message);
        path = WriteImage(".png", SKEncodedImageFormat.Png, 20, 20);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Null(ImageFilePreview.Read(path).Thumbnail);
        var alias = Path.Combine(_root, "alias.png");
        Assert.True(CreateHardLink(alias, path, IntPtr.Zero));
        Assert.Null(ImageFilePreview.Read(path).Thumbnail);
        Assert.Null(ImageFilePreview.Read(alias).Thumbnail);
    }

    [Fact]
    public void LinkedParentIsRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var path = WriteImage(".png", SKEncodedImageFormat.Png, 20, 20);
        File.Copy(path, Path.Combine(target, "sample.png"));
        var alias = Path.Combine(_root, "junction");
        // A junction needs no Developer Mode or elevation; the target is our own fixture only.
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(alias); start.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try { Assert.Null(ImageFilePreview.Read(Path.Combine(alias, "sample.png")).Thumbnail); }
        finally { Directory.Delete(alias); }
    }

    [Fact]
    public void CancellationIsObservedBeforeFileAccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ImageFilePreview.Read(Path.Combine(_root, "missing.png"), cancellation.Token));
    }

    [Theory]
    [InlineData(1, 255, 0, 0)]
    [InlineData(2, 0, 255, 0)]
    [InlineData(3, 255, 255, 0)]
    [InlineData(4, 0, 0, 255)]
    [InlineData(5, 255, 0, 0)]
    [InlineData(6, 0, 0, 255)]
    [InlineData(7, 255, 255, 0)]
    [InlineData(8, 0, 255, 0)]
    public void JpegOrientationIsApplied(int orientation, byte red, byte green, byte blue)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = WriteImage(".jpg", SKEncodedImageFormat.Jpeg, 256, 128);
        var jpeg = File.ReadAllBytes(path);
        byte[] exif = [0xFF, 0xE1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
        File.WriteAllBytes(path, jpeg[..2].Concat(exif).Concat(jpeg[2..]).ToArray());
        var result = ImageFilePreview.Read(path);
        Assert.NotNull(result.Thumbnail);
        using var thumbnail = SKBitmap.Decode(result.Thumbnail);
        Assert.Equal(orientation >= 5 ? 128 : 256, thumbnail.Width);
        Assert.Equal(orientation >= 5 ? 256 : 128, thumbnail.Height);
        var pixel = thumbnail.GetPixel(10, 10);
        Assert.InRange(Math.Abs(pixel.Red - red), 0, 15);
        Assert.InRange(Math.Abs(pixel.Green - green), 0, 15);
        Assert.InRange(Math.Abs(pixel.Blue - blue), 0, 15);
    }

    private string WriteImage(string extension, SKEncodedImageFormat format, int width, int height)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + extension);
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            foreach (var (color, x, y) in new[] { (SKColors.Red, 0, 0), (SKColors.Lime, width / 2, 0), (SKColors.Blue, 0, height / 2), (SKColors.Yellow, width / 2, height / 2) })
            {
                paint.Color = color;
                canvas.DrawRect(x, y, width / 2f, height / 2f, paint);
            }
        }
        using var encoded = bitmap.Encode(format, 100);
        File.WriteAllBytes(path, encoded.ToArray());
        return path;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string alias, string original, IntPtr security);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
