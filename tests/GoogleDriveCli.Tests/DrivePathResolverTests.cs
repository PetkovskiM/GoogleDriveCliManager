using GoogleDriveCli.Models;
using GoogleDriveCli.Services.Local;

namespace GoogleDriveCli.Tests;

public class DrivePathResolverTests
{
    [Fact]
    public void Resolve_FileAtRoot_ReturnsJustTheFileName()
    {
        var file = MakeFile("f1", "notes.txt", parents: Array.Empty<string>());

        var path = DrivePathResolver.Resolve(file, new Dictionary<string, DriveFile>());

        Assert.Equal("notes.txt", path);
    }

    [Fact]
    public void Resolve_FileInNestedFolders_MirrorsTheFolderHierarchy()
    {
        var work = MakeFolder("folder-work", "Work", parents: Array.Empty<string>());
        var projA = MakeFolder("folder-projA", "proj-a", parents: new[] { "folder-work" });
        var file = MakeFile("f1", "notes.txt", parents: new[] { "folder-projA" });

        var folderMap = new Dictionary<string, DriveFile>
        {
            ["folder-work"] = work,
            ["folder-projA"] = projA,
        };

        var path = DrivePathResolver.Resolve(file, folderMap);

        Assert.Equal(Path.Combine("Work", "proj-a", "notes.txt"), path);
    }

    [Fact]
    public void Resolve_FileNameWithInvalidChars_ReplacesThemWithUnderscore()
    {
        var file = MakeFile("f1", "weird:name?.txt", parents: Array.Empty<string>());

        var path = DrivePathResolver.Resolve(file, new Dictionary<string, DriveFile>());

        Assert.Equal("weird_name_.txt", path);
    }

    [Fact]
    public void Resolve_CycleInParentChain_TerminatesGracefully()
    {
        // A → B → A would loop forever without the visited-set guard.
        var a = MakeFolder("folder-a", "A", parents: new[] { "folder-b" });
        var b = MakeFolder("folder-b", "B", parents: new[] { "folder-a" });
        var file = MakeFile("f1", "x.txt", parents: new[] { "folder-a" });

        var folderMap = new Dictionary<string, DriveFile>
        {
            ["folder-a"] = a,
            ["folder-b"] = b,
        };

        var path = DrivePathResolver.Resolve(file, folderMap);

        // The exact segments aren't the point — the point is that it returns.
        Assert.False(string.IsNullOrEmpty(path));
        Assert.EndsWith("x.txt", path);
    }

    private static DriveFile MakeFile(string id, string name, IReadOnlyList<string> parents) =>
        new(id, name, "text/plain", 0, DateTimeOffset.UtcNow, parents);

    private static DriveFile MakeFolder(string id, string name, IReadOnlyList<string> parents) =>
        new(id, name, "application/vnd.google-apps.folder", null, DateTimeOffset.UtcNow, parents);
}
