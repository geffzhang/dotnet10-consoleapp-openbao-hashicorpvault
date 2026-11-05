# OpenBao 双模式完整编排实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目标：** 完成 Aspire 对 OpenBao 的 PostgreSQL 持久化和内存开发双模式编排，并将控制台、演示 Secret 写入及运行验证接入完整流程。

**架构：** 在现有 AppHost 内增加 `AddOpenBao` 和模式选择，继续使用同一个 `OpenBaoResource`。持久化模式由 PostgreSQL 命名卷提供存储并保留手动初始化/解封；开发模式使用内存服务器，以独立 Aspire 项目资源运行现有控制台的种子命令，并在成功后才启动读取流程。控制台优先解析 Aspire 连接字符串，同时保留独立运行配置。

**技术栈：** .NET 10、Aspire 13.4.6、OpenBao `ghcr.io/openbao/openbao:2.7.2`、VaultSharp 1.17.5.1、xUnit、VSTest、Docker。

**Spec:** [整体编排设计](../specs/2026-11-05-aspire-openbao-design.md)

## Global Constraints

- 两种模式均使用 `ghcr.io/openbao/openbao:2.7.2`；默认 PostgreSQL 持久化，显式 `development` 模式使用内存。
- 两种模式使用同一个 `OpenBaoResource : ContainerResource, IResourceWithConnectionString`；`AddOpenBao` 返回 `IResourceBuilder<OpenBaoResource>`。
- 连接字符串字段为 `Address` 和 `Token`，资源 `openbao` 通过标准 `WithReference` 注入 `ConnectionStrings__openbao`；保留延迟端点和机密参数引用。
- 持久化模式允许无 Token 启动基础设施；初始化、解封、启用 KV v2 和 Secret 写入必须由用户显式操作，不自动覆盖持久化数据。
- 开发模式不创建 PostgreSQL；Root Token 通过机密参数提供，种子服务成功写入 `secret/app/config` 和 `secret/integracoes/api` 后才允许控制台启动。
- PostgreSQL 使用独立命名卷及专用数据库；数据库用户名和密码均通过机密参数传递，本地连接使用 `BAO_PG_CONNECTION_URL` 和 `sslmode=disable`，数据库 TLS 关闭只适用于本地容器网络。
- 不在代码、普通日志、发布清单或计划中写入真实 Token、数据库密码、解封密钥或凭据文件；不要记录连接字符串。
- 控制台配置错误必须明确失败，不得静默回退或泄露 Token；独立运行未设置 `BAO_ADDR` 时才使用 `http://localhost:1337`。
- 保留 `src/scripts-openbao/docker-compose.yml`、其 `openbao-data` 卷、Raft 数据和现有直接 `dotnet run` 工作流；不自动迁移数据。
- API 和数据库端点仅用于本机开发；不添加自动解封、高可用、生产 TLS、自动备份或无关服务。
- 所有运行验证使用合成凭据和独立测试数据；只清理本轮明确创建的资源，绝不删除既有 Compose 卷或不明来源的数据。

## Review Focus

1. Aspire 连接字符串未设置与显式空值必须区分；空值、缺字段、额外字段、非法 URI 均报错且不泄露 Token。由 Task 1 的 `ConnectionStringRejectsInvalidOrEmptyValues`、`StandaloneConfigurationRejectsExplicitEmptyAddress` 和 `ConfigurationErrorsDoNotContainToken` 覆盖。
2. Token 含分号、等号、引号或首尾空格时必须无损解析；由 Task 1 的 `ConnectionStringRoundTripsSpecialTokenCharacters` 覆盖。
3. 持久化模式无 Token 时仍能启动数据库和 OpenBao，且不自动启动控制台或写入 Secret；由 Task 2 的模型测试和 Task 5 的首次启动验证覆盖。
4. OpenBao 未初始化或已封印时不得放行下游；PostgreSQL 未就绪时不得启动持久化 OpenBao；由 Task 2 的依赖模型测试及 Task 5 的容器验证覆盖。
5. 开发模式种子写入失败时必须非零退出且控制台不能启动；由 Task 3 的 `SeedAsync_WhenWriteFails_ThrowsAndDoesNotReportSuccess` 和 `DevelopmentConsoleWaitsForSeederCompletion` 覆盖。

