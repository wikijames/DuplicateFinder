using System;
using System.Globalization;
using System.IO;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public sealed class DuplicateGroupItem(DuplicateGroup group)
{
    public DuplicateGroup Group { get; } = group;
    public string Title => Path.GetFileName(Group.FilePaths[0]);
    public string Summary => $"{Group.FilePaths.Count} files · {DisplayFileSize.Format(Group.SizeBytes)} each";
    public string Reclaimable => $"{DisplayFileSize.Format(Group.ReclaimableBytes)} in extra copies";
    public override string ToString() => $"{Title} — {Summary}";
}

public sealed class DuplicateFileItem(string path, long sizeBytes, bool keep)
{
    public string FileName { get; } = Path.GetFileName(path);
    public string FullPath { get; } = path;
    public string Size { get; } = DisplayFileSize.Format(sizeBytes);
    public string Disposition { get; } = keep ? "KEEP" : "REMOVE AFTER CONFIRMATION";
    public override string ToString() => $"{Disposition}: {FileName}";
}

public sealed class KeepFileChoice(string path)
{
    public string FullPath { get; } = path;
    public string DisplayName { get; } = Path.GetFileName(path) + " — " + Path.GetDirectoryName(path);
    public override string ToString() => DisplayName;
}

internal static class DisplayFileSize
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
