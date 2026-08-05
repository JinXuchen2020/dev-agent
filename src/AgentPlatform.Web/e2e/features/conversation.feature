@e2e
Feature: 会话与聊天界面
  As a 已登录用户
  I want 新建会话并发送消息收到回复
  So that 可与智能体对话

  Scenario: 新建会话并发送消息收到智能体回复
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/conversations"
    And 我点击按钮 "新建会话"
    Then 我被重定向到 "/conversations"
    When 我在对话输入框输入 "你好，请介绍一下你自己"
    And 我点击按钮 "发送"
    Then 收到智能体回复

  Scenario: 会话列表搜索与状态筛选控件渲染
    Given 集成后端可达且我已以 admin 登录
    When 我打开 "/conversations"
    Then 页面显示 "会话列表"
    And 状态筛选控件可见
    And 搜索框控件可见
