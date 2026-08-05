@e2e
Feature: 创建智能体冒烟
  As a 租户管理员
  I want 创建智能体
  So that 智能体生命周期可用

  Scenario: 管理员可创建智能体并看见卡片
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/agents"
    And 我点击按钮 "新建智能体"
    And 我在智能体表单填写名称 "E2E 冒烟智能体"
    And 我点击按钮 "保存"
    Then 智能体创建成功
    And 页面出现智能体 "E2E 冒烟智能体"
