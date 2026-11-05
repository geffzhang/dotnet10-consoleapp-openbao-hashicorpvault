using System.Data.Common;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace OpenBao.AppHost.Tests;

public sealed class OpenBaoReferenceTests
{
    [Fact]
    public async Task WithReferenceInjectsDefaultConnectionStringKey()
    {
        var application = DistributedApplication.CreateBuilder([]);
        var (openBao, _) = AddOpenBao(application, "openbao", "demo-token", "127.0.0.1", 1337);
        var consumer = application.AddResource(new ContainerResource("consumer"));
        consumer.WithReference(openBao);

        var environment = await EvaluateEnvironmentAsync(application, consumer.Resource);

        Assert.Contains("ConnectionStrings__openbao", environment.Keys);
    }

    [Fact]
    public async Task CustomResourceNamesProduceIndependentConnectionKeys()
    {
        var application = DistributedApplication.CreateBuilder([]);
        var (first, firstToken) = AddOpenBao(application, "bao-one", "first-token", "10.0.0.1", 18101);
        var (second, secondToken) = AddOpenBao(application, "bao-two", "second-token", "10.0.0.2", 18102);
        var consumer = application.AddResource(new ContainerResource("consumer"));
        consumer.WithReference(first);
        consumer.WithReference(second);

        var environment = await EvaluateEnvironmentAsync(application, consumer.Resource);
        var firstReference = Assert.IsType<ConnectionStringReference>(environment["ConnectionStrings__bao-one"]);
        var secondReference = Assert.IsType<ConnectionStringReference>(environment["ConnectionStrings__bao-two"]);
        var firstReferences = GetReferencedObjects(firstReference.Resource.ConnectionStringExpression);
        var secondReferences = GetReferencedObjects(secondReference.Resource.ConnectionStringExpression);

        Assert.Contains(firstToken, firstReferences);
        Assert.DoesNotContain(secondToken, firstReferences);
        Assert.Contains(secondToken, secondReferences);
        Assert.DoesNotContain(firstToken, secondReferences);

        var firstConnection = new DbConnectionStringBuilder
        {
            ConnectionString = await first.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None)
        };
        var secondConnection = new DbConnectionStringBuilder
        {
            ConnectionString = await second.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None)
        };

        Assert.Equal("http://10.0.0.1:18101", firstConnection["Address"]);
        Assert.Equal("first-token", firstConnection["Token"]);
        Assert.Equal("http://10.0.0.2:18102", secondConnection["Address"]);
        Assert.Equal("second-token", secondConnection["Token"]);
    }

    [Fact]
    public void WithReferenceDoesNotAddReadinessWait()
    {
        var application = DistributedApplication.CreateBuilder([]);
        var (openBao, _) = AddOpenBao(application, "openbao", "demo-token", "127.0.0.1", 1337);
        var consumer = application.AddResource(new ContainerResource("consumer"));

        consumer.WithReference(openBao);

        Assert.Empty(consumer.Resource.Annotations.OfType<WaitAnnotation>());
    }

    [Fact]
    public void WaitForAddsReadinessDependency()
    {
        var application = DistributedApplication.CreateBuilder([]);
        var (openBao, _) = AddOpenBao(application, "openbao", "demo-token", "127.0.0.1", 1337);
        var consumer = application.AddResource(new ContainerResource("consumer"));

        consumer.WaitFor(openBao);

        Assert.Contains(
            consumer.Resource.Annotations.OfType<WaitAnnotation>(),
            annotation => ReferenceEquals(annotation.Resource, openBao.Resource));
    }

    [Fact]
    public async Task ModelExpressionsDoNotContainResolvedToken()
    {
        const string tokenValue = "synthetic-private-token";
        var application = DistributedApplication.CreateBuilder([]);
        var (openBao, token) = AddOpenBao(application, "openbao", tokenValue, "127.0.0.1", 1337);
        var consumer = application.AddResource(new ContainerResource("consumer"));
        consumer.WithReference(openBao);

        var environment = await EvaluateEnvironmentAsync(application, consumer.Resource);
        var reference = Assert.IsType<ConnectionStringReference>(environment["ConnectionStrings__openbao"]);
        var expression = reference.Resource.ConnectionStringExpression;

        Assert.DoesNotContain(tokenValue, expression.ValueExpression);
        Assert.All(expression.ManifestExpressions, value => Assert.DoesNotContain(tokenValue, value));
        Assert.DoesNotContain(tokenValue, ((IManifestExpressionProvider)reference).ValueExpression);
        Assert.Contains(token, GetReferencedObjects(expression));
    }

    private static (IResourceBuilder<OpenBaoResource> Resource, ParameterResource Token) AddOpenBao(
        IDistributedApplicationBuilder application,
        string name,
        string tokenValue,
        string host,
        int port)
    {
        var token = application.AddParameter($"{name}-token", tokenValue, secret: true).Resource;
        var resource = new OpenBaoResource(name, token);
        var resourceBuilder = application.AddResource(resource)
            .WithHttpEndpoint(targetPort: 8200, name: "http");
        var endpoint = resource.PrimaryEndpoint.EndpointAnnotation;
        endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, host, port, "http");

        return (resourceBuilder, token);
    }

    private static async Task<Dictionary<string, object>> EvaluateEnvironmentAsync(
        IDistributedApplicationBuilder application,
        IResource consumer)
    {
        var variables = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(
            application.ExecutionContext,
            consumer,
            variables,
            CancellationToken.None);

        foreach (var annotation in consumer.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return variables;
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
