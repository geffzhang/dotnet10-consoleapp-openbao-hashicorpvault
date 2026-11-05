# OpenBaoResource 中文实施计划

> **执行者要求：** 执行前使用 `executing-plans` 技能；如用户明确选择子代理执行，则使用 `subagent-driven-development`。按任务逐项验证，不跳过失败测试。

**目标：** 实现继承 `ContainerResource`、实现 `IResourceWithConnectionString` 的 `OpenBaoResource`，验证其他 Aspire 资源可通过 `WithReference` 引用它。

**架构：** 资源类放在 AppHost 项目内，不新增独立集成类库。连接地址通过 HTTP 端点引用获得，Token 通过机密参数引用获得，连接配置延迟到运行时求值。资源建模、配置注入和就绪等待分别承担各自职责。

**技术栈：** .NET 10、C#、Aspire 13.4.6、xUnit、VSTest。Aspire 版本对齐参考类库，不照搬其不安全的资源类型转换。

**设计依据：** [使用 Aspire 编排 OpenBao](../specs/2026-11-05-aspire-openbao-design.md)。

**当前状态：** 仅保存实施计划，未获准开始代码实现。

## 本阶段范围

本计划是整体 Aspire 编排方案的第一个里程碑，不代表完整方案已完成。

包含：

- 建立可编译的 AppHost 和测试项目。
- 定义 `OpenBaoResource` 的类型、构造函数、端点和连接字符串契约。
- 验证标准 `WithReference` 注入路径及延迟求值行为。
- 补充中文说明和使用示例。

不包含：

- PostgreSQL 容器、存储配置、数据卷和重启持久性验证。
- 完整 `AddOpenBao` 双模式扩展、启动配置、初始化和解封流程。
- 演示数据写入服务、控制台配置读取改造及完整端到端运行。
- Kubernetes 或其他部署目标的发布适配。

上述未包含内容保留在整体设计中，不能以本阶段测试通过替代整体方案验收。

## 全局约束

- 项目目标框架使用 `net10.0`。
- 完整编排阶段的镜像固定为 `ghcr.io/openbao/openbao:2.7.2`。
- 默认模式使用 PostgreSQL 持久化；显式开发模式使用内存，两者共用同一种资源类型。
- 不修改现有 Compose 配置、Raft 数据或已有控制台行为。
- Token 使用机密参数，不在代码、普通日志或发布配置中固化真实值。
- 连接字符串使用 `Address` 和 `Token` 字段；资源名为 `openbao` 时，默认注入 `ConnectionStrings__openbao`。
- `WithReference` 不代替 `WaitFor`，也不初始化、解封或写入 Secret。
- 不安装依赖、不运行容器、不编写产品代码，直到用户确认开始执行。

## 重点审查

1. Token 包含分号、等号、单双引号或前后空格时，必须可无损回读，不能形成额外字段。
2. 端点尚未分配时，构造资源或读取表达式不得提前解析地址、访问参数值或连接服务。
3. 非机密参数或空参数传入时必须明确报错，不能把 Token 当成普通配置传播。
4. 下游资源只调用 `WithReference` 即可取得连接字符串，不应依赖额外手动注入环境变量。
5. 不同资源名及端点分配必须各自独立，不能硬编码 `localhost`、宿主机端口或连接字符串键名。

## 前置技术门禁：延迟求值与字段转义

已核对 Aspire `v13.4.6` 的官方实现：

- `IResourceWithConnectionString.ConnectionStringExpression` 是 `ReferenceExpression`。
- Aspire 通过该表达式传播引用，并支持带上下文的运行时求值。
- 该版本的 `FormattingHelpers.FormatValue` 仅支持 `uri` 格式。

因此，不能假设存在标准连接字符串字段格式化器，不能简单使用
`Address={endpoint};Token={token}` 支持任意 Token，也不能把 URI 编码当成标准字段转义。

执行时先验证以下组合是否成立，再进入资源类的最终实现：

- 延迟值提供器在求值时使用 `DbConnectionStringBuilder` 生成连接字符串。
- 使用当前消费者的求值上下文解析端点，保留宿主机与容器的地址解析能力。
- 引用遍历仍能发现 HTTP 端点和原始机密参数。
- 表达式检查及发布路径不包含真实 Token，不伪造 Aspire 不支持的表达式语法。

