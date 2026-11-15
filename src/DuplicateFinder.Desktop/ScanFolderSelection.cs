using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public sealed class ScanFolderSelection
{
    public IReadOnlyList<string> Folders { get; private set; } = Array.Empty<string>();

    public void Add(IEnumerable<string> paths)
    {
        var additions = paths.ToArray();
        foreach (var path in additions)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Enter a folder path.");
            var fullPath = Path.GetFullPath(path);
            if (fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => string.Equals(segment, QuarantineService.FolderName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Quarantine folders are reserved for batch restore, not scanning.");
            if (OperatingSystem.IsWindows()) WindowsCleanupSession.GetSupportedPath(Path.Combine(fullPath, "scan-root-check"));
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("Only existing folders can be added.");
        }
        if (additions.Length > 0) Folders = ScanRoots.Normalize(Folders.Concat(additions));
    }

    public void Remove(string path) => Folders = Folders.Where(folder => !string.Equals(folder, path,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToList().AsReadOnly();

    public void Clear() => Folders = Array.Empty<string>();
}
