@e2e
Feature: 分析看板界面
  As a 已登录用户
  I want 查看 KPI 与切换时间范围
  So that 掌握租户运行状况

  Scenario: 仪表盘渲染 KPI 卡片
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/"
    Then 页面显示标题 "仪表盘"
    And 页面显示 "活跃智能体"
    And 页面显示 "总执行数"

  Scenario: 切换时间范围控件可用
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/"
    And 我点击文本 "近 30 天"
    Then 页面显示 "活跃智能体"
