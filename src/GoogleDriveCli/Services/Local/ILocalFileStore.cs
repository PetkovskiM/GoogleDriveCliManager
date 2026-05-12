namespace GoogleDriveCli.Services.Local;

/// <summary>
/// Reads and writes files under a local Downloads root. Intentionally minimal —
/// callers compute relative paths via <see cref="DrivePathResolver"/>.
/// </summary>
public interface ILocalFileStore
{
    string DownloadsRoot { get; }

    bool Exists(string relativePath);

    /// <summary>
    /// Creates the parent directory (and any ancestors) and invokes <paramref name="writer"/>
    /// with an open write stream. The stream is disposed when the writer returns.
    /// </summary>
    Task WriteAsync(string relativePath, Func<Stream, Task> writer, CancellationToken cancellationToken);
}
