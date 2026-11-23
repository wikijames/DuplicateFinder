using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DuplicateFinder.Desktop;

// Read-only preview handles are independent of cleanup sessions and never request write/delete access.
internal static class ImagePreviewFileReader
{
    internal static byte[] Read(string path, CancellationToken cancellationToken)
    {
        path = WindowsCleanupSession.GetSupportedPath(path);
        var parents = new Stack<string>();
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            parents.Push(parent);
        if (parents.Count > 512) throw new IOException("Too many parent directories.");
        var handles = new List<SafeFileHandle>();
        try
        {
            while (parents.TryPop(out var parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Refuse links and prevent parent replacement while opening the file.
                var handle = Open(parent, 0x80, FileShare.Read | FileShare.Write, directory: true);
                handles.Add(handle);
            }
            using var fileHandle = Open(path, 0x80000000, FileShare.Read, directory: false);
            using var stream = new FileStream(fileHandle, FileAccess.Read);
            if (stream.Length > ImageFilePreview.MaximumBytes) throw new ImagePreviewLimitException();
            var bytes = new byte[checked((int)stream.Length)];
            var count = 0;
            while (count < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(bytes, count, Math.Min(65536, bytes.Length - count));
                if (read == 0) throw new IOException("Image changed while reading.");
                count += read;
            }
            return bytes;
        }
        finally
        {
            for (var index = handles.Count - 1; index >= 0; index--) handles[index].Dispose();
        }
    }

    private static SafeFileHandle Open(string path, uint access, FileShare share, bool directory)
    {
        var handle = CreateFile(@"\\?\" + path, access, share, IntPtr.Zero, 3,
            0x00200000u | (directory ? 0x02000000u : 0), IntPtr.Zero);
        try
        {
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) throw new IOException("Image is inaccessible.");
            var attributes = (FileAttributes)info.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                ((attributes & FileAttributes.Directory) != 0) != directory || (!directory && info.NumberOfLinks != 1))
                throw new IOException("Linked images are not previewed.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
}

internal sealed class ImagePreviewLimitException : IOException;