**停止条件：** 如果无法同时满足上述四项，停止本阶段实现，提交具体失败测试及原因，
由用户决定调整连接字符串格式或资源契约。不得静默删减特殊字符支持、提前求值、
引入明文 Token 副本，或声称资源已经完全兼容标准引用。

这是一项执行门禁，不是已经验证通过的实现结论。

## 文件与职责

| 路径 | 职责 |
|---|---|
| `src/OpenBao.AppHost/OpenBao.AppHost.csproj` | AppHost 项目及 Aspire 依赖 |
| `src/OpenBao.AppHost/Program.cs` | 最小 AppHost 入口，本阶段不自动启动 OpenBao |
| `src/OpenBao.AppHost/Hosting/OpenBaoResource.cs` | 资源类型、端点、Token 参数和连接字符串表达式 |
| `tests/OpenBao.AppHost.Tests/OpenBao.AppHost.Tests.csproj` | 不依赖 Docker 的测试项目 |
| `tests/OpenBao.AppHost.Tests/OpenBaoResourceTests.cs` | 资源类型、参数校验、引用及求值测试 |
| `tests/OpenBao.AppHost.Tests/OpenBaoReferenceTests.cs` | 标准引用的环境变量注入测试 |
| `src/ConsoleAppTestsVaultSharp/ConsoleAppTestsVaultSharp.slnx` | 纳入 AppHost 和测试项目，保留现有项目 |
| `README.md` | 中文资源契约说明及阶段边界 |

如技术门禁证明必须增加延迟值提供器，将其作为资源实现细节放在同一文件中；
不为其他服务抽象通用框架。

## 任务一：验证并实现资源类

**对外接口：**

- 命名空间：`Aspire.Hosting.ApplicationModel`。
- 类型：`public sealed class OpenBaoResource : ContainerResource, IResourceWithConnectionString`。
- 构造函数：`OpenBaoResource(string name, ParameterResource token)`。
- 属性：`public EndpointReference PrimaryEndpoint { get; }`，绑定名称为 `http` 的端点。
- 属性：`public ParameterResource TokenParameter { get; }`，保留传入的原始参数。
- 属性：`public ReferenceExpression ConnectionStringExpression { get; }`，暴露延迟连接配置。
- 保持默认连接字符串环境变量命名，不覆盖为固定键名。

- [ ] 创建 AppHost 和测试项目配置，将它们加入现有解决方案。
  AppHost 使用 `Aspire.AppHost.Sdk/13.4.6`；测试使用 `Microsoft.NET.Test.Sdk`、
  `xunit`、`xunit.runner.visualstudio`，显式设置为 VSTest 测试项目。
  测试不启动 AppHost 入口或 Docker，不将测试项目注册为分布式应用资源。
- [ ] 编写失败测试：`ImplementsContainerAndConnectionStringContracts` 断言资源满足两个类型契约；
  `PreservesSecretParameterAndHttpEndpoint` 断言参数引用不变，端点归属及名称正确；
  `RejectsNullOrNonSecretTokenParameter` 断言空参数和非机密参数产生明确异常。
- [ ] 编写失败测试：`ConstructionAndExpressionCreationDoNotResolveValues`，
  使用一旦被访问就抛出异常的参数值提供器，断言构造和创建表达式均不触发该提供器；
  `RetainsEndpointAndSecretReferences` 断言表达式引用图保留端点和原始参数。
- [ ] 编写参数化测试 `ConnectionStringRoundTripsToken`，使用合成测试值：
  `demo-token`、`a;b=c`、`a"b'c`、` leading and trailing `；
  以 `DbConnectionStringBuilder` 解析求值结果，断言恰有 `Address`、`Token` 两个字段，
  且 Token 与原始测试值完全一致。
- [ ] 编写 `ResolvesEndpointUsingConsumerContext`，通过 Aspire 测试 API 分别模拟宿主机消费者
  和容器消费者，断言 Address 使用相应上下文的已分配端点，而非共享硬编码地址。
- [ ] 执行红灯验证：
  `dotnet test .\tests\OpenBao.AppHost.Tests\OpenBao.AppHost.Tests.csproj --filter FullyQualifiedName~OpenBaoResourceTests`。
  必须先观察到资源类型缺失或所需行为缺失导致的失败。
