using GoogleDriveCli.Models;
using GoogleDriveCli.Services.Drive;
using GoogleDriveCli.Services.Local;
using GoogleDriveCli.Services.Manifest;
using Spectre.Console;

namespace GoogleDriveCli.Commands;

/// <summary>
/// Orchestrates the parallel download flow for <c>gdrive sync</c>.
/// <para>
/// Flow:
///   1. Load the existing manifest (so we can skip up-to-date files).
///   2. List every file + folder from Drive in one pass; partition into
///      folders (used to resolve local paths) and downloadables.
///   3. Run <see cref="Parallel.ForEachAsync"/> across the downloadables
///      with a bounded degree of parallelism. Each worker:
///         * resolves the local path via <see cref="DrivePathResolver"/>,
///         * skips when the manifest entry's <c>DriveModifiedTime</c> still
///           matches the Drive file's <c>modifiedTime</c> and the local
///           file is present,
///         * otherwise streams the binary content to disk and updates the
///           manifest + statistics atomically.
///   4. Save the manifest, render a summary table.
/// </para>
/// <para>
/// Cancellation: every async hop accepts a <see cref="CancellationToken"/>.
/// On Ctrl+C the parallel loop unwinds, the <c>finally</c> still runs the
/// manifest save, and the next sync resumes from the partial state.
/// </para>
/// </summary>
public sealed class SyncCommandHandler
{
    // I/O-bound work, not CPU-bound — so this is not Environment.ProcessorCount.
    // 8 was chosen to stay well under Google's 1000 requests / 100 seconds quota
    // while keeping the pipeline saturated for typical home-connection bandwidth.
    private const int MaxDegreeOfParallelism = 8;

    private readonly IDriveClient _driveClient;
    private readonly ILocalFileStore _localStore;
    private readonly IManifestStore _manifest;

    public SyncCommandHandler(IDriveClient driveClient, ILocalFileStore localStore, IManifestStore manifest)
    {
        _driveClient = driveClient;
        _localStore = localStore;
        _manifest = manifest;
    }

    public async Task<int> HandleAsync(bool dryRun, CancellationToken cancellationToken)
    {
        await _manifest.LoadAsync(cancellationToken);

        AnsiConsole.MarkupLine("[bold]Listing files from Google Drive...[/]");
        var allFiles = await CollectAsync(_driveClient.ListAllAsync(cancellationToken));

        var folderMap = allFiles
            .Where(f => f.IsFolder)
            .ToDictionary(f => f.Id, f => f);
        var downloadable = allFiles.Where(f => !f.IsFolder && !f.IsGoogleNative).ToList();
        var nativeSkipped = allFiles.Count(f => f.IsGoogleNative);

        AnsiConsole.MarkupLine(
            $"Found [green]{downloadable.Count}[/] downloadable file(s); " +
            $"skipping [yellow]{nativeSkipped}[/] Google native file(s).");

        if (downloadable.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Nothing to download.[/]");
            return 0;
        }

        if (dryRun)
            AnsiConsole.MarkupLine("[bold yellow]Dry run — no files will be written.[/]");
        else
            AnsiConsole.MarkupLine($"Downloads root: [cyan]{Markup.Escape(_localStore.DownloadsRoot)}[/]");

        var stats = new SyncStatistics();
        stats.Start();

        try
        {
            await AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Syncing[/]", maxValue: downloadable.Count);

                    await Parallel.ForEachAsync(
                        downloadable,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                            CancellationToken = cancellationToken,
                        },
                        async (file, ct) =>
                        {
                            try
                            {
                                await ProcessFileAsync(file, folderMap, dryRun, stats, ct);
                            }
                            catch (OperationCanceledException)
                            {
                                // Let cancellation propagate so the parallel loop can unwind.
                                throw;
                            }
                            catch (Exception ex)
                            {
                                // Anything else: record and continue. One failed file does
                                // not abort the sync; the user sees it in the summary.
                                stats.RecordFailure(file.Id, file.Name, ex.Message);
                            }
                            finally
                            {
                                task.Increment(1);
                            }
                        });
                });
        }
        finally
        {
            stats.Stop();
            // Save manifest even on cancellation so a re-run resumes correctly.
            if (!dryRun) await _manifest.SaveAsync(CancellationToken.None);
        }

        PrintSummary(stats, dryRun);
        return stats.Failure > 0 ? 1 : 0;
    }

    private async Task ProcessFileAsync(
        DriveFile file,
        IReadOnlyDictionary<string, DriveFile> folderMap,
        bool dryRun,
        SyncStatistics stats,
        CancellationToken cancellationToken)
    {
        var localPath = DrivePathResolver.Resolve(file, folderMap);

        // Incremental: skip if we've already downloaded this exact version.
        var existing = _manifest.TryGet(file.Id);
        if (existing is not null
            && _localStore.Exists(localPath)
            && existing.DriveModifiedTime == file.ModifiedTime)
        {
            stats.RecordSkipped();
            return;
        }

        if (dryRun)
        {
            stats.RecordSuccess(file.Size ?? 0);
            return;
        }

        await _localStore.WriteAsync(localPath,
            async stream => await _driveClient.DownloadAsync(file.Id, stream, cancellationToken),
            cancellationToken);

        _manifest.AddOrUpdate(new ManifestEntry(
            FileId: file.Id,
            LocalRelativePath: localPath,
            DriveModifiedTime: file.ModifiedTime ?? DateTimeOffset.UtcNow,
            SizeBytes: file.Size ?? 0,
            DownloadedAt: DateTimeOffset.UtcNow));

        stats.RecordSuccess(file.Size ?? 0);
    }

    private static void PrintSummary(SyncStatistics stats, bool dryRun)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow(dryRun ? "Would download" : "Downloaded", stats.Success.ToString("N0"));
        table.AddRow("Skipped (up-to-date)", stats.Skipped.ToString("N0"));
        table.AddRow("Failed", stats.Failure.ToString("N0"));
        table.AddRow("Total bytes", FormatBytes(stats.TotalBytes));
        table.AddRow("Elapsed", stats.Elapsed.ToString(@"hh\:mm\:ss\.fff"));

        AnsiConsole.Write(table);

        if (stats.Failure > 0)
        {
            AnsiConsole.MarkupLine($"[red]{stats.Failure} file(s) failed:[/]");
            foreach (var failure in stats.Failures.Take(10))
            {
                AnsiConsole.MarkupLine(
                    $"  [red]x[/] {Markup.Escape(failure.FileName)}: {Markup.Escape(failure.Reason)}");
            }
            if (stats.Failure > 10)
                AnsiConsole.MarkupLine($"  [grey]... and {stats.Failure - 10} more[/]");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
