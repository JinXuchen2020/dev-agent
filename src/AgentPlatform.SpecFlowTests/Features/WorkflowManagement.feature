Feature: 工作流管理（CRUD / 运行 / 版本 / 导入 / 租户隔离）
  作为平台 Admin/Operator，我需要创建工作流、查看、更新、版本化、运行与导入导出，
  且工作流严格按租户隔离；越权操作被拒。
  覆盖端点：GET /workflows、GET /workflows/{id}、POST /workflows（创建并运行）、
  POST /workflows/import、PUT /workflows/{id}、POST /workflows/{id}/versions、
  GET /workflows/{id}/versions、POST /workflows/{id}/run。

  Scenario: 未认证访问工作流列表返回 401
    When 匿名发送 GET 请求到 "/api/v1/workflows"
    Then 响应状态码为 401

  Scenario: Admin 列出工作流返回 200
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份发送 GET 请求到 "/api/v1/workflows"
    Then 响应状态码为 200

  Scenario: Admin 按 id 获取种子工作流
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份发送 GET 请求到 "/api/v1/workflows/44444444-4444-4444-4444-444444444444"
    Then 响应状态码为 200
    And 响应 JSON 属性 "id" 等于 "44444444-4444-4444-4444-444444444444"

  Scenario: Admin 获取不存在的工作流返回 404
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份发送 GET 请求到 "/api/v1/workflows/99999999-9999-9999-9999-999999999999"
    Then 响应状态码为 404

  Scenario: 未认证获取工作流返回 401
    When 匿名发送 GET 请求到 "/api/v1/workflows/99999999-9999-9999-9999-999999999999"
    Then 响应状态码为 401

  Scenario: 非 Admin 成员创建工作流返回 403
    Given 以 T1 非 Admin 成员身份已登录
    When 以 成员 身份发送 POST 请求到 "/api/v1/workflows"
      """
      {"name":"x","initialContext":"{}"}
      """
    Then 响应状态码为 403

  Scenario: Admin 创建并运行工作流返回 200
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份发送 POST 请求到 "/api/v1/workflows"
      """
      {"name":"BDD Run Workflow","initialContext":"{}"}
      """
    Then 响应状态码为 200
    And 响应 JSON 含属性 "id"

  Scenario: Admin 导入工作流并可在租户内读取
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份导入一条工作流
    Then 响应状态码为 200
    When 以 admin 身份获取导入的工作流
    Then 响应状态码为 200

  Scenario: 租户隔离——T2 无法读取 T1 导入的工作流
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份导入一条工作流
    When 以 T2 用户身份获取导入的工作流
    Then 响应状态码为 404

  Scenario: 非 Admin 成员更新工作流返回 403
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份导入一条工作流
    When 以 成员 身份更新导入的工作流
    Then 响应状态码为 403

  Scenario: Admin 空更新工作流返回 400
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份导入一条工作流
    When 以 admin 身份空更新导入的工作流
    Then 响应状态码为 400

  Scenario: Admin 更新工作流名称返回 200
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份导入一条工作流
    When 以 admin 身份用新名称更新导入的工作流
    Then 响应状态码为 200

  Scenario: Admin 为工作流创建版本并返回版本列表 200
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份导入一条工作流
    When 以 admin 身份为导入的工作流创建版本
    Then 响应状态码为 200
    When 以 admin 身份列出导入的工作流的版本
    Then 响应状态码为 200

  Scenario: Admin 运行现有工作流返回 200
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份导入一条工作流
    When 以 admin 身份运行导入的工作流
    Then 响应状态码为 200