---

### Task 1: 控制台配置解析与独立运行兼容

**Files:**
- Create: `src/ConsoleAppTestsVaultSharp/OpenBaoConnectionSettings.cs`
- Create: `src/ConsoleAppTestsVaultSharp/Properties/AssemblyInfo.cs`
- Modify: `src/ConsoleAppTestsVaultSharp/Program.cs`
- Modify: `src/ConsoleAppTestsVaultSharp/ConsoleAppTestsVaultSharp.csproj`
- Create: `tests/ConsoleAppTestsVaultSharp.Tests/ConsoleAppTestsVaultSharp.Tests.csproj`
- Create: `tests/ConsoleAppTestsVaultSharp.Tests/OpenBaoConnectionSettingsTests.cs`
- Modify: `src/ConsoleAppTestsVaultSharp/ConsoleAppTestsVaultSharp.slnx`

**Interfaces:**
- Produces: `internal sealed class OpenBaoConnectionSettings`，包含 `Uri Address`、`string Token`，以及 `static OpenBaoConnectionSettings Load(IConfiguration configuration)`。
- `Load` 优先读取 `configuration.GetConnectionString("openbao")`；仅在该值为 `null` 时读取独立配置 `BAO_ADDR` / `BAO_TOKEN`。`BAO_ADDR` 未设置时使用 `http://localhost:1337`。
- `Load` 对已配置的连接字符串要求且仅允许 `Address`、`Token` 两个字段；地址必须是绝对 HTTP(S) URI，Token 不得为空。独立配置遵守同样的地址和 Token 校验规则。
- `Program.cs` 使用解析出的地址和 Token 创建 `VaultClientSettings`；仅在交互式输入时提示按回车退出，重定向输入时正常结束。

- [ ] **Step 1: 创建测试项目并编写配置解析失败测试**

创建 xUnit 测试项目并引用控制台项目。在 `OpenBaoConnectionSettingsTests.cs` 添加测试：连接字符串缺失 `Address` / `Token`、包含未知字段、空字符串、非法 URI 时抛出 `InvalidOperationException`；显式空 `BAO_ADDR` 和缺失/空 `BAO_TOKEN` 也抛出该异常。断言异常消息不包含测试 Token。

- [ ] **Step 2: 运行测试确认失败**

运行：`dotnet test .\tests\ConsoleAppTestsVaultSharp.Tests\ConsoleAppTestsVaultSharp.Tests.csproj -c Release --filter FullyQualifiedName~OpenBaoConnectionSettingsTests`

预期：因测试项目或 `OpenBaoConnectionSettings` 尚不存在而失败。

- [ ] **Step 3: 添加无效连接字符串错误不泄露 Token 的测试**

添加 `ConfigurationErrorsDoNotContainToken`，使用合成 Token 和错误连接字符串，断言抛出的异常不包含该 Token。

- [ ] **Step 4: 添加 Token 特殊字符无损解析测试**

添加 `ConnectionStringRoundTripsSpecialTokenCharacters`，至少覆盖 `;`、`=`、单双引号和首尾空格；通过 `DbConnectionStringBuilder` 构造输入，断言 `Load` 返回原始 Token。

- [ ] **Step 5: 添加独立运行回退测试**

添加测试覆盖：无 Aspire 连接字符串且无 `BAO_ADDR` 时默认地址为 `http://localhost:1337`；设置有效 `BAO_ADDR` 时使用该地址；空地址和缺失 Token 时报错。

- [ ] **Step 6: 实现配置模型和测试项目**

