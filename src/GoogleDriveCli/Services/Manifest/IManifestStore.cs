using GoogleDriveCli.Models;

namespace GoogleDriveCli.Services.Manifest;

/// <summary>
/// Persists per-file download metadata so subsequent syncs can skip files
/// whose Drive <c>modifiedTime</c> hasn't changed since they were downloaded.
/// All in-memory mutation methods are thread-safe (intended for parallel sync).
/// </summary>
public interface IManifestStore
{
    Task LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);

    ManifestEntry? TryGet(string fileId);
    void AddOrUpdate(ManifestEntry entry);

    IReadOnlyCollection<ManifestEntry> AllEntries { get; }
}
