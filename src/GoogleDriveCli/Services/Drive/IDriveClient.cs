using GoogleDriveCli.Models;

namespace GoogleDriveCli.Services.Drive;

/// <summary>
/// Thin abstraction over <see cref="Google.Apis.Drive.v3.DriveService"/>.
/// Exists so consumers (commands, tests) depend on a small interface
/// rather than the entire Google SDK surface.
/// </summary>
public interface IDriveClient
{
    /// <summary>
    /// Enumerates every non-trashed file and folder in the user's Drive,
    /// streaming results page-by-page as the Drive API returns them.
    /// </summary>
    IAsyncEnumerable<DriveFile> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates folders only. Cheap targeted query used by <c>search</c>
    /// to build a parent-path lookup map without pulling every file in the Drive.
    /// </summary>
    IAsyncEnumerable<DriveFile> ListFoldersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Streams files and folders whose name contains <paramref name="query"/>
    /// (case-insensitive). The query is passed to Drive itself (per the spec),
    /// not filtered locally, so it finds anything available in the cloud.
    /// </summary>
    IAsyncEnumerable<DriveFile> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>
    /// Streams the binary content of the given Drive file to the destination stream.
    /// Caller owns the stream and is responsible for closing it.
    /// </summary>
    Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken);
}
