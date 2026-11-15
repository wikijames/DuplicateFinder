namespace DuplicateFinder.Desktop;

// Cached presentation values only; the recorder remains the source of state.
internal sealed record DiagnosticsStatusPresentation(string Title, string Header, string Tone)
{
    private static readonly DiagnosticsStatusPresentation Off = new("Recording Off", "Diagnostics Off", "neutral");
    private static readonly DiagnosticsStatusPresentation Recording = new("Recording Locally", "Recording Locally", "recording");
    private static readonly DiagnosticsStatusPresentation Stopped = new("Recording Stopped", "Diagnostics Stopped", "neutral");
    private static readonly DiagnosticsStatusPresentation FileLimit = new("Recording Stopped — File Size Limit", "Diagnostics Limit", "attention");
    private static readonly DiagnosticsStatusPresentation QueueLimit = new("Recording Stopped — Queue Limit", "Diagnostics Limit", "attention");
    private static readonly DiagnosticsStatusPresentation Failed = new("Recording Failed", "Diagnostics Failed", "attention");

    public static DiagnosticsStatusPresentation From(DiagnosticRecordingState? state) => state switch
    {
        DiagnosticRecordingState.Recording => Recording,
        DiagnosticRecordingState.Stopped => Stopped,
        DiagnosticRecordingState.SizeLimitReached => FileLimit,
        DiagnosticRecordingState.QueueLimitReached => QueueLimit,
        DiagnosticRecordingState.WriteFailed => Failed,
        _ => Off
    };
}
