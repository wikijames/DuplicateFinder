using System;
using System.Linq;
using System.Globalization;
using System.IO;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public static class ScanSetup
{
    public static ScanOptions CreateOptions(FileTypePreset preset, string? customExtensions, ScanFilterSettings settings)
    {
        var options = CreateOptions(preset, customExtensions);
        ApplyFilters(options, settings);
        return options;
    }

    public static void ApplyFilters(ScanOptions options, ScanFilterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        var minimum = ParseSize(settings.MinimumSizeMiB, minimum: true);
        var maximum = ParseSize(settings.MaximumSizeMiB, minimum: false);
        var names = settings.ExcludedFolderNames.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Any(name => name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\')))
            throw new ArgumentException("Enter excluded folder names, not paths or wildcard patterns.");
        options.MinimumSizeBytes = minimum;
        options.MaximumSizeBytes = maximum;
        options.IncludeHidden = settings.IncludeHidden;
        options.ExcludedDirectoryNames = new[] { QuarantineService.FolderName }.Concat(names).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        options.Validate();
    }

    private static long? ParseSize(string text, bool minimum)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out var value) ||
            value < 0 || value > (decimal)long.MaxValue / (1024 * 1024))
            throw new ArgumentException("Sizes must be non-negative numbers in MiB within the supported range.");
        var bytes = value * 1024 * 1024;
        var rounded = minimum ? decimal.Ceiling(bytes) : decimal.Floor(bytes);
        if (rounded > long.MaxValue) throw new ArgumentException("Size exceeds the supported range.");
        return (long)rounded;
    }

    public static ScanOptions CreateOptions(FileTypePreset preset, string? customExtensions)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.IsCustom && string.IsNullOrWhiteSpace(customExtensions))
        {
            throw new ArgumentException("Enter custom extensions or choose All files.", nameof(customExtensions));
        }
        return CreateOptions(preset.IsCustom ? customExtensions : preset.Extensions);
    }

    public static ScanOptions CreateOptions(string? extensionText)
    {
        var extensions = (extensionText ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (extensions.Any(extension => extension.Length < 2 || extension.Skip(1).Any(character => !char.IsLetterOrDigit(character) && character is not '_' and not '-' and not '+')))
        {
            throw new ArgumentException("Use extensions such as .txt, .jpg or .pdf, separated by commas.", nameof(extensionText));
        }

        return new ScanOptions { AllowedExtensions = extensions };
    }
}
