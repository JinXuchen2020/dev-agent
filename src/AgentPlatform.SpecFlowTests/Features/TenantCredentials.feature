Feature: 租户凭据与模型发现（F13 / F14，真 HTTP + 真 DB）
  作为平台运营方/租户 Admin，我需要为租户配置自有的模型与搜索凭据（BYO-Key），
  且密钥绝不以明文返回、严格按租户隔离；并能探测供应商模型清单。

  Scenario: Admin 获取模型类凭据列表（租户隔离，初始可能为空）
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份发送 GET 请求到 "/api/v1/tenant/credentials?category=0"
    Then 响应状态码为 200

  Scenario: Admin 新增模型凭据后密钥以掩码返回
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份新增一条模型凭据
    Then 响应状态码为 200
    And 返回的密钥为掩码形式

  Scenario: 租户隔离——T2 看不到 T1 创建的凭据
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份新增一条模型凭据
    Then T2 的模型凭据列表不含该凭据
    And T1 的模型凭据列表含该凭据

  Scenario: 探测模型清单——无效密钥返回 400 而非明文错误泄露
    Given 以集成租户 T1 admin 身份已登录
    When 以 admin 身份发送 POST 请求到 "/api/v1/tenant/credentials/discover-models"
      """
      {"provider":"OpenAI","apiKey":"invalid-key","baseUrl":"http://127.0.0.1:1/v1"}
      """
    Then 响应状态码为 400
