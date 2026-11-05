using System.Data.Common;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace OpenBao.AppHost.Tests;

public sealed class OpenBaoResourceTests
{
    [Fact]
    public void ImplementsContainerAndConnectionStringContracts()
    {
        var (resource, _) = CreateResource("demo-token");

        Assert.IsAssignableFrom<ContainerResource>(resource);
        Assert.IsAssignableFrom<IResourceWithConnectionString>(resource);
    }

    [Fact]
    public void PreservesSecretParameterAndHttpEndpoint()
    {
        var (_, token) = CreateResource("demo-token");
        var resource = new OpenBaoResource("openbao", token);

        Assert.Same(token, resource.TokenParameter);
        Assert.Same(resource, resource.PrimaryEndpoint.Resource);
        Assert.Equal("http", resource.PrimaryEndpoint.EndpointName);
    }

    [Fact]
    public void RejectsNullOrNonSecretTokenParameter()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenBaoResource("openbao", null!));

        var nonSecret = new ParameterResource("token", _ => "not-secret", secret: false);

        Assert.Throws<ArgumentException>(() => new OpenBaoResource("openbao", nonSecret));
    }

    [Fact]
    public void ConstructionAndExpressionCreationDoNotResolveValues()
    {
        var token = new ParameterResource(
            "token",
            _ => throw new InvalidOperationException("The token value must remain deferred."),
            secret: true);

        var resource = new OpenBaoResource("openbao", token);

        Assert.NotNull(resource.ConnectionStringExpression);
    }

    [Fact]
    public void RetainsEndpointAndSecretReferences()
    {
        var (resource, token) = CreateResource("demo-token");
        var references = GetReferencedObjects(resource.ConnectionStringExpression);

        Assert.Contains(resource.PrimaryEndpoint, references);
        Assert.Contains(token, references);
        Assert.DoesNotContain("demo-token", resource.ConnectionStringExpression.ValueExpression);
        Assert.All(
            resource.ConnectionStringExpression.ManifestExpressions,
            expression => Assert.DoesNotContain("demo-token", expression));
    }

    [Theory]
    [InlineData("demo-token")]
    [InlineData("a;b=c")]
    [InlineData("a\"b'c")]
    [InlineData(" leading and trailing ")]
    public async Task ConnectionStringRoundTripsToken(string tokenValue)
    {
        var (resource, _) = CreateResource(tokenValue);
        AllocateEndpoint(resource, "127.0.0.1", 1337);

        var connectionString = await resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None);
        var parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };

        Assert.Equal(2, parsed.Count);
        Assert.Equal("http://127.0.0.1:1337", parsed["Address"]);
        Assert.Equal(tokenValue, parsed["Token"]);
    }

    [Fact]
    public async Task ResolvesEndpointUsingConsumerContext()
    {
        var (resource, _) = CreateResource("demo-token");
        var annotation = resource.PrimaryEndpoint.EndpointAnnotation;
        var containerNetwork = new NetworkIdentifier("container");
        annotation.AllocatedEndpoint = new AllocatedEndpoint(annotation, "127.0.0.1", 1337, "http");
        annotation.AllAllocatedEndpoints.AddOrUpdateAllocatedEndpoint(
            containerNetwork,
            new AllocatedEndpoint(
                annotation,
                "openbao",
                8200,
                EndpointBindingMode.SingleAddress,
                networkId: containerNetwork));

        var hostContext = new ValueProviderContext
        {
            Caller = new ContainerResource("host-consumer")
        };
        var containerContext = new ValueProviderContext
        {
            Caller = new ContainerResource("container-consumer"),
            Network = containerNetwork
        };

        var provider = Assert.Single(resource.ConnectionStringExpression.ValueProviders);
        var hostValue = await provider.GetValueAsync(hostContext, CancellationToken.None);
        var containerValue = await provider.GetValueAsync(containerContext, CancellationToken.None);

        Assert.Contains("127.0.0.1", hostValue);
        Assert.Contains("openbao", containerValue);
        Assert.NotEqual(hostValue, containerValue);
    }

    private static (OpenBaoResource Resource, ParameterResource Token) CreateResource(string tokenValue)
    {
        var token = new ParameterResource("token", _ => tokenValue, secret: true);
        var resource = new OpenBaoResource("openbao", token);
        resource.Annotations.Add(new EndpointAnnotation(
            ProtocolType.Tcp,
            "http",
            "http",
            "http",
            port: 1337,
            targetPort: 8200,
            isExternal: true));

        return (resource, token);
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

    private static void AllocateEndpoint(OpenBaoResource resource, string host, int port)
    {
        var endpoint = resource.PrimaryEndpoint.EndpointAnnotation;
        endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, host, port, "http");
    }
}
