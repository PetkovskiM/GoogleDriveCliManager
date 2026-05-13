using GoogleDriveCli.Services.Auth;

namespace GoogleDriveCli.Tests;

public class GoogleAuthServiceTests
{
    [Fact]
    public async Task AuthorizeAsync_WhenClientSecretFileMissing_ThrowsHelpfulFileNotFoundException()
    {
        // ARRANGE — point at a non-existent directory whose missing file is named
        // 'client_secret.json' (the same name the production constructor uses,
        // so the rendered error message looks identical to what a real user would see).
        var fakeDir = Path.Combine(Path.GetTempPath(), $"gauth-test-{Guid.NewGuid():N}");
        var missingSecret = Path.Combine(fakeDir, "client_secret.json");
        var tokenDir = Path.Combine(fakeDir, "tokens");
        var service = new GoogleAuthService(missingSecret, tokenDir);

        // ACT + ASSERT
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.AuthorizeAsync(CancellationToken.None));

        // The error must point the user (and the reviewer) at both the
        // missing file and the README section that explains the setup.
        Assert.Contains("client_secret.json", ex.Message);
        Assert.Contains("README", ex.Message);
    }
}
