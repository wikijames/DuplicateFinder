using System.IO;
using System.ComponentModel;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

// Presentation only: row selection is for preview; the keep selector owns the decision.
internal sealed class FileReviewRow(DuplicateGroup group, string path, bool keep) : ReviewResultRow(group), INotifyPropertyChanged
{
    public string FileName { get; } = Path.GetFileName(path);
    public string FullPath { get; } = path;
    public string Size { get; } = DisplayFileSize.Format(group.SizeBytes);
    public bool IsKeep { get; private set; } = keep;
    public bool IsRemovalCandidate => !IsKeep;
    public bool CanCompareImage => ImageFilePreview.Supports(FullPath);
    public string PreviewAccessibleName => $"Compare image {FileName}, {FullPath}";
    public string KeepAccessibleName => $"Keep this file: {FileName}, {FullPath}";
    public string AccessibleName => $"{FileName}, {Size}, {(IsKeep ? "keep this file" : "removal candidate after confirmation")}, {FullPath}";
    public override string ToString() => AccessibleName;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void SetKeep(bool keep)
    {
        if (IsKeep == keep) return;
        IsKeep = keep;
        foreach (var name in new[] { nameof(IsKeep), nameof(IsRemovalCandidate), nameof(AccessibleName) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
