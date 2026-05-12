using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;

namespace GoogleDriveCli.Services.Auth;

/// <summary>
/// Default <see cref="IGoogleAuthService"/> implementation that:
///   * loads OAuth client secrets from <c>client_secret.json</c> in the
///     current working directory, and
///   * persists access/refresh tokens to
///     <c>%APPDATA%\GoogleDriveCliManager\tokens\</c> via
///     <see cref="FileDataStore"/> (DPAPI-encrypted on Windows).
/// </summary>
public sealed class GoogleAuthService : IGoogleAuthService
{
    private const string ApplicationName = "GoogleDriveCliManager";
    private const string CredentialsFileName = "client_secret.json";

    // Read + write access — needed because `upload` writes files back to Drive.
    private static readonly string[] Scopes = [DriveService.Scope.Drive];

    public async Task<UserCredential> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var credentialsPath = Path.Combine(Directory.GetCurrentDirectory(), CredentialsFileName);

        if (!File.Exists(credentialsPath))
        {
            throw new FileNotFoundException(
                $"OAuth credentials not found at '{credentialsPath}'. " +
                "See the 'Setting up Google Cloud' section in README.md for the one-time setup.");
        }

        await using var stream = File.OpenRead(credentialsPath);
        var secrets = await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken);

        var tokenStorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ApplicationName,
            "tokens");

        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            Scopes,
            user: "user",
            cancellationToken,
            new FileDataStore(tokenStorePath, fullPath: true));
    }
}
