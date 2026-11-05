using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public enum OpenBaoMode
{
    Persistent,
    Development
}

public static class OpenBaoExtensions
{
    private const string Image = "ghcr.io/openbao/openbao";
    private const string ImageTag = "2.7.2";

    public static IResourceBuilder<OpenBaoResource> AddOpenBao(
        this IDistributedApplicationBuilder builder,
        string name,
        OpenBaoMode mode,
        ParameterResource token)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (mode is not OpenBaoMode.Persistent and not OpenBaoMode.Development)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var resource = new OpenBaoResource(name, token);
        if (mode == OpenBaoMode.Persistent)
        {
            var userName = builder.AddParameter($"{name}-postgres-user", secret: true);
            var password = builder.AddParameter($"{name}-postgres-password", secret: true);
            var postgres = builder.AddPostgres($"{name}-postgres", userName, password)
                .WithDataVolume($"{name}-postgres-data");
            var database = postgres.AddDatabase($"{name}-database", name);

            var openBao = AddOpenBaoContainer(builder, resource);
            openBao.WithContainerFiles("/openbao/config",
                [
                    new ContainerFile
                    {
                        Name = "openbao.hcl",
                        Contents = """
                            ui = true
                            disable_mlock = true

                            storage "postgresql" {}

                            listener "tcp" {
                              address = "0.0.0.0:8200"
                              tls_disable = true
                            }
                            """
                    }
                ])
                .WithArgs("server", "-config=/openbao/config/openbao.hcl")
                .WithEnvironment(
                    "BAO_PG_CONNECTION_URL",
                    ReferenceExpression.Create($"{database.Resource.UriExpression}?sslmode=disable"))
                .WaitFor(database);

            return openBao;
        }

        var developmentOpenBao = AddOpenBaoContainer(builder, resource);
        developmentOpenBao.WithArgs("server", "-dev")
            .WithEnvironment("BAO_DEV_ROOT_TOKEN_ID", ReferenceExpression.Create($"{token}"))
            .WithEnvironment("BAO_DEV_LISTEN_ADDRESS", "0.0.0.0:8200");

        return developmentOpenBao;
    }

    private static IResourceBuilder<OpenBaoResource> AddOpenBaoContainer(
        IDistributedApplicationBuilder builder,
        OpenBaoResource resource) =>
        builder.AddResource(resource)
            .WithImage(Image, ImageTag)
            .WithEntrypoint("bao")
            .WithHttpEndpoint(targetPort: 8200, name: "http")
            .WithHttpHealthCheck("/v1/sys/health", endpointName: "http");
}
