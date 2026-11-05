var builder = DistributedApplication.CreateBuilder(args);

OpenBaoAppHost.Configure(builder);

builder.Build().Run();

internal static class OpenBaoAppHost
{
    internal static void Configure(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var mode = ParseMode(builder.Configuration["OpenBao:Mode"]);
        var token = mode == OpenBaoMode.Development
            ? builder.AddParameter("openbao-token", secret: true)
            : builder.AddParameter("openbao-token", string.Empty, secret: true);
        var openBao = builder.AddOpenBao("openbao", mode, token.Resource);
        var console = builder.AddProject<Projects.ConsoleAppTestsVaultSharp>("console")
            .WithReference(openBao);

        if (mode == OpenBaoMode.Persistent)
        {
            console.WithExplicitStart().WaitFor(openBao);
            return;
        }

        var seeder = builder.AddProject<Projects.ConsoleAppTestsVaultSharp>("openbao-seed")
            .WithArgs("--seed-demo-data")
            .WithReference(openBao)
            .WaitFor(openBao);

        console.WaitForCompletion(seeder);
    }

    private static OpenBaoMode ParseMode(string? configuredMode) => configuredMode switch
    {
        null => OpenBaoMode.Persistent,
        "Persistent" => OpenBaoMode.Persistent,
        "Development" => OpenBaoMode.Development,
        _ => throw new InvalidOperationException(
            "OpenBao:Mode must be either 'Persistent' or 'Development'.")
    };
}
