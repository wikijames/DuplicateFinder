using System.Security.Cryptography;
using DuplicateFinder.Core;

namespace DuplicateFinder.Tests;

public sealed class HashBufferBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(1048583)]
    public void FullAndSampleHashesMatchFrameworkAcrossBufferBoundaries(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        using var stream = new MemoryStream(bytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), DuplicateDetector.ComputeHash(stream, CancellationToken.None));
        stream.Position = 0;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, Math.Min(length, 65536)))),
            DuplicateDetector.ComputeHash(stream, CancellationToken.None, 65536));
    }
}
