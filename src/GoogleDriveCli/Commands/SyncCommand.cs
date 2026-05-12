using System.CommandLine;

namespace GoogleDriveCli.Commands;

/// <summary>
/// <c>gdrive sync</c> — downloads every file from the user's Drive in parallel,
/// mirroring Drive's folder structure into a local <c>Downloads/</c> directory.
/// The actual work lives in <see cref="SyncCommandHandler"/>; this class only
/// declares the command + options and dispatches.
/// </summary>
public sealed class SyncCommand : Command
{
    public SyncCommand(SyncCommandHandler handler)
        : base("sync", "Download all Drive files to a local Downloads directory (parallel, resumable).")
    {
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "List what would be downloaded without writing any files."
        };
        Options.Add(dryRunOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            var dryRun = parseResult.GetValue(dryRunOption);
            return await handler.HandleAsync(dryRun, cancellationToken);
        });
    }
}
