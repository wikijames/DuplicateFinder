using DuplicateFinder.Desktop;

namespace DuplicateFinder.Tests;

public sealed class ThumbnailLoaderTests
{
    [Fact]
    public async Task ConcurrentRequestsDecodeOnceAndUseBoundedLeastRecentlyUsedCache()
    {
        var reads = 0;
        using var loader = new ThumbnailLoader((path, token) =>
        {
            Interlocked.Increment(ref reads);
            return new ImagePreviewResult("Synthetic preview", [1, 2, 3]);
        });
        var requests = Enumerable.Range(0, 20).Select(_ => loader.LoadAsync("shared.png", CancellationToken.None));
        await Task.WhenAll(requests);
        Assert.Equal(1, reads);
        for (var index = 0; index < ThumbnailLoader.MaximumCachedThumbnails; index++)
            await loader.LoadAsync($"image-{index}.png", CancellationToken.None);
        Assert.Equal(ThumbnailLoader.MaximumCachedThumbnails, loader.CachedCount);
        var before = reads;
        await loader.LoadAsync("shared.png", CancellationToken.None);
        Assert.Equal(before + 1, reads);
    }

    [Fact]
    public async Task OnlyOneDecoderRunsAndCancelledQueuedWorkIsNeverDecoded()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reads = 0;
        using var loader = new ThumbnailLoader((path, token) =>
        {
            Interlocked.Increment(ref reads);
            started.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return new ImagePreviewResult("Synthetic");
        });
        var first = loader.LoadAsync("first.png", CancellationToken.None);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            using var cancelled = new CancellationTokenSource();
            var queued = loader.LoadAsync("queued.png", cancelled.Token);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(1, reads);
        }
        finally { release.Set(); }
        await first;
    }

    [Fact]
    public async Task ClearRejectsLateResultsAndNextScanReadsAgain()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reads = 0;
        using var loader = new ThumbnailLoader((path, token) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                started.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
            return new ImagePreviewResult("Synthetic");
        });
        var previous = loader.LoadAsync("same.png", CancellationToken.None);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            loader.Clear();
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previous);
        Assert.Equal(0, loader.CachedCount);
        await loader.LoadAsync("same.png", CancellationToken.None);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task PausedOperationsDoNotStartReadersAndCanResume()
    {
        var reads = 0;
        using var loader = new ThumbnailLoader((path, token) => { reads++; return new ImagePreviewResult("Synthetic"); });
        loader.SetPaused(true);
        await loader.LoadAsync("image.png", CancellationToken.None);
        await loader.WaitForIdleAsync();
        Assert.Equal(0, reads);
        loader.SetPaused(false);
        await loader.LoadAsync("image.png", CancellationToken.None);
        Assert.Equal(1, reads);
    }
}
