using System;
using System.IO;
using DuplicateFinder.Core;
using Microsoft.VisualBasic.FileIO;

namespace DuplicateFinder.Desktop;

public sealed class WindowsCleanupService
{
    public void Execute(DuplicateCleanupOperation operation)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (!operation.CanExecute)
        {
            throw new InvalidOperationException("This cleanup operation cannot be executed.");
        }

        foreach (var path in operation.PathsToRemove)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            if (operation.Action == DuplicateAction.Delete)
            {
                File.Delete(path);
                continue;
            }

            if (operation.Action == DuplicateAction.MoveToRecycleBin)
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
        }
    }
}
