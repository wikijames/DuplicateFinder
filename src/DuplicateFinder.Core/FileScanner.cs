namespace DuplicateFinder.Core;

public sealed class FileScanner
{
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
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        foreach (var candidatePath in EnumerateCandidatePaths(rootPath, effectivePattern, options.IncludeHidden, excludedDirectoryNames, excludedPathSegments, excludedFileNames, enumerationOptions, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!options.IncludeHidden && IsHidden(candidatePath))
            {
                continue;
            }

            var fileInfo = new FileInfo(candidatePath);

            if (options.MinimumSizeBytes is long minimumSize && fileInfo.Length < minimumSize)
            {
                continue;
            }

            if (options.MaximumSizeBytes is long maximumSize && fileInfo.Length > maximumSize)
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

    private static IEnumerable<string> EnumerateCandidatePaths(string rootPath, string searchPattern, bool includeHidden, HashSet<string>? exclusionNames, HashSet<string>? excludedPathSegments, HashSet<string>? excludedFileNames, EnumerationOptions options, CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(rootPath);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDirectory = directories.Pop();

            if (!includeHidden && IsHidden(currentDirectory))
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

            IEnumerable<string> childDirectories;

            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory, "*", options);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
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

                if (IsReparsePoint(childDirectory))
                {
                    continue;
                }

                if (!includeHidden && IsHidden(childDirectory))
                {
                    continue;
                }

                directories.Push(childDirectory);
            }

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(currentDirectory, searchPattern, options);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsReparsePoint(file))
                {
                    continue;
                }

                var fileName = Path.GetFileName(file);
                if (excludedFileNames is not null && excludedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!includeHidden && IsHidden(file))
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

    private static bool IsHidden(string filePath)
    {
        if (!File.Exists(filePath) && !Directory.Exists(filePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        var startsWithDot = !string.IsNullOrEmpty(fileName) && fileName.StartsWith(".", StringComparison.Ordinal);

        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(filePath);
            return startsWithDot || (attributes & FileAttributes.Hidden) != 0;
        }

        return startsWithDot;
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

        var relativePath = path;
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

    private static bool IsReparsePoint(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }
}
