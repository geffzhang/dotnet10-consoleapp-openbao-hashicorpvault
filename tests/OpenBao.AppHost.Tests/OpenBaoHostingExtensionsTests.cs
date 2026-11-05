using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenBao.AppHost.Tests;

public sealed class OpenBaoHostingExtensionsTests
{
    [Fact]
    public async Task PersistentModeUsesPostgresNamedVolumeAndWaitsForDatabase()
    {
        var application = DistributedApplication.CreateBuilder([]);
        var token = application.AddParameter("openbao-token", "synthetic-token", secret: true).Resource;

        var openBao = application.AddOpenBao("openbao", OpenBaoMode.Persistent, token);

        var postgres = Assert.Single(application.Resources.OfType<PostgresServerResource>());
        var database = Assert.Single(application.Resources.OfType<PostgresDatabaseResource>());
        var dataVolume = Assert.Single(
            postgres.Annotations.OfType<ContainerMountAnnotation>(),
            mount => mount.Type == ContainerMountType.Volume);
        var image = Assert.Single(openBao.Resource.Annotations.OfType<ContainerImageAnnotation>());

        Assert.IsAssignableFrom<ContainerResource>(openBao.Resource);
        Assert.IsAssignableFrom<IResourceWithConnectionString>(openBao.Resource);
        Assert.Equal("ghcr.io/openbao/openbao", image.Image);
        Assert.Equal("2.7.2", image.Tag);
        Assert.Equal("openbao", database.DatabaseName);
        Assert.Same(postgres, database.Parent);
        Assert.Equal("openbao-postgres-data", dataVolume.Source);
        Assert.True(postgres.UserNameParameter!.Secret);
        Assert.True(postgres.PasswordParameter!.Secret);
        Assert.Contains(
            openBao.Resource.Annotations.OfType<WaitAnnotation>(),
            annotation => ReferenceEquals(annotation.Resource, database));
        Assert.NotEmpty(openBao.Resource.Annotations.OfType<HealthCheckAnnotation>());

        var executionConfiguration = await ExecutionConfigurationBuilder
            .Create(openBao.Resource)
            .WithArgumentsConfig()
            .BuildAsync(application.ExecutionContext, NullLogger.Instance);
        var arguments = executionConfiguration.Arguments.Select(argument => argument.Value);
        Assert.Contains("server", arguments);
        Assert.Contains("-config=/openbao/config/openbao.hcl", arguments);
        Assert.Contains(token, GetReferencedObjects(openBao.Resource.ConnectionStringExpression));
    }

    [Fact]
    public async Task DevelopmentModeUsesMemoryWithoutPostgres()
    {
        var application = DistributedApplication.CreateBuilder([]);
        var token = application.AddParameter("openbao-token", "synthetic-token", secret: true).Resource;

        var openBao = application.AddOpenBao("openbao", OpenBaoMode.Development, token);

        var image = Assert.Single(openBao.Resource.Annotations.OfType<ContainerImageAnnotation>());
        var executionConfiguration = await ExecutionConfigurationBuilder
            .Create(openBao.Resource)
            .WithArgumentsConfig()
            .BuildAsync(application.ExecutionContext, NullLogger.Instance);

        Assert.Empty(application.Resources.OfType<PostgresServerResource>());
        Assert.Empty(application.Resources.OfType<PostgresDatabaseResource>());
        Assert.IsAssignableFrom<ContainerResource>(openBao.Resource);
        Assert.IsAssignableFrom<IResourceWithConnectionString>(openBao.Resource);
        Assert.Equal("ghcr.io/openbao/openbao", image.Image);
        Assert.Equal("2.7.2", image.Tag);
        var arguments = executionConfiguration.Arguments.Select(argument => argument.Value);
        Assert.Contains("server", arguments);
        Assert.Contains("-dev", arguments);
        Assert.Contains(token, GetReferencedObjects(openBao.Resource.ConnectionStringExpression));
    }

    private static IEnumerable<object> GetReferencedObjects(ReferenceExpression expression)
    {
        foreach (var provider in expression.ValueProviders)
        {
            yield return provider;

            if (provider is IValueWithReferences valueWithReferences)
            {
                foreach (var reference in valueWithReferences.References)
                {
                    yield return reference;
                }
            }
        }
    }
}
