using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public sealed class QuarantineService
{
    public const string FolderName = ".DuplicateFinder-Quarantine";
    public const string ManifestName = "recovery.manifest";
    private readonly Action<int>? _afterMove;

    public QuarantineService() { }
    internal QuarantineService(Action<int> afterMove) => _afterMove = afterMove;

    public QuarantineBatch Quarantine(DuplicateCleanupOperation operation, string destinationFolder)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Action != DuplicateAction.Quarantine) throw new InvalidOperationException("An explicit quarantine operation is required.");
        var destination = Path.GetDirectoryName(WindowsCleanupSession.GetSupportedPath(Path.Combine(destinationFolder, "destination-check")))!;
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException("Select an existing quarantine destination folder.");
        var originals = operation.PathsToRemove.Select(WindowsCleanupSession.GetSupportedPath).ToArray();
        foreach (var path in originals.Prepend(operation.KeepPath))
        {
            if (!SameDrive(path, destination)) throw new IOException("Quarantine requires all reviewed files and the destination on the same drive.");
            if (IsQuarantinePath(path)) throw new IOException("Files inside quarantine cannot be quarantined again.");
        }

        using var session = WindowsCleanupSession.Prepare(operation);
        var batchId = Guid.NewGuid().ToString("N");
        var batchDirectory = Path.Combine(destination, FolderName, "batch-" + batchId);
        Directory.CreateDirectory(batchDirectory);
        var entries = originals.Select(path => new QuarantineEntry(path, Guid.NewGuid().ToString("N") + ".bin", operation.SizeBytes, operation.ContentHash!)).ToArray();
        session.PrepareDestinations(entries.Select(entry => Path.Combine(batchDirectory, entry.StoredName)).ToArray());

        var plainText = JsonSerializer.SerializeToUtf8Bytes(new BatchManifest(1, batchId, entries));
        if (plainText.Length > 1024 * 1024 - 4096) throw new IOException("Recovery manifest is too large. Review a smaller group.");
        var protectedData = WindowsDataProtection.Protect(plainText);
        using var manifest = new FileStream(Path.Combine(batchDirectory, ManifestName), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        manifest.Write(protectedData);
        manifest.Flush(flushToDisk: true);
        try
        {
            session.MoveTargets(_afterMove);
            return new QuarantineBatch(batchDirectory, Convert.ToHexString(SHA256.HashData(protectedData)), Array.AsReadOnly(entries));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new QuarantineInterruptedException(batchDirectory, session.MovedCount, exception);
        }
    }

    public QuarantineBatch Inspect(string batchDirectory)
    {
        var (file, batch) = OpenManifest(batchDirectory);
        using (file) return batch;
    }

    public RestoreResult Restore(QuarantineBatch reviewedBatch)
    {
        ArgumentNullException.ThrowIfNull(reviewedBatch);
        var (file, currentBatch) = OpenManifest(reviewedBatch.DirectoryPath);
        using (file)
        {
            if (!string.Equals(currentBatch.ManifestFingerprint, reviewedBatch.ManifestFingerprint, StringComparison.Ordinal))
            {
                throw new IOException("Recovery manifest changed after review. Inspect the batch again.");
            }

            using var session = WindowsCleanupSession.PrepareRestore(currentBatch.DirectoryPath, currentBatch.Entries);
            try
            {
                session.MoveTargets(_afterMove);
                return new RestoreResult(session.MovedCount, session.AlreadyOriginalCount);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new QuarantineInterruptedException(currentBatch.DirectoryPath, session.MovedCount, exception);
            }
        }
    }

    private static (FileStream File, QuarantineBatch Batch) OpenManifest(string batchDirectory)
    {
        var directory = Path.GetDirectoryName(WindowsCleanupSession.GetSupportedPath(Path.Combine(batchDirectory, ManifestName)))!;
        var file = new FileStream(Path.Combine(directory, ManifestName), FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (file.Length is < 1 or > 1024 * 1024) throw new IOException("Invalid recovery manifest size.");
            var protectedData = new byte[(int)file.Length];
            file.ReadExactly(protectedData);
            var manifest = JsonSerializer.Deserialize<BatchManifest>(WindowsDataProtection.Unprotect(protectedData));
            if (manifest is null || manifest.Version != 1 || !Guid.TryParseExact(manifest.BatchId, "N", out _) ||
                Path.GetFileName(directory) != "batch-" + manifest.BatchId || manifest.Entries is null || manifest.Entries.Length is < 1 or > 128)
            {
                throw new IOException("Unsupported recovery manifest.");
            }

            var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var storedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.OriginalPath) || string.IsNullOrWhiteSpace(entry.StoredName) ||
                    !entry.StoredName.EndsWith(".bin", StringComparison.Ordinal) || !Guid.TryParseExact(entry.StoredName[..^4], "N", out _) ||
                    entry.SizeBytes < 0 || entry.ContentHash is null || entry.ContentHash.Length != 64 || entry.ContentHash.Any(character => !Uri.IsHexDigit(character)))
                {
                    throw new IOException("Invalid recovery entry.");
                }
                var original = WindowsCleanupSession.GetSupportedPath(entry.OriginalPath);
                if (!string.Equals(original, entry.OriginalPath, StringComparison.OrdinalIgnoreCase) ||
                    !SameDrive(original, directory) || IsQuarantinePath(original) || !originals.Add(original) || !storedNames.Add(entry.StoredName))
                {
                    throw new IOException("Invalid or overlapping recovery paths.");
                }
            }
            return (file, new QuarantineBatch(directory, Convert.ToHexString(SHA256.HashData(protectedData)), Array.AsReadOnly(manifest.Entries)));
        }
        catch (JsonException exception)
        {
            file.Dispose();
            throw new IOException("Recovery manifest could not be read.", exception);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static bool SameDrive(string first, string second) =>
        string.Equals(Path.GetPathRoot(first), Path.GetPathRoot(second), StringComparison.OrdinalIgnoreCase);

    private static bool IsQuarantinePath(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => string.Equals(segment, FolderName, StringComparison.OrdinalIgnoreCase));

    private sealed record BatchManifest(int Version, string BatchId, QuarantineEntry[] Entries);
}
