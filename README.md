# dotnet10-consoleapp-openbao-hashicorpvault

使用 .NET 10、C# 和 VaultSharp 读取 HashiCorp Vault / OpenBao 中的机密配置的控制台示例。仓库提供 OpenBao 的 Docker Compose 持久化部署配置、测试数据初始化服务和 HTTP 请求示例。

## 项目结构

| 文件 | 用途 |
|---|---|
| [Program.cs](src/ConsoleAppTestsVaultSharp/Program.cs) | 使用 Token 认证，读取两组 KV v2 Secret，输出中文提示并等待回车退出 |
| [ConsoleAppTestsVaultSharp.csproj](src/ConsoleAppTestsVaultSharp/ConsoleAppTestsVaultSharp.csproj) | .NET 10 控制台项目，引用 VaultSharp 1.17.5.1 |
| [OpenBao.AppHost.csproj](src/OpenBao.AppHost/OpenBao.AppHost.csproj) | Aspire 13.4.6 / .NET 10 AppHost 项目 |
| [OpenBaoResource.cs](src/OpenBao.AppHost/Hosting/OpenBaoResource.cs) | 实现容器资源及连接字符串资源契约 |
| [OpenBao.AppHost.Tests.csproj](tests/OpenBao.AppHost.Tests/OpenBao.AppHost.Tests.csproj) | 不启动容器的 Aspire 资源模型测试 |
| [docker-compose.yml](src/scripts-openbao/docker-compose.yml) | OpenBao 服务、Raft 数据卷、健康检查及测试数据初始化服务 |
| [tests.http](src/scripts-openbao/tests.http) | 服务健康检查、Secret 列表、写入与读取请求示例 |

## 当前部署方式

- 镜像：`ghcr.io/openbao/openbao:2.7.2`。
- **非开发模式**：单节点 Raft 存储，数据保存在命名卷 `openbao-data` 的 `/openbao/file` 目录中。
- API 和 Web UI：http://localhost:1337，仅映射到宿主机的 `127.0.0.1`。
- 首次使用需要初始化；每次启动或重启后都需要手动解封（unseal）。
- `openbao-init` 是 `init` profile 下的显式初始化服务，仅在 OpenBao 健康后写入测试数据，不负责初始化或解封服务器。
- 控制台和初始化服务均从 `BAO_TOKEN` 环境变量读取 Token，不再使用开发模式的固定 Root Token。

> 此配置面向本地学习和测试：HTTP 未启用 TLS，`disable_mlock = true`，且只有一个节点，不提供多节点高可用。生产环境需要额外配置 TLS、最小权限策略、安全的密钥管理及备份。

## 环境要求

- .NET 10 SDK。
- Docker Desktop 或支持 Linux 容器的 Docker 环境。
- Docker Compose 2.23.1 或更高版本，以支持 `configs.content`。
- PowerShell。以下命令均在仓库根目录执行；需要连续使用变量的命令应在同一个窗口运行。

## 首次部署

### 1. 启动并初始化 OpenBao

```powershell
$compose = ".\src\scripts-openbao\docker-compose.yml"
docker compose -f $compose up -d openbao
docker compose -f $compose exec openbao bao status
```

只有当状态为 `Initialized: false` 时，才执行初始化：

```powershell
docker compose -f $compose exec openbao bao operator init -key-shares=5 -key-threshold=3
```

命令会生成五份解封密钥和一个 Root Token，需要三份不同的密钥才能解封。请将它们安全保存在仓库和数据卷之外，并避免写入日志、聊天或版本控制。

**每个数据卷只初始化一次。已初始化的服务不要再次执行 `operator init`。**

### 2. 手动解封

执行以下命令三次，每次在提示中输入一份不同的解封密钥：

```powershell
docker compose -f $compose exec openbao bao operator unseal
docker compose -f $compose exec openbao bao operator unseal
docker compose -f $compose exec openbao bao operator unseal
docker compose -f $compose exec openbao bao status
```

解封后应看到 `Initialized: true`、`Sealed: false`。未初始化或尚未解封时，健康检查不会通过，容器显示 `starting` 或 `unhealthy` 属于预期行为。

### 3. 设置 Token 并启用 KV v2

在当前 PowerShell 会话中输入 Token，避免将 Token 字面值写入命令历史：

```powershell
$token = Read-Host "请输入 OpenBao Token" -AsSecureString
$env:BAO_TOKEN = [pscredential]::new("openbao", $token).GetNetworkCredential().Password
```

