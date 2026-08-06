@e2e
Feature: 工作流用量与版本对比
  As a 已登录用户
  I want 查看每个工作流的用量指标并对比历史版本定义
  So that 掌握运行成本与变更轨迹

  Scenario: 用量页渲染标题与时间范围控件
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/usage"
    Then 页面显示标题 "工作流用量"
    And 页面显示 "近 30 天"
    And 没有意外的 JS 或 HTTP 错误发生

  Scenario: 工作流版本历史抽屉可打开（diff 入口）
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/workflows"
    And 我点击第一个工作流的 "版本历史" 按钮
    Then 版本抽屉显示 "存为版本"
