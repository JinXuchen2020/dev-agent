Feature: 智能体配置管理
  覆盖智能体配置创建、列表、详情 404、模板 RBAC 与 404（设计文档 B8）。

  Scenario: 非 Admin 成员不能创建配置
    Given 以 T1 非 Admin 成员身份已登录
    And 以 成员 身份创建智能体配置
    Then 响应状态码为 403

  Scenario: Admin 可以创建配置并拿到 id
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建智能体配置 "BDD Config"
    Then 响应状态码为 200
    And 响应 JSON 含属性 "id"

  Scenario: Admin 可以列出配置
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建智能体配置 "BDD Config"
    And 以 admin 身份列出智能体配置
    Then 响应状态码为 200

  Scenario: 获取不存在的配置返回 404
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份获取不存在的智能体配置
    Then 响应状态码为 404

  Scenario: 模板仅 Admin 可见（成员被拒）
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建智能体配置 "BDD Config"
    And 以 T1 非 Admin 成员身份已登录
    And 以 成员 身份获取配置模板
    Then 响应状态码为 403

  Scenario: Admin 获取不存在的配置模板返回 404
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份获取不存在的配置模板
    Then 响应状态码为 404
