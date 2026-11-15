using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DuplicateFinder.Desktop;

public sealed record TextPreviewResult(string Message, string? Text = null);

public static class TextFilePreview
{
    public const int MaximumBytes = 65536;
    private static readonly string[] Extensions = { ".txt", ".md", ".csv", ".json", ".xml", ".log", ".cs", ".css", ".html", ".yml", ".yaml" };

    public static async Task<TextPreviewResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return new("Metadata only: this file type has no text preview.");
        if (!OperatingSystem.IsWindows()) return new("Text preview is currently supported on Windows only.");
        try
        {
            var bytes = await WindowsCleanupSession.ReadPreviewBytesAsync(path, cancellationToken);
            if (bytes.Length > MaximumBytes) return new("Metadata only: text preview is limited to files of 64 KiB or less.");
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
            var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            if (text.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
                return new("Metadata only: this file contains non-text characters.");
            return new("Read-only UTF-8 text from the current file. Preview does not verify scan results.", text);
        }
        catch (DecoderFallbackException) { return new("Metadata only: this file is not supported UTF-8 text."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new("Preview unavailable: the file changed, is inaccessible, linked, or uses an unsupported path.");
        }
    }
}
