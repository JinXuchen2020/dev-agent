@e2e
Feature: 多工作空间切换（F35）
  As a tenant admin
  I want to create workspaces and switch between them
  So that business data is isolated per workspace inside the same tenant

  Background:
    Given 集成后端可达且我已以 admin 登录

  Scenario: Admin 新建工作空间并切换
    When 我打开 "/"
    When 我在顶栏工作空间管理菜单中新建工作空间 "E2E Workspace F35"
    Then 工作空间切换器包含 "E2E Workspace F35"
    When 我选择工作空间 "E2E Workspace F35"
    Then 页面显示 "已切换到「E2E Workspace F35」"
    Then no unexpected HTTP or JS errors occurred during the flow
