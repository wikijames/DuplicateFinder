namespace DuplicateFinder.Core;

using System.Security.Cryptography;

public sealed class DuplicateDetector
{
    public IReadOnlyList<DuplicateGroup> FindDuplicates(string rootPath, ScanOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory '{rootPath}' was not found.");
        }

        var scanner = new FileScanner();
        var files = scanner.EnumerateFiles(rootPath, options ?? new ScanOptions())
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicateGroups = new List<DuplicateGroup>();

        foreach (var sizeGroup in files.GroupBy(filePath => new FileInfo(filePath).Length).Where(group => group.Count() > 1))
        {
            var hashGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in sizeGroup)
            {
                var hash = ComputeHash(filePath);

                if (!hashGroups.TryGetValue(hash, out var group))
                {
                    group = new List<string>();
                    hashGroups[hash] = group;
                }

                group.Add(filePath);
            }

            foreach (var hashGroup in hashGroups.Values.Where(group => group.Count > 1))
            {
                var orderedPaths = hashGroup.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
                var sizeBytes = new FileInfo(orderedPaths[0]).Length;
                duplicateGroups.Add(new DuplicateGroup(orderedPaths, sizeBytes));
            }
        }

        return duplicateGroups;
    }

    private static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
