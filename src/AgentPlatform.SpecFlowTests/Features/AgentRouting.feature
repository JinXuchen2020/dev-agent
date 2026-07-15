Feature: 多模型路由降级
	作为平台编排器
	我希望主模型失败时自动降级到备用模型
	以保证工作流不被中断

Scenario Outline: 主模型超时后降级到备用模型
	Given 主模型 "<Primary>" 调用超时
	When 路由层触发降级策略
	Then 应使用备用模型 "<Fallback>" 重试

	Examples:
	| Primary   | Fallback  |
	| gpt-4o    | deepseek  |
	| deepseek  | gpt-4o    |

Scenario: 所有模型都失败时抛出异常
	Given 所有模型调用都抛出 HttpRequestException
	When 路由层触发降级策略
	Then 应抛出 AllModelsFailedException

Scenario: 预算耗尽时跳过所有候选模型
	Given 所有模型调用都抛出 HttpRequestException
	And 预算设置为零
	When 路由层触发降级策略
	Then 应抛出 AllModelsFailedException

Scenario: 不可重试异常直接抛出
	Given 主模型 "gpt-4o" 抛出 InvalidOperationException
	When 路由层触发降级策略
	Then 应抛出 InvalidOperationException

Scenario: 空候选列表时抛出异常
	Given 候选模型列表为空
	When 路由层触发降级策略
	Then 应抛出 AllModelsFailedException

Scenario: 指定偏好模型时优先使用
	Given 模型 "qwen" 调用返回成功
	And 其他模型调用返回成功
	When 路由层触发降级策略并指定偏好模型 "qwen"
	Then 应使用模型 "qwen" 响应
