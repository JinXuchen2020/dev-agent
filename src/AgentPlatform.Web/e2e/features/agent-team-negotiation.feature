@e2e
Feature: 搭建 Agent 团队与协商模式产品化（F8）
  As a 已登录用户
  I want 一键生成协商式多智能体图并显式选择编排模式
  So that 快速获得 Negotiation + Critic 差异化能力

  # 注：新建工作流首次「保存并运行」走线性创建并跳转编辑页（id 生成）；
  # 第二次「保存并运行」在已有工作流上走 runExistingWorkflow(id, preset)，
  # 协商式 DAG 同步跑完，画布节点带 Completed 终态——这是断言 Completed 的关键路径。
  Scenario: 脚手架生成协商图、协商模式可见、保存运行达终态
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/workflows/new"
    And 我点击按钮 "搭建 Agent 团队"
    Then 画布含 Critic 评审节点
    And 画布显示协商模式指示
    And 我在名称框输入 "F8 Negotiation E2E"
    And 我点击按钮 "保存并运行"
    Then 画布含 Critic 评审节点
    And 我点击按钮 "保存并运行"
    Then 工作流达终态 Completed
    And 没有意外的 JS 或 HTTP 错误发生
