@e2e
Feature: 搭建 Agent 团队与协商模式产品化（F8）
  # F8 是纯前端产品化：脚手架（Start→Architect→Developer→Critic→End）+ 编排模式选择器 + 协商模式可见指示。
  # 后端协商原语（NegotiationOrchestrator / CriticStepExecutor / DetectPreset）已就绪，不在本 E2E 断言运行终态——
  # 协商收敛依赖真实 LLM，CI 无 LLM 时跑不出 Completed，断言终态会把纯前端测试耦合到后端执行，属越界。
  # 本 E2E 仅验证前端交付：脚手架生成 Critic 节点、图含 Critic 时自动识别协商模式并渲染指示、保存后编辑页持久化。
  Scenario: 脚手架生成协商图、协商模式可见、保存持久化
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/workflows/new"
    And 我点击按钮 "搭建 Agent 团队"
    Then 画布含 Critic 评审节点
    And 画布显示协商模式指示
    And 我在名称框输入 "F8 Negotiation E2E"
    And 我点击按钮 "保存并运行"
    Then 画布含 Critic 评审节点
    And 画布显示协商模式指示
    And 没有意外的 JS 或 HTTP 错误发生