为控制台添加标准 `Microsoft.Extensions.Configuration` 环境变量配置依赖。实现 `OpenBaoConnectionSettings.Load(IConfiguration)`，使用 `DbConnectionStringBuilder` 解析字段并提供不含配置值的错误信息。通过 `InternalsVisibleTo("ConsoleAppTestsVaultSharp.Tests")` 仅向测试项目开放内部配置类型。

- [ ] **Step 7: 接入现有控制台**

在 `Program.cs` 构建环境变量配置、调用 `OpenBaoConnectionSettings.Load`，以 `Address` 和 `Token` 创建 VaultSharp 客户端。保留现有中文文案、KV v2 路径和输出字段；只有 `!Console.IsInputRedirected` 时等待回车。

- [ ] **Step 8: 运行配置测试和 Release 构建**

运行上面的筛选测试，再运行：`dotnet build .\src\ConsoleAppTestsVaultSharp\ConsoleAppTestsVaultSharp.slnx -c Release`

预期：测试全部通过，构建零警告、零错误。

- [ ] **Step 9: 提交 Task 1**

提交消息：`feat: read OpenBao Aspire connection settings`

### Task 2: `AddOpenBao` 双模式资源模型

**Files:**
- Create: `src/OpenBao.AppHost/Hosting/OpenBaoExtensions.cs`
- Modify: `src/OpenBao.AppHost/Hosting/OpenBaoResource.cs`
- Modify: `src/OpenBao.AppHost/OpenBao.AppHost.csproj`
- Create: `tests/OpenBao.AppHost.Tests/OpenBaoHostingExtensionsTests.cs`
- Modify: `tests/OpenBao.AppHost.Tests/OpenBaoResourceTests.cs`

**Interfaces:**
- Produces: `public enum OpenBaoMode { Persistent, Development }`。
- Produces: `public static IResourceBuilder<OpenBaoResource> AddOpenBao(this IDistributedApplicationBuilder builder, string name, OpenBaoMode mode, ParameterResource token)`。
- 持久化模式创建 PostgreSQL 资源和 `openbao` 专用数据库、命名卷 `openbao-postgres-data`；数据库用户名与密码是机密参数；OpenBao 等待数据库并使用 PostgreSQL 存储及 `BAO_PG_CONNECTION_URL`。
- 开发模式使用 `server -dev` 和同一个机密 Token 参数，不创建 PostgreSQL。
- 两种模式均创建 `OpenBaoResource`、命名 `http` 的 HTTP 端点、`/v1/sys/health` 健康检查及标准 `Address` / `Token` 连接字符串；OpenBao API 端口使用容器端口 `8200`，宿主端口由 Aspire 分配。

- [ ] **Step 1: 编写持久化模式模型测试**

在 `OpenBaoHostingExtensionsTests.cs` 添加 `PersistentModeUsesPostgresNamedVolumeAndWaitsForDatabase`，断言 OpenBao 为 `OpenBaoResource`、镜像为 `ghcr.io/openbao/openbao:2.7.2`、存在 PostgreSQL 数据库和 `openbao-postgres-data` 命名卷、OpenBao 具有 PostgreSQL 依赖及 `/v1/sys/health` 检查，并确认 Token 参数仍为机密引用。

- [ ] **Step 2: 运行测试确认失败**

运行：`dotnet test .\tests\OpenBao.AppHost.Tests\OpenBao.AppHost.Tests.csproj -c Release --filter FullyQualifiedName~OpenBaoHostingExtensionsTests`

预期：因双模式扩展尚不存在而失败。

- [ ] **Step 3: 编写开发模式模型测试**

添加 `DevelopmentModeUsesMemoryWithoutPostgres`，断言 OpenBao 使用开发模式命令和 Token 参数，不创建 PostgreSQL；验证两种模式返回的资源均满足 `ContainerResource` 与 `IResourceWithConnectionString` 契约。

- [ ] **Step 4: 实现资源扩展和模式特有配置**

