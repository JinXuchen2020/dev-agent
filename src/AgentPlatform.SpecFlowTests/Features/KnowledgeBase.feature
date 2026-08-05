Feature: 知识库管理
  覆盖知识库创建、列表、详情与删除的 404、文档上传（multipart 入库）以及租户隔离，
  验证认证与跨租户数据不可见（设计文档 B5）。

  Scenario: 未认证用户不能列出知识库
    Given 匿名发送 GET 请求到 "/api/v1/knowledge-bases"
    Then 响应状态码为 401

  Scenario: Admin 可以创建知识库并拿到 id
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建知识库 "BDD KB"
    Then 响应状态码为 200
    And 响应 JSON 含属性 "id"

  Scenario: Admin 可以列出知识库
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建知识库 "BDD KB"
    And 以 admin 身份列出知识库
    Then 响应状态码为 200

  Scenario: 获取不存在的知识库返回 404
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份获取不存在的知识库
    Then 响应状态码为 404

  Scenario: 删除不存在的知识库返回 404
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份删除不存在的知识库
    Then 响应状态码为 404

  Scenario: Admin 可向知识库上传文档并入库
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建知识库 "BDD KB"
    And 以 admin 身份向该知识库上传文档
    Then 响应状态码为 200

  Scenario: 知识库按租户隔离（T2 列表不含 T1 库）
    Given 以集成租户 T1 admin 身份已登录
    And 以 admin 身份创建知识库 "BDD KB"
    And 以 T2 用户身份列出知识库
    Then 响应状态码为 200
    And T2 列表不含该知识库 id