- [ ] 按前置技术门禁完成延迟求值与转义验证。门禁失败则停止，不继续编写完整编排。
- [ ] 实现上述资源类接口。构造函数只校验参数并保存引用；
  由编排扩展负责添加端点注解，资源类不隐式启动容器或分配宿主机端口。
- [ ] 重跑相同测试命令，确认全部通过且测试数量非零。门禁测试也必须保持通过。

## 任务二：验证标准 WithReference 集成

**依赖：** 任务一通过全部测试及技术门禁。

**使用接口：** `OpenBaoResource(string name, ParameterResource token)`；
`builder.AddResource(resource)` 返回 `IResourceBuilder<OpenBaoResource>`。

本阶段直接使用标准资源构建 API 组成测试模型，不提前实现完整双模式 `AddOpenBao`。

- [ ] 编写失败测试 `WithReferenceInjectsDefaultConnectionStringKey`：
  构造 `openbao` 资源、机密参数、HTTP 端点及消费者，消费者仅调用 `WithReference(openbao)`；
  用 Aspire 的环境变量求值测试 API 断言生成 `ConnectionStrings__openbao`。
- [ ] 编写 `CustomResourceNamesProduceIndependentConnectionKeys`：
  使用 `bao-one`、`bao-two` 两个资源，断言各自连接配置不串用 Token 或端点，
  环境变量键遵循 Aspire 对资源名的默认规则。
- [ ] 编写 `WithReferenceDoesNotAddReadinessWait` 与 `WaitForAddsReadinessDependency`：
  断言引用注解不冒充就绪依赖，显式调用 `WaitFor` 后才存在相应等待关系。
- [ ] 编写 `ModelExpressionsDoNotContainResolvedToken`：
  使用合成 Token，断言模型中的表达式保持机密参数引用，不包含其已解析值；
  如发布路径不受支持，必须明确报错，不能输出错误或不安全的清单。
- [ ] 执行：
  `dotnet test .\tests\OpenBao.AppHost.Tests\OpenBao.AppHost.Tests.csproj --filter FullyQualifiedName~OpenBaoReferenceTests`。
  先确认失败原因，再对任务一的资源实现作必要且局部的修正，最终全部通过。

## 任务三：文档与阶段验收

**依赖：** 任务一、任务二通过。

- [ ] 在 README 中以中文说明资源类型、构造函数、机密参数、`http` 端点、
  `WithReference` 注入键及 `WaitFor` 职责。
- [ ] 提供基于 `AddResource(new OpenBaoResource(...))` 的资源模型示例，
  明确这是契约示例，不宣称已经实现 PostgreSQL、双模式启动或 Secret 初始化。
- [ ] 运行：
  `dotnet build .\src\ConsoleAppTestsVaultSharp\ConsoleAppTestsVaultSharp.slnx -c Release`。
  预期构建成功，没有本阶段引入的编译警告。
- [ ] 运行：
  `dotnet test .\tests\OpenBao.AppHost.Tests\OpenBao.AppHost.Tests.csproj -c Release`。
  预期全部测试通过且执行数量非零。
- [ ] 向用户报告已实现接口、实际验证结果、技术门禁结果及尚未实现的整体设计内容。
  不把资源模型测试报告为 OpenBao 运行或 PostgreSQL 持久性验证。
- [ ] 本阶段不自动提交代码；提交或进入完整编排实施均等待用户指示。

## 执行方式与审批

推荐由当前会话直接实施：三个任务紧密依赖同一资源契约，拆分子代理会增加接口协调成本。

当前只保存计划，不开始执行。用户审阅后可选择：

1. 由当前会话按计划逐项实施。
2. 使用子代理逐任务实施和审查。
3. 调整范围或先解决前置技术门禁。

## 核对资料

- [Aspire 13.4.6 连接字符串接口](https://github.com/dotnet/aspire/blob/v13.4.6/src/Aspire.Hosting/ApplicationModel/IResourceWithConnectionString.cs)
- [Aspire 13.4.6 表达式格式化实现](https://github.com/dotnet/aspire/blob/v13.4.6/src/Aspire.Hosting/Utils/FormattingHelpers.cs)
- [Aspire 13.4.6 Redis 资源实现](https://github.com/dotnet/aspire/blob/v13.4.6/src/Aspire.Hosting.Redis/RedisResource.cs)