首次启用 `secret` 挂载点下的 KV v2 引擎：

```powershell
docker compose -f $compose exec -e BAO_TOKEN openbao bao secrets enable -path=secret -version=2 kv
```

启用引擎需要管理权限。可使用 Root Token 完成本地首次配置；日常读取应改用具备最小读取权限的 Token。

如果 `secret` 已存在，先检查配置，不要重复启用或删除引擎：

```powershell
docker compose -f $compose exec -e BAO_TOKEN openbao bao secrets list -detailed
```

该挂载点应为 `kv` 类型，且 `version` 为 `2`。

### 4. 写入测试数据

在服务已解封且 `BAO_TOKEN` 已设置的情况下执行：

```powershell
docker compose -f $compose --profile init run --rm openbao-init
```

初始化服务写入以下两组配置：

| 引擎内路径 | 字段 | 示例说明 |
|---|---|---|
| `app/config` | `username`、`password` | 用户名为 `admin`；示例密码见 Compose 配置，此处不展示 |
| `integracoes/api` | `api_key`、`endpoint` | 示例 API Key 和 `https://api.exemplo.com` 地址 |

引擎挂载点为 `secret`，对应的 HTTP 读取路径分别是 `/v1/secret/data/app/config` 和 `/v1/secret/data/integracoes/api`。KV v2 写入请求需要将字段放在外层 `data` 对象内。

重复运行初始化服务会将这两组配置恢复为 Compose 中的示例值，并生成新的 Secret 版本。它不会在普通 `up -d openbao` 时自动运行；Token 缺失、无效、权限不足或引擎缺失都会明确报错。

## 运行控制台

确认服务已解封、两组 Secret 已存在，并在当前会话中设置了具有读取权限的 `BAO_TOKEN`：

```powershell
dotnet run --project .\src\ConsoleAppTestsVaultSharp\ConsoleAppTestsVaultSharp.csproj
```

程序连接 `http://localhost:1337`，依次读取账号密码和 API 配置。标题、读取进度、Token 缺失提示及退出提示均为中文；`username`、`password`、`api_key`、`endpoint` 字段名保持不变。读取完成后按回车退出。

**程序会明文输出密码和 API Key，只应使用演示数据。** 缺少 `BAO_TOKEN` 时会立即报错；网络、权限、Secret 路径或字段错误也会导致运行失败。

## HTTP 请求示例

[tests.http](src/scripts-openbao/tests.http) 使用 `http://localhost:1337/v1` 作为基础地址，并通过 `{{$processEnv BAO_TOKEN}}` 读取运行请求工具的进程环境变量。

文件中的变量占位符和多行 `curl` 请求需要由支持该格式的工具解析，不能原样粘贴到 PowerShell 执行。PowerShell 中可使用 `Invoke-RestMethod`。

如果使用 VS Code 请求工具，确保 VS Code 进程也能读取 `BAO_TOKEN`。仅在集成终端中设置变量不会更新已经运行的 VS Code 父进程；必要时从已设置变量的终端重新启动 VS Code。

## 重启与数据保留

```powershell
$compose = ".\src\scripts-openbao\docker-compose.yml"
docker compose -f $compose restart openbao
```

重启后，使用三份不同的解封密钥重复执行：

```powershell
docker compose -f $compose exec openbao bao operator unseal
```

已有 Secret、版本和 Token 数据保留在卷中，重启本身不会清空它们；Token 仍受其有效期和撤销状态限制。无需重新初始化或重新写入测试数据。

- `docker compose -f $compose down` 保留命名卷。
- **不要执行 `docker compose -f $compose down -v`**，否则会删除持久化卷。
- Compose 默认会为卷名添加项目名前缀，例如 `scripts-openbao_openbao-data`。更改项目名会使用另一个卷，可能看起来像数据丢失。
- 数据持久化不等于备份，请另外保存数据备份及解封密钥。

## 从开发模式迁移

开发模式的数据保存在内存中，不会自动迁移到 Raft 卷。替换旧容器前，先导出需要保留的 Secret；新服务初始化、解封并启用 KV v2 后，再写入并回读验证。旧的固定开发 Token 不再有效，需使用新服务生成的 Token。

对于已完成迁移的环境，不要重新执行“首次部署”中的初始化步骤。

### 使用本机加密保存的迁移凭据

