using GoogleDriveCli.Models;
using GoogleDriveCli.Services.Drive;
using GoogleDriveCli.Services.Local;
using GoogleDriveCli.Services.Manifest;
using Spectre.Console;

namespace GoogleDriveCli.Commands;

/// <summary>
/// Runs the Drive search and renders the results as a Spectre table with each
/// row tagged by sync status:
///   * <c>Downloaded</c>            — manifest entry exists and the local file is present
///   * <c>[Not Downloaded]</c>      — file is in Drive but not on disk locally
///   * <c>—</c> (folder / native)   — folders and Google-native files aren't downloaded in our model
///
/// To disambiguate same-named files in different folders, the handler first
/// pulls a folder-only listing from Drive and builds a parent-id → folder
/// lookup map, then resolves a Drive-relative "Location" for every result
/// via <see cref="DrivePathResolver"/>.
///
/// The "manifest entry + filesystem existence" check is the hybrid state-management
/// approach the task spec asks for: manifest is the primary source of truth, but we
/// verify the local file actually still exists so a user who manually deleted the
/// Downloads folder still sees the truth.
/// </summary>
public sealed class SearchCommandHandler
{
    private readonly IDriveClient _driveClient;
    private readonly ILocalFileStore _localStore;
    private readonly IManifestStore _manifest;
    private readonly IAnsiConsole _console;

    public SearchCommandHandler(
        IDriveClient driveClient,
        ILocalFileStore localStore,
        IManifestStore manifest,
        IAnsiConsole console)
    {
        _driveClient = driveClient;
        _localStore = localStore;
        _manifest = manifest;
        _console = console;
    }

    public async Task<int> HandleAsync(string query, CancellationToken cancellationToken)
    {
        await _manifest.LoadAsync(cancellationToken);

        // Folder map first — cheap targeted query, used to resolve each result's parent path.
        var folderMap = new Dictionary<string, DriveFile>();
        await foreach (var folder in _driveClient.ListFoldersAsync(cancellationToken))
            folderMap[folder.Id] = folder;

        var results = new List<DriveFile>();
        await foreach (var file in _driveClient.SearchAsync(query, cancellationToken))
            results.Add(file);

        if (results.Count == 0)
        {
            _console.MarkupLine($"[yellow]No files matched [bold]\"{Markup.Escape(query)}\"[/].[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("Type")
            .AddColumn("Size")
            .AddColumn("Location")
            .AddColumn("Status");

        // Folders first (they shape the structure), then files alphabetically.
        var ordered = results
            .OrderBy(f => f.IsFolder ? 0 : 1)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var file in ordered)
        {
            table.AddRow(
                Markup.Escape(file.Name),
                FormatType(file),
                FormatSize(file.Size),
                FormatLocation(file, folderMap),
                FormatStatus(file));
        }

        _console.MarkupLine(
            $"Found [green]{results.Count}[/] result(s) for [bold]\"{Markup.Escape(query)}\"[/]:");
        _console.Write(table);
        return 0;
    }

    private string FormatStatus(DriveFile file)
    {
        if (file.IsFolder || file.IsGoogleNative) return "[grey]—[/]";

        var entry = _manifest.TryGet(file.Id);
        var downloaded = entry is not null && _localStore.Exists(entry.LocalRelativePath);
        return downloaded ? "[green]Downloaded[/]" : "[yellow][[Not Downloaded]][/]";
    }

    private static string FormatLocation(DriveFile file, IReadOnlyDictionary<string, DriveFile> folderMap)
    {
        // DrivePathResolver returns the full mirrored path (segments joined with the
        // platform separator). For Location we want just the parent — everything
        // except the file's own name — displayed with forward slashes to match
        // Drive's UI convention.
        var fullPath = DrivePathResolver.Resolve(file, folderMap);
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent)) return "[grey]My Drive[/]";
        return Markup.Escape(parent.Replace(Path.DirectorySeparatorChar, '/'));
    }

    private static string FormatType(DriveFile file)
    {
        if (file.IsFolder) return "Folder";
        if (file.IsGoogleNative)
            return file.MimeType!.Replace("application/vnd.google-apps.", "", StringComparison.Ordinal);
        var ext = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? "file" : ext;
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes is null) return "—";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
