using System.Text;
using System.Text.Json;
using DuplicateFinder.Desktop;

namespace DuplicateFinder.Tests;

public sealed class SessionDiagnosticsTests
{
    [Fact]
    public async Task RecordsContainOnlyApprovedFieldsAndCategories()
    {
        var stream = new MemoryStream();
        var diagnostics = new SessionDiagnostics(stream, stream, 65536);
        var failure = SessionDiagnostics.Classify(new IOException(@"private C:\private\secret.txt content and stack details"));
        Assert.True(diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Failed, failure, groups: 2, files: 4));
        await diagnostics.StopAsync();
        var text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("private", text);
        Assert.DoesNotContain("secret", text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var record = JsonDocument.Parse(lines[0]);
        Assert.Equal(new[] { "Schema", "Utc", "Operation", "Outcome", "Failure", "Groups", "Files", "SkippedFiles", "SkippedFolders" },
            record.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("InputOutput", record.RootElement.GetProperty("Failure").GetString());
        Assert.Equal(2, record.RootElement.GetProperty("Groups").GetInt32());
    }

    [Fact]
    public async Task StopFlushesAcceptedRecordsAndRejectsLaterEvents()
    {
        var stream = new MemoryStream();
        var diagnostics = new SessionDiagnostics(stream, stream, 65536);
        diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Completed);
        await diagnostics.StopAsync();
        var before = stream.ToArray();
        Assert.Equal(DiagnosticRecordingState.Stopped, diagnostics.State);
        Assert.False(diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Started));
        await diagnostics.StopAsync();
        Assert.Equal(before, stream.ToArray());
    }

    [Fact]
    public async Task SizeLimitIsEnforcedWithoutRotationOrOverwriting()
    {
        var stream = new MemoryStream();
        var diagnostics = new SessionDiagnostics(stream, stream, 512);
        for (var index = 0; index < 20; index++) diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Completed);
        await diagnostics.StopAsync();
        Assert.Equal(DiagnosticRecordingState.SizeLimitReached, diagnostics.State);
        Assert.InRange(stream.ToArray().Length, 1, 512);
        foreach (var line in Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            using (JsonDocument.Parse(line)) { }
    }

    [Fact]
    public async Task QueuePressureStopsAcceptingWithoutBlockingCaller()
    {
        var stream = new PausedStream();
        var diagnostics = new SessionDiagnostics(stream, stream, 65536);
        diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Started);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            for (var index = 0; index < 70; index++) diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Completed);
            Assert.Equal(DiagnosticRecordingState.QueueLimitReached, diagnostics.State);
        }
        finally { stream.Release.TrySetResult(); }
        await diagnostics.StopAsync();
    }

    [Fact]
    public async Task WriteFailureDisablesRecordingWithoutPropagating()
    {
        var stream = new FailingStream();
        var diagnostics = new SessionDiagnostics(stream, stream, 65536);
        diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Started);
        await diagnostics.StopAsync();
        Assert.Equal(DiagnosticRecordingState.WriteFailed, diagnostics.State);
        Assert.False(diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Completed));
    }

    [Fact]
    public async Task InvalidEnumsAndCountsCannotEnterSchema()
    {
        var stream = new MemoryStream();
        var diagnostics = new SessionDiagnostics(stream, stream, 65536);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => diagnostics.Record((DiagnosticOperation)999, DiagnosticOutcome.Started));
            Assert.Throws<ArgumentOutOfRangeException>(() => diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Started, files: -1));
        }
        finally { await diagnostics.StopAsync(); }
    }

    [Fact]
    public async Task InvalidLimitDoesNotCreateAnyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "DuplicateFinder-Diagnostics-" + Guid.NewGuid().ToString("N") + ".jsonl");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SessionDiagnostics.StartAsync(path, 65537));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task GuardedCreationRefusesExistingFilesAndCanCreateNewLocalFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "DuplicateFinder-Diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var existing = Path.Combine(root, "existing.jsonl");
            await File.WriteAllTextAsync(existing, "Synthetic existing content");
            await Assert.ThrowsAsync<IOException>(() => SessionDiagnostics.StartAsync(existing, 16384));
            Assert.Equal("Synthetic existing content", await File.ReadAllTextAsync(existing));
            var path = Path.Combine(root, "new.jsonl");
            var diagnostics = await SessionDiagnostics.StartAsync(path, 16384);
            diagnostics.Record(DiagnosticOperation.Scan, DiagnosticOutcome.Completed, groups: 1, files: 2);
            await diagnostics.StopAsync();
            Assert.InRange(new FileInfo(path).Length, 1, 16384);
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("Scan", text);
            Assert.DoesNotContain(root, text);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class PausedStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task;
            await base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("Synthetic write failure"));
    }
}
