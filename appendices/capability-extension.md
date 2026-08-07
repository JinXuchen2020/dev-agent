## 附录 F：能力扩展体系（Tool / Skill / MCP 三层架构）

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **背景**：Agent 除了调用大模型，还需要调用外部能力。本附录定义三层能力体系——Tool（原生函数）、Skill（SK Plugin 打包）、MCP（标准协议连外部），三者通过统一接口融入 DDD 架构，LLM 不感知能力来源差异。

### F.1 三个概念区分

```
┌──────────────────────────────────────────────────────────────────┐
│  Tool（工具）                                                      │
│  最细粒度的能力单元，单个函数                                       │
│  例：search_web(query)、run_code(code)、get_weather(city)          │
│  蓝图现状：已有 ToolDefinition 聚合根                              │
├──────────────────────────────────────────────────────────────────┤
│  Skill（技能）                                                     │
│  Tool 的"打包升级版"，一组相关工具 + Prompt 模板 + 配置            │
│  例：pdf-skill = {extract_text, merge_pdf, split_pdf} + 模板      │
│  SK 原生概念：Semantic Kernel 的 Plugin（前身就叫 Skill）          │
├──────────────────────────────────────────────────────────────────┤
│  MCP（Model Context Protocol）                                     │
│  Anthropic 推出的开放协议，标准化 Agent ↔ 外部世界的连接            │
│  类比：MCP 之于 AI = USB 之于电脑（即插即用标准）                  │
│  一个 MCP Server 可以同时提供：tools + resources + prompts         │
│  例：mcp-github = {search_repos, create_issue, read_file...}       │
└──────────────────────────────────────────────────────────────────┘
```

三者的层级关系：

```
        Agent
         │
         ├── 调用 LLM（已有：IModelClient）
         │
         ├── 调用 Tool（已有：ToolDefinition，单个函数）
         │
         ├── 调用 Skill（新增：SkillPackage，Tool 的打包）
         │       └── 内含多个 Tool + Prompt 模板
         │
         └── 调用 MCP（新增：McpServer，标准协议连接外部）
                 └── 内含 tools + resources + prompts
```

### F.2 为什么应该接入 Skill 和 MCP

```
只靠 Tool（现状）的问题：

❌ 每个 Tool 都要手写注册代码，数量一多就维护噩梦
❌ Tool 之间无法共享 Prompt 上下文
❌ 想接入 GitHub/Jira/Slack？每个都要自己写对接代码
❌ 别人写好的工具你用不了，没有标准协议

接入 Skill + MCP 后：

✅ Skill：一组相关工具打包，SK Plugin 原生支持，可复用
✅ MCP：标准协议，社区有几百个现成 MCP Server
        接入 GitHub？装个 mcp-github 就行，零开发成本
✅ 三者统一走 ToolCalling 管道，LLM 自动选择
```

**MCP 的杀手锏——生态复用：**

```
没有 MCP：
  想让 Agent 操作 GitHub → 自己写 GitHub API 封装
  想让 Agent 查数据库   → 自己写 SQL 执行器
  想让 Agent 读 Slack   → 自己写 Slack SDK 对接
  每接一个外部系统 = 一周开发量

有 MCP：
  想让 Agent 操作 GitHub → 安装 mcp-server-github，5 分钟
  想让 Agent 查数据库   → 安装 mcp-server-postgres，5 分钟
  想让 Agent 读 Slack   → 安装 mcp-server-slack，5 分钟
  社区现成的 MCP Server 数百个，即插即用
```

### F.3 架构融合：不破坏 DDD 分层