新增 `OpenBaoMode` 和 `AddOpenBao`。持久化分支通过 Aspire PostgreSQL 集成创建服务、专用数据库和命名卷，生成本地 `sslmode=disable` 的 `BAO_PG_CONNECTION_URL` 引用，并通过 `WithContainerFiles` 将不含机密的 OpenBao HCL 配置文件写入容器，再以 `server -config=/openbao/config/openbao.hcl` 启动。开发分支运行 `server -dev`，通过机密环境变量传递开发 Token。两个分支都添加 HTTP 端点、健康检查及相同类型的 OpenBao 资源。

- [ ] **Step 5: 添加资源契约回归断言**

在现有资源测试中确认新的扩展不改变延迟连接字符串求值、宿主/容器端点解析、特殊字符转义、Token 机密引用及 `WithReference` 默认键行为。

- [ ] **Step 6: 运行 AppHost 测试与构建**

运行：`dotnet test .\tests\OpenBao.AppHost.Tests\OpenBao.AppHost.Tests.csproj -c Release`

再运行：`dotnet build .\src\ConsoleAppTestsVaultSharp\ConsoleAppTestsVaultSharp.slnx -c Release`

预期：全部 AppHost 测试通过，构建零警告、零错误。

- [ ] **Step 7: 提交 Task 2**

提交消息：`feat: add persistent and development OpenBao modes`

### Task 3: 开发模式 Secret 种子和 AppHost 启动关系

**Files:**
- Modify: `src/ConsoleAppTestsVaultSharp/Program.cs`
- Create: `src/ConsoleAppTestsVaultSharp/OpenBaoDemoSeeder.cs`
- Modify: `src/OpenBao.AppHost/Program.cs`
- Create: `src/OpenBao.AppHost/Properties/launchSettings.json`
- Modify: `tests/OpenBao.AppHost.Tests/OpenBaoHostingExtensionsTests.cs`

**Interfaces:**
- Produces: `internal static class OpenBaoDemoSeeder`，提供 `static Task SeedAsync(Func<string, IDictionary<string, object>, Task> writeSecret)`；委托参数依次为 Secret 相对路径和字段字典，允许单元测试模拟写入失败。
- 控制台以 `--seed-demo-data` 参数运行时只执行种子写入、输出不含 Secret 值的成功状态并退出；失败时向标准错误输出不含凭据的错误并返回非零退出码。
- AppHost 读取 `OpenBao:Mode`；未设置时为 `Persistent`，只接受 `Persistent` 和 `Development`。
- AppHost 添加名为 `console` 的项目资源并通过 `.WithReference(openBao)` 传递连接字符串。持久化模式使用 `.WithExplicitStart()` 和 `.WaitFor(openBao)`；开发模式添加同一控制台项目的 `openbao-seed` 实例，传入 `--seed-demo-data`，并令 `console` 使用 `.WaitForCompletion(openBaoSeed)`。
- 启动配置名为 `persistent`、`development`；前者设置 `OpenBao__Mode=Persistent`，后者设置 `OpenBao__Mode=Development`。持久化 Token 参数可为空且为机密参数；开发 Token 参数为必需机密参数，并供 OpenBao、种子服务和控制台共同引用。

- [ ] **Step 1: 编写 AppHost 启动依赖模型测试**

添加 `PersistentConsoleIsExplicitAndNoSeederIsCreated`，断言持久化模式的控制台显式启动、引用并等待 OpenBao，且没有种子资源；添加 `DevelopmentConsoleWaitsForSeederCompletion`，断言开发模式种子服务引用并等待 OpenBao，控制台等待种子进程完成，且种子与控制台引用同一个 OpenBao 资源。

- [ ] **Step 2: 运行测试确认失败**

运行：`dotnet test .\tests\OpenBao.AppHost.Tests\OpenBao.AppHost.Tests.csproj -c Release --filter FullyQualifiedName~PersistentConsoleIsExplicitAndNoSeederIsCreated`

