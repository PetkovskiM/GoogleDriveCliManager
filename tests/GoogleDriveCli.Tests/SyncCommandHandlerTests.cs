using GoogleDriveCli.Commands;
using GoogleDriveCli.Models;
using GoogleDriveCli.Services.Drive;
using GoogleDriveCli.Services.Local;
using GoogleDriveCli.Services.Manifest;
using Moq;
using Spectre.Console.Testing;

namespace GoogleDriveCli.Tests;

/// <summary>
/// Integration tests for <see cref="SyncCommandHandler"/>: mock the Drive
/// client, run the real handler against real file-system and manifest stores
/// in temp paths, and assert end-to-end behaviour. Each test gets a fresh
/// temp directory + manifest path, cleaned up in <see cref="Dispose"/>.
/// </summary>
public class SyncCommandHandlerTests : IDisposable
{
    private readonly string _downloadsDir;
    private readonly string _manifestPath;
    private readonly TestConsole _console = new();

    public SyncCommandHandlerTests()
    {
        _downloadsDir = Path.Combine(Path.GetTempPath(), $"sync-test-{Guid.NewGuid():N}");
        _manifestPath = Path.Combine(Path.GetTempPath(), $"manifest-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_downloadsDir)) Directory.Delete(_downloadsDir, recursive: true);
        if (File.Exists(_manifestPath)) File.Delete(_manifestPath);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_DownloadsEveryFileAndPopulatesManifest()
    {
        // ARRANGE — 20 files, all at the Drive root
        var files = MakeFlatFiles(20);
        var driveClient = MakeMockClient(files);

        var localStore = new LocalFileStore(_downloadsDir);
        var manifest = new JsonManifestStore(_manifestPath);
        var handler = new SyncCommandHandler(driveClient.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync(dryRun: false, CancellationToken.None);

        // ASSERT
        Assert.Equal(0, exit);
        driveClient.Verify(
            c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Exactly(files.Length));
        Assert.Equal(files.Length, manifest.AllEntries.Count);
        foreach (var file in files)
            Assert.True(File.Exists(Path.Combine(_downloadsDir, file.Name)),
                $"Expected {file.Name} on disk");
    }

    [Fact]
    public async Task HandleAsync_WhenSomeDownloadsThrow_RecordsFailuresAndContinuesWithRest()
    {
        // ARRANGE — every file but id-2 and id-7 succeeds
        var files = MakeFlatFiles(10);
        var driveClient = new Mock<IDriveClient>();
        driveClient.Setup(c => c.ListAllAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable(files));
        driveClient.Setup(c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((string id, Stream _, CancellationToken _) =>
                id is "id-2" or "id-7"
                    ? throw new InvalidOperationException("simulated transient failure")
                    : Task.CompletedTask);

        var localStore = new LocalFileStore(_downloadsDir);
        var manifest = new JsonManifestStore(_manifestPath);
        var handler = new SyncCommandHandler(driveClient.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync(dryRun: false, CancellationToken.None);

        // ASSERT — handler returns non-zero (some failed), but every file was attempted
        // and the successful ones still ended up in the manifest.
        Assert.Equal(1, exit);
        driveClient.Verify(
            c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Exactly(files.Length));
        Assert.Equal(files.Length - 2, manifest.AllEntries.Count);
    }

    [Fact]
    public async Task HandleAsync_DryRun_NeverCallsDownloadAndWritesNothing()
    {
        // ARRANGE
        var files = MakeFlatFiles(5);
        var driveClient = MakeMockClient(files);

        var localStore = new LocalFileStore(_downloadsDir);
        var manifest = new JsonManifestStore(_manifestPath);
        var handler = new SyncCommandHandler(driveClient.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync(dryRun: true, CancellationToken.None);

        // ASSERT
        Assert.Equal(0, exit);
        driveClient.Verify(
            c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.False(Directory.Exists(_downloadsDir));
        Assert.False(File.Exists(_manifestPath));
    }

    [Fact]
    public async Task HandleAsync_SecondRunWithUnchangedFiles_SkipsAllDownloads()
    {
        // ARRANGE — fixed Drive modifiedTime so the second run matches the manifest
        var files = MakeFlatFiles(5, modifiedTime: new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero));

        // FIRST RUN — downloads everything
        var firstClient = MakeMockClient(files);
        var localStore = new LocalFileStore(_downloadsDir);
        var firstManifest = new JsonManifestStore(_manifestPath);
        await new SyncCommandHandler(firstClient.Object, localStore, firstManifest, _console)
            .HandleAsync(dryRun: false, CancellationToken.None);

        // SECOND RUN — fresh handler + fresh manifest store (loads from disk)
        var secondClient = MakeMockClient(files);
        var secondManifest = new JsonManifestStore(_manifestPath);
        var handler = new SyncCommandHandler(secondClient.Object, localStore, secondManifest, _console);

        // ACT
        await handler.HandleAsync(dryRun: false, CancellationToken.None);

        // ASSERT — second run skips every file because manifest modifiedTime matches.
        secondClient.Verify(
            c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static DriveFile[] MakeFlatFiles(int count, DateTimeOffset? modifiedTime = null)
    {
        var ts = modifiedTime ?? DateTimeOffset.UtcNow;
        return Enumerable.Range(0, count)
            .Select(i => new DriveFile(
                Id: $"id-{i}",
                Name: $"file-{i}.txt",
                MimeType: "text/plain",
                Size: 100,
                ModifiedTime: ts,
                Parents: Array.Empty<string>()))
            .ToArray();
    }

    private static Mock<IDriveClient> MakeMockClient(IReadOnlyCollection<DriveFile> files)
    {
        var mock = new Mock<IDriveClient>();
        mock.Setup(c => c.ListAllAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable(files));
        mock.Setup(c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static async IAsyncEnumerable<DriveFile> AsAsyncEnumerable(IEnumerable<DriveFile> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