```
┌─────────────────────────────────────────────────────────────┐
│  Agent                                                       │
│  └── 持有可用的能力列表（Tools + Skills + McpServers）        │
└──────────────────────────┬──────────────────────────────────┘
                           │ LLM 决定调用某个能力
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  ToolCallingDispatcher（统一调度器）                          │
│  根据 ToolSource 类型路由到对应执行器                          │
└──────┬──────────────┬──────────────────┬────────────────────┘
       │              │                  │
       ▼              ▼                  ▼
  NativeTool     SkillPackage        McpClient
  (本地函数)     (SK Plugin)        (MCP 协议)
       │              │                  │
       ▼              ▼                  ▼
  C# 代码执行     SK KernelFunction   JSON-RPC over
  进程内          进程内              stdio/HTTP/SSE
                                          │
                                          ▼
                                     外部 MCP Server
                                     (GitHub/DB/Slack...)
```

### F.4 统一调度接口设计

```csharp
// Application/Abstractions/IToolExecutor.cs
public interface IToolExecutor
{
    ToolSource Source { get; }  // 这个执行器处理哪种来源

    Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct = default);
}

public record ToolInvocation(
    Guid AgentId,
    string ToolName,
    string ArgumentsJson,         // JSON 格式的参数
    string? PluginName = null,    // Skill 的 Plugin 名（仅 SkillPackage）
    string? EndpointUrl = null,   // MCP Server 地址（仅 McpServer）
    IReadOnlyDictionary<string, object>? Context = null);

public record ToolResult(
    bool Success,
    string OutputJson,            // JSON 格式的结果
    string? ErrorMessage = null);

// Application/Abstractions/IToolRegistry.cs
// 聚合三种来源的能力清单，供 Agent 调用 LLM 时附带
public interface IToolRegistry
{
    /// <summary>获取 Agent 可用的所有能力（Tool + Skill + MCP 展开）</summary>
    Task<IReadOnlyList<ToolDefinition>> GetAvailableCapabilitiesAsync(
        IReadOnlyList<ToolDefinition> nativeTools,
        IReadOnlyList<string> skillPackageNames,
        IReadOnlyList<string> mcpServerNames,
        CancellationToken ct = default);
}
```

### F.5 三个执行器实现（基础设施层）

> 注：以下为早期进程内 handler 设计示意。F5（2026-07-24）已将 `NativeToolExecutor` 实现为对 `ToolDefinition.EndpointUrl` 的**真实 HTTP 调用**（`IHttpClientFactory` + 方法解析 + 真实成功/失败/超时回打）；F10（2026-08-06）已将 `SkillPackageExecutor`（Semantic Kernel 真实调用 Plugin 函数）与 `McpClient`（ModelContextProtocol 2.1.0 真实连接/列举/调用）真实化，三者均经 `ToolCallingDispatcher` 按 `ToolSource` 分派；F11（2026-08-07）为执行后端新增 OS 级隔离层 `ISandboxIsolation`（Windows JobObject 资源限额 + AppContainer 真实禁网，fail-safe 回退）；**F34（2026-08-07）双层收敛**：`Provider=Docker` 且守护进程可用 → `DockerSandboxIsolation`（复用 F9 `DockerCodeSandbox` 容器强隔离，`NetworkMode=none` + 内存限额 + 只读代码挂载，结果标 `IsolationStrength.Strong`）经 `ISandboxIsolation` 接入唯一入口 `ProcessCodeSandbox`；Docker 不可用透明回退 F11 进程级（Weak）/非 Windows（None），`SandboxResult.IsolationStrength` 回传强度供观测；`IDockerProbe` 单例一次性探测守护进程可用性（fail-safe）。详见 `src/AgentPlatform.Infrastructure/Tools/*` 与 `src/AgentPlatform.Infrastructure/Sandbox/*`。

#### NativeToolExecutor —— 原生 C# 函数（早期示意）

