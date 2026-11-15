using System;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DuplicateFinder.Desktop;

public enum DiagnosticOperation { Session, Scan, Quarantine, PermanentDelete, Restore, FolderSelection }
public enum DiagnosticOutcome { Started, Completed, Cancelled, Failed, Interrupted }
public enum DiagnosticFailure { None, Missing, AccessDenied, InputOutput, InvalidInput, Other }
public enum DiagnosticRecordingState { Recording, Stopped, SizeLimitReached, QueueLimitReached, WriteFailed }

/// <summary>Explicit opt-in recording of a closed schema. Never attaches to general trace output.</summary>
internal sealed class SessionDiagnostics
{
    public const int MaximumFileBytes = 65536;
    private readonly object _gate = new();
    private readonly Channel<byte[]> _records = Channel.CreateBounded<byte[]>(64);
    private readonly Task _writer;
    private readonly int _maximumBytes;
    private int _reservedBytes;
    private DiagnosticRecordingState _state = DiagnosticRecordingState.Recording;
    public DiagnosticRecordingState State { get { lock (_gate) return _state; } }

    internal SessionDiagnostics(Stream stream, IDisposable owner, int maximumBytes)
    {
        if (maximumBytes is < 512 or > MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _maximumBytes = maximumBytes;
        _writer = Task.Run(async () =>
        {
            try
            {
                try
                {
                    await foreach (var bytes in _records.Reader.ReadAllAsync())
                    {
                        await stream.WriteAsync(bytes);
                        await stream.FlushAsync();
                    }
                }
                finally { owner.Dispose(); }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                lock (_gate) { _state = DiagnosticRecordingState.WriteFailed; _records.Writer.TryComplete(); }
            }
        });
    }

    public static async Task<SessionDiagnostics> StartAsync(string path, int maximumBytes)
    {
        if (maximumBytes is < 512 or > MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var session = await Task.Run(() => WindowsCleanupSession.CreateDiagnosticFile(path));
        var diagnostics = new SessionDiagnostics(session.DiagnosticStream, session, maximumBytes);
        diagnostics.Record(DiagnosticOperation.Session, DiagnosticOutcome.Started);
        return diagnostics;
    }

    public bool Record(DiagnosticOperation operation, DiagnosticOutcome outcome, DiagnosticFailure failure = DiagnosticFailure.None,
        int? groups = null, int? files = null, int? skippedFiles = null, int? skippedFolders = null)
    {
        if (!Enum.IsDefined(operation) || !Enum.IsDefined(outcome) || !Enum.IsDefined(failure) ||
            groups < 0 || files < 0 || skippedFiles < 0 || skippedFolders < 0) throw new ArgumentOutOfRangeException(nameof(operation));
        lock (_gate)
        {
            if (_state != DiagnosticRecordingState.Recording) return false;
            var line = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Schema = 1, Utc = DateTimeOffset.UtcNow,
                Operation = operation.ToString(), Outcome = outcome.ToString(), Failure = failure.ToString(),
                Groups = groups, Files = files, SkippedFiles = skippedFiles, SkippedFolders = skippedFolders
            });
            if (_reservedBytes + line.Length + 1 > _maximumBytes)
            {
                _state = DiagnosticRecordingState.SizeLimitReached;
                _records.Writer.TryComplete();
                return false;
            }
            var bytes = new byte[line.Length + 1];
            line.CopyTo(bytes, 0);
            bytes[^1] = (byte)'\n';
            if (!_records.Writer.TryWrite(bytes))
            {
                _state = DiagnosticRecordingState.QueueLimitReached;
                _records.Writer.TryComplete();
                return false;
            }
            _reservedBytes += bytes.Length;
            return true;
        }
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (_state == DiagnosticRecordingState.Recording)
            {
                Record(DiagnosticOperation.Session, DiagnosticOutcome.Completed);
                if (_state == DiagnosticRecordingState.Recording) _state = DiagnosticRecordingState.Stopped;
            }
            _records.Writer.TryComplete();
        }
        await _writer;
    }

    public static DiagnosticFailure Classify(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => DiagnosticFailure.Missing,
        UnauthorizedAccessException => DiagnosticFailure.AccessDenied,
        IOException => DiagnosticFailure.InputOutput,
        ArgumentException => DiagnosticFailure.InvalidInput,
        _ => DiagnosticFailure.Other
    };
}
