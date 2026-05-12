using GoogleDriveCli.Commands;
using GoogleDriveCli.Models;
using GoogleDriveCli.Services.Drive;
using Moq;
using Spectre.Console.Testing;

namespace GoogleDriveCli.Tests;

/// <summary>
/// Integration tests for <see cref="UploadCommandHandler"/>. The Drive client is
/// mocked; a temp file on disk stands in for the "local file" argument; the
/// Spectre console is captured so we can assert on the user-facing messages.
/// </summary>
public class UploadCommandHandlerTests : IDisposable
{
    private readonly string _tempFile;
    private readonly TestConsole _console = new();

    public UploadCommandHandlerTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"upload-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(_tempFile, "hello");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public async Task HandleAsync_LocalFileMissing_PrintsErrorAndExitsNonZero()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        var client = new Mock<IDriveClient>();
        var handler = new UploadCommandHandler(client.Object, _console);

        var exit = await handler.HandleAsync(nonexistent, "Work", CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("File not found", _console.Output);
        client.VerifyNoOtherCalls();  // never touched Drive
    }

    [Fact]
    public async Task HandleAsync_EmptyDrivePath_UploadsToMyDriveRootWithoutFolderLookups()
    {
        var client = new Mock<IDriveClient>();
        client.Setup(c => c.UploadFileAsync(_tempFile, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveFile("new-id", Path.GetFileName(_tempFile), "text/plain", 5,
                DateTimeOffset.UtcNow, new[] { "root" }));

        var handler = new UploadCommandHandler(client.Object, _console);

        var exit = await handler.HandleAsync(_tempFile, "", CancellationToken.None);

        Assert.Equal(0, exit);
        client.Verify(c => c.UploadFileAsync(_tempFile, "root", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.CreateFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AllFolderSegmentsExist_WalksThemAndUploadsToLeaf()
    {
        var client = new Mock<IDriveClient>();

        // Path "Work/Reports" — both folders already exist
        client.Setup(c => c.FindFolderAsync("Work", "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-work", "Work"));
        client.Setup(c => c.FindFolderAsync("Reports", "folder-work", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-reports", "Reports"));
        client.Setup(c => c.UploadFileAsync(_tempFile, "folder-reports", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveFile("new-id", Path.GetFileName(_tempFile), "text/plain", 5,
                DateTimeOffset.UtcNow, new[] { "folder-reports" }));

        var handler = new UploadCommandHandler(client.Object, _console);

        var exit = await handler.HandleAsync(_tempFile, "Work/Reports", CancellationToken.None);

        Assert.Equal(0, exit);
        // Upload landed on the leaf folder, never created anything new.
        client.Verify(c => c.UploadFileAsync(_tempFile, "folder-reports", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.CreateFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MiddleFolderMissing_CreatesItAndContinues()
    {
        var client = new Mock<IDriveClient>();

        // Path "Work/NewProject" — "Work" exists, "NewProject" doesn't
        client.Setup(c => c.FindFolderAsync("Work", "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-work", "Work"));
        client.Setup(c => c.FindFolderAsync("NewProject", "folder-work", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DriveFile?)null);
        client.Setup(c => c.CreateFolderAsync("NewProject", "folder-work", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-new", "NewProject"));
        client.Setup(c => c.UploadFileAsync(_tempFile, "folder-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveFile("new-id", Path.GetFileName(_tempFile), "text/plain", 5,
                DateTimeOffset.UtcNow, new[] { "folder-new" }));

        var handler = new UploadCommandHandler(client.Object, _console);

        var exit = await handler.HandleAsync(_tempFile, "Work/NewProject", CancellationToken.None);

        Assert.Equal(0, exit);
        client.Verify(c => c.CreateFolderAsync("NewProject", "folder-work", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.UploadFileAsync(_tempFile, "folder-new", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("Creating folder", _console.Output);
        Assert.Contains("NewProject", _console.Output);
    }

    [Fact]
    public async Task HandleAsync_AllSegmentsMissing_CreatesEntireChain()
    {
        var client = new Mock<IDriveClient>();

        // Path "A/B/C" — nothing exists
        client.Setup(c => c.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DriveFile?)null);
        client.Setup(c => c.CreateFolderAsync("A", "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-a", "A"));
        client.Setup(c => c.CreateFolderAsync("B", "folder-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-b", "B"));
        client.Setup(c => c.CreateFolderAsync("C", "folder-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-c", "C"));
        client.Setup(c => c.UploadFileAsync(_tempFile, "folder-c", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveFile("new-id", Path.GetFileName(_tempFile), "text/plain", 5,
                DateTimeOffset.UtcNow, new[] { "folder-c" }));

        var handler = new UploadCommandHandler(client.Object, _console);

        var exit = await handler.HandleAsync(_tempFile, "A/B/C", CancellationToken.None);

        Assert.Equal(0, exit);
        client.Verify(c => c.CreateFolderAsync("A", "root", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.CreateFolderAsync("B", "folder-a", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.CreateFolderAsync("C", "folder-b", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.UploadFileAsync(_tempFile, "folder-c", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_BackslashSeparators_NormalizeToForwardSlashes()
    {
        // "Work\Reports" should be treated identically to "Work/Reports"
        var client = new Mock<IDriveClient>();
        client.Setup(c => c.FindFolderAsync("Work", "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-work", "Work"));
        client.Setup(c => c.FindFolderAsync("Reports", "folder-work", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder("folder-reports", "Reports"));
        client.Setup(c => c.UploadFileAsync(_tempFile, "folder-reports", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveFile("new-id", Path.GetFileName(_tempFile), "text/plain", 5,
                DateTimeOffset.UtcNow, new[] { "folder-reports" }));

        var handler = new UploadCommandHandler(client.Object, _console);

        var exit = await handler.HandleAsync(_tempFile, @"Work\Reports", CancellationToken.None);

        Assert.Equal(0, exit);
        client.Verify(c => c.UploadFileAsync(_tempFile, "folder-reports", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DriveFile MakeFolder(string id, string name) => new(
        id, name, "application/vnd.google-apps.folder", null,
        DateTimeOffset.UtcNow, Array.Empty<string>());
}
