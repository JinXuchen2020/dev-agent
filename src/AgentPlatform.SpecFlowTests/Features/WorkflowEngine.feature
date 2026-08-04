Feature: 工作流引擎重试与回滚（真实 IOrchestrationPrimitive）
	作为平台编排器
	我希望工作流在步骤失败时按真实引擎语义重试并回滚
	以保证执行状态真实持久化到数据库

	# 诚实替代说明：旧 WorkflowStateMachine.feature / MultiAgentPipeline.feature 用 TestStateMachineEngine /
	# TestAgentOrchestrator 在测试内重写引擎逻辑（且实现已废弃的 IStateMachineEngine / IAgentOrchestrator），
	# 零真实覆盖。本 feature 直接驱动生产代码 IOrchestrationPrimitive（RunAsync → SequentialOrchestrator /
	# NegotiationOrchestrator），仅经 ConfigurableStepExecutor 隔离外部 LLM 步骤行为，断言真实重试 / 回滚语义
	# 并验证结果已持久化到真实文件 SQLite。

Scenario: 步骤重试耗尽后回滚已完成步骤
	Given a 3-step workflow is defined
	And step 2 is configured to fail with retryable error
	When the workflow is executed sequentially
	Then step 1 should be in state Completed
	And step 2 should be in state Pending
	And step 3 should be in state Pending
	And step 2 should have been attempted 3 times
	And the workflow should be in state RolledBack
	And the workflow state should be persisted to the database

Scenario: 全部步骤成功则工作流完成
	Given a 3-step workflow is defined
	When the workflow is executed sequentially
	Then step 1 should be in state Completed
	And step 2 should be in state Completed
	And step 3 should be in state Completed
	And the workflow should be in state Completed
	And the workflow state should be persisted to the database

Scenario: 协商预设多智能体管线成功完成
	Given a 3-step workflow is defined
	When the workflow is executed with the negotiation preset
	Then the workflow should be in state Completed
	And the workflow state should be persisted to the database
