namespace DuplicateFinder.Tests;

using DuplicateFinder.Core;

public sealed class CleanupValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DuplicateFinder.Tests", Guid.NewGuid().ToString("N"));
    private readonly string[] _paths;
    private readonly DuplicateCleanupOperation _operation;

    public CleanupValidatorTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new[] { "keep.txt", "first.txt", "second.txt" }.Select(name => Path.Combine(_root, name)).ToArray();
        foreach (var path in _paths)
        {
            File.WriteAllText(path, new string('a', 128 * 1024 + 17));
        }

        var selection = new DuplicateReviewSelection(Assert.Single(new DuplicateDetector().FindDuplicates(_root)), _paths[0]);
        _operation = new DuplicateCleanupOperation(new DuplicateCleanupExecution(new DuplicateCleanupPlan(selection, DuplicateAction.Delete)));
    }

    [Fact]
    public void Validate_AcceptsIdenticalFilesWithoutModifyingThem()
    {
        new CleanupValidator().Validate(_operation);

        Assert.All(_paths, path => Assert.Equal(new string('a', 128 * 1024 + 17), File.ReadAllText(path)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Validate_RejectsSameLengthContentChangesIncludingBeyondFirstBuffer(int changedIndex)
    {
        using (var stream = File.OpenWrite(_paths[changedIndex]))
        {
            stream.Position = stream.Length - 1;
            stream.WriteByte((byte)'b');
        }

        Assert.Throws<IOException>(() => new CleanupValidator().Validate(_operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Validate_RejectsMissingFiles(int missingIndex)
    {
        File.Delete(_paths[missingIndex]);

        Assert.Throws<FileNotFoundException>(() => new CleanupValidator().Validate(_operation));
        Assert.True(File.Exists(_paths[1]));
    }

    [Fact]
    public void Validate_RejectsChangedLength()
    {
        File.AppendAllText(_paths[2], "changed");

        Assert.Throws<IOException>(() => new CleanupValidator().Validate(_operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Validate_HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new CleanupValidator().Validate(_operation, cancellation.Token));
    }

    [Fact]
    public void Validate_RejectsFilesChangedIdenticallySinceScanning()
    {
        foreach (var path in _paths)
        {
            File.WriteAllText(path, new string('b', 128 * 1024 + 17));
        }

        Assert.Throws<IOException>(() => new CleanupValidator().Validate(_operation));
        Assert.All(_paths, path => Assert.True(File.Exists(path)));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