```csharp
// Infrastructure/Tools/NativeToolExecutor.cs
// 处理 Source = NativeTool 的调用，进程内执行 C# 函数
public class NativeToolExecutor : IToolExecutor
{
    public ToolSource Source => ToolSource.NativeTool;

    private readonly Dictionary<string, Func<string, CancellationToken, Task<string>>> _handlers;

    public NativeToolExecutor(IServiceProvider sp)
    {
        // 通过 DI 注册的 handler（如 run_code、search_web）
        _handlers = new()
        {
            ["run_code"]   = (args, ct) => ExecuteCodeAsync(args, ct),
            ["search_web"] = (args, ct) => SearchWebAsync(args, ct),
        };
    }

    public async Task<ToolResult> ExecuteAsync(ToolInvocation inv, CancellationToken ct)
    {
        var handler = _handlers[inv.ToolName];
        var output = await handler(inv.ArgumentsJson, ct);
        return new ToolResult(true, output);
    }
}
```

#### SkillPackageExecutor —— Semantic Kernel Plugin

```csharp
// Infrastructure/Tools/SkillPackageExecutor.cs
// 处理 Source = SkillPackage 的调用，委托给 SK Plugin
public class SkillPackageExecutor : IToolExecutor
{
    public ToolSource Source => ToolSource.SkillPackage;

    private readonly Kernel _kernel;   // SK 内核

    public SkillPackageExecutor(Kernel kernel) => _kernel = kernel;

    public async Task<ToolResult> ExecuteAsync(ToolInvocation inv, CancellationToken ct)
    {
        // SK Plugin 的函数调用（PluginName.FunctionName）
        var function = _kernel.Plugins.GetFunction(inv.PluginName!, inv.ToolName);
        var arguments = KernelArguments.FromJson(inv.ArgumentsJson);
        var result = await _kernel.InvokeAsync(function, arguments, ct);
        return new ToolResult(true, result.GetValue<string>()!);
    }
}
```

#### McpClient —— MCP 协议外部调用

```csharp
// Infrastructure/Tools/McpClient.cs
// 处理 Source = McpServer 的调用，通过 MCP 协议连接外部服务
public class McpClient : IToolExecutor
{
    public ToolSource Source => ToolSource.McpServer;

    public async Task<ToolResult> ExecuteAsync(ToolInvocation inv, CancellationToken ct)
    {
        // MCP 协议调用：JSON-RPC over stdio/HTTP
        var request = new McpToolCallRequest(inv.ToolName, inv.ArgumentsJson);
        var response = await _mcpServer.CallToolAsync(inv.EndpointUrl!, request, ct);
        return new ToolResult(response.IsSuccess, response.Content, response.Error);
    }
}
```

### F.6 统一调度器（路由到对应执行器）

```csharp
// Application/Tools/ToolCallingDispatcher.cs
public class ToolCallingDispatcher
{
    private readonly Dictionary<ToolSource, IToolExecutor> _executors;
    private readonly IToolDefinitionRepository _toolRepo;
    private readonly IAuditLogService _auditLog;

    public ToolCallingDispatcher(
        IEnumerable<IToolExecutor> executors,
        IToolDefinitionRepository toolRepo,
        IAuditLogService auditLog)
    {
        _executors = executors.ToDictionary(e => e.Source);
        _toolRepo = toolRepo;
        _auditLog = auditLog;
    }

    public async Task<ToolResult> DispatchAsync(ToolInvocation inv, CancellationToken ct)
    {
        // 1. 查 ToolDefinition 获取能力来源
        var toolDef = await _toolRepo.GetByNameAsync(inv.ToolName, ct);
        var executor = _executors[toolDef.Source];    // 自动路由到 Native/Skill/MCP 执行器

        // 2. 执行（带超时、审计、错误处理）
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));    // 工具调用硬超时

        var result = await executor.ExecuteAsync(inv, cts.Token);

        // 3. 审计日志（第九章安全要求）
        await _auditLog.RecordToolCallAsync(inv.AgentId, inv.ToolName, result);

        return result;
    }
}
```

### F.7 MCP Server 配置与接入

MCP Server 通过 Yaml 配置，零代码接入：

