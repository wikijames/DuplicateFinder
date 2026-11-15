using System.Text;
using System.Runtime.InteropServices;
using DuplicateFinder.Desktop;

namespace DuplicateFinder.Tests;

public sealed class TextFilePreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder-Preview-" + Guid.NewGuid().ToString("N"));

    public TextFilePreviewTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf8TextIsReadWithoutChangingFile(bool bom)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "sample.txt");
        await File.WriteAllTextAsync(path, "Synthetic π text\r\nSecond line", new UTF8Encoding(bom));
        var original = await File.ReadAllBytesAsync(path);
        var result = await TextFilePreview.ReadAsync(path);
        Assert.Equal("Synthetic π text\r\nSecond line", result.Text);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task UnsupportedTypeDoesNotRequireOpeningFile()
    {
        var result = await TextFilePreview.ReadAsync(Path.Combine(_root, "missing.exe"));
        Assert.Null(result.Text);
        Assert.Contains("file type", result.Message);
    }

    [Theory]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    public async Task FileSizeLimitIsEnforced(int length, bool supported)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "large.txt");
        await File.WriteAllTextAsync(path, new string('x', length), new UTF8Encoding(false));
        var result = await TextFilePreview.ReadAsync(path);
        Assert.Equal(supported, result.Text is not null);
        if (!supported) Assert.Contains("64 KiB", result.Message);
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 2 })]
    [InlineData(new byte[] { 255, 254, 128 })]
    public async Task BinaryAndInvalidUtf8UseMetadataOnly(byte[] bytes)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "binary.txt");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.Null((await TextFilePreview.ReadAsync(path)).Text);
    }

    [Fact]
    public async Task MissingAndLockedFilesHaveSanitizedFailureMessages()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "locked.txt");
        var missing = await TextFilePreview.ReadAsync(path);
        Assert.Null(missing.Text);
        Assert.DoesNotContain(_root, missing.Message);
        await File.WriteAllTextAsync(path, "synthetic");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Null((await TextFilePreview.ReadAsync(path)).Text);
    }

    [Fact]
    public async Task HardLinkedFilesAreNotPreviewed()
    {
        if (!OperatingSystem.IsWindows()) return;
        var original = Path.Combine(_root, "original.txt");
        var alias = Path.Combine(_root, "alias.txt");
        await File.WriteAllTextAsync(original, "Synthetic linked text");
        if (!CreateHardLink(alias, original, IntPtr.Zero)) throw new IOException("Synthetic hard link could not be created.");
        Assert.Null((await TextFilePreview.ReadAsync(original)).Text);
        Assert.Null((await TextFilePreview.ReadAsync(alias)).Text);
        Assert.Equal("Synthetic linked text", await File.ReadAllTextAsync(original));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string alias, string original, IntPtr security);

    [Fact]
    public async Task CancellationIsObservedBeforeOpening()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TextFilePreview.ReadAsync(Path.Combine(_root, "missing.txt"), cancellation.Token));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
