using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;

namespace GoogleDriveCli.Services.Auth;

/// <summary>
/// Default <see cref="IGoogleAuthService"/> implementation that:
///   * loads OAuth client secrets from <c>client_secret.json</c> in the current
///     working directory (or, as a fallback for the installed binary scenario,
///     from the same directory as the executable), and
///   * persists access/refresh tokens to
///     <c>%APPDATA%\GoogleDriveCliManager\tokens\</c> via
///     <see cref="FileDataStore"/> (DPAPI-encrypted on Windows).
/// <para>
/// The paths are injectable via the parameterized constructor so tests can
/// point at temp locations; the parameterless constructor wires up the
/// production defaults and is what DI resolves.
/// </para>
/// </summary>
public sealed class GoogleAuthService : IGoogleAuthService
{
    private const string ApplicationName = "GoogleDriveCliManager";
    private const string CredentialsFileName = "client_secret.json";

    // Read + write access — needed because `upload` writes files back to Drive.
    private static readonly string[] Scopes = [DriveService.Scope.Drive];

    private readonly string _credentialsPath;
    private readonly string _tokenStorePath;

    /// <summary>Production defaults: CWD-then-binary-dir for the secrets file, %APPDATA% for the token store.</summary>
    public GoogleAuthService()
        : this(
            credentialsPath: ResolveDefaultCredentialsPath(),
            tokenStorePath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ApplicationName,
                "tokens"))
    {
    }

    public GoogleAuthService(string credentialsPath, string tokenStorePath)
    {
        _credentialsPath = credentialsPath;
        _tokenStorePath = tokenStorePath;
    }

    public async Task<UserCredential> AuthorizeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_credentialsPath))
        {
            throw new FileNotFoundException(
                $"OAuth credentials not found at '{_credentialsPath}'. " +
                "See the 'Setting up Google Cloud' section in README.md for the one-time setup.");
        }

        await using var stream = File.OpenRead(_credentialsPath);
        var secrets = await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken);

        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            Scopes,
            user: "user",
            cancellationToken,
            new FileDataStore(_tokenStorePath, fullPath: true));
    }

    /// <summary>
    /// Resolution order for <c>client_secret.json</c>:
    ///   1. Current working directory — natural for <c>dotnet run</c> from the repo root.
    ///   2. The directory containing the executable — natural for an installed <c>gdrive.exe</c>
    ///      sitting on the PATH, where the user wouldn't otherwise know to keep their CWD pinned.
    /// If neither exists we still return path #1 so the error message points the user at the
    /// most likely place to drop the file.
    /// </summary>
    private static string ResolveDefaultCredentialsPath()
    {
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), CredentialsFileName);
        if (File.Exists(cwdPath)) return cwdPath;

        var binDirPath = Path.Combine(AppContext.BaseDirectory, CredentialsFileName);
        if (File.Exists(binDirPath)) return binDirPath;

        return cwdPath;
    }
}
