namespace GoogleDriveCli.Models;

/// <summary>
/// One row in the local sync manifest. Used to determine whether a Drive file
/// has already been downloaded and whether the local copy is still current.
/// </summary>
public sealed record ManifestEntry(
    string FileId,
    string LocalRelativePath,
    DateTimeOffset DriveModifiedTime,
    long SizeBytes,
    DateTimeOffset DownloadedAt);
