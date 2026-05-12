using System.CommandLine;

namespace GoogleDriveCli.Commands;

/// <summary>
/// <c>gdrive upload &lt;local-path&gt; &lt;drive-path&gt;</c> — uploads a local file to a
/// Drive folder, creating the folder chain if any segment is missing.
/// </summary>
public sealed class UploadCommand : Command
{
    public UploadCommand(UploadCommandHandler handler)
        : base("upload", "Upload a local file to a folder in Google Drive (creates the folder path if missing).")
    {
        var localArg = new Argument<string>("local-path")
        {
            Description = "Path to the local file to upload."
        };
        var driveArg = new Argument<string>("drive-path")
        {
            Description = "Destination folder path in Drive (e.g. \"Work/Reports\"). Use \"\" or \"/\" for My Drive root."
        };

        Arguments.Add(localArg);
        Arguments.Add(driveArg);

        SetAction(async (parseResult, cancellationToken) =>
        {
            var local = parseResult.GetValue(localArg);
            var drive = parseResult.GetValue(driveArg);

            if (string.IsNullOrWhiteSpace(local))
            {
                Console.Error.WriteLine("local-path is required.");
                return 1;
            }

            return await handler.HandleAsync(local, drive ?? string.Empty, cancellationToken);
        });
    }
}
