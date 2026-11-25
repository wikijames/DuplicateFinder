using System;
using System.Collections.Generic;
using System.Linq;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

internal abstract class ReviewResultRow(DuplicateGroup group)
{
    public DuplicateGroup Group { get; } = group;
}

internal sealed class ReviewGroupHeader(DuplicateGroup group, int number) : ReviewResultRow(group)
{
    public string Title => $"Group {number} · {System.IO.Path.GetFileName(Group.FilePaths[0])}";
    public string Summary => $"{Group.FilePaths.Count} files · {DisplayFileSize.Format(Group.SizeBytes)} each · {DisplayFileSize.Format(Group.ReclaimableBytes)} in extra copies";
    public override string ToString() => Title + ", " + Summary;
}

// Lightweight presentation records only; no file access or decoding while building results.
internal sealed class GroupedReviewRows
{
    private readonly Dictionary<DuplicateGroup, ReviewGroupHeader> _headers = new();
    private readonly Dictionary<DuplicateGroup, List<FileReviewRow>> _files = new();
    public IReadOnlyList<ReviewResultRow> Rows { get; }

    public GroupedReviewRows(IReadOnlyList<DuplicateGroup> groups, Func<DuplicateGroup, string> keepPath)
    {
        var rows = new List<ReviewResultRow>();
        foreach (var group in groups)
        {
            var header = new ReviewGroupHeader(group, _headers.Count + 1);
            _headers.Add(group, header);
            rows.Add(header);
            var keep = keepPath(group);
            var files = group.FilePaths.Select(path => new FileReviewRow(group, path,
                string.Equals(path, keep, StringComparison.OrdinalIgnoreCase))).ToList();
            _files.Add(group, files);
            rows.AddRange(files);
        }
        Rows = rows;
    }

    public ReviewGroupHeader? HeaderFor(DuplicateGroup group) => _headers.GetValueOrDefault(group);
    public FileReviewRow? FirstFileFor(DuplicateGroup group) => _files.GetValueOrDefault(group)?.FirstOrDefault();
    public void UpdateKeep(DuplicateGroup group, string keepPath)
    {
        if (!_files.TryGetValue(group, out var files)) return;
        foreach (var file in files) file.SetKeep(string.Equals(file.FullPath, keepPath, StringComparison.OrdinalIgnoreCase));
    }
}
