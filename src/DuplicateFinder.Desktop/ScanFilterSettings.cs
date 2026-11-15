namespace DuplicateFinder.Desktop;

public sealed record ScanFilterSettings(string MinimumSizeMiB = "", string MaximumSizeMiB = "", bool IncludeHidden = false, string ExcludedFolderNames = "");
