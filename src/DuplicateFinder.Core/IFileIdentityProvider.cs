namespace DuplicateFinder.Core;

/// <summary>Reads identity from the caller's open file; does not own or close it.</summary>
public interface IFileIdentityProvider
{
    FileIdentity ReadIdentity(FileStream stream);
}

public readonly record struct FileIdentity(ulong VolumeSerialNumber, ulong FileIdLow, ulong FileIdHigh, uint HardLinkCount);
