using System;
using System.Linq;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public static class ScanSetup
{
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
