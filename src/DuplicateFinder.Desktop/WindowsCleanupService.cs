using System;
using System.IO;
using DuplicateFinder.Core;

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
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
