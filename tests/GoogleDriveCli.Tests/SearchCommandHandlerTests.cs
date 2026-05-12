using GoogleDriveCli.Commands;
using GoogleDriveCli.Models;
using GoogleDriveCli.Services.Drive;
using GoogleDriveCli.Services.Local;
using GoogleDriveCli.Services.Manifest;
using Moq;
using Spectre.Console.Testing;

namespace GoogleDriveCli.Tests;

/// <summary>
/// Integration tests for <see cref="SearchCommandHandler"/>. The Drive client
/// is mocked; the file system and manifest stores are real (in temp paths);
/// the Spectre console is a <see cref="TestConsole"/> so we can assert on
/// the rendered output (which IS the command's behaviour).
/// </summary>
public class SearchCommandHandlerTests : IDisposable
{
    private readonly string _downloadsDir;
    private readonly string _manifestPath;
    private readonly TestConsole _console = new();

    public SearchCommandHandlerTests()
    {
        _downloadsDir = Path.Combine(Path.GetTempPath(), $"search-test-{Guid.NewGuid():N}");
        _manifestPath = Path.Combine(Path.GetTempPath(), $"manifest-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_downloadsDir)) Directory.Delete(_downloadsDir, recursive: true);
        if (File.Exists(_manifestPath)) File.Delete(_manifestPath);
    }

    [Fact]
    public async Task HandleAsync_FileInManifestAndOnDisk_RendersAsDownloaded()
    {
        // ARRANGE — file 'notes.txt' is downloaded (manifest entry + on disk)
        var file = MakeFile("id-1", "notes.txt");
        var client = MakeMockClient(searchResults: new[] { file });

        var localStore = new LocalFileStore(_downloadsDir);
        Directory.CreateDirectory(_downloadsDir);
        File.WriteAllText(Path.Combine(_downloadsDir, "notes.txt"), "x");

        var manifest = new JsonManifestStore(_manifestPath);
        manifest.AddOrUpdate(new ManifestEntry(
            FileId: "id-1",
            LocalRelativePath: "notes.txt",
            DriveModifiedTime: file.ModifiedTime!.Value,
            SizeBytes: 100,
            DownloadedAt: DateTimeOffset.UtcNow));
        await manifest.SaveAsync(CancellationToken.None);

        var handler = new SearchCommandHandler(client.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync("notes", CancellationToken.None);

        // ASSERT
        Assert.Equal(0, exit);
        Assert.Contains("Downloaded", _console.Output);
        Assert.DoesNotContain("[Not Downloaded]", _console.Output);
    }

    [Fact]
    public async Task HandleAsync_FileNotInManifest_RendersAsNotDownloaded()
    {
        // ARRANGE — file exists in Drive results but neither manifest nor disk knows about it
        var file = MakeFile("id-2", "absent.txt");
        var client = MakeMockClient(searchResults: new[] { file });

        var localStore = new LocalFileStore(_downloadsDir);
        var manifest = new JsonManifestStore(_manifestPath);
        var handler = new SearchCommandHandler(client.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync("absent", CancellationToken.None);

        // ASSERT
        Assert.Equal(0, exit);
        Assert.Contains("[Not Downloaded]", _console.Output);
        // The literal column header "Status" appears once; nothing should render as plain "Downloaded".
        Assert.DoesNotContain(" Downloaded ", _console.Output);
    }

    [Fact]
    public async Task HandleAsync_FileInManifestButLocalCopyMissing_RendersAsNotDownloaded()
    {
        // ARRANGE — manifest claims a download happened, but the file is gone from disk.
        // The hybrid state-management approach should catch this and mark as Not Downloaded.
        var file = MakeFile("id-3", "deleted.txt");
        var client = MakeMockClient(searchResults: new[] { file });

        var localStore = new LocalFileStore(_downloadsDir);
        // Note: do NOT create the file on disk.
        var manifest = new JsonManifestStore(_manifestPath);
        manifest.AddOrUpdate(new ManifestEntry(
            FileId: "id-3",
            LocalRelativePath: "deleted.txt",
            DriveModifiedTime: file.ModifiedTime!.Value,
            SizeBytes: 100,
            DownloadedAt: DateTimeOffset.UtcNow));
        await manifest.SaveAsync(CancellationToken.None);

        var handler = new SearchCommandHandler(client.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync("deleted", CancellationToken.None);

        // ASSERT
        Assert.Equal(0, exit);
        Assert.Contains("[Not Downloaded]", _console.Output);
    }

    [Fact]
    public async Task HandleAsync_NoResults_PrintsNoMatchesMessage()
    {
        // ARRANGE
        var client = MakeMockClient(searchResults: Array.Empty<DriveFile>());
        var localStore = new LocalFileStore(_downloadsDir);
        var manifest = new JsonManifestStore(_manifestPath);
        var handler = new SearchCommandHandler(client.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync("doesnotexist", CancellationToken.None);

        // ASSERT
        Assert.Equal(0, exit);
        Assert.Contains("No files matched", _console.Output);
    }

    [Fact]
    public async Task HandleAsync_FoldersAndGoogleNativeFiles_RenderWithEmDashStatus()
    {
        // ARRANGE — both a folder and a Google-native doc; neither is "downloadable"
        var folder = new DriveFile("id-f", "MyFolder",
            "application/vnd.google-apps.folder", null, DateTimeOffset.UtcNow, Array.Empty<string>());
        var native = new DriveFile("id-g", "MyDoc",
            "application/vnd.google-apps.document", null, DateTimeOffset.UtcNow, Array.Empty<string>());

        var client = MakeMockClient(searchResults: new[] { folder, native });
        var localStore = new LocalFileStore(_downloadsDir);
        var manifest = new JsonManifestStore(_manifestPath);
        var handler = new SearchCommandHandler(client.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync("My", CancellationToken.None);

        // ASSERT
        Assert.Equal(0, exit);
        Assert.DoesNotContain("[Not Downloaded]", _console.Output);
        Assert.DoesNotContain(" Downloaded ", _console.Output);
    }

    [Fact]
    public async Task HandleAsync_SameNamedFilesInDifferentFolders_RenderWithDistinctLocations()
    {
        // ARRANGE — two files both named "Third.png" but in different folders
        var workFolder = new DriveFile("folder-work", "Work",
            "application/vnd.google-apps.folder", null, DateTimeOffset.UtcNow, Array.Empty<string>());
        var personalFolder = new DriveFile("folder-personal", "Personal",
            "application/vnd.google-apps.folder", null, DateTimeOffset.UtcNow, Array.Empty<string>());

        var workFile = new DriveFile("id-1", "Third.png", "image/png", 100,
            DateTimeOffset.UtcNow, new[] { "folder-work" });
        var personalFile = new DriveFile("id-2", "Third.png", "image/png", 100,
            DateTimeOffset.UtcNow, new[] { "folder-personal" });

        var client = MakeMockClient(
            searchResults: new[] { workFile, personalFile },
            folders: new[] { workFolder, personalFolder });

        var localStore = new LocalFileStore(_downloadsDir);
        var manifest = new JsonManifestStore(_manifestPath);
        var handler = new SearchCommandHandler(client.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync("Third", CancellationToken.None);

        // ASSERT — both folder names should appear, disambiguating the rows
        Assert.Equal(0, exit);
        Assert.Contains("Work", _console.Output);
        Assert.Contains("Personal", _console.Output);
    }

    [Fact]
    public async Task HandleAsync_FileAtDriveRoot_RendersLocationAsMyDrive()
    {
        // ARRANGE — file with no parents (or only the root) should show "My Drive"
        var file = MakeFile("id-1", "rootfile.txt", parents: Array.Empty<string>());
        var client = MakeMockClient(searchResults: new[] { file });

        var localStore = new LocalFileStore(_downloadsDir);
        var manifest = new JsonManifestStore(_manifestPath);
        var handler = new SearchCommandHandler(client.Object, localStore, manifest, _console);

        // ACT
        var exit = await handler.HandleAsync("rootfile", CancellationToken.None);

        // ASSERT
        Assert.Equal(0, exit);
        Assert.Contains("My Drive", _console.Output);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static DriveFile MakeFile(string id, string name, IReadOnlyList<string>? parents = null) =>
        new(id, name, "text/plain", 100,
            new DateTimeOffset(2026, 5, 12, 10, 0, 0, TimeSpan.Zero),
            parents ?? Array.Empty<string>());

    private static Mock<IDriveClient> MakeMockClient(
        IEnumerable<DriveFile> searchResults,
        IEnumerable<DriveFile>? folders = null)
    {
        var mock = new Mock<IDriveClient>();
        mock.Setup(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable(searchResults));
        mock.Setup(c => c.ListFoldersAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable(folders ?? Array.Empty<DriveFile>()));
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
