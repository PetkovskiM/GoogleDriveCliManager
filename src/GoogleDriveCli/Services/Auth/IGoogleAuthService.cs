using Google.Apis.Auth.OAuth2;

namespace GoogleDriveCli.Services.Auth;

/// <summary>
/// Performs the OAuth 2.0 installed-application flow against Google Drive
/// and returns a credential that can be used to construct an authenticated
/// <see cref="Google.Apis.Drive.v3.DriveService"/>.
/// </summary>
public interface IGoogleAuthService
{
    /// <summary>
    /// Authorizes the user and returns a <see cref="UserCredential"/>.
    /// On first invocation this opens the system browser for user consent.
    /// Subsequent invocations reuse the persisted token, refreshing it
    /// silently if it has expired.
    /// </summary>
    Task<UserCredential> AuthorizeAsync(CancellationToken cancellationToken);
}
