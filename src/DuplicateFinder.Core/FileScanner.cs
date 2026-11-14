namespace DuplicateFinder.Core;

public sealed class FileScanner
{
    public IEnumerable<string> EnumerateFiles(string rootPath, string searchPattern = "*", bool includeHidden = false)
    {
        return EnumerateFiles(rootPath, new ScanOptions
        {
            SearchPattern = searchPattern ?? "*",
            IncludeHidden = includeHidden
        });
    }

    public IEnumerable<string> EnumerateFiles(string rootPath, ScanOptions? options)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory '{rootPath}' was not found.");
        }

        options ??= new ScanOptions();

        var effectivePattern = string.IsNullOrWhiteSpace(options.SearchPattern) ? "*" : options.SearchPattern;
        var normalizedExtensions = GetNormalizedExtensions(options.AllowedExtensions);

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        foreach (var file in Directory.EnumerateFiles(rootPath, effectivePattern, enumerationOptions))
        {
            if (!options.IncludeHidden && IsHidden(file))
            {
                continue;
            }

            var fileInfo = new FileInfo(file);

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
                var extension = Path.GetExtension(file);
                if (!normalizedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            yield return file;
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

    private static bool IsHidden(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(filePath);
            return (attributes & FileAttributes.Hidden) != 0;
        }

        var fileName = Path.GetFileName(filePath);
        return !string.IsNullOrEmpty(fileName) && fileName.StartsWith(".", StringComparison.Ordinal);
    }
}
