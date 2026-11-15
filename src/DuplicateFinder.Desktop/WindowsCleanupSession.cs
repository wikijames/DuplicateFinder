using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DuplicateFinder.Core;
using Microsoft.Win32.SafeHandles;

namespace DuplicateFinder.Desktop;

/// <summary>Owns locked files from preflight through handle-based deletion.</summary>
public sealed class WindowsCleanupSession : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint ReadAttributes = 0x80;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;
    private const int FileDispositionInformation = 4;
    private readonly List<SafeFileHandle> _directories = new();
    private readonly List<FileStream> _targets = new();
    private FileStream? _keep;
    private bool _disposed;

    private WindowsCleanupSession() { }

    public static WindowsCleanupSession Prepare(DuplicateCleanupOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Cleanup requires Windows.");
        }

        if (!operation.CanExecute || operation.Action != DuplicateAction.Delete ||
            operation.PathsToRemove.Count is < 1 or > 128)
        {
            throw new InvalidOperationException("Cleanup requires a permanent-delete review of at most 128 targets. No files were removed.");
        }

        var session = new WindowsCleanupSession();
        try
        {
            var paths = operation.PathsToRemove.Prepend(operation.KeepPath).Select(GetSupportedPath).ToArray();
            if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            {
                throw new IOException("Cleanup paths overlap. Rescan before cleanup.");
            }

            var lockedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                session.LockParents(path, lockedDirectories);
            }
            session._keep = OpenFile(paths[0], deleteAccess: false);
            foreach (var path in paths.Skip(1))
            {
                session._targets.Add(OpenFile(path, deleteAccess: true));
            }

            var validator = new CleanupValidator();
            foreach (var target in session._targets)
            {
                validator.ValidatePair(operation, session._keep, target);
            }
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static string GetSupportedPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new IOException("Cleanup requires absolute local paths. Rescan before cleanup.");
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.Length < 4 || fullPath[1] != ':' || fullPath[2] != '\\' ||
            fullPath.AsSpan(2).Contains(':') ||
            fullPath.Split('\\').Skip(1).Any(segment => segment.EndsWith(' ') || segment.EndsWith('.')))
        {
            throw new IOException("Cleanup supports ordinary local file paths only.");
        }

        if (new DriveInfo(Path.GetPathRoot(fullPath)!).DriveType is not DriveType.Fixed and not DriveType.Removable)
        {
            throw new IOException("Cleanup requires a local fixed or removable drive.");
        }

        return fullPath;
    }

    private void LockParents(string path, HashSet<string> lockedDirectories)
    {
        var parents = new Stack<string>();
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
        {
            parents.Push(parent);
        }
        while (parents.TryPop(out var parent))
        {
            if (!lockedDirectories.Add(parent))
            {
                continue;
            }
            if (_directories.Count >= 512)
            {
                throw new IOException("Too many cleanup directories. Review a smaller group.");
            }
            var handle = OpenHandle(parent, ReadAttributes, FileShare.Read | FileShare.Write, BackupSemantics | OpenReparsePoint);
            _directories.Add(handle);
            CheckAttributes(handle, directory: true);
        }
    }

    private static FileStream OpenFile(string path, bool deleteAccess)
    {
        var handle = OpenHandle(path, GenericRead | (deleteAccess ? DeleteAccess : 0), FileShare.Read, OpenReparsePoint);
        try
        {
            CheckAttributes(handle, directory: false);
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenHandle(string path, uint access, FileShare share, uint flags)
    {
        var handle = CreateFile(path, access, share, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("Could not lock cleanup files. Close other applications and rescan.", new Win32Exception(error));
        }

        return handle;
    }

    private static void CheckAttributes(SafeFileHandle handle, bool directory)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            throw new IOException("Could not inspect cleanup file.", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        var attributes = (FileAttributes)information.Attributes;
        if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != directory)
        {
            throw new IOException("Linked files and directories cannot be used for cleanup. Rescan before cleanup.");
        }
    }

    internal void DeleteTargets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Every validated file remains locked; no path is reopened for deletion.
        foreach (var target in _targets)
        {
            byte deleteFile = 1;
            if (!SetFileInformationByHandle(target.SafeFileHandle, FileDispositionInformation, ref deleteFile, 1))
            {
                throw new IOException("Cleanup stopped; some targets may already be removed. Rescan before retrying.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var target in _targets)
        {
            target.Dispose();
        }
        _keep?.Dispose();
        for (var index = _directories.Count - 1; index >= 0; index--)
        {
            _directories[index].Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte information, uint size);
}
