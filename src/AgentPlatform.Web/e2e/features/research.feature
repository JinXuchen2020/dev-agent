@e2e
Feature: 调研（Research）界面
  As a 已登录用户
  I want 输入问题并运行调研
  So that 获得结构化调研报告

  Scenario: 运行调研并生成报告
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/research"
    And 我在调研输入框输入 "2025 年大模型推理成本下降趋势及主要驱动因素"
    And 我点击开始调研
    Then 调研报告已生成
