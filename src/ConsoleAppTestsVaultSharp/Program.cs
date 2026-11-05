using ConsoleAppTestsVaultSharp;
using Microsoft.Extensions.Configuration;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;

Console.WriteLine("*** .NET 10 + HashiCorp Vault/OpenBao + VaultSharp（NuGet 包）测试 ***");

var configuration = new ConfigurationManager();
configuration.AddEnvironmentVariables();
var connectionSettings = OpenBaoConnectionSettings.Load(configuration);

IAuthMethodInfo authMethod = new TokenAuthMethodInfo(connectionSettings.Token);

var vaultClientSettings = new VaultClientSettings(connectionSettings.Address.ToString(), authMethod);
IVaultClient vaultClient = new VaultClient(vaultClientSettings);

Console.WriteLine("");
Console.WriteLine("正在读取第一组 Secret：app/config...");
Secret<SecretData> kv2Secret1 =
    await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(
        path: "app/config",
        mountPoint: "secret");

string? username = kv2Secret1.Data.Data["username"].ToString();
string? password = kv2Secret1.Data.Data["password"].ToString();

Console.WriteLine($"username = {username}");
Console.WriteLine($"password = {password}");


Console.WriteLine("");
Console.WriteLine("正在读取第二组 Secret：integracoes/api...");
Secret<SecretData> kv2Secret2 =
    await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(
        path: "integracoes/api",
        mountPoint: "secret");

string? api_key = kv2Secret2.Data.Data["api_key"].ToString();
string? endpoint = kv2Secret2.Data.Data["endpoint"].ToString();

Console.WriteLine($"api_key = {api_key}");
Console.WriteLine($"endpoint = {endpoint}");

Console.WriteLine();
if (!Console.IsInputRedirected)
{
    Console.WriteLine("按回车键退出...");
    Console.ReadLine();
}