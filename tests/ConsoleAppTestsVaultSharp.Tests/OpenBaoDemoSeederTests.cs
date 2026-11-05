namespace ConsoleAppTestsVaultSharp.Tests;

public sealed class OpenBaoDemoSeederTests
{
    [Fact]
    public async Task SeedAsync_WritesBothExpectedSecretPaths()
    {
        var writes = new List<(string Path, IDictionary<string, object> Data)>();

        await OpenBaoDemoSeeder.SeedAsync((path, data) =>
        {
            writes.Add((path, data));
            return Task.CompletedTask;
        });

        Assert.Collection(
            writes,
            write =>
            {
                Assert.Equal("app/config", write.Path);
                Assert.Equal(["password", "username"], write.Data.Keys.Order());
            },
            write =>
            {
                Assert.Equal("integracoes/api", write.Path);
                Assert.Equal(["api_key", "endpoint"], write.Data.Keys.Order());
            });
    }

    [Fact]
    public async Task SeedAsync_WhenWriteFails_ThrowsAndDoesNotReportSuccess()
    {
        var paths = new List<string>();
        var failure = new InvalidOperationException("synthetic write failure");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OpenBaoDemoSeeder.SeedAsync((path, _) =>
            {
                paths.Add(path);
                return paths.Count == 2 ? Task.FromException(failure) : Task.CompletedTask;
            }));

        Assert.Same(failure, exception);
        Assert.Equal(["app/config", "integracoes/api"], paths);
    }
}
