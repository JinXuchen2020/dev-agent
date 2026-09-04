Feature: 会话与聊天管理
  覆盖会话生命周期、消息发送、成本报告 RBAC、工作流绑定（Chat 触发器）等核心行为，
  验证认证、租户隔离与越权防护（设计文档 B4）。

  Scenario: 未认证用户不能列出会话
    Given 匿名发送 GET 请求到 "/api/v1/conversations"
    Then 响应状态码为 401

  Scenario: 非 Admin 成员不能创建会话（仅 Admin/Operator）
    Given 以 T1 非 Admin 成员身份已登录
    And 以 成员 身份创建会话
    Then 响应状态码为 403

  Scenario: Admin 可以创建会话并拿到 id
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建会话
    Then 响应状态码为 200
    And 响应 JSON 含属性 "id"

  Scenario: 任意已认证用户可列出会话
    Given 以 T1 非 Admin 成员身份已登录
    And 以 成员 身份列出会话
    Then 响应状态码为 200

  Scenario: 获取不存在的会话返回 404
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份获取不存在的会话
    Then 响应状态码为 404

  Scenario: Admin 创建会话后向其发送消息得到回复
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建会话
    And 向该会话发送消息 "你好，介绍一下自己"
    Then 响应状态码为 200
    And 响应 JSON 含属性 "reply"

  Scenario: 成本报告仅 Admin 可见（成员被拒）
    Given 以 T1 非 Admin 成员身份已登录
    And 以 成员 身份访问成本报告
    Then 响应状态码为 403

  Scenario: Admin 可访问成本报告
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份访问成本报告
    Then 响应状态码为 200

  Scenario: 会话可绑定租户内的种子工作流并列出
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建会话
    And 该会话绑定种子工作流
    Then 响应状态码为 200
    When 列出该会话的工作流绑定
    Then 响应状态码为 200
    And 响应体包含 "44444444-4444-4444-4444-444444444444"

  Scenario: 会话列表支持按归属 agent 过滤（F36 per-agent 对话隔离）
    Given 以集成租户 T1 admin 身份已登录
    When 列出会话并按 agent "33333333-3333-3333-3333-333333333301" 过滤
    Then 响应状态码为 200
    And 响应体包含 "55555555-5555-5555-5555-555555555501"
    When 列出会话并按 agent "00000000-0000-0000-0000-000000000099" 过滤
    Then 响应状态码为 200
    And 响应体不包含 "55555555-5555-5555-5555-555555555501"

  Scenario: 触发未绑定工作流返回 404
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建会话
    And 触发该会话未绑定的工作流
    Then 响应状态码为 404