预期：因 AppHost 尚未声明完整资源关系而失败。

- [ ] **Step 3: 编写种子写入失败测试**

在控制台测试项目新增 `OpenBaoDemoSeederTests.cs`。添加 `SeedAsync_WhenWriteFails_ThrowsAndDoesNotReportSuccess`：模拟第二次写入失败，断言异常向调用方传播，且种子操作未被视为成功。

- [ ] **Step 4: 实现演示 Secret 写入器**

新增 `OpenBaoDemoSeeder.SeedAsync(Func<string, IDictionary<string, object>, Task> writeSecret)`，依次写入 KV v2 mount `secret` 下 `app/config` 的 `username` / `password` 和 `integracoes/api` 的 `api_key` / `endpoint`。值沿用 `src/scripts-openbao/docker-compose.yml` 的公开演示数据，不在新代码、日志或计划中复制或展示这些值；只有两次写入都完成才正常返回。

- [ ] **Step 5: 添加控制台种子运行入口**

在 `Program.cs` 识别唯一的 `--seed-demo-data` 参数，使用同一配置解析结果和 VaultSharp Token 客户端调用种子写入器；成功时只报告完成，不输出 Secret 值；失败时不吞异常、不输出连接字符串或 Token，并返回非零退出码。普通无参数运行仍保留两组 Secret 读取行为。

- [ ] **Step 6: 编写开发 Token 参数共享测试**

为开发模式模型断言同一个机密参数引用用于 OpenBao 开发服务器连接配置、`openbao-seed` 和 `console`；确认模型表达式和清单中不包含 Token 实际值。

- [ ] **Step 7: 接入 AppHost 两种启动配置**

修改 `Program.cs` 读取并校验 `OpenBao:Mode`，创建模式对应的机密 Token 参数并调用 `AddOpenBao`。持久化控制台设为显式启动；开发模式运行种子项目资源并使用 `.WaitForCompletion` 门控控制台。添加两个启动配置，默认模式仍为持久化。

- [ ] **Step 8: 运行配置及 AppHost 测试**

运行：`dotnet test .\tests\OpenBao.AppHost.Tests\OpenBao.AppHost.Tests.csproj -c Release` 和 `dotnet test .\tests\ConsoleAppTestsVaultSharp.Tests\ConsoleAppTestsVaultSharp.Tests.csproj -c Release`

预期：两组测试全部通过；模型检查确认种子写入不影响持久化 Secret。

- [ ] **Step 9: 提交 Task 3**

提交消息：`feat: seed development OpenBao before console startup`

### Task 4: 用户文档和解决方案验收

**Files:**
- Modify: `README.md`
- Modify: `src/ConsoleAppTestsVaultSharp/ConsoleAppTestsVaultSharp.slnx`

**Interfaces:**
- README 命令以仓库根目录为当前目录，使用 Windows PowerShell 路径格式，列出两个 Aspire 启动配置的运行方式。
- 文档必须区分 AppHost PostgreSQL 持久化模式、AppHost 开发模式和现有 Compose/Raft 模式；说明它们之间没有自动迁移或共享数据卷。

- [ ] **Step 1: 补齐解决方案成员**

将 `ConsoleAppTestsVaultSharp.Tests` 项目加入现有 `.slnx`，保留 AppHost、AppHost.Tests 和控制台项目。

- [ ] **Step 2: 更新 README Aspire 操作说明**

说明环境要求、默认/显式模式、AppHost 命令、机密参数设置、动态端点查看、`AddOpenBao` / `WithReference` / `WaitFor` 职责、标准 `GetConnectionString("openbao")` 配置读取和独立运行回退。

- [ ] **Step 3: 记录初始化、Secret 和数据边界**

