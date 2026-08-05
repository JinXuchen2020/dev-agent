Feature: 发布工作流为 API / MCP Server（F22，真 HTTP + 真 DB）

  后端 BDD 集成层：所有场景经真实 HttpClient 走完整 ASP.NET Core 管线
  （认证中间件 / 异常处理器 / MediatR + UoW / EF），连真实文件 SQLite 数据库，
  零 mock Repository、零 in-memory。对应设计文档 features/bdd-integration-design.md §4.3。

  Background:
    Given 集成租户 T1 下存在一个 Completed 状态的工作流 W1
    And 集成租户 T1 持有一个有效的 ApiKey

  Scenario: 发布为 API 模式并生成 slug
    When 发布 W1 为 API 模式
    Then 响应 200 且返回 16 位 URL 安全 slug
    And 查询 W1 发布状态为 Enabled

  Scenario: 用绑定 Key 经 slug 运行
    Given W1 已发布为 Api 模式并绑定 T1 Key
    When 带 ApiKey 调用 slug 运行并附输入
    Then 响应 200 且返回工作流最终输出

  Scenario: 错误 Key 被拒
    Given W1 已发布并绑定 T1 Key
    When 用 T2 的 Key 调用 slug 运行
    Then 响应 404

  Scenario: 跨租户不可运行他人发布
    Given 租户 T2 发布了 W2，Api 模式，T2 Key
    When 租户 T1 用自身 Key 调用 W2 的 slug
    Then 响应 404

  Scenario: MCP tools/list 仅暴露启用且 Mcp 模式的发布
    Given W1 发布为 Mcp 模式并启用
    And W3 发布为 Api 模式，应被列表排除
    When 带 ApiKey 发送 MCP tools list 请求
    Then tools 列表仅含 W1

  Scenario: 取消发布后 slug 不可用
    Given W1 已发布
    When 取消发布 W1
    Then 再调用 slug 端点返回 404
