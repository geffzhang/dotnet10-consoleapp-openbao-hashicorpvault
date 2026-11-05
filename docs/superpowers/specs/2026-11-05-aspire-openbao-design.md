# 使用 Aspire 编排 OpenBao

## 目标与范围

新增 Aspire AppHost，编排 OpenBao 和现有的 .NET 10 VaultSharp 控制台应用。
参考用户提供的 BellaBaxter.Aspire.Host 中的容器编排、端点注入和依赖管理方式，
但不依赖 Bella Baxter 的整套服务。核心交付是符合 Aspire 资源契约的
`OpenBaoResource`，而不只是通过 `AddContainer` 启动服务。

两种模式均固定使用 `ghcr.io/openbao/openbao:2.7.2`。
用户已确认支持两种模式：默认使用 PostgreSQL 后端的持久化模式，以及显式选择的内存开发模式。

本方案面向本地学习和演示，不作为生产部署方案。
现有 Docker Compose 的行为及已有 Raft 数据保持不变。

## 实现方式

使用一个 AppHost 项目，通过两个启动配置选择模式，在项目内定义 `OpenBaoResource`
及其编排扩展。不新增独立的集成类库，将 AppHost 加入现有解决方案。

优先使用 Aspire 直接编排容器，而不是包装 Compose，以便由 AppHost 管理资源依赖并注入实际端点。
当前仓库规模不需要额外的可复用集成包及其公共 API。

## Aspire 资源契约

AppHost 通过 `IDistributedApplicationBuilder` 定义资源及其关系，不直接替代 Kubernetes。
OpenBao 采用以下资源模型：

- `OpenBaoResource` 继承 `ContainerResource` 并实现 `IResourceWithConnectionString`，
  同时具备容器生命周期、端点、环境变量及连接字符串能力。
- `AddOpenBao` 扩展返回 `IResourceBuilder<OpenBaoResource>`，两种模式使用相同资源类型，
  不将普通容器强制转换为具备连接字符串能力的资源。
- 通过 Aspire 注解及标准编排扩展描述 HTTP 端点、环境变量、配置挂载、健康检查和依赖。
  PostgreSQL 数据卷属于数据库资源，不伪装成 OpenBao 自身的存储卷。
- 通过 `ConnectionStringExpression` 暴露延迟解析的地址与 Token，保留端点和机密参数的引用，
  不在构建资源模型时提前求值或将凭据固化到发布配置。
- 下游使用 `.WithReference(openbao)` 获取连接配置，使用 `.WaitFor(openbao)` 声明就绪依赖。
  `WithReference` 负责配置传递，不等同于等待健康检查通过。

调用形式如下，其中 `openbao` 是 `AddOpenBao` 返回的资源构建器：

```csharp
builder.AddProject<Projects.ConsoleAppTestsVaultSharp>("console")
    .WithReference(openbao)
    .WaitFor(openbao);
```

开发模式还需要声明演示数据写入成功的依赖；持久化模式保留显式启动控制台的操作。

### 连接字符串

- 连接字符串使用 `Address` 和 `Token` 两个字段，逻辑格式为
  `Address=<实际 HTTP 端点>;Token=<机密参数值>`。
- 沿用 Aspire 默认连接字符串命名。资源名为 `openbao` 时，`WithReference` 向下游注入
  `ConnectionStrings__openbao`，应用可通过标准配置的 `GetConnectionString("openbao")` 读取。
- `Address` 来自资源的 HTTP 端点引用，支持 Aspire 为宿主机进程和容器消费者解析适用地址；
  不能把 AppHost 机器上的 `localhost` 字符串硬编码给所有消费者。
- `Token` 来自机密参数引用，不在源码、文档示例、普通日志或发布清单中保存真实值。
  连接字符串整体按敏感配置处理，不记录其内容。
- 字段必须按标准连接字符串规则正确转义并解析，覆盖分号、等号及引号等字符，
  不使用简单的字符串分割，也不假设所有合法 Token 都不含特殊字符。
- `WithReference` 本身不执行认证、初始化、解封或 Secret 写入。

## 持久化模式

- 启动 PostgreSQL，使用命名数据卷，并为 OpenBao 创建专用数据库。
- 数据库就绪后才启动 OpenBao。
- 配置 OpenBao 的 `postgresql` 存储后端，允许其自动创建存储表，不引入独立的数据库迁移服务。
- 数据库凭据通过机密参数和运行时环境变量传递，不写入受版本控制的连接字符串或配置文件。
  使用 OpenBao 后端支持的 PostgreSQL 环境变量。
- 仅在本地容器网络中禁用数据库 TLS；文档说明生产部署需要 TLS 和适当的数据库权限。
- 启用 OpenBao UI 和 HTTP API，通过 Aspire 解析宿主机访问端点，控制台不假设固定端口。
- 保持手动初始化和解封，不在项目文件或自动初始化日志中采集、保存解封密钥及 Root Token。
- 未初始化或已密封状态视为未就绪；OpenBao 就绪检查通过后，控制台才允许启动。
- 不自动写入或覆盖持久化 Secret；文档说明如何显式启用 KV v2，并写入现有两组演示数据。
- Secret 数据存储在 PostgreSQL 数据卷中。重启 OpenBao 后需要重新解封，但 Secret 及其版本必须保留。

首次运行时，即使尚无可供控制台使用的 Token，也必须能够启动基础设施。
完成初始化、解封、KV v2 配置及 Token 设置后，用户应能显式启动控制台。
若加载新的环境变量或用户机密需要重启 AppHost，应在文档中说明。
不能因为缺少凭据而阻止用户启动用于获取这些凭据的基础设施。

