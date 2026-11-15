using System;
using DuplicateFinder.Core;

namespace DuplicateFinder.Desktop;

public sealed class WindowsCleanupService
{
    public void Execute(DuplicateCleanupOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var session = WindowsCleanupSession.Prepare(operation);
        session.DeleteTargets();
    }
}
