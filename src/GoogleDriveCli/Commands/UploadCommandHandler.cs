using GoogleDriveCli.Services.Drive;
using Spectre.Console;

namespace GoogleDriveCli.Commands;

/// <summary>
/// Implements <c>gdrive upload &lt;local&gt; &lt;drive-path&gt;</c>.
/// <para>
/// Walks the destination Drive path segment by segment using
/// <see cref="IDriveClient.FindFolderAsync"/>. Missing segments are created via
/// <see cref="IDriveClient.CreateFolderAsync"/>, which is the spec's "create the
/// path" branch of the graceful-handling requirement. Once the leaf folder
/// exists the file is streamed via <see cref="IDriveClient.UploadFileAsync"/>.
/// </para>
/// </summary>
public sealed class UploadCommandHandler
{
    private const string DriveRootId = "root";

    private readonly IDriveClient _driveClient;
    private readonly IAnsiConsole _console;

    public UploadCommandHandler(IDriveClient driveClient, IAnsiConsole console)
    {
        _driveClient = driveClient;
        _console = console;
    }

    public async Task<int> HandleAsync(string localPath, string drivePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(localPath))
        {
            _console.MarkupLine($"[red]File not found: {Markup.Escape(localPath)}[/]");
            return 1;
        }

        var segments = ParseSegments(drivePath);
        var parentId = DriveRootId;

        // Walk segments, creating any that don't exist.
        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await _driveClient.FindFolderAsync(segment, parentId, cancellationToken);
            if (existing is not null)
            {
                parentId = existing.Id;
                continue;
            }

            _console.MarkupLine($"[yellow]Creating folder:[/] {Markup.Escape(segment)}");
            var created = await _driveClient.CreateFolderAsync(segment, parentId, cancellationToken);
            parentId = created.Id;
        }

        var localName = Path.GetFileName(localPath);
        var displayDest = segments.Length == 0 ? "My Drive" : string.Join("/", segments);
        _console.MarkupLine($"Uploading [cyan]{Markup.Escape(localName)}[/] to [cyan]{Markup.Escape(displayDest)}[/]...");

        try
        {
            var uploaded = await _driveClient.UploadFileAsync(localPath, parentId, cancellationToken);
            _console.MarkupLine(
                $"[green]Uploaded[/] [bold]{Markup.Escape(uploaded.Name)}[/] (id: {Markup.Escape(uploaded.Id)}).");
            return 0;
        }
        catch (OperationCanceledException)
        {
            _console.MarkupLine("[red]Upload cancelled.[/]");
            return 130;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Upload failed: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    /// <summary>
    /// Normalizes a Drive path: backslashes become forward slashes, leading and
    /// trailing slashes are stripped, empty segments are ignored. An empty
    /// or "/" path returns an empty array — meaning "upload to My Drive root".
    /// </summary>
    private static string[] ParseSegments(string drivePath)
    {
        if (string.IsNullOrWhiteSpace(drivePath)) return Array.Empty<string>();
        return drivePath
            .Replace('\\', '/')
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
