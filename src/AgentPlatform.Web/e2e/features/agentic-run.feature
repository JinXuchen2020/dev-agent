@e2e
Feature: 自主智能体运行（F29）
  As a 租户管理员
  I want 对智能体发起自主运行
  So that agentic 控制循环可经 UI + API 端到端验证

  Scenario: 管理员可创建带工具白名单的智能体并发起自主运行
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/agents"
    And 我点击按钮 "新建智能体"
    And 我在智能体表单填写名称 "F29 自主运行智能体"
    And 我在智能体表单勾选允许工具 "read_file"
    And 我点击按钮 "保存"
    Then 智能体创建成功
    And 页面出现智能体 "F29 自主运行智能体"
    When 我点击智能体 "F29 自主运行智能体" 的运行按钮
    And 我在运行弹窗输入目标 "用一句话总结平台能力"
    And 我点击按钮 "开始运行"
    Then 运行弹窗显示最终回答
