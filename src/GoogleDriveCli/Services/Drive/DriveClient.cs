using System.Runtime.CompilerServices;
using Google.Apis.Services;
using Google.Apis.Upload;
using GoogleDriveCli.Models;
using GoogleDriveCli.Services.Auth;
using GoogleDrive = Google.Apis.Drive.v3;

namespace GoogleDriveCli.Services.Drive;

/// <summary>
/// Default <see cref="IDriveClient"/>. Lazily authenticates on first use, then
/// caches the underlying <see cref="GoogleDrive.DriveService"/> for the lifetime
/// of the process. The Google SDK has built-in exponential backoff on 5xx and
/// transient HTTP errors, so we don't layer Polly on top.
/// </summary>
public sealed class DriveClient : IDriveClient, IDisposable
{
    private const string ApplicationName = "GoogleDriveCliManager";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string FileFields = "nextPageToken, files(id, name, mimeType, size, modifiedTime, parents)";
    private const string SingleFileFields = "id, name, mimeType, size, modifiedTime, parents";

    private readonly IGoogleAuthService _authService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private GoogleDrive.DriveService? _service;

    public DriveClient(IGoogleAuthService authService) => _authService = authService;

    public IAsyncEnumerable<DriveFile> ListAllAsync(CancellationToken cancellationToken)
        => EnumerateAsync("trashed = false", cancellationToken);

    public IAsyncEnumerable<DriveFile> ListFoldersAsync(CancellationToken cancellationToken)
        => EnumerateAsync($"mimeType = '{FolderMimeType}' and trashed = false", cancellationToken);

    public IAsyncEnumerable<DriveFile> SearchAsync(string query, CancellationToken cancellationToken)
    {
        // Drive's query language wraps strings in single quotes (`name contains 'foo'`).
        // Single quotes inside the value must be escaped with a backslash.
        var escaped = EscapeQuotes(query);
        return EnumerateAsync($"name contains '{escaped}' and trashed = false", cancellationToken);
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var request = service.Files.Get(fileId);
        await request.DownloadAsync(destination, cancellationToken);
    }

    public async Task<DriveFile?> FindFolderAsync(string name, string parentId, CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var escapedName = EscapeQuotes(name);
        var escapedParent = EscapeQuotes(parentId);

        var request = service.Files.List();
        request.Fields = $"files({SingleFileFields})";
        request.PageSize = 10;
        request.Q = $"name = '{escapedName}' and mimeType = '{FolderMimeType}' and '{escapedParent}' in parents and trashed = false";

        var response = await request.ExecuteAsync(cancellationToken);
        var first = response.Files.FirstOrDefault();
        return first is null ? null : Map(first);
    }

    public async Task<DriveFile> CreateFolderAsync(string name, string parentId, CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var metadata = new GoogleDrive.Data.File
        {
            Name = name,
            MimeType = FolderMimeType,
            Parents = new[] { parentId },
        };

        var request = service.Files.Create(metadata);
        request.Fields = SingleFileFields;
        var created = await request.ExecuteAsync(cancellationToken);
        return Map(created);
    }

    public async Task<DriveFile> UploadFileAsync(string localPath, string parentId, CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var metadata = new GoogleDrive.Data.File
        {
            Name = Path.GetFileName(localPath),
            Parents = new[] { parentId },
        };

        // application/octet-stream is the safe generic choice; Drive infers the
        // real type from the file extension for display purposes anyway.
        await using var stream = File.OpenRead(localPath);
        var upload = service.Files.Create(metadata, stream, "application/octet-stream");
        upload.Fields = SingleFileFields;

        var progress = await upload.UploadAsync(cancellationToken);
        if (progress.Status == UploadStatus.Failed)
        {
            throw new InvalidOperationException(
                progress.Exception?.Message ?? "Upload failed for an unknown reason.");
        }

        return Map(upload.ResponseBody);
    }

    /// <summary>
    /// Shared paginated enumerator. <see cref="ListAllAsync"/>, <see cref="ListFoldersAsync"/>
    /// and <see cref="SearchAsync"/> all differ only in the <c>q</c> filter passed
    /// to Drive — the paging, field selection, and DTO mapping are identical.
    /// </summary>
    private async IAsyncEnumerable<DriveFile> EnumerateAsync(
        string driveQuery,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        string? pageToken = null;

        do
        {
            var request = service.Files.List();
            request.PageSize = 1000;
            request.Fields = FileFields;
            request.PageToken = pageToken;
            request.Q = driveQuery;

            var response = await request.ExecuteAsync(cancellationToken);
            foreach (var f in response.Files)
                yield return Map(f);

            pageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));
    }

    /// <summary>
    /// Double-checked locking around DriveService construction so concurrent
    /// callers don't both build a service. The fast path (service already
    /// initialized) is a single non-locked field read.
    /// </summary>
    private async Task<GoogleDrive.DriveService> GetServiceAsync(CancellationToken cancellationToken)
    {
        if (_service is not null) return _service;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_service is not null) return _service;

            var credential = await _authService.AuthorizeAsync(cancellationToken);
            _service = new GoogleDrive.DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
            return _service;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static DriveFile Map(GoogleDrive.Data.File f) => new(
        Id: f.Id,
        Name: f.Name,
        MimeType: f.MimeType,
        Size: f.Size,
        ModifiedTime: f.ModifiedTimeDateTimeOffset,
        Parents: f.Parents?.ToArray() ?? Array.Empty<string>());

    private static string EscapeQuotes(string value) => value.Replace("'", @"\'");

    public void Dispose()
    {
        _service?.Dispose();
        _initLock.Dispose();
    }
}
