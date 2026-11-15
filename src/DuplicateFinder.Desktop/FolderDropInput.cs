using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace DuplicateFinder.Desktop;

public static class FolderDropInput
{
    public static IReadOnlyList<string> GetPaths(IDataTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        var items = transfer.TryGetFiles();
        if (items is null || items.Length == 0) throw new ArgumentException("Drop local folders only.");
        return items.Select(item => item.TryGetLocalPath() ?? throw new ArgumentException("Drop local folders only.")).ToArray();
    }
}
