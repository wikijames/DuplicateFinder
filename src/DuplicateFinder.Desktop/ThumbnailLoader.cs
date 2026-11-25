using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DuplicateFinder.Desktop;

// One decoder at a time; cache small encoded previews, never source-size bitmaps.
internal sealed class ThumbnailLoader : IDisposable
{
    internal const int MaximumCachedThumbnails = 64;
    internal const int ThumbnailSize = 96;
    private readonly SemaphoreSlim _decoder = new(1, 1);
    private readonly Dictionary<string, LinkedListNode<(string Path, ImagePreviewResult Result)>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Path, ImagePreviewResult Result)> _recent = new();
    private readonly Func<string, CancellationToken, ImagePreviewResult> _read;
    private CancellationTokenSource _lifetime = new();
    private bool _paused;
    private bool _disposed;
    private int _generation;
    internal event Action? Resumed;
    internal int CachedCount { get { lock (_cache) return _cache.Count; } }

    internal ThumbnailLoader(Func<string, CancellationToken, ImagePreviewResult>? read = null) =>
        _read = read ?? ((path, token) => ImageFilePreview.Read(path, token, ThumbnailSize));

    internal async Task<ImagePreviewResult> LoadAsync(string path, CancellationToken token)
    {
        CancellationTokenSource request;
        int generation;
        lock (_cache)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (_paused) return new("Preview paused while an operation is in progress.");
            if (TryGetCached(path) is { } cached) return cached;
            request = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            generation = _generation;
        }
        using (request)
        {
            await _decoder.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                request.Token.ThrowIfCancellationRequested();
                lock (_cache) if (TryGetCached(path) is { } cached) return cached;
                var result = await Task.Run(() => _read(path, request.Token), request.Token).ConfigureAwait(false);
                request.Token.ThrowIfCancellationRequested();
                lock (_cache)
                {
                    // Clear/pause/dispose cancels this request before publishing to the cache.
                    request.Token.ThrowIfCancellationRequested();
                    if (generation != _generation) throw new OperationCanceledException(request.Token);
                    var node = _recent.AddFirst((path, result));
                    _cache.Add(path, node);
                    while (_cache.Count > MaximumCachedThumbnails)
                    {
                        _cache.Remove(_recent.Last!.Value.Path);
                        _recent.RemoveLast();
                    }
                }
                return result;
            }
            finally { _decoder.Release(); }
        }
    }

    private ImagePreviewResult? TryGetCached(string path)
    {
        if (!_cache.TryGetValue(path, out var node)) return null;
        _recent.Remove(node);
        _recent.AddFirst(node);
        return node.Value.Result;
    }

    internal void SetPaused(bool paused)
    {
        CancellationTokenSource? previous = null;
        lock (_cache)
        {
            if (_disposed || _paused == paused) return;
            _paused = paused;
            if (paused) { _generation++; previous = _lifetime; _lifetime = new(); }
        }
        previous?.Cancel();
        previous?.Dispose();
        if (!paused) Resumed?.Invoke();
    }

    internal async Task WaitForIdleAsync()
    {
        await _decoder.WaitAsync().ConfigureAwait(false);
        _decoder.Release();
    }

    internal void Clear()
    {
        CancellationTokenSource previous;
        lock (_cache)
        {
            if (_disposed) return;
            previous = _lifetime;
            _generation++;
            _lifetime = new();
            _cache.Clear();
            _recent.Clear();
        }
        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        lock (_cache)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _cache.Clear();
            _recent.Clear();
        }
        _lifetime.Cancel();
        _lifetime.Dispose();
        // Async waiters release the semaphore after cancellation. It has no OS
        // wait handle, and must not be disposed while a decoder is unwinding.
    }
}
