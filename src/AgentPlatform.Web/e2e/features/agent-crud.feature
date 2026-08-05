@e2e
Feature: 智能体生命周期界面
  As a 租户管理员
  I want 创建与删除智能体
  So that 智能体可被管理

  Scenario: 管理员可创建智能体并看见卡片
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/agents"
    And 我点击按钮 "新建智能体"
    And 我在智能体表单填写名称 "E2E 智能体 001"
    And 我点击按钮 "保存"
    Then 智能体创建成功
    And 页面出现智能体 "E2E 智能体 001"

  Scenario: 管理员可删除智能体
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/agents"
    And 我点击按钮 "新建智能体"
    And 我在智能体表单填写名称 "E2E 智能体 002"
    And 我点击按钮 "保存"
    Then 智能体创建成功
    When 我删除智能体 "E2E 智能体 002"
    Then 智能体已删除 "E2E 智能体 002"
