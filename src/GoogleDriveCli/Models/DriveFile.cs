namespace GoogleDriveCli.Models;

/// <summary>
/// Slim domain DTO over <see cref="Google.Apis.Drive.v3.Data.File"/>.
/// Carries only the fields we actually need so the rest of the codebase
/// is decoupled from Google's data model.
/// </summary>
public sealed record DriveFile(
    string Id,
    string Name,
    string? MimeType,
    long? Size,
    DateTimeOffset? ModifiedTime,
    IReadOnlyList<string> Parents)
{
    /// <summary>True for folders. Folders are not downloaded; they shape the local path.</summary>
    public bool IsFolder => MimeType == "application/vnd.google-apps.folder";

    /// <summary>
    /// True for Google-native types (Docs, Sheets, Slides, Forms, ...).
    /// These have no binary content and would require <c>Files.Export</c> to convert
    /// to Office formats. We skip them — documented in the README.
    /// </summary>
    public bool IsGoogleNative => !IsFolder
        && MimeType is not null
        && MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal);
}
