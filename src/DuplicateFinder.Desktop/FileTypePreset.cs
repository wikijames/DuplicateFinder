using System;
using System.Collections.Generic;

namespace DuplicateFinder.Desktop;

public sealed record FileTypePreset(string Name, string Extensions, bool IsCustom = false)
{
    public static IReadOnlyList<FileTypePreset> CommonTypes { get; } = Array.AsReadOnly(new[]
    {
        new FileTypePreset("All files", ""),
        new FileTypePreset("Images", ".jpg, .jpeg, .png, .gif, .bmp, .webp, .tif, .tiff, .heic, .heif, .avif, .svg, .ico"),
        new FileTypePreset("Audio", ".mp3, .wav, .flac, .aac, .m4a, .ogg, .opus, .wma, .aiff, .aif, .alac"),
        new FileTypePreset("Videos", ".mp4, .mkv, .mov, .avi, .wmv, .webm, .m4v, .mpg, .mpeg, .3gp, .mts, .m2ts"),
        new FileTypePreset("PDF", ".pdf"),
        new FileTypePreset("Documents", ".doc, .docx, .odt, .rtf, .txt, .md, .xls, .xlsx, .ods, .csv, .ppt, .pptx, .odp"),
        new FileTypePreset("Archives", ".zip, .7z, .rar, .tar, .gz, .bz2, .xz, .tgz"),
        new FileTypePreset("Custom extensions", "", IsCustom: true)
    });

    public override string ToString() => Name;
}
