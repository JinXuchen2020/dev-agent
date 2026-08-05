@e2e
Feature: 知识库（RAG）管理界面
  As a 租户管理员
  I want 创建知识库并上传文档
  So that 知识可被检索增强

  Scenario: 创建知识库成功
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/knowledge-bases"
    And 我点击新建知识库
    And 我在知识库表单填写名称 "E2E 测试知识库"
    And 我点击按钮 "新建"
    Then 知识库创建成功

  Scenario: 打开知识库详情并上传文档入库
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/knowledge-bases"
    And 我点击新建知识库
    And 我在知识库表单填写名称 "E2E 上传知识库"
    And 我点击按钮 "新建"
    Then 知识库创建成功
    When 我打开知识库 "E2E 上传知识库"
    And 我上传文档 "e2e-kb-doc.txt"
    Then 文档入库成功
