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

    private readonly IGoogleAuthService _authService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private GoogleDrive.DriveService? _service;

    public DriveClient(IGoogleAuthService authService) => _authService = authService;

    public async IAsyncEnumerable<DriveFile> ListAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        string? pageToken = null;

        do
        {
            var request = service.Files.List();
            request.PageSize = 1000;
            request.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime, parents)";
            request.PageToken = pageToken;
            request.Q = "trashed = false";

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

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var request = service.Files.Get(fileId);
        await request.DownloadAsync(destination, cancellationToken);
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
