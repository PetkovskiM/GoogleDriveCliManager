using System.Runtime.CompilerServices;
using Google.Apis.Services;
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
    private const string FileFields = "nextPageToken, files(id, name, mimeType, size, modifiedTime, parents)";

    private readonly IGoogleAuthService _authService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private GoogleDrive.DriveService? _service;

    public DriveClient(IGoogleAuthService authService) => _authService = authService;

    public IAsyncEnumerable<DriveFile> ListAllAsync(CancellationToken cancellationToken)
        => EnumerateAsync("trashed = false", cancellationToken);

    public IAsyncEnumerable<DriveFile> ListFoldersAsync(CancellationToken cancellationToken)
        => EnumerateAsync("mimeType = 'application/vnd.google-apps.folder' and trashed = false", cancellationToken);

    public IAsyncEnumerable<DriveFile> SearchAsync(string query, CancellationToken cancellationToken)
    {
        // Drive's query language wraps strings in single quotes (`name contains 'foo'`).
        // Single quotes inside the value must be escaped with a backslash.
        var escaped = query.Replace("'", @"\'");
        return EnumerateAsync($"name contains '{escaped}' and trashed = false", cancellationToken);
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var request = service.Files.Get(fileId);
        await request.DownloadAsync(destination, cancellationToken);
    }

    /// <summary>
    /// Shared paginated enumerator. Both <see cref="ListAllAsync"/> and
    /// <see cref="SearchAsync"/> differ only in the <c>q</c> filter passed
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
            {
                yield return new DriveFile(
                    Id: f.Id,
                    Name: f.Name,
                    MimeType: f.MimeType,
                    Size: f.Size,
                    ModifiedTime: f.ModifiedTimeDateTimeOffset,
                    Parents: f.Parents?.ToArray() ?? Array.Empty<string>());
            }

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

    public void Dispose()
    {
        _service?.Dispose();
        _initLock.Dispose();
    }
}
