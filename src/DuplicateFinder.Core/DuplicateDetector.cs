namespace DuplicateFinder.Core;

using System.Security.Cryptography;

public sealed class DuplicateDetector
{
    private readonly FileScanner _scanner;
    private readonly Func<string, string> _groupHash;
    private readonly Action? _beforeVerification;
    private readonly IFileIdentityProvider? _identityProvider;

    public DuplicateDetector() : this(new FileScanner()) { }
    public DuplicateDetector(IFileIdentityProvider? identityProvider) : this(new FileScanner(), identityProvider: identityProvider) { }
    internal DuplicateDetector(FileScanner scanner, Func<string, string>? groupHash = null, Action? beforeVerification = null,
        IFileIdentityProvider? identityProvider = null)
    {
        _scanner = scanner;
        _groupHash = groupHash ?? (hash => hash);
        _beforeVerification = beforeVerification;
        _identityProvider = identityProvider;
    }

    public IReadOnlyList<DuplicateGroup> FindDuplicates(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default)
        => Scan(rootPath, options, cancellationToken).Groups;

    public IReadOnlyList<DuplicateGroup> FindDuplicates(IEnumerable<string> roots, ScanOptions? options = null, CancellationToken cancellationToken = default)
        => Scan(roots, options, cancellationToken).Groups;

    public DuplicateDetectionResult Scan(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default)
        => Scan(new[] { rootPath }, options, cancellationToken);

    public DuplicateDetectionResult Scan(IEnumerable<string> rootPaths, ScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roots = ScanRoots.Normalize(rootPaths);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("A selected scan folder was not found.");
        }

