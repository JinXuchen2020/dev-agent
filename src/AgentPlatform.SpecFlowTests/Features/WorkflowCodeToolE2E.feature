Feature: Workflow Tool/Code node end-to-end execution
  As a platform operator
  I want a workflow containing real Tool (HTTP) and Code (python subprocess) nodes to execute end-to-end
  So that I can verify real side effects land in node results and execution logs

  Background:
    Given the F12 real-executor host is initialized

  Scenario: Run workflow with Code and Tool nodes asserts real stdout and HTTP response
    Given I am logged in as T1 admin
    When I import a workflow with Start, Code, Tool, End nodes via the F12 API
    And I run the imported workflow via the F12 API
    Then the Code node result should contain "hello-from-code"
    And the Tool node result should contain "bdd-echo-tool"
    And each graph node state should be Completed (3)
    When I query execution logs for the workflow via the F12 API
    Then the execution log should contain a step with result containing "hello-from-code"
    And the execution log should contain a step with result containing "bdd-echo-tool"
