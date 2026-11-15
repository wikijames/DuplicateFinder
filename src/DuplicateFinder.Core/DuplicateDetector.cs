namespace DuplicateFinder.Core;

using System.Security.Cryptography;

public sealed class DuplicateDetector
{
    public IReadOnlyList<DuplicateGroup> FindDuplicates(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory '{rootPath}' was not found.");
        }

        var scanner = new FileScanner();
        var files = scanner.EnumerateFiles(rootPath, options ?? new ScanOptions(), cancellationToken)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicateGroups = new List<DuplicateGroup>();

        foreach (var sizeGroup in files.GroupBy(filePath => new FileInfo(filePath).Length).Where(group => group.Count() > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sampleHashGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in sizeGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sampleHash = ComputeHash(filePath, cancellationToken, 64 * 1024);

                if (!sampleHashGroups.TryGetValue(sampleHash, out var group))
                {
                    group = new List<string>();
                    sampleHashGroups[sampleHash] = group;
                }

                group.Add(filePath);
            }

            foreach (var sampleGroup in sampleHashGroups.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hashGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var filePath in sampleGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var hash = ComputeHash(filePath, cancellationToken);

                    if (!hashGroups.TryGetValue(hash, out var group))
                    {
                        group = new List<string>();
                        hashGroups[hash] = group;
                    }

                    group.Add(filePath);
                }

                foreach (var hashEntry in hashGroups.Where(entry => entry.Value.Count > 1))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var orderedPaths = hashEntry.Value.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
                    var sizeBytes = new FileInfo(orderedPaths[0]).Length;
                    duplicateGroups.Add(new DuplicateGroup(orderedPaths, sizeBytes, hashEntry.Key));
                }
            }
        }

        return duplicateGroups;
    }

    private static string ComputeHash(string filePath, CancellationToken cancellationToken, long? maxBytes = null)
    {
        using var stream = File.OpenRead(filePath);
        return ComputeHash(stream, cancellationToken, maxBytes);
    }

    internal static string ComputeHash(Stream stream, CancellationToken cancellationToken, long? maxBytes = null)
    {
        using var sha256 = SHA256.Create();
        var buffer = new byte[1024 * 1024];
        long bytesProcessed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (maxBytes.HasValue && bytesProcessed >= maxBytes.Value)
            {
                break;
            }

            var remainingBytes = maxBytes.HasValue ? maxBytes.Value - bytesProcessed : buffer.Length;
            var bytesToRead = remainingBytes < buffer.Length ? (int)remainingBytes : buffer.Length;

            var bytesRead = stream.Read(buffer, 0, bytesToRead);
            if (bytesRead == 0)
            {
                break;
            }

            bytesProcessed += bytesRead;
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);

            if (maxBytes.HasValue && bytesProcessed >= maxBytes.Value)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>());
    }
}
