using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DuplicateFinder.Core;
using Microsoft.Win32.SafeHandles;

namespace DuplicateFinder.Desktop;

public sealed class WindowsFileIdentityProvider : IFileIdentityProvider
{
    private const int FileIdInformationClass = 18;
    private const int FileStandardInformationClass = 1;

    public FileIdentity ReadIdentity(FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows file identity is unavailable on this platform.");
        if (!GetFileInformationByHandleEx(stream.SafeFileHandle, FileIdInformationClass, out FileIdInformation identity, (uint)Marshal.SizeOf<FileIdInformation>()) ||
            !GetFileInformationByHandleEx(stream.SafeFileHandle, FileStandardInformationClass, out FileStandardInformation standard, (uint)Marshal.SizeOf<FileStandardInformation>()))
        {
            throw new IOException("File identity could not be read.", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        if (standard.NumberOfLinks == 0 || standard.DeletePending != 0 || standard.Directory != 0)
            throw new IOException("File identity is not suitable for duplicate scanning.");
        return new FileIdentity(identity.VolumeSerialNumber, identity.FileIdLow, identity.FileIdHigh, standard.NumberOfLinks);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInformation
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInformation
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileIdInformation information, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileStandardInformation information, uint size);
}
