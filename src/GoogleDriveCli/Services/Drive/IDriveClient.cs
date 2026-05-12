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
    /// Streams the binary content of the given Drive file to the destination stream.
    /// Caller owns the stream and is responsible for closing it.
    /// </summary>
    Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken);
}
