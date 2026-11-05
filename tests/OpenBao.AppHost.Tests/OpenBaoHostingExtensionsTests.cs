using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenBao.AppHost.Tests;

public sealed class OpenBaoHostingExtensionsTests
{
    [Fact]
    public async Task PersistentConsoleIsExplicitAndNoSeederIsCreated()
    {
        var application = DistributedApplication.CreateBuilder([]);

        OpenBaoAppHost.Configure(application);

        var openBao = Assert.Single(application.Resources.OfType<OpenBaoResource>());
        var console = Assert.Single(
            application.Resources.OfType<ProjectResource>(),
            resource => resource.Name == "console");

        Assert.NotEmpty(console.Annotations.OfType<ExplicitStartupAnnotation>());
        Assert.Contains(
            console.Annotations.OfType<WaitAnnotation>(),
            annotation => ReferenceEquals(annotation.Resource, openBao));
        Assert.DoesNotContain(application.Resources, resource => resource.Name == "openbao-seed");

        var environment = await EvaluateEnvironmentAsync(application, console);
        var connectionString = Assert.IsType<ConnectionStringReference>(
            environment["ConnectionStrings__openbao"]);
        Assert.Same(openBao, connectionString.Resource);
    }

    [Fact]
    public void ConfigureRejectsUnsupportedMode()
    {
        var application = DistributedApplication.CreateBuilder([]);
        application.Configuration["OpenBao:Mode"] = "Unsupported";

        Assert.Throws<InvalidOperationException>(() => OpenBaoAppHost.Configure(application));
    }

    [Fact]
    public async Task DevelopmentConsoleWaitsForSeederCompletion()
    {
        const string tokenValue = "synthetic-development-token";
        var application = DistributedApplication.CreateBuilder([]);
        application.Configuration["OpenBao:Mode"] = "Development";
        application.Configuration["Parameters:openbao-token"] = tokenValue;

        OpenBaoAppHost.Configure(application);

        var openBao = Assert.Single(application.Resources.OfType<OpenBaoResource>());
        var token = Assert.Single(
            application.Resources.OfType<ParameterResource>(),
            parameter => parameter.Name == "openbao-token");
        var console = Assert.Single(
            application.Resources.OfType<ProjectResource>(),
            resource => resource.Name == "console");
        var seeder = Assert.Single(
            application.Resources.OfType<ProjectResource>(),
            resource => resource.Name == "openbao-seed");

        Assert.True(token.Secret);
        Assert.Contains(
            console.Annotations.OfType<WaitAnnotation>(),
            annotation => ReferenceEquals(annotation.Resource, seeder) &&
                annotation.WaitType == WaitType.WaitForCompletion);
        Assert.Contains(
            seeder.Annotations.OfType<WaitAnnotation>(),
            annotation => ReferenceEquals(annotation.Resource, openBao));

        var openBaoEnvironment = await EvaluateEnvironmentAsync(application, openBao);
        var tokenExpression = Assert.IsType<ReferenceExpression>(
            openBaoEnvironment["BAO_DEV_ROOT_TOKEN_ID"]);
        Assert.Contains(token, GetReferencedObjects(tokenExpression));
        Assert.DoesNotContain(tokenValue, tokenExpression.ValueExpression);

        var consoleEnvironment = await EvaluateEnvironmentAsync(application, console);
        var consoleConnection = Assert.IsType<ConnectionStringReference>(
            consoleEnvironment["ConnectionStrings__openbao"]);
        var seederEnvironment = await EvaluateEnvironmentAsync(application, seeder);
        var seederConnection = Assert.IsType<ConnectionStringReference>(
            seederEnvironment["ConnectionStrings__openbao"]);

        Assert.Same(openBao, consoleConnection.Resource);
        Assert.Same(openBao, seederConnection.Resource);
        Assert.Contains(token, GetReferencedObjects(openBao.ConnectionStringExpression));
        Assert.DoesNotContain(
            tokenValue,
            ((IManifestExpressionProvider)consoleConnection).ValueExpression);
        Assert.DoesNotContain(
            tokenValue,
            ((IManifestExpressionProvider)seederConnection).ValueExpression);
    }

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

    private static async Task<Dictionary<string, object>> EvaluateEnvironmentAsync(
        IDistributedApplicationBuilder application,
        IResource resource)
    {
        var variables = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(
            application.ExecutionContext,
            resource,
            variables,
            CancellationToken.None);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return variables;
    }
}
