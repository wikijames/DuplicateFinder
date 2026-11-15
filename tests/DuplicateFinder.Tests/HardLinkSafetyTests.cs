namespace DuplicateFinder.Tests;

using System.ComponentModel;
using System.Runtime.InteropServices;
using DuplicateFinder.Core;
using DuplicateFinder.Desktop;

public sealed class HardLinkSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly string _outside;
    private readonly string _destination;
    private readonly string[] _paths;
    private const string Contents = "synthetic physical-file identity fixture";

    public HardLinkSafetyTests()
    {
        _source = Path.Combine(_root, "source");
        _outside = Path.Combine(_root, "outside");
        _destination = Path.Combine(_root, "destination");
        foreach (var folder in new[] { _source, _outside, _destination }) Directory.CreateDirectory(folder);
        _paths = new[] { "first.txt", "second.txt", "keep.txt" }.Select(name => Path.Combine(_source, name)).ToArray();
        foreach (var path in _paths) File.WriteAllText(path, Contents);
    }

    [Fact]
    public void NativeIdentity_RecognizesAliasesAndDistinguishesAnIndependentCopy()
    {
        if (!OperatingSystem.IsWindows()) return;
        var alias = Path.Combine(_source, "alias.txt");
        Link(alias, _paths[0]);
        var provider = new WindowsFileIdentityProvider();
        using var original = File.OpenRead(_paths[0]);
        using var linked = File.OpenRead(alias);
        using var independent = File.OpenRead(_paths[1]);

        var originalIdentity = provider.ReadIdentity(original);
        Assert.Equal(originalIdentity, provider.ReadIdentity(linked));
        Assert.Equal(2u, originalIdentity.HardLinkCount);
        Assert.NotEqual(originalIdentity, provider.ReadIdentity(independent));
    }

    [Fact]
    public void AliasesInsideScan_AreExcludedFromGroupsAndExtraCopyTotals()
    {
        if (!OperatingSystem.IsWindows()) return;
        Link(Path.Combine(_source, "alias.txt"), _paths[0]);

        var result = new DuplicateDetector(new WindowsFileIdentityProvider()).Scan(_source);

        Assert.True(result.Issues.IsComplete);
        Assert.Equal(2, result.Issues.ExcludedHardLinkPathCount);
        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.FilePaths.Count);
        Assert.DoesNotContain(_paths[0], group.FilePaths);
        Assert.Equal(new FileInfo(_paths[1]).Length, group.ReclaimableBytes);
    }

    [Fact]
    public void AliasOutsideScan_StillProtectsTheEncounteredFileWithoutScanningOutside()
    {
        if (!OperatingSystem.IsWindows()) return;
        Link(Path.Combine(_outside, "alias.txt"), _paths[0]);

        var report = new DuplicateScanCoordinator(new WindowsFileIdentityProvider()).CreateReport(_source);

        Assert.Equal(1, report.Summary.Issues.ExcludedHardLinkPathCount);
        Assert.Equal(2, Assert.Single(report.Summary.Groups).FilePaths.Count);
        Assert.Contains("Excluded 1 hard-linked path(s)", report.StatusMessage);
        Assert.DoesNotContain(_root, report.StatusMessage);
    }

    [Fact]
    public void AliasOnlyScan_DoesNotReportDuplicateStorage()
    {
        if (!OperatingSystem.IsWindows()) return;
        Link(Path.Combine(_source, "alias.txt"), _paths[0]);
        File.Delete(_paths[1]);
        File.Delete(_paths[2]);

        var report = new DuplicateScanCoordinator(new WindowsFileIdentityProvider()).CreateReport(_source);

        Assert.Empty(report.Summary.Groups);
        Assert.Equal(0, report.Summary.TotalReclaimableBytes);
        Assert.Equal(2, report.Summary.Issues.ExcludedHardLinkPathCount);
        Assert.True(report.Summary.IsComplete);
    }

    [Fact]
    public void SameContentReplacementWithPreservedTimestamp_IsDetectedByIdentity()
    {
        if (!OperatingSystem.IsWindows()) return;
        var detector = new DuplicateDetector(new FileScanner(), beforeVerification: () =>
        {
            var time = File.GetLastWriteTimeUtc(_paths[0]);
            File.Move(_paths[0], Path.Combine(_outside, "original.txt"));
            File.WriteAllText(_paths[0], Contents);
            File.SetLastWriteTimeUtc(_paths[0], time);
        }, identityProvider: new WindowsFileIdentityProvider());

        var result = detector.Scan(_source);

        Assert.Equal(1, result.Issues.ChangedFileCount);
        Assert.DoesNotContain(_paths[0], Assert.Single(result.Groups).FilePaths);
    }

    [Theory]
    [InlineData(0, DuplicateAction.Delete)]
    [InlineData(2, DuplicateAction.Delete)]
    [InlineData(0, DuplicateAction.Quarantine)]
    [InlineData(2, DuplicateAction.Quarantine)]
    public void LinkAddedAfterScan_BlocksCleanupBeforeAnyFileIsMovedOrDeleted(int linkedIndex, DuplicateAction action)
    {
        if (!OperatingSystem.IsWindows()) return;
        var group = Assert.Single(new DuplicateDetector().FindDuplicates(_source));
        var operation = CreateCleanupOperation(group, _paths[2], action);
        Link(Path.Combine(_outside, "new-alias.txt"), _paths[linkedIndex]);

        if (action == DuplicateAction.Delete)
            Assert.Throws<IOException>(() => new WindowsCleanupService().Execute(operation));
        else
            Assert.Throws<IOException>(() => new QuarantineService().Quarantine(operation, _destination));

        Assert.All(_paths, path => Assert.Equal(Contents, File.ReadAllText(path)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_destination));
    }

    [Fact]
    public void Restore_RejectsHardLinkedPayloadBeforeAnyMoveAndCanRetryAfterResolution()
    {
        if (!OperatingSystem.IsWindows()) return;
        var group = Assert.Single(new DuplicateDetector().FindDuplicates(_source));
        var service = new QuarantineService();
        var batch = service.Quarantine(CreateCleanupOperation(group, _paths[2], DuplicateAction.Quarantine), _destination);
        var alias = Path.Combine(_outside, "payload-alias.bin");
        Link(alias, Path.Combine(batch.DirectoryPath, batch.Entries[0].StoredName));

        Assert.Throws<IOException>(() => service.Restore(batch));
        Assert.All(batch.Entries, entry => Assert.True(File.Exists(Path.Combine(batch.DirectoryPath, entry.StoredName))));
        File.Delete(alias);
        Assert.Equal(2, service.Restore(batch).RestoredCount);
    }

    [Fact]
    public void IdentityReadFailure_ProducesIncompleteResultsWithoutAssumingIndependentFiles()
    {
        var result = new DuplicateDetector(new UnavailableIdentityProvider()).Scan(_source);

        Assert.Empty(result.Groups);
        Assert.False(result.Issues.IsComplete);
        Assert.Equal(3, result.Issues.SkippedFileCount);
    }

    internal static DuplicateCleanupOperation CreateCleanupOperation(DuplicateGroup group, string keep, DuplicateAction action) =>
        new(new DuplicateCleanupExecution(new DuplicateCleanupPlan(new DuplicateReviewSelection(group, keep), action)));

    private sealed class UnavailableIdentityProvider : IFileIdentityProvider
    {
        public FileIdentity ReadIdentity(FileStream stream) => throw new IOException("synthetic unsupported identity query");
    }

    private static void Link(string alias, string original)
    {
        if (!CreateHardLink(alias, original, IntPtr.Zero)) throw new IOException("Synthetic hard link could not be created.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string alias, string original, IntPtr security);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
