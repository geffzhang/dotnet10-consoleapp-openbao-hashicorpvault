using System.Data.Common;

namespace Aspire.Hosting.ApplicationModel;

public sealed class OpenBaoResource : ContainerResource, IResourceWithConnectionString
{
    public OpenBaoResource(string name, ParameterResource token) : base(name)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (!token.Secret)
        {
            throw new ArgumentException("The OpenBao token parameter must be secret.", nameof(token));
        }

        PrimaryEndpoint = new EndpointReference(this, "http");
        TokenParameter = token;
        ConnectionStringExpression = ReferenceExpression.Create(
            $"{new OpenBaoConnectionStringValueProvider(PrimaryEndpoint, TokenParameter)}");
    }

    public EndpointReference PrimaryEndpoint { get; }

    public ParameterResource TokenParameter { get; }

    public ReferenceExpression ConnectionStringExpression { get; }

    private sealed class OpenBaoConnectionStringValueProvider(
        EndpointReference endpoint,
        ParameterResource token) : IValueProvider, IManifestExpressionProvider, IValueWithReferences
    {
        private readonly ReferenceExpression _manifestExpression = ReferenceExpression.Create(
            $"Address={endpoint};Token={token}");

        public IEnumerable<object> References => [endpoint, token];

        public string ValueExpression => _manifestExpression.ValueExpression;

        public ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default) =>
            GetValueAsync(new ValueProviderContext(), cancellationToken);

        public async ValueTask<string?> GetValueAsync(
            ValueProviderContext context,
            CancellationToken cancellationToken = default)
        {
            var address = await endpoint.GetValueAsync(context, cancellationToken).ConfigureAwait(false);
            var tokenValue = await token.GetValueAsync(context, cancellationToken).ConfigureAwait(false);

            var connectionString = new DbConnectionStringBuilder
            {
                ["Address"] = address,
                ["Token"] = tokenValue
            };

            return connectionString.ConnectionString;
        }
    }
}
