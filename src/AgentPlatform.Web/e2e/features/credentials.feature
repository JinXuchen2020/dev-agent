@e2e
Feature: 租户凭据（BYO-Key）管理界面
  As a 租户管理员
  I want 在界面上添加模型凭据
  So that 对话使用本租户自有密钥

  Scenario: 凭据页面渲染且可打开添加模型凭据表单
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/credentials"
    Then 页面显示标题 "我的凭据"
    When 我点击按钮 "添加模型凭据"
    Then 凭据表单显示

  Scenario: 添加模型凭据并保存成功
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/credentials"
    And 我点击按钮 "添加模型凭据"
    And 我在凭据表单填写名称 "E2E 测试模型凭据"
    And 我在凭据表单选择 Provider "OpenAI"
    And 我在凭据表单填写 API Key "sk-e2e-test-12345"
    And 我在凭据表单填写模型名称 "gpt-4o"
    And 我点击按钮 "保存"
    Then 页面出现凭据 "E2E 测试模型凭据"
