using GoogleDriveCli.Models;
using GoogleDriveCli.Services.Manifest;

namespace GoogleDriveCli.Tests;

public class ManifestStoreTests : IDisposable
{
    private readonly string _tempPath;

    public ManifestStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"manifest-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllEntries()
    {
        // ARRANGE
        var entry1 = new ManifestEntry("file-1", "Work/notes.txt",
            new DateTimeOffset(2026, 5, 12, 10, 0, 0, TimeSpan.Zero), 1024,
            new DateTimeOffset(2026, 5, 12, 10, 5, 0, TimeSpan.Zero));
        var entry2 = new ManifestEntry("file-2", "Personal/diary.txt",
            new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero), 2048,
            new DateTimeOffset(2026, 5, 11, 9, 5, 0, TimeSpan.Zero));

        var writer = new JsonManifestStore(_tempPath);
        writer.AddOrUpdate(entry1);
        writer.AddOrUpdate(entry2);
        await writer.SaveAsync(CancellationToken.None);

        // ACT
        var reader = new JsonManifestStore(_tempPath);
        await reader.LoadAsync(CancellationToken.None);

        // ASSERT
        Assert.Equal(2, reader.AllEntries.Count);
        Assert.Equal(entry1, reader.TryGet("file-1"));
        Assert.Equal(entry2, reader.TryGet("file-2"));
    }

    [Fact]
    public async Task LoadAsync_WhenManifestDoesNotExist_LeavesStoreEmpty()
    {
        var store = new JsonManifestStore(_tempPath);
        await store.LoadAsync(CancellationToken.None);
        Assert.Empty(store.AllEntries);
        Assert.Null(store.TryGet("anything"));
    }

    [Fact]
    public void AddOrUpdate_SecondCallWithSameId_OverwritesPreviousEntry()
    {
        var store = new JsonManifestStore(_tempPath);
        var first = new ManifestEntry("file-1", "old/path.txt",
            DateTimeOffset.UtcNow.AddDays(-1), 100, DateTimeOffset.UtcNow.AddDays(-1));
        var second = first with { LocalRelativePath = "new/path.txt", SizeBytes = 200 };

        store.AddOrUpdate(first);
        store.AddOrUpdate(second);

        Assert.Single(store.AllEntries);
        Assert.Equal(second, store.TryGet("file-1"));
    }
}
