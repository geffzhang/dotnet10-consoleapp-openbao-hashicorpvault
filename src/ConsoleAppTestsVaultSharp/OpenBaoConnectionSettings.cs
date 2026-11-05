using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace ConsoleAppTestsVaultSharp;

internal sealed class OpenBaoConnectionSettings
{
    private const string DefaultAddress = "http://localhost:1337";

    private OpenBaoConnectionSettings(Uri address, string token)
    {
        Address = address;
        Token = token;
    }

    public Uri Address { get; }

    public string Token { get; }

    public static OpenBaoConnectionSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("openbao");
        if (connectionString is not null)
        {
            return ParseConnectionString(connectionString);
        }

        var addressValue = configuration["BAO_ADDR"] ?? DefaultAddress;
        var address = ParseAddress(addressValue);
        var token = configuration["BAO_TOKEN"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "请设置 BAO_TOKEN 环境变量，并使用具有 Secret 读取权限的 Token。");
        }

        return new OpenBaoConnectionSettings(address, token);
    }

    private static OpenBaoConnectionSettings ParseConnectionString(string connectionString)
    {
        var values = new DbConnectionStringBuilder();
        try
        {
            values.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Aspire ConnectionStrings:openbao 格式无效。");
        }

        if (values.Count != 2 || !values.ContainsKey("Address") || !values.ContainsKey("Token"))
        {
            throw new InvalidOperationException(
                "Aspire ConnectionStrings:openbao 必须且只能包含 Address 和 Token 字段。");
        }

        var addressValue = Convert.ToString(values["Address"], CultureInfo.InvariantCulture);
        var token = Convert.ToString(values["Token"], CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Aspire ConnectionStrings:openbao 缺少必需的 Token。");
        }

        return new OpenBaoConnectionSettings(ParseAddress(addressValue), token);
    }

    private static Uri ParseAddress(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(address.UserInfo))
        {
            throw new InvalidOperationException("OpenBao 地址必须是有效的绝对 HTTP 或 HTTPS URI。");
        }

        return address;
    }
}
