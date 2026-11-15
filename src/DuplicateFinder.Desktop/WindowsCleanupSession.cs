using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DuplicateFinder.Core;
using Microsoft.Win32.SafeHandles;

namespace DuplicateFinder.Desktop;

/// <summary>Owns locked files from preflight through handle-based deletion or recovery moves.</summary>
public sealed class WindowsCleanupSession : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint ReadAttributes = 0x80;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;
    private const int FileDispositionInformation = 4;
    private const int FileRenameInformation = 3;
    private readonly List<SafeFileHandle> _directories = new();
    private readonly List<FileStream> _targets = new();
    private readonly List<FileStream> _references = new();
    private readonly List<string> _destinations = new();
    private FileStream? _keep;
    private bool _disposed;
    private bool _permanentDeletion;
    public int MovedCount { get; private set; }
    public int AlreadyOriginalCount { get; private set; }

    private WindowsCleanupSession() { }

    internal Stream DiagnosticStream => _keep ?? throw new InvalidOperationException("No diagnostic file is open.");

    internal static WindowsCleanupSession CreateDiagnosticFile(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var session = new WindowsCleanupSession();
        try
        {
            path = GetSupportedPath(path);
            session.LockParents(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            // CREATE_NEW refuses existing files, including existing links. No deletion access is requested.
            var handle = CreateFile(NativePath(path), 0x40000000 | ReadAttributes, FileShare.Read,
                IntPtr.Zero, 1, OpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new IOException("A new diagnostic file could not be created.");
            }
            try
            {
                CheckAttributes(handle, directory: false);
                session._keep = new FileStream(handle, FileAccess.Write);
            }
            catch { handle.Dispose(); throw; }
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    internal static async System.Threading.Tasks.Task<byte[]> ReadPreviewBytesAsync(string path, System.Threading.CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        cancellationToken.ThrowIfCancellationRequested();
        using var session = new WindowsCleanupSession();
        path = GetSupportedPath(path);
        session.LockParents(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        session._keep = OpenFile(path, deleteAccess: false);
        if (session._keep.Length > TextFilePreview.MaximumBytes) return new byte[TextFilePreview.MaximumBytes + 1];
        var buffer = new byte[TextFilePreview.MaximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await session._keep.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        return buffer.AsSpan(0, count).ToArray();
    }

    public static WindowsCleanupSession Prepare(DuplicateCleanupOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Cleanup requires Windows.");
        }

        if (!operation.CanExecute || operation.Action is not DuplicateAction.Delete and not DuplicateAction.Quarantine ||
            operation.PathsToRemove.Count is < 1 or > 128)
        {
            throw new InvalidOperationException("Cleanup requires a delete or quarantine review of at most 128 targets. No files were removed.");
        }

        var session = new WindowsCleanupSession { _permanentDeletion = operation.Action == DuplicateAction.Delete };
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

    internal static string GetSupportedPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new IOException("Cleanup requires absolute local paths. Rescan before cleanup.");
        }

        // Windows normalization can erase these suffixes, so inspect the input first.
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Skip(1)
            .Any(segment => segment is not "." and not ".." && (segment.EndsWith(' ') || segment.EndsWith('.'))))
        {
            throw new IOException("Cleanup does not accept trailing-dot or trailing-space path aliases.");
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

    internal void PrepareDestinations(IReadOnlyList<string> paths)
    {
        if (paths.Count != _targets.Count) throw new ArgumentException("A destination is required for every target.", nameof(paths));
        var lockedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var destination = GetSupportedPath(path);
            LockParents(destination, lockedDirectories);
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Recovery destination already exists.");
            _destinations.Add(destination);
        }
    }

    internal static WindowsCleanupSession PrepareRestore(string batchDirectory, IReadOnlyList<QuarantineEntry> entries)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Restore requires Windows.");
        var session = new WindowsCleanupSession();
        try
        {
            var lockedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var validator = new CleanupValidator();
            foreach (var entry in entries)
            {
                var original = GetSupportedPath(entry.OriginalPath);
                var stored = GetSupportedPath(Path.Combine(batchDirectory, entry.StoredName));
                session.LockParents(original, lockedDirectories);
                session.LockParents(stored, lockedDirectories);
                if (Directory.Exists(stored) || Directory.Exists(original)) throw new IOException("A recovery path is occupied by a directory.");
                if (File.Exists(stored))
                {
                    if (File.Exists(original)) throw new IOException("Restore would overwrite an existing file. No files were restored.");
                    var file = OpenFile(stored, deleteAccess: true);
                    session._targets.Add(file);
                    session._destinations.Add(original);
                    validator.ValidateFingerprint(file, entry.SizeBytes, entry.ContentHash);
                }
                else
                {
                    var file = OpenFile(original, deleteAccess: false);
                    session._references.Add(file);
                    validator.ValidateFingerprint(file, entry.SizeBytes, entry.ContentHash);
                    session.AlreadyOriginalCount++;
                }
            }
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal void MoveTargets(Action<int>? afterMove = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_destinations.Count != _targets.Count) throw new InvalidOperationException("Recovery destinations are not prepared.");
        if (_keep is not null) CheckAttributes(_keep.SafeFileHandle, directory: false);
        for (var index = 0; index < _targets.Count; index++)
        {
            CheckAttributes(_targets[index].SafeFileHandle, directory: false);
            var name = Encoding.Unicode.GetBytes(NativePath(_destinations[index]));
            var nameOffset = Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.FileName)).ToInt32();
            var lengthOffset = Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.FileNameLength)).ToInt32();
            var buffer = new byte[Math.Max(Marshal.SizeOf<RenameInformation>(), nameOffset + name.Length + 2)];
            BitConverter.GetBytes(name.Length).CopyTo(buffer, lengthOffset);
            name.CopyTo(buffer, nameOffset);
            if (!SetFileInformationByHandle(_targets[index].SafeFileHandle, FileRenameInformation, buffer, (uint)buffer.Length))
            {
                throw new IOException("Recovery move stopped without overwriting a destination.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
            MovedCount++;
            afterMove?.Invoke(MovedCount);
        }
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
        var handle = CreateFile(NativePath(path), access, share, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
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
        if (!directory && information.NumberOfLinks != 1)
        {
            throw new IOException("Hard-linked files are excluded from cleanup and restore.");
        }
    }

    internal void DeleteTargets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_permanentDeletion) throw new InvalidOperationException("This session does not authorize permanent deletion.");
        if (_keep is not null) CheckAttributes(_keep.SafeFileHandle, directory: false);
        // Every validated file remains locked; no path is reopened for deletion.
        foreach (var target in _targets)
        {
            CheckAttributes(target.SafeFileHandle, directory: false);
            byte deleteFile = 1;
            if (!SetFileInformationByHandle(target.SafeFileHandle, FileDispositionInformation, ref deleteFile, 1))
            {
                throw new IOException("Cleanup stopped; some targets may already be removed. Rescan before retrying.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
    }

    private static string NativePath(string path) => path.Length >= 260 ? @"\\?\" + path : path;

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
        foreach (var reference in _references) reference.Dispose();
        for (var index = _directories.Count - 1; index >= 0; index--)
        {
            _directories[index].Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RenameInformation
    {
        public uint Flags;
        public IntPtr RootDirectory;
        public uint FileNameLength;
        public char FileName;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, byte[] information, uint size);
}
