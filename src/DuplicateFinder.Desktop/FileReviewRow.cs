using System.IO;

namespace DuplicateFinder.Desktop;

// Presentation only: row selection is for preview; the keep selector owns the decision.
internal sealed class FileReviewRow(string path, long sizeBytes, bool keep)
{
    public string FileName { get; } = Path.GetFileName(path);
    public string FullPath { get; } = path;
    public string Size { get; } = DisplayFileSize.Format(sizeBytes);
    public bool IsKeep { get; } = keep;
    public bool IsRemovalCandidate => !IsKeep;
    public string AccessibleName => $"{FileName}, {Size}, {(IsKeep ? "keep this file" : "removal candidate after confirmation")}, {FullPath}";
    public override string ToString() => AccessibleName;
}
