Feature: 认证与 RBAC（Auth + 角色鉴权）
  作为平台用户，我需要正确的登录鉴权与基于角色的访问控制，
  确保未认证请求被拒、越权请求被拒、合法用户可获取身份。

  Scenario: 正确凭据登录成功并返回身份信息
    When 匿名发送 POST 请求到 "/api/v1/auth/login"
      """
      {"email":"admin@acme.io","password":"Admin@123456"}
      """
    Then 响应状态码为 200
    And 响应 JSON 含属性 "user"
    And 响应 JSON 属性 "user.email" 等于 "admin@acme.io"

  Scenario: 错误密码登录被拒
    When 匿名发送 POST 请求到 "/api/v1/auth/login"
      """
      {"email":"admin@acme.io","password":"wrong-password"}
      """
    Then 响应状态码为 401

  Scenario: 未认证访问受保护端点返回 401
    When 匿名发送 GET 请求到 "/api/v1/auth/me"
    Then 响应状态码为 401

  Scenario: 合法令牌可获取当前用户身份
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份发送 GET 请求到 "/api/v1/auth/me"
    Then 响应状态码为 200
    And 响应 JSON 属性 "email" 等于 "admin@acme.io"
    And 响应 JSON 属性 "role" 等于 "Admin"

  Scenario: 未认证访问 Agent 列表返回 401
    When 匿名发送 GET 请求到 "/api/v1/agents"
    Then 响应状态码为 401

  Scenario: 非 Admin 成员访问 Admin 端点返回 403
    Given 以 T1 非 Admin 成员身份已登录
    When 以 成员 身份发送 GET 请求到 "/api/v1/tenant/credentials"
    Then 响应状态码为 403

  Scenario: Admin 可访问租户凭据端点
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份发送 GET 请求到 "/api/v1/tenant/credentials"
    Then 响应状态码为 200
