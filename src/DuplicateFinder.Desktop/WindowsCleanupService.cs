using System;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public sealed class WindowsCleanupService
{
    public void Execute(DuplicateCleanupOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Action != DuplicateAction.Delete)
        {
            throw new InvalidOperationException("Permanent deletion requires an explicit delete operation.");
        }
        using var session = WindowsCleanupSession.Prepare(operation);
        session.DeleteTargets();
    }
}
