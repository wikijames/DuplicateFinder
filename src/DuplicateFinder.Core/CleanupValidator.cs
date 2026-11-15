namespace DuplicateFinder.Core;

using System.Security.Cryptography;

/// <summary>Checks current file contents without modifying files.</summary>
public sealed class CleanupValidator
{
    public void Validate(DuplicateCleanupOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!operation.CanExecute || operation.PathsToRemove.Count == 0 ||
            operation.Action is not DuplicateAction.Delete and not DuplicateAction.MoveToRecycleBin)
        {
            throw new InvalidOperationException("This cleanup operation cannot be executed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        RejectReparsePoint(operation.KeepPath);
        using var keepStream = File.Open(operation.KeepPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (var path in operation.PathsToRemove)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(operation.KeepPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new IOException("Cleanup target is the keep file. Rescan before cleanup.");
            }

            RejectReparsePoint(path);
            using var targetStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ValidatePair(operation, keepStream, targetStream, cancellationToken);
        }
    }

    /// <summary>Validates already-open files; the caller retains ownership of both streams.</summary>
    public void ValidatePair(DuplicateCleanupOperation operation, Stream keepStream, Stream targetStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(keepStream);
        ArgumentNullException.ThrowIfNull(targetStream);
        if (string.IsNullOrWhiteSpace(operation.ContentHash))
        {
            throw new IOException("Scan fingerprint is missing. Rescan before cleanup.");
        }

        if (keepStream.Length != operation.SizeBytes || targetStream.Length != operation.SizeBytes)
        {
            throw new IOException("Files no longer match the scan. Rescan before cleanup.");
        }

        keepStream.Position = 0;
        targetStream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var keepBuffer = new byte[64 * 1024];
        var targetBuffer = new byte[keepBuffer.Length];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keepCount = keepStream.ReadAtLeast(keepBuffer, keepBuffer.Length, throwOnEndOfStream: false);
            var targetCount = targetStream.ReadAtLeast(targetBuffer, targetBuffer.Length, throwOnEndOfStream: false);
            if (keepCount != targetCount || !keepBuffer.AsSpan(0, keepCount).SequenceEqual(targetBuffer.AsSpan(0, targetCount)))
            {
                throw new IOException("Files no longer match. Rescan before cleanup.");
            }

            if (keepCount == 0)
            {
                break;
            }

            hash.AppendData(keepBuffer, 0, keepCount);
        }

        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), operation.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Files changed since the scan. Rescan before cleanup.");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Linked files require a new scan and review before cleanup.");
        }
    }
}
