Feature: 研究助手
  覆盖研究端点的认证、SSE 流返回、以及必填字段校验（设计文档 B6）。

  Scenario: 未认证用户不能发起研究
    Given 匿名发送 POST 请求到 "/api/v1/research"
      """
      {"question":"人工智能的最新进展是什么？"}
      """
    Then 响应状态码为 401

  Scenario: Admin 发起研究返回 SSE 流（以 event: done 收尾）
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份发送 POST 请求到 "/api/v1/research"
      """
      {"question":"人工智能的最新进展是什么？"}
      """
    Then 响应状态码为 200
    And 响应体包含 "event: done"

  Scenario: 缺少 question 时返回 400
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份发送 POST 请求到 "/api/v1/research"
      """
      {}
      """
    Then 响应状态码为 400
