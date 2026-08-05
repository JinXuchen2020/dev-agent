@e2e
Feature: 登录与受保护路由鉴权
  As a 未登录用户
  I want 访问受保护页面时被重定向到登录页
  So that 未授权访问被拦截

  Scenario: 未登录访问受保护页跳转到登录页
    When 我未登录访问 "/agents"
    Then 我被重定向到 "/login"

  Scenario: 登录页正确渲染欢迎语
    When 我打开 "/login"
    Then 页面显示 "欢迎回来"

  Scenario: 使用正确凭据通过界面登录成功
    Given 集成后端可达
    When 我打开 "/login"
    And 我在登录页输入邮箱 "admin@acme.io" 与密码 "Admin@123456"
    And 我点击登录按钮
    Then 我被重定向到 "/"

  Scenario: 使用错误密码登录被拒绝并提示错误
    Given 集成后端可达
    When 我打开 "/login"
    And 我在登录页输入邮箱 "admin@acme.io" 与密码 "wrong-password"
    And 我点击登录按钮
    Then 页面显示 "邮箱或密码错误"
