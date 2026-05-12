using GoogleDriveCli.Models;

namespace GoogleDriveCli.Services.Local;

/// <summary>
/// Resolves a Drive file's position in the user's folder tree to a local relative path.
/// Walks up the <c>Parents</c> chain through a pre-built folder lookup map, sanitizes
/// each path segment for filesystem-safe characters, and joins them with the platform
/// path separator. Pure function — easy to test.
/// </summary>
public static class DrivePathResolver
{
    // Hardcoded "invalid" set rather than Path.GetInvalidFileNameChars() so behaviour
    // is consistent across Windows, macOS and Linux (Path.GetInvalidFileNameChars()
    // returns different sets per platform). This is the union: the Windows-restrictive
    // set plus all C0 control characters.
    private static readonly HashSet<char> InvalidChars = BuildInvalidCharSet();

    public static string Resolve(DriveFile file, IReadOnlyDictionary<string, DriveFile> folderMap)
    {
        var segments = new Stack<string>();
        segments.Push(Sanitize(file.Name));

        var visited = new HashSet<string>();
        var parentId = file.Parents.Count > 0 ? file.Parents[0] : null;

        while (parentId is not null
            && folderMap.TryGetValue(parentId, out var folder)
            && visited.Add(parentId))
        {
            segments.Push(Sanitize(folder.Name));
            parentId = folder.Parents.Count > 0 ? folder.Parents[0] : null;
        }

        return Path.Combine(segments.ToArray());
    }

    private static string Sanitize(string name)
    {
        var chars = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
            chars[i] = InvalidChars.Contains(name[i]) ? '_' : name[i];
        return new string(chars);
    }

    private static HashSet<char> BuildInvalidCharSet()
    {
        var set = new HashSet<char> { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        for (var c = 0; c < 32; c++) set.Add((char)c);
        return set;
    }
}
