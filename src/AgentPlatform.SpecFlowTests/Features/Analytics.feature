Feature: 运营分析
  覆盖分析看板摘要端点的认证、合法返回与日期范围校验（设计文档 B7）。

  Scenario: 未认证用户不能访问分析摘要
    Given 匿名发送 GET 请求到 "/api/v1/analytics/summary"
    Then 响应状态码为 401

  Scenario: Admin 可获取分析摘要
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份发送 GET 请求到 "/api/v1/analytics/summary"
    Then 响应状态码为 200
    And 响应 JSON 含属性 "kpis"

  Scenario: 起始日期晚于结束日期返回 400
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份发送 GET 请求到 "/api/v1/analytics/summary?from=2026-01-10&to=2026-01-01"
    Then 响应状态码为 400

  Scenario: 日期跨度超过 366 天返回 400
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份发送 GET 请求到 "/api/v1/analytics/summary?from=2024-01-01&to=2026-01-01"
    Then 响应状态码为 400
