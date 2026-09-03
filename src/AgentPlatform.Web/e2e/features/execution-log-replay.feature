@e2e
Feature: 执行日志回放诊断（F40）
  As a tenant admin
  I want to open replay diagnostics for a failed execution
  So that I can see the rebuilt failure path and its disclosed data gaps without re-running anything

  Background:
    Given 集成后端可达且我已以 admin 登录

  Scenario: 失败执行的回放诊断
    When 我打开失败执行日志的详情页
    Then 执行日志详情显示步骤明细标签
    When 我切到回放诊断标签
    Then 回放诊断显示执行路径时间线
    And 回放诊断标注失败节点
    And 回放诊断披露数据缺口
    When 我展开回放路径中的第一个节点
    Then 节点详情显示输入输出与错误栏
    Then no unexpected HTTP or JS errors occurred during the flow