```yaml
# config/mcp-servers.yaml
mcpServers:
  - name: "github"
    transport: "stdio"                    # stdio / http / sse
    command: "npx"
    args: ["-y", "@modelcontextprotocol/server-github"]
    env:
      GITHUB_TOKEN: "${GITHUB_TOKEN}"

  - name: "postgres"
    transport: "http"
    url: "http://mcp-db:3000"
    env:
      DATABASE_URL: "${DB_CONNECTION}"

  - name: "filesystem"
    transport: "stdio"
    command: "npx"
    args: ["-y", "@modelcontextprotocol/server-filesystem", "/data/docs"]
```

```csharp
// Api/Program.cs — 启动时注册所有 MCP Server
builder.Services.AddMcpServers(builder.Configuration.GetSection("mcpServers"));
// 每个 MCP Server 会被发现，其 tools 自动注册为 ToolDefinition（Source = McpServer）
```

### F.8 完整的 Tool Calling 流程（含 Skill + MCP）

```
┌──────────────────────────────────────────────────────────────────┐
│  Agent 调用 LLM（通过 IModelClient）                                │
│  附带可用能力清单（从 Tools + Skills + McpServers 聚合生成）         │
│                                                                   │
│  可用能力示例：                                                    │
│  ┌─────────────────────────────────────────────────────┐         │
│  │  [Native] run_code    - 执行代码                      │         │
│  │  [Native] search_web  - 网页搜索                      │         │
│  │  [Skill]  pdf.extract - 提取 PDF 文本（来自 pdf-skill）│        │
│  │  [Skill]  pdf.merge   - 合并 PDF                     │         │
│  │  [MCP]    github.create_issue - 创建 GitHub Issue     │        │
│  │  [MCP]    postgres.query - 查询数据库                  │         │
│  └─────────────────────────────────────────────────────┘         │
└──────────────────────────┬──────────────────────────────────────┘
                           │ LLM 返回 tool_call 决策
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│  ToolCallingDispatcher                                            │
│  1. 查 ToolDefinition → Source = "McpServer"                     │
│  2. 路由到 McpClient                                              │
│  3. 30 秒硬超时 + 审计日志                                        │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│  对应执行器执行                                                    │
│  ├── NativeToolExecutor   → 进程内 C# 函数                        │
│  ├── SkillPackageExecutor → SK KernelFunction                     │
│  └── McpClient            → JSON-RPC → 外部 MCP Server            │
└──────────────────────────┬──────────────────────────────────────┘
                           │ 结果返回
                           ▼
                    结果送回 LLM 继续推理
```

### F.9 与现有架构的一致性

| 现有组件 | 扩展点 | 是否破坏现有设计 |
| :--- | :--- | :---: |
| `ToolDefinition` 聚合根 | 新增 `Source` / `EndpointUrl` / `SkillPluginName` 字段 | ❌ 仅追加字段 |
| `Agent` 聚合根 | 新增 `SkillPackages` / `McpServers` 列表 | ❌ 仅追加字段 |
| `IToolExecutor` 接口 | 新增抽象（Application 层） | ❌ 新增，不改旧的 |
| `ToolCallingDispatcher` | 新增调度器（Application 层） | ❌ 新增，不改旧的 |
| 三个执行器实现 | 新增（Infrastructure 层） | ❌ 新增，不改旧的 |
| DDD 分层依赖方向 | 依然向内 | ❌ 完全遵守 |
| MediatR 领域事件 | 工具调用后发 `ToolCallCompleted` 事件 | ❌ 沿用现有模式 |
| 审计日志 | 工具调用记录到 `AuditLog`（ActionType = ToolCall） | ❌ 沿用第九章设计 |

> **一句话总结**：Agent 完全可以调用 Skill 和 MCP。Tool（原生函数）、Skill（SK Plugin 打包）、MCP（标准协议连外部）三者通过统一的 `IToolExecutor` 接口和 `ToolSource` 枚举融入现有 DDD 架构——LLM 不关心能力来源，`ToolCallingDispatcher` 自动路由到对应执行器。MCP 的核心价值是生态复用：社区数百个现成 MCP Server（GitHub / 数据库 / Slack），装一个就能用，省去自己对接每个外部系统的开发量。
