@e2e
Feature: Workflow debugger (variable watch + step re-run + error branch)
  As a tenant admin
  I want to debug a workflow step by step with variable watch
  So that I can observe and intervene during development

  Background:
    Given 集成后端可达且我已以 admin 登录

  Scenario: Start a debug session and step through a workflow
    When 我打开 "/workflows"
    And I open the workflow detail for the fixture workflow "Integration Fixture Workflow"
    And I open the debugger for that workflow
    Then the debugger start control is visible
    When I start a debug session
    Then a debug session is started and variables panel shows
    When I step the debugger
    Then the debug variables panel is shown
    And no unexpected HTTP or JS errors occurred during the flow
