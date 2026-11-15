using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public enum ResultGroupSort { Name, ExtraCopySize, FileSize, CopyCount }

public static class ResultGroupQuery
{
    public static IReadOnlyList<DuplicateGroup> Apply(IReadOnlyList<DuplicateGroup> groups, string? search, ResultGroupSort sort)
    {
        var term = search?.Trim() ?? string.Empty;
        var matching = groups.Where(group => group.FilePaths.Any(path => path.Contains(term, StringComparison.OrdinalIgnoreCase)));
        var ordered = sort switch
        {
            ResultGroupSort.Name => matching.OrderBy(group => Path.GetFileName(group.FilePaths[0]), StringComparer.OrdinalIgnoreCase),
            ResultGroupSort.ExtraCopySize => matching.OrderByDescending(group => group.ReclaimableBytes),
            ResultGroupSort.FileSize => matching.OrderByDescending(group => group.SizeBytes),
            ResultGroupSort.CopyCount => matching.OrderByDescending(group => group.FilePaths.Count),
            _ => throw new ArgumentOutOfRangeException(nameof(sort))
        };
        return ordered.ThenBy(group => group.FilePaths[0], StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }
}