记录持久化模式需手动初始化/解封、启用 KV v2、用户显式写入演示数据、设置 Token 后重启/显式启动控制台；普通停止保留 PostgreSQL 卷，删除卷会销毁数据。记录开发模式重启丢失 Secret、仅供本地演示，以及控制台会明文输出 Secret、开发服务器日志不安全。明确保留现有 Compose/Raft 方式且不共享数据卷。

- [ ] **Step 4: 运行解决方案 Release 测试和构建**

运行：`dotnet test .\src\ConsoleAppTestsVaultSharp\ConsoleAppTestsVaultSharp.slnx -c Release`

预期：测试执行数大于零且全部通过。

运行：`dotnet build .\src\ConsoleAppTestsVaultSharp\ConsoleAppTestsVaultSharp.slnx -c Release`

预期：零警告、零错误。

- [ ] **Step 5: 提交 Task 4**

提交消息：`docs: document OpenBao Aspire orchestration modes`

### Task 5: Docker 开发模式与 PostgreSQL 持久化验证

**Files:**
- Runtime verification only; no Compose or existing volume changes.

**Interfaces:**
- 使用 Aspire Dashboard 显示的动态宿主地址访问 OpenBao，不假设固定宿主端口。
- 只使用合成 Token、临时解封凭据和随机测试 Secret；解封密钥及 Root Token 仅保存在当前进程内，不写入仓库文件、日志或提交。

- [ ] **Step 1: 检查 Docker 和已有资源**

使用 `docker version`、`docker ps -a` 和 `docker volume ls` 确认 Docker 可用，并记录现有 Compose 容器及卷；不得停止或删除现有 `src/scripts-openbao/docker-compose.yml` 资源或 `openbao-data` 卷。

- [ ] **Step 2: 运行开发模式**

运行：`dotnet run --project .\src\OpenBao.AppHost\OpenBao.AppHost.csproj --launch-profile development`

预期：启动 OpenBao 开发容器和种子进程、不启动 PostgreSQL；种子成功后控制台退出或完成读取，`secret/app/config` 与 `secret/integracoes/api` 可通过 VaultSharp 读取。

- [ ] **Step 3: 首次启动持久化模式且不提供 OpenBao Token**

运行：`dotnet run --project .\src\OpenBao.AppHost\OpenBao.AppHost.csproj --launch-profile persistent`

预期：PostgreSQL 命名卷、专用数据库及 OpenBao 均启动；OpenBao 等待数据库；无 Token 不阻止基础设施启动；控制台不自动启动且没有 Secret 被写入。

- [ ] **Step 4: 初始化并写入持久化探测 Secret**

通过 Aspire 显示的 OpenBao 地址，使用本次专用的临时解封凭据手动初始化并解封，启用 KV v2，设置本机 Aspire secret 中的 Token 并重启 AppHost。写入随机 `persistence_probe` 值并立即回读；全过程不保存或提交初始化凭据。

- [ ] **Step 5: 重启 OpenBao 并验证 PostgreSQL 持久性**

仅重启 OpenBao 容器，按设计重新解封，再次读取相同的 `persistence_probe` 值。预期值及 Secret 版本仍存在；确认持久化模式未创建或运行开发种子服务。

- [ ] **Step 6: 清理本轮专用资源并记录结果**

停止本轮 AppHost 容器。仅当确认 PostgreSQL 命名卷由本轮创建且其中没有需要保留的数据时才删除；不得清理任何预先存在的卷。若 Docker 或运行环境不可用，记录实际受阻的运行检查，不能将构建通过表述为持久化验证通过。

- [ ] **Step 7: 完成最终 Release 验证**

运行：`dotnet test .\src\ConsoleAppTestsVaultSharp\ConsoleAppTestsVaultSharp.slnx -c Release`

预期：测试执行数大于零且全部通过。随后运行：`dotnet build .\src\ConsoleAppTestsVaultSharp\ConsoleAppTestsVaultSharp.slnx -c Release`

预期：构建零警告、零错误；在任务报告中明确记录开发模式与持久化验证是否完成。
