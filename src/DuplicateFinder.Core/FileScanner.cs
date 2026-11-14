namespace DuplicateFinder.Core;

public sealed class FileScanner
{
    public IEnumerable<string> EnumerateFiles(string rootPath, string searchPattern = "*", bool includeHidden = false)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory '{rootPath}' was not found.");
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        foreach (var file in Directory.EnumerateFiles(rootPath, searchPattern ?? "*", options))
        {
            if (!includeHidden && IsHidden(file))
            {
                continue;
            }

            yield return file;
        }
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