        var diagnostics = new ScanDiagnostics();
        var seenPaths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var seenIdentities = new HashSet<FileIdentity>();
        var sizeGroups = new Dictionary<long, List<Candidate>>();
        foreach (var path in EnumerateRoots(roots, options ?? new ScanOptions(), diagnostics, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenPaths.Add(path)) continue;
            try
            {
                var info = new FileInfo(path);
                info.Refresh();
                if (!info.Exists) throw new FileNotFoundException("Scan candidate disappeared.");
                FileIdentity? identity = null;
                if (_identityProvider is not null)
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    identity = _identityProvider.ReadIdentity(stream);
                    if (identity.Value.HardLinkCount < 1) throw new IOException("File link count could not be determined.");
                    if (identity.Value.HardLinkCount > 1)
                    {
                        diagnostics.HardLinkExcluded(path);
                        continue;
                    }
                    if (!seenIdentities.Add(identity.Value)) continue;
                }
                Add(sizeGroups, info.Length, new Candidate(path, info.Length, info.LastWriteTimeUtc, Identity: identity));
            }
            catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception))
            {
                diagnostics.FileFailed(path);
            }
        }
        var duplicateGroups = new List<DuplicateGroup>();

        foreach (var sizeGroup in sizeGroups.Values.Where(group => group.Count > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sampleHashGroups = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
            foreach (var candidate in sizeGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sampleHash = TryHash(candidate, diagnostics, cancellationToken, 64 * 1024);
                if (sampleHash is not null) Add(sampleHashGroups, sampleHash, candidate);
            }
            var hashGroups = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
            foreach (var sampleGroup in sampleHashGroups.Values.Where(group => group.Count > 1))
            {
                foreach (var candidate in sampleGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hash = TryHash(candidate, diagnostics, cancellationToken);
                    if (hash is not null) Add(hashGroups, _groupHash(hash), candidate with { ContentHash = hash });
                }
            }
            _beforeVerification?.Invoke();
            foreach (var bucket in hashGroups.Values.Where(group => group.Count > 1))
            {
                VerifyBucket(bucket, duplicateGroups, diagnostics, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new DuplicateDetectionResult(duplicateGroups.OrderBy(group => group.FilePaths[0], StringComparer.OrdinalIgnoreCase).ToArray(), diagnostics.Snapshot());
    }

    private IEnumerable<string> EnumerateRoots(IReadOnlyList<string> roots, ScanOptions options, ScanDiagnostics diagnostics, CancellationToken token)
    {
        options.Validate();
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            using var enumerator = _scanner.EnumerateFiles(root, options, diagnostics, token).GetEnumerator();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var moved = false;
                try { moved = enumerator.MoveNext(); }
                catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception)) { diagnostics.DirectoryFailed(root); }
                if (!moved) break;
                yield return enumerator.Current;
            }
        }
    }

    private static void Add<TKey>(Dictionary<TKey, List<Candidate>> groups, TKey key, Candidate candidate) where TKey : notnull
    {
        if (!groups.TryGetValue(key, out var group)) groups[key] = group = new List<Candidate>();
        group.Add(candidate);
    }

    private string? TryHash(Candidate candidate, ScanDiagnostics diagnostics, CancellationToken token, long? maxBytes = null)
    {
        try
        {
            EnsureUnchanged(candidate);
            using var stream = File.Open(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ValidateIdentity(candidate, stream);
            var hash = ComputeHash(stream, token, maxBytes);
            ValidateIdentity(candidate, stream);
            EnsureUnchanged(candidate);
            return hash;
        }
        catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception))
        {
            diagnostics.FileFailed(candidate.Path, exception is ScanFileChangedException);
            return null;
        }
    }

    private static void EnsureUnchanged(Candidate candidate)
    {
        var info = new FileInfo(candidate.Path);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("Scan candidate disappeared.");
        if (info.Length != candidate.Size || info.LastWriteTimeUtc != candidate.LastWriteUtc || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new ScanFileChangedException();
    }

    private void VerifyBucket(List<Candidate> bucket, List<DuplicateGroup> groups, ScanDiagnostics diagnostics, CancellationToken token)
    {
        var remaining = bucket.OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).ToList();
        while (remaining.Count > 1)
        {
            token.ThrowIfCancellationRequested();
            var reference = remaining[0];
            remaining.RemoveAt(0);
            var matches = new List<Candidate> { reference };
            var referenceFailed = false;
            for (var index = 0; index < remaining.Count;)
            {
                token.ThrowIfCancellationRequested();
                var candidate = remaining[index];
                try
                {
                    if (Compare(reference, candidate, token))
                    {
                        matches.Add(candidate);
                        remaining.RemoveAt(index);
                    }
                    else index++;
                }
                catch (VerificationFailure failure)
                {
                    diagnostics.FileFailed(failure.File.Path, failure.InnerException is ScanFileChangedException);
                    if (failure.File == reference)
                    {
                        remaining.InsertRange(0, matches.Skip(1));
                        referenceFailed = true;
                        break;
                    }
                    remaining.RemoveAt(index);
                }
            }
            if (!referenceFailed && matches.Count > 1)
                groups.Add(new DuplicateGroup(matches.Select(candidate => candidate.Path), reference.Size, reference.ContentHash));
        }
    }

    private bool Compare(Candidate reference, Candidate candidate, CancellationToken token)
    {
        using var referenceStream = OpenForVerification(reference);
        using var candidateStream = OpenForVerification(candidate);
        using var referenceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var candidateHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var referenceBuffer = new byte[64 * 1024];
        var candidateBuffer = new byte[referenceBuffer.Length];
        var equal = true;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var referenceCount = Read(referenceStream, referenceBuffer, reference);
            var candidateCount = Read(candidateStream, candidateBuffer, candidate);
            if (referenceCount != candidateCount || !referenceBuffer.AsSpan(0, referenceCount).SequenceEqual(candidateBuffer.AsSpan(0, candidateCount))) equal = false;
            referenceHash.AppendData(referenceBuffer, 0, referenceCount);
            candidateHash.AppendData(candidateBuffer, 0, candidateCount);
            if (referenceCount == 0 && candidateCount == 0) break;
        }
        ValidateHash(reference, referenceStream, referenceHash);
        ValidateHash(candidate, candidateStream, candidateHash);
        token.ThrowIfCancellationRequested();
        return equal;
    }

    private FileStream OpenForVerification(Candidate candidate)
    {
        FileStream? stream = null;
        try
        {
            EnsureUnchanged(candidate);
            stream = File.Open(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ValidateIdentity(candidate, stream);
            if (stream.Length != candidate.Size) throw new ScanFileChangedException();
            return stream;
        }
        catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception))
        {
            stream?.Dispose();
            throw new VerificationFailure(candidate, exception);
        }
    }

    private static int Read(Stream stream, byte[] buffer, Candidate candidate)
    {
        try { return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false); }
        catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception)) { throw new VerificationFailure(candidate, exception); }
    }

    private void ValidateHash(Candidate candidate, FileStream stream, IncrementalHash hash)
    {
        try
        {
            EnsureUnchanged(candidate);
            ValidateIdentity(candidate, stream);
            if (Convert.ToHexString(hash.GetHashAndReset()) != candidate.ContentHash) throw new ScanFileChangedException();
        }
        catch (Exception exception) when (ScanDiagnostics.IsRecoverable(exception)) { throw new VerificationFailure(candidate, exception); }
    }

    private void ValidateIdentity(Candidate candidate, FileStream stream)
    {
        if (candidate.Identity is { } expected && _identityProvider!.ReadIdentity(stream) != expected)
            throw new ScanFileChangedException();
    }

    private sealed record Candidate(string Path, long Size, DateTime LastWriteUtc, string? ContentHash = null, FileIdentity? Identity = null);
    private sealed class VerificationFailure(Candidate file, Exception inner) : IOException("Scan verification failed.", inner)
    {
        public Candidate File { get; } = file;
    }

    internal static string ComputeHash(Stream stream, CancellationToken cancellationToken, long? maxBytes = null)
    {
        using var sha256 = SHA256.Create();
        var buffer = new byte[1024 * 1024];
        long bytesProcessed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (maxBytes.HasValue && bytesProcessed >= maxBytes.Value)
            {
                break;
            }

            var remainingBytes = maxBytes.HasValue ? maxBytes.Value - bytesProcessed : buffer.Length;
            var bytesToRead = remainingBytes < buffer.Length ? (int)remainingBytes : buffer.Length;

            var bytesRead = stream.Read(buffer, 0, bytesToRead);
            if (bytesRead == 0)
            {
                break;
            }

            bytesProcessed += bytesRead;
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);

            if (maxBytes.HasValue && bytesProcessed >= maxBytes.Value)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>());
    }
}
