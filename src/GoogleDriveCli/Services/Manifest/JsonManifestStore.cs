using System.Collections.Concurrent;
using System.Text.Json;
using GoogleDriveCli.Models;

namespace GoogleDriveCli.Services.Manifest;

/// <summary>
/// JSON-backed <see cref="IManifestStore"/>. Defaults to a per-user location:
/// <c>%APPDATA%\GoogleDriveCliManager\manifest.json</c> on Windows, and the
/// platform equivalent (<c>~/.config/GoogleDriveCliManager/manifest.json</c>)
/// on macOS / Linux.
/// <para>
/// In-memory entries live in a <see cref="ConcurrentDictionary{TKey,TValue}"/>,
/// so the parallel sync command can <c>AddOrUpdate</c> from many workers
/// simultaneously without external synchronization. Load and Save are not
/// thread-safe — call them from a single thread at the start and end of sync.
/// </para>
/// </summary>
public sealed class JsonManifestStore : IManifestStore
{
    private const string ApplicationName = "GoogleDriveCliManager";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private ConcurrentDictionary<string, ManifestEntry> _entries = new();

    public JsonManifestStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ApplicationName,
            "manifest.json"))
    {
    }

    public JsonManifestStore(string path)
    {
        _path = path;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            _entries = new ConcurrentDictionary<string, ManifestEntry>();
            return;
        }

        await using var stream = File.OpenRead(_path);
        var list = await JsonSerializer.DeserializeAsync<List<ManifestEntry>>(
            stream, cancellationToken: cancellationToken);

        _entries = new ConcurrentDictionary<string, ManifestEntry>(
            (list ?? new()).Select(e => new KeyValuePair<string, ManifestEntry>(e.FileId, e)));
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(
            stream, _entries.Values.ToArray(), JsonOptions, cancellationToken);
    }

    public ManifestEntry? TryGet(string fileId) =>
        _entries.TryGetValue(fileId, out var entry) ? entry : null;

    public void AddOrUpdate(ManifestEntry entry) => _entries[entry.FileId] = entry;

    public IReadOnlyCollection<ManifestEntry> AllEntries => _entries.Values.ToArray();
}
