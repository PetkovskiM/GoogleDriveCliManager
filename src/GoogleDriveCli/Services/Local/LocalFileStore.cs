namespace GoogleDriveCli.Services.Local;

/// <summary>
/// Filesystem-backed <see cref="ILocalFileStore"/>. Downloads root defaults to
/// <c>{cwd}/Downloads</c>. Directories are created lazily on first write.
/// </summary>
public sealed class LocalFileStore : ILocalFileStore
{
    public string DownloadsRoot { get; }

    public LocalFileStore()
        : this(Path.Combine(Directory.GetCurrentDirectory(), "Downloads"))
    {
    }

    public LocalFileStore(string downloadsRoot)
    {
        DownloadsRoot = downloadsRoot;
    }

    public bool Exists(string relativePath) =>
        File.Exists(Path.Combine(DownloadsRoot, relativePath));

    public async Task WriteAsync(string relativePath, Func<Stream, Task> writer, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(DownloadsRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(fullPath);
        await writer(stream);
    }
}
