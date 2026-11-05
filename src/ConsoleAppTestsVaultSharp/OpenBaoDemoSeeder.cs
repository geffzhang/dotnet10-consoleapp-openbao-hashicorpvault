namespace ConsoleAppTestsVaultSharp;

internal static class OpenBaoDemoSeeder
{
    public static async Task SeedAsync(Func<string, IDictionary<string, object>, Task> writeSecret)
    {
        ArgumentNullException.ThrowIfNull(writeSecret);

        await writeSecret(
            "app/config",
            new Dictionary<string, object>
            {
                ["username"] = "dev-user",
                ["password"] = "dev-only-password"
            });
        await writeSecret(
            "integracoes/api",
            new Dictionary<string, object>
            {
                ["api_key"] = "dev-only-api-key",
                ["endpoint"] = "https://api.example.test"
            });
    }
}