## 开发模式

- 使用 `server -dev` 启动 OpenBao，此模式不启动 PostgreSQL。
- 通过 Aspire 机密参数提供开发用 Root Token，不在源码中硬编码。
  数据写入操作和控制台使用同一个参数进行认证。
- 等待 OpenBao 就绪后，写入现有两组演示 Secret：
  `secret/app/config` 和 `secret/integracoes/api`。
- 沿用现有演示字段和值，并明确标注为公开演示数据，不作为真实凭据使用。
- 演示数据写入成功后才启动控制台；写入失败必须明确显示，不能被报告为成功。
- 文档明确说明重启会丢失 Secret，开发模式不适用于生产环境。

## 控制台集成与凭据

保留现有 Token 认证、KV v2 路径、中文提示和独立运行方式。

- 继续通过 `BAO_TOKEN` 读取 Token。
- Aspire 启动时优先读取 `ConnectionStrings:openbao`，解析 `Address` 和 `Token`；
  不依赖 AppHost 为控制台额外注入一套重复的 `BAO_ADDR` 和 `BAO_TOKEN`。
- 未配置该连接字符串时，保留独立运行方式，通过 `BAO_ADDR` 和 `BAO_TOKEN` 读取配置。
- 仅在未设置 `BAO_ADDR` 时使用 `http://localhost:1337`，保留独立运行的默认行为。
- 对已配置但为空、缺少必要字段或格式无效的连接字符串明确报错，不静默回退到独立运行配置。
- 对显式配置为空或格式无效的地址、缺少 Token 等情况给出明确错误，不输出敏感内容。
- 通过 `WithReference(openbao)` 注入实际端点及 Token，确保资源契约是正式集成路径。
- 持久化模式支持显式启动控制台；开发模式的自动启动流程不依赖交互式终端输入。
- 独立交互运行时保留“按回车退出”的行为，非交互启动时不等待输入。

敏感输入使用 Aspire 机密参数和用户机密管理，不提交 Token、数据库密码、解封密钥或本地凭据文件。
不新增输出凭据的自定义日志。
文档说明现有控制台会有意明文输出演示 Secret，以及开发服务器本身存在不安全的日志输出特征。

## 兼容性与操作边界

- 保留现有 Compose 文件，作为独立的 Raft 存储运行方式。
- 不复用其数据卷，不自动将其数据迁移到 PostgreSQL。
- 明确说明切换启动配置不等于迁移已有数据。
- 保留现有控制台项目及直接使用 `dotnet run` 的工作流。
- API 和数据库端点仅供本机开发访问，不向外部网络公开。
- 不引入高可用、自动解封、生产 TLS、自动备份或无关的 Bella Baxter 服务。

## 文档更新

更新仓库根目录 README，补充环境要求、解决方案及 AppHost 命令、两种启动配置、
参数设置、实际端点查看方式、手动初始化与解封、显式写入演示数据、控制台启动方式和持久化行为。
说明 `AddOpenBao`、`WithReference` 和 `WaitFor` 的职责、连接字符串字段、
标准配置读取方式，以及独立运行配置的回退规则。
说明普通停止操作不应删除数据库卷，删除数据卷会销毁其中的数据。

## 验证与验收

1. 通过解决方案构建 AppHost 和现有控制台。
2. 验证两种编排模型中的镜像版本、模式专属资源、PostgreSQL 命名卷、机密参数引用、
   端点注入、就绪依赖和演示数据写入完成依赖；确认两种模式返回的资源均满足
   `ContainerResource` 和 `IResourceWithConnectionString` 契约。
3. 验证仅通过 `WithReference(openbao)` 即可注入 `ConnectionStrings__openbao`，
   地址来自端点引用，Token 来自机密参数引用；不要求调用方再手动注入二者。
4. 验证连接字符串的字段读取、特殊字符转义、缺失字段和无效输入行为；
   确认错误不会泄露 Token，配置错误不会触发静默回退。
5. 尽可能在不连接真实机密服务的情况下，验证无 Aspire 连接字符串时的独立运行行为：
   `BAO_ADDR` 的默认地址、配置地址、无效地址以及缺少 Token 时的错误。
6. Docker 可用时，运行开发模式，验证两组 Secret 均可读取，且控制台正常完成。
7. Docker 可用时，创建临时 PostgreSQL 后端实例，初始化并解封，启用 KV v2，
   写入和回读测试 Secret；重启 OpenBao 并重新解封后，再次确认数据仍然存在。
8. 确认持久化模式启动时不会自动写入或覆盖 Secret。
9. 不改动已有 Compose 资源和用户数据，仅清理为验证而创建的资源。

如果 Docker 或其他必要运行环境不可用，必须明确说明哪些运行验证受阻；
仅构建成功不能证明数据持久化正确。

## 参考资料

- 用户提供的本地参考：`bella-baxter-dotnet` 仓库中的 `BellaBaxter.Aspire.Host`。
- [Aspire 的 IResourceWithConnectionString 契约](https://github.com/dotnet/aspire/blob/main/src/Aspire.Hosting/ApplicationModel/IResourceWithConnectionString.cs)：
  连接字符串表达式和资源引用；实施时按选定 Aspire 版本核对 API。
- [OpenBao PostgreSQL 存储后端](https://openbao.org/docs/configuration/storage/postgresql/)：
  后端环境变量和自动建表行为。
- [OpenBao 开发服务器](https://openbao.org/docs/concepts/dev-server/)：
  自动初始化、自动解封及非持久化的内存存储。
