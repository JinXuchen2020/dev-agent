Feature: 智能体管理
  覆盖智能体创建、列表、详情 404、删除 404、RBAC（仅 Admin 可写/删）与租户隔离（设计文档 B8）。

  Scenario: 未认证用户不能列出智能体
    Given 匿名发送 GET 请求到 "/api/v1/agents"
    Then 响应状态码为 401

  Scenario: Admin 可列出智能体
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份发送 GET 请求到 "/api/v1/agents"
    Then 响应状态码为 200

  Scenario: 非 Admin 成员（development 角色）不能列出智能体
    Given 以 T1 非 Admin 成员身份已登录
    And 以 成员 身份列出智能体
    Then 响应状态码为 403

  Scenario: Admin 可以创建智能体并拿到 id
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建智能体 "BDD Agent"
    Then 响应状态码为 200
    And 响应 JSON 含属性 "id"

  Scenario: 非 Admin 成员不能创建智能体
    Given 以 T1 非 Admin 成员身份已登录
    And 以 成员 身份创建智能体
    Then 响应状态码为 403

  Scenario: 获取不存在的智能体返回 404
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份获取不存在的智能体
    Then 响应状态码为 404

  Scenario: 删除不存在的智能体返回 404
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份删除不存在的智能体
    Then 响应状态码为 404

  Scenario: 智能体按租户隔离（T2 列表不含 T1 智能体）
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建智能体 "BDD Agent"
    And 以 T2 用户身份列出智能体
    Then 响应状态码为 200
    And T2 智能体列表不含该智能体 id
