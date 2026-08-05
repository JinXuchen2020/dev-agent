@e2e
Feature: 工作流管理界面
  As a 租户用户
  I want 查看工作流列表并快速运行
  So that 工作流可被管理

  Scenario: 工作流列表页渲染且包含夹具工作流
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/workflows"
    Then 工作流列表渲染

  Scenario: 快速运行未命名工作流提示名称必填
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/workflows"
    And 我点击按钮 "快速运行"
    And 我点击按钮 "运行"
    Then 提示请输入工作流名称
