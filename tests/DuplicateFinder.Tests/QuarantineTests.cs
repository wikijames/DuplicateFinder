namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class QuarantineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly string _destination;
    private readonly string[] _paths;
    private readonly DuplicateCleanupOperation _operation;
    private const string Contents = "generated quarantine fixture, original contents";

    public QuarantineTests()
    {
        _source = Path.Combine(_root, "source");
        _destination = Path.Combine(_root, "destination");
        Directory.CreateDirectory(Path.Combine(_source, "first"));
        Directory.CreateDirectory(Path.Combine(_source, "second"));
        Directory.CreateDirectory(_destination);
        _paths = [Path.Combine(_source, "first", "copy.txt"), Path.Combine(_source, "second", "copy.txt"), Path.Combine(_source, "keep.txt")];
        foreach (var path in _paths) File.WriteAllText(path, Contents);
        var group = Assert.Single(new DuplicateDetector().FindDuplicates(_source));
        _operation = new DuplicateCleanupOperation(new DuplicateCleanupExecution(new DuplicateCleanupPlan(
            new DuplicateReviewSelection(group, _paths[2]), DuplicateAction.Quarantine)));
    }

    [Fact]
    public void QuarantineAndRestore_PreserveSameNamedFilesAndTheChosenKeepFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var batch = new QuarantineService().Quarantine(_operation, _destination);

        Assert.All(_paths.Take(2), path => Assert.False(File.Exists(path)));
        Assert.Equal(Contents, File.ReadAllText(_paths[2]));
        Assert.Equal(2, batch.Entries.Select(entry => entry.StoredName).Distinct().Count());
        Assert.All(batch.Entries, entry => Assert.Equal(Contents, File.ReadAllText(StoredPath(batch, entry))));

        var reopened = new QuarantineService().Inspect(batch.DirectoryPath);
        var result = new QuarantineService().Restore(reopened);
        Assert.Equal(2, result.RestoredCount);
        Assert.Equal(0, result.AlreadyOriginalCount);
        Assert.All(_paths, path => Assert.Equal(Contents, File.ReadAllText(path)));
        Assert.All(batch.Entries, entry => Assert.False(File.Exists(StoredPath(batch, entry))));
        Assert.True(File.Exists(Path.Combine(batch.DirectoryPath, QuarantineService.ManifestName)));
    }

    [Fact]
    public void InterruptedQuarantine_CanBeRecoveredFromThePrewrittenManifest()
    {
        if (!OperatingSystem.IsWindows()) return;
        var exception = Assert.Throws<QuarantineInterruptedException>(() =>
            new QuarantineService(_ => throw new IOException("simulated interruption")).Quarantine(_operation, _destination));

        Assert.Equal(1, exception.CompletedCount);
        var batch = new QuarantineService().Inspect(exception.BatchDirectory);
        var result = new QuarantineService().Restore(batch);
        Assert.Equal(1, result.RestoredCount);
        Assert.Equal(1, result.AlreadyOriginalCount);
        Assert.All(_paths, path => Assert.Equal(Contents, File.ReadAllText(path)));
    }

    [Fact]
    public void InterruptedRestore_CanBeResumedAndRepeatedWithoutOverwriting()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        var exception = Assert.Throws<QuarantineInterruptedException>(() =>
            new QuarantineService(_ => throw new IOException("simulated interruption")).Restore(batch));

        Assert.Equal(1, exception.CompletedCount);
        Assert.Equal(new RestoreResult(1, 1), service.Restore(batch));
        Assert.Equal(new RestoreResult(0, 2), service.Restore(batch));
        Assert.All(_paths, path => Assert.Equal(Contents, File.ReadAllText(path)));
    }

    [Fact]
    public void Restore_RejectsAnExistingOriginalBeforeMovingAnyFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        File.WriteAllText(_paths[1], "a newer unrelated file");

        Assert.Throws<IOException>(() => service.Restore(batch));
        Assert.All(batch.Entries, entry => Assert.True(File.Exists(StoredPath(batch, entry))));
        Assert.Equal("a newer unrelated file", File.ReadAllText(_paths[1]));
        Assert.False(File.Exists(_paths[0]));
    }

    [Fact]
    public void Restore_RefusesAConflictCreatedAfterPreflightWithoutOverwritingIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var batch = new QuarantineService().Quarantine(_operation, _destination);
        var exception = Assert.Throws<QuarantineInterruptedException>(() => new QuarantineService(count =>
        {
            if (count == 1) File.WriteAllText(batch.Entries[1].OriginalPath, "concurrent destination");
        }).Restore(batch));

        Assert.Equal(1, exception.CompletedCount);
        Assert.Equal("concurrent destination", File.ReadAllText(batch.Entries[1].OriginalPath));
        Assert.Equal(Contents, File.ReadAllText(StoredPath(batch, batch.Entries[1])));
    }

    [Fact]
    public void Restore_RejectsChangedQuarantinedContentsBeforeMovingAnything()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        File.WriteAllText(StoredPath(batch, batch.Entries[1]), new string('x', Contents.Length));

        Assert.Throws<IOException>(() => service.Restore(batch));
        Assert.All(_paths.Take(2), path => Assert.False(File.Exists(path)));
        Assert.All(batch.Entries, entry => Assert.True(File.Exists(StoredPath(batch, entry))));
    }

    [Fact]
    public void Restore_RejectsMissingPayloadBeforeMovingAnything()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        File.Delete(StoredPath(batch, batch.Entries[1]));

        Assert.Throws<IOException>(() => service.Restore(batch));
        Assert.True(File.Exists(StoredPath(batch, batch.Entries[0])));
        Assert.All(_paths.Take(2), path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void Restore_DoesNotDependOnTheKeepFileStillExisting()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        File.Delete(_paths[2]);

        Assert.Equal(2, service.Restore(batch).RestoredCount);
        Assert.All(_paths.Take(2), path => Assert.Equal(Contents, File.ReadAllText(path)));
    }

    [Fact]
    public void Inspect_RejectsCorruptProtectedManifest()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        var manifestPath = Path.Combine(batch.DirectoryPath, QuarantineService.ManifestName);
        var bytes = File.ReadAllBytes(manifestPath);
        bytes[bytes.Length / 2] ^= 1;
        File.WriteAllBytes(manifestPath, bytes);

        Assert.Throws<IOException>(() => service.Inspect(batch.DirectoryPath));
        Assert.All(batch.Entries, entry => Assert.True(File.Exists(StoredPath(batch, entry))));
    }

    [Fact]
    public void Restore_RejectsAManifestReplacedAfterReview()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        var path = Path.Combine(batch.DirectoryPath, QuarantineService.ManifestName);
        File.WriteAllBytes(path, WindowsDataProtection.Protect(WindowsDataProtection.Unprotect(File.ReadAllBytes(path))));

        Assert.Throws<IOException>(() => service.Restore(batch));
        Assert.All(batch.Entries, entry => Assert.True(File.Exists(StoredPath(batch, entry))));
    }

    [Fact]
    public void Quarantine_RejectsStaleScanBeforeCreatingABatch()
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(_paths[0], "modified after scanning");

        Assert.Throws<IOException>(() => new QuarantineService().Quarantine(_operation, _destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_destination));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void DefaultScan_ExcludesQuarantinedFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        new QuarantineService().Quarantine(_operation, _destination);

        Assert.Empty(new DuplicateDetector().FindDuplicates(_root));
    }

    [Fact]
    public void PermanentDeleteService_RejectsAQuarantineOperation()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Throws<InvalidOperationException>(() => new WindowsCleanupService().Execute(_operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    private static string StoredPath(QuarantineBatch batch, QuarantineEntry entry) => Path.Combine(batch.DirectoryPath, entry.StoredName);

    [Fact]
    public void Inspect_RejectsTraversalEvenInAValidlyProtectedManifest()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        var entries = batch.Entries.Select(entry => entry with { StoredName = "../outside.bin" }).ToArray();
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, BatchId = Path.GetFileName(batch.DirectoryPath)["batch-".Length..], Entries = entries
        });
        File.WriteAllBytes(Path.Combine(batch.DirectoryPath, QuarantineService.ManifestName), WindowsDataProtection.Protect(payload));

        Assert.Throws<IOException>(() => service.Inspect(batch.DirectoryPath));
        Assert.All(batch.Entries, entry => Assert.True(File.Exists(StoredPath(batch, entry))));
    }

    [Fact]
    public void Restore_RequiresOriginalParentFoldersAndCanRetryAfterTheyAreRecreated()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        Directory.Delete(Path.GetDirectoryName(_paths[1])!);

        Assert.Throws<IOException>(() => service.Restore(batch));
        Assert.All(batch.Entries, entry => Assert.True(File.Exists(StoredPath(batch, entry))));
        Directory.CreateDirectory(Path.GetDirectoryName(_paths[1])!);
        Assert.Equal(2, service.Restore(batch).RestoredCount);
    }

    [Fact]
    public void Inspect_RejectsNonCanonicalOriginalPathsEvenInAProtectedManifest()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new QuarantineService();
        var batch = service.Quarantine(_operation, _destination);
        var entries = batch.Entries.Select(entry => entry with
        {
            OriginalPath = Path.Combine(_source, "..", "source", Path.GetRelativePath(_source, entry.OriginalPath))
        }).ToArray();
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, BatchId = Path.GetFileName(batch.DirectoryPath)["batch-".Length..], Entries = entries
        });
        File.WriteAllBytes(Path.Combine(batch.DirectoryPath, QuarantineService.ManifestName), WindowsDataProtection.Protect(payload));

        Assert.Throws<IOException>(() => service.Inspect(batch.DirectoryPath));
        Assert.All(batch.Entries, entry => Assert.True(File.Exists(StoredPath(batch, entry))));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
