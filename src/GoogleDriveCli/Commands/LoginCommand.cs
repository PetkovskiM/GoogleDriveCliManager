using System.CommandLine;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using GoogleDriveCli.Services.Auth;

namespace GoogleDriveCli.Commands;

/// <summary>
/// <c>gdrive login</c> — runs the OAuth flow and verifies the credential
/// works by reading the authenticated user's profile from the Drive
/// <c>About</c> endpoint.
/// </summary>
public sealed class LoginCommand : Command
{
    private const string ApplicationName = "GoogleDriveCliManager";

    public LoginCommand(IGoogleAuthService authService)
        : base("login", "Authenticate with Google Drive and persist the OAuth token locally.")
    {
        SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var credential = await authService.AuthorizeAsync(cancellationToken);

                using var driveService = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });

                var about = driveService.About.Get();
                about.Fields = "user";
                var result = await about.ExecuteAsync(cancellationToken);

                Console.WriteLine($"Authenticated as {result.User.EmailAddress}.");
                return 0;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Authentication cancelled.");
                return 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Authentication failed: {ex.Message}");
                return 1;
            }
        });
    }
}
