using System.Data.Common;
using Microsoft.Extensions.Configuration;

namespace ConsoleAppTestsVaultSharp.Tests;

public sealed class OpenBaoConnectionSettingsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Address=http://localhost:1337")]
    [InlineData("Token=synthetic-token")]
    [InlineData("Address=http://localhost:1337;Token=")]
    [InlineData("Address=not-a-uri;Token=synthetic-token")]
    [InlineData("Address=ftp://localhost:1337;Token=synthetic-token")]
    [InlineData("Address=http://localhost:1337;Token=synthetic-token;Other=value")]
    public void ConnectionStringRejectsInvalidOrEmptyValues(string connectionString)
    {
        var configuration = CreateConfiguration(
            ("ConnectionStrings:openbao", connectionString),
            ("BAO_ADDR", "http://fallback:1337"),
            ("BAO_TOKEN", "fallback-token"));

        Assert.Throws<InvalidOperationException>(
            () => OpenBaoConnectionSettings.Load(configuration));
    }

    [Fact]
    public void ConfigurationErrorsDoNotContainToken()
    {
        const string token = "synthetic-private-token";
        var configuration = CreateConfiguration(
            ("ConnectionStrings:openbao", $"Address=not-a-uri;Token={token}"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenBaoConnectionSettings.Load(configuration));

        Assert.DoesNotContain(token, exception.Message);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("a;b=c")]
    [InlineData("a\"b'c")]
    [InlineData(" leading and trailing ")]
    public void ConnectionStringRoundTripsSpecialTokenCharacters(string token)
    {
        var builder = new DbConnectionStringBuilder
        {
            ["Address"] = "http://localhost:1337",
            ["Token"] = token
        };
        var configuration = CreateConfiguration(("ConnectionStrings:openbao", builder.ConnectionString));

        var settings = OpenBaoConnectionSettings.Load(configuration);

        Assert.Equal(new Uri("http://localhost:1337"), settings.Address);
        Assert.Equal(token, settings.Token);
    }

    [Fact]
    public void AspireConnectionStringTakesPrecedenceOverStandaloneEnvironment()
    {
        var builder = new DbConnectionStringBuilder
        {
            ["Address"] = "https://aspire.example.test:8443",
            ["Token"] = "aspire-token"
        };
        var configuration = CreateConfiguration(
            ("ConnectionStrings:openbao", builder.ConnectionString),
            ("BAO_ADDR", "http://standalone.example.test:1337"),
            ("BAO_TOKEN", "standalone-token"));

        var settings = OpenBaoConnectionSettings.Load(configuration);

        Assert.Equal(new Uri("https://aspire.example.test:8443"), settings.Address);
        Assert.Equal("aspire-token", settings.Token);
    }

    [Fact]
    public void StandaloneConfigurationUsesDefaultAddressWhenAddressIsNotSet()
    {
        var configuration = CreateConfiguration(("BAO_TOKEN", "synthetic-token"));

        var settings = OpenBaoConnectionSettings.Load(configuration);

        Assert.Equal(new Uri("http://localhost:1337"), settings.Address);
        Assert.Equal("synthetic-token", settings.Token);
    }

    [Fact]
    public void StandaloneConfigurationUsesConfiguredAddress()
    {
        var configuration = CreateConfiguration(
            ("BAO_ADDR", "https://bao.example.test:8443"),
            ("BAO_TOKEN", "synthetic-token"));

        var settings = OpenBaoConnectionSettings.Load(configuration);

        Assert.Equal(new Uri("https://bao.example.test:8443"), settings.Address);
    }

    [Fact]
    public void StandaloneConfigurationRejectsExplicitEmptyAddress()
    {
        var configuration = CreateConfiguration(
            ("BAO_ADDR", ""),
            ("BAO_TOKEN", "synthetic-token"));

        Assert.Throws<InvalidOperationException>(
            () => OpenBaoConnectionSettings.Load(configuration));
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("ftp://bao.example.test")]
    public void StandaloneConfigurationRejectsInvalidAddress(string address)
    {
        var configuration = CreateConfiguration(
            ("BAO_ADDR", address),
            ("BAO_TOKEN", "synthetic-token"));

        Assert.Throws<InvalidOperationException>(
            () => OpenBaoConnectionSettings.Load(configuration));
    }

    [Fact]
    public void StandaloneConfigurationRejectsMissingToken()
    {
        var configuration = CreateConfiguration(("BAO_ADDR", "http://localhost:1337"));

        Assert.Throws<InvalidOperationException>(
            () => OpenBaoConnectionSettings.Load(configuration));
    }

    private static ConfigurationManager CreateConfiguration(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();
        foreach (var (key, value) in values)
        {
            configuration[key] = value;
        }

        return configuration;
    }
}
