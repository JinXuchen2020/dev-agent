@e2e
Feature: 页面交互打磨（会话筛选 / 工作流快速运行 / 执行日志筛选）
  As a 已登录用户
  I want 关键筛选与表单校验可用
  So that 界面交互正确

  Scenario: 会话列表搜索与状态筛选控件渲染
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/conversations"
    Then 页面显示 "会话列表"
    And 状态筛选控件可见
    And 搜索框控件可见

  Scenario: 工作流快速运行未命名提示名称必填
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/workflows"
    And 我点击按钮 "快速运行"
    And 我点击按钮 "运行"
    Then 提示请输入工作流名称

  Scenario: 执行日志状态筛选控件渲染
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/execution-logs"
    Then 页面显示标题 "执行日志"
    And 执行日志状态筛选控件可见
