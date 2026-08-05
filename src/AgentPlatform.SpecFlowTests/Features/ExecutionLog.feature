Feature: Execution Log
    As a platform operator
    I want execution logs for all workflow steps
    So that I can audit, debug, and analyze pipeline executions

    Background:
        Given the execution log store is reset

    Scenario: Query execution history
        Given 3 workflow executions have completed
        When a user queries execution logs for the workflow
        Then they should receive 3 log entries
        And each entry should contain status, duration, and timestamp

    Scenario: Failed step includes error details
        Given step 2 of the workflow failed
        When a user queries execution logs
        Then the log entry for step 2 should include error details
        And the error message should describe the failure reason

    Scenario: Execution log filtered by future date range returns none
        Given 3 workflow executions have completed
        When a user filters logs by a future date range
        Then no logs should be returned

    Scenario: Execution log filtered by today date range returns all
        Given 3 workflow executions have completed
        When a user filters logs by a range covering today
        Then all logs within that range should be returned

    Scenario: Execution log filtered by status
        Given some executions succeeded and some failed
        When a user filters logs by status "Failed"
        Then only failed execution entries should be returned

    Scenario: Execution log pagination
        Given 50 execution logs exist
        When a user queries with page 1 and page size 20
        Then they should receive 20 entries
        And total count should be 50
