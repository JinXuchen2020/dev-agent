@e2e
Feature: 执行日志回放诊断（F40）
  As a tenant admin
  I want to open replay diagnostics for an execution log
  So that I can see the rebuilt node path, its verdict banner and disclosed data gaps
  without re-running anything from the UI

  Background:
    Given 集成后端可达且我已以 admin 登录

  # 数据由本场景自造（仅 Start→End 图，不触真实 LLM）：E2E 后端只有 DatabaseInitializer 的
  # Integration 夹具，进程级种子不可依赖，详见 e2e/steps/executionLog.steps.ts 顶部说明。
  Scenario: 打开一次执行的回放诊断
    When 我经 API 创建一个不含模型节点的工作流并运行 "E2E Replay WF"
    And 我打开该工作流最新的执行日志详情
    Then 执行日志详情显示步骤明细标签
    When 我切到回放诊断标签
    Then 回放诊断显示执行路径时间线
    And 回放诊断给出明确的结论横幅
    And 回放诊断披露数据缺口
    When 我展开回放路径中的第一个节点
    Then 节点详情显示输入输出与错误栏
    Then no unexpected HTTP or JS errors occurred during the flow