如果迁移时已使用 Windows DPAPI 保存凭据，文件位于当前用户目录下的 `.openbao\migration-<时间戳>\` 中：

- `initialization-credentials.dpapi`：加密的初始化结果，包含解封密钥和 Root Token。
- `secrets-backup.dpapi`：加密的迁移前 Secret 备份。

这些文件不在仓库中，也不是 Compose 自动创建的。读取初始化凭据并设置当前会话的 Token：

```powershell
$credentialFile = Read-Host "请输入 initialization-credentials.dpapi 的完整路径"
$secure = (Get-Content -LiteralPath $credentialFile -Raw).Trim() | ConvertTo-SecureString
$credentials = [pscredential]::new("openbao", $secure).GetNetworkCredential().Password | ConvertFrom-Json
$env:BAO_TOKEN = $credentials.root_token
```

若服务已初始化但仍处于封印状态，可使用保存的三份密钥解封，避免将密钥放入命令行参数：

```powershell
for ($i = 0; $i -lt 3; $i++) {
    $body = @{ key = $credentials.unseal_keys_b64[$i] } | ConvertTo-Json
    $status = Invoke-RestMethod -Method Put `
        -Uri "http://localhost:1337/v1/sys/unseal" `
        -ContentType "application/json" -Body $body
}
$status | Select-Object initialized, sealed, progress
```

DPAPI 文件需要由原 Windows 用户在原机器上解密；不要把它当作可跨机器恢复的唯一备份。请另行妥善保管解封密钥，不要打印完整的 `$credentials` 对象。使用结束后可清除当前会话中的 Token 和凭据变量：

```powershell
$env:BAO_TOKEN = $null
$token = $null
$credentials = $null
$secure = $null
$body = $null
```

## 常见问题

| 现象 | 检查项 |
|---|---|
| 无法连接 `localhost:1337` | 检查 Docker 是否运行、容器日志以及端口是否被占用 |
| 容器显示 `unhealthy` | 使用 `bao status` 确认是否已初始化并解封 |
| 提示设置 `BAO_TOKEN` | 在运行程序的同一个 PowerShell 会话中设置变量 |
| 权限不足或 Token 无效 | 检查是否误用旧开发 Token，以及新 Token 是否有效、具备相应权限 |
| 引擎或 Secret 不存在 | 确认 `secret` 为 KV v2，并已加载两组测试数据 |
| 重启后无法读取 | 先手动解封，不要重新初始化或删除数据卷 |

## Aspire OpenBaoResource 资源契约

仓库还包含一个 Aspire AppHost 项目和 `OpenBaoResource` 资源类型。该类型继承
`ContainerResource` 并实现 `IResourceWithConnectionString`；构造函数接收资源名和
机密 `ParameterResource`。Token 必须使用机密参数，不能作为普通配置或硬编码字符串传入。
资源通过名为 `http` 的 `EndpointReference` 暴露地址；创建资源模型时，调用方负责添加
HTTP 端点注解。

下面仅展示资源模型和标准引用契约，不是可直接启动 OpenBao 的完整配置：

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);
var token = builder.AddParameter("openbao-token", secret: true);

var openbao = builder.AddResource(
        new OpenBaoResource("openbao", token.Resource))
    .WithHttpEndpoint(targetPort: 8200, name: "http");

var consumer = builder.AddResource(new ContainerResource("consumer"))
    .WithReference(openbao)
    .WaitFor(openbao);
```

`WithReference(openbao)` 按 Aspire 默认命名规则向消费者注入
`ConnectionStrings__openbao`，连接字符串包含 `Address` 和 `Token` 字段，并保留由 Aspire
分配且适用于消费者网络的 HTTP 端点地址。Token 以机密参数引用延迟求值，连接字符串字段按
标准连接字符串规则转义。不同资源名使用各自的默认连接字符串键。

`WithReference` 负责传递连接配置，不代表资源已就绪；需要就绪依赖时应另外调用
`WaitFor(openbao)`。引用本身不会启动或初始化 OpenBao，也不会解封、写入 Secret 或配置存储。

此阶段只实现并测试资源模型契约，**尚未实现** PostgreSQL 持久化、内存开发模式、
完整 `AddOpenBao` 扩展、自动启动配置、初始化/解封流程或演示 Secret 写入。
测试只验证 Aspire 模型与环境变量引用，不代表 OpenBao 容器运行或 PostgreSQL 数据持久化已验证。
