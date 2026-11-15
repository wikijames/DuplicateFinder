using System;
using System.Collections.Generic;
using System.IO;

namespace DuplicateFinder.Desktop;

public sealed record QuarantineEntry(string OriginalPath, string StoredName, long SizeBytes, string ContentHash);

public sealed record QuarantineBatch(string DirectoryPath, string ManifestFingerprint, IReadOnlyList<QuarantineEntry> Entries);

public sealed record RestoreResult(int RestoredCount, int AlreadyOriginalCount);

public sealed class QuarantineInterruptedException(string batchDirectory, int completedCount, Exception innerException)
    : IOException("Recovery operation stopped. Keep the batch and restore it after resolving the failure.", innerException)
{
    public string BatchDirectory { get; } = batchDirectory;
    public int CompletedCount { get; } = completedCount;
}
