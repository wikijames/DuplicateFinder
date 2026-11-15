namespace DuplicateFinder.Core;

public sealed class FileScanner
{
    private readonly Func<string, string, EnumerationOptions, IEnumerable<string>> _enumerateFiles = Directory.EnumerateFiles;
    private readonly Func<string, string, EnumerationOptions, IEnumerable<string>> _enumerateDirectories = Directory.EnumerateDirectories;

    public FileScanner() { }
    internal FileScanner(Func<string, string, EnumerationOptions, IEnumerable<string>> enumerateFiles,
        Func<string, string, EnumerationOptions, IEnumerable<string>>? enumerateDirectories = null)
    {
        _enumerateFiles = enumerateFiles;
        _enumerateDirectories = enumerateDirectories ?? Directory.EnumerateDirectories;
    }
    public IEnumerable<string> EnumerateFiles(string rootPath, string searchPattern = "*", bool includeHidden = false, CancellationToken cancellationToken = default)
    {
        return EnumerateFiles(rootPath, new ScanOptions
        {
            SearchPattern = searchPattern ?? "*",
            IncludeHidden = includeHidden
        }, cancellationToken);
    }

    public IEnumerable<string> EnumerateFiles(string rootPath, ScanOptions? options, CancellationToken cancellationToken = default)
    {
        return EnumerateFiles(rootPath, options, new ScanDiagnostics(), cancellationToken);
    }

    internal IEnumerable<string> EnumerateFiles(string rootPath, ScanOptions? options, ScanDiagnostics diagnostics, CancellationToken cancellationToken = default)
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

        options ??= new ScanOptions();
        options.Validate();

        var effectivePattern = string.IsNullOrWhiteSpace(options.SearchPattern) ? "*" : options.SearchPattern;
        var normalizedExtensions = GetNormalizedExtensions(options.AllowedExtensions);
        var excludedDirectoryNames = GetNormalizedDirectoryNames(options.ExcludedDirectoryNames);
        var excludedPathSegments = GetNormalizedDirectoryNames(options.ExcludedPathSegments);
        var excludedFileNames = GetNormalizedFileNames(options.ExcludedFileNames);

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.System,
            ReturnSpecialDirectories = false
        };

        foreach (var candidatePath in EnumerateCandidatePaths(rootPath, effectivePattern, options.IncludeHidden, excludedDirectoryNames, excludedPathSegments, excludedFileNames, enumerationOptions, diagnostics, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            long length;
            try
            {
                length = new FileInfo(candidatePath).Length;
            }
            catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception))
            {
                diagnostics.FileFailed(candidatePath);
                continue;
            }

            if (options.MinimumSizeBytes is long minimumSize && length < minimumSize)
            {
                continue;
            }

            if (options.MaximumSizeBytes is long maximumSize && length > maximumSize)
            {
                continue;
            }

            if (normalizedExtensions.Count > 0)
            {
                var extension = Path.GetExtension(candidatePath);
                if (!normalizedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            yield return candidatePath;
        }
    }

    private IEnumerable<string> EnumerateCandidatePaths(string rootPath, string searchPattern, bool includeHidden, HashSet<string>? exclusionNames, HashSet<string>? excludedPathSegments, HashSet<string>? excludedFileNames, EnumerationOptions options, ScanDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(rootPath);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDirectory = directories.Pop();

            if (!TryGetAttributes(currentDirectory, true, diagnostics, out var directoryAttributes)) continue;
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                diagnostics.DirectoryFailed(currentDirectory);
                continue;
            }

            if (!includeHidden && IsHidden(currentDirectory, directoryAttributes))
            {
                continue;
            }

            if (!string.Equals(currentDirectory, rootPath, StringComparison.OrdinalIgnoreCase) && IsExcludedPath(currentDirectory, rootPath, excludedPathSegments))
            {
                continue;
            }

            if (!string.Equals(currentDirectory, rootPath, StringComparison.OrdinalIgnoreCase) && IsExcludedPath(currentDirectory, rootPath, exclusionNames))
            {
                continue;
            }

            foreach (var childDirectory in EnumerateSafely(() => _enumerateDirectories(currentDirectory, "*", options), currentDirectory, diagnostics, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var childDirectoryName = Path.GetFileName(childDirectory);
                if (exclusionNames is not null && exclusionNames.Contains(childDirectoryName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsExcludedPath(childDirectory, rootPath, excludedPathSegments))
                {
                    continue;
                }

                if (!TryGetAttributes(childDirectory, true, diagnostics, out var childAttributes)) continue;
                if ((childAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (!includeHidden && IsHidden(childDirectory, childAttributes))
                {
                    continue;
                }

                directories.Push(childDirectory);
            }

            foreach (var file in EnumerateSafely(() => _enumerateFiles(currentDirectory, searchPattern, options), currentDirectory, diagnostics, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryGetAttributes(file, false, diagnostics, out var fileAttributes)) continue;
                if ((fileAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var fileName = Path.GetFileName(file);
                if (excludedFileNames is not null && excludedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!includeHidden && IsHidden(file, fileAttributes))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static HashSet<string> GetNormalizedExtensions(IEnumerable<string>? extensions)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (extensions is null)
        {
            return normalized;
        }

        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                continue;
            }

            var normalizedExtension = extension.Trim();
            if (!normalizedExtension.StartsWith(".", StringComparison.Ordinal))
            {
                normalizedExtension = "." + normalizedExtension;
            }

            normalized.Add(normalizedExtension);
        }

        return normalized;
    }

    private static HashSet<string> GetNormalizedDirectoryNames(IEnumerable<string>? directoryNames)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (directoryNames is null)
        {
            return normalized;
        }

        foreach (var directoryName in directoryNames)
        {
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                continue;
            }

            normalized.Add(directoryName.Trim());
        }

        return normalized;
    }

    private static HashSet<string> GetNormalizedFileNames(IEnumerable<string>? fileNames)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (fileNames is null)
        {
            return normalized;
        }

        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            normalized.Add(fileName.Trim());
        }

        return normalized;
    }

    private static bool IsHidden(string filePath, FileAttributes attributes)
    {
        var fileName = Path.GetFileName(filePath);
        var startsWithDot = !string.IsNullOrEmpty(fileName) && fileName.StartsWith(".", StringComparison.Ordinal);
        return startsWithDot || (attributes & FileAttributes.Hidden) != 0;
    }

    private static bool IsExcludedPath(string path, string rootPath, HashSet<string>? excludedPathSegments)
    {
        if (excludedPathSegments is null || excludedPathSegments.Count == 0)
        {
            return false;
        }

        if (string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string relativePath = path;
        if (path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = path.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

        foreach (var segment in segments)
        {
            if (excludedPathSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetAttributes(string path, bool directory, ScanDiagnostics diagnostics, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception))
        {
            if (directory) diagnostics.DirectoryFailed(path);
            else diagnostics.FileFailed(path);
            attributes = default;
            return false;
        }
    }

    private static IEnumerable<string> EnumerateSafely(Func<IEnumerable<string>> factory, string directory, ScanDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        IEnumerator<string>? enumerator = null;
        try { enumerator = factory().GetEnumerator(); }
        catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception)) { diagnostics.DirectoryFailed(directory); }
        if (enumerator is null) yield break;
        using (enumerator)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var moved = false;
                try { moved = enumerator.MoveNext(); }
                catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception)) { diagnostics.DirectoryFailed(directory); }
                if (!moved) yield break;
                yield return enumerator.Current;
            }
        }
    }
}
