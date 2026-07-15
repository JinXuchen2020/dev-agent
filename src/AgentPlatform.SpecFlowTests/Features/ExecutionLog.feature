Feature: Execution Log
    As a platform operator
    I want execution logs for all workflow steps
    So that I can audit, debug, and analyze pipeline executions

    Background:
        Given the execution log repository is initialized
        And a workflow has completed with 3 steps

    Scenario: Query execution history
        When a user queries execution logs for the workflow
        Then they should receive 3 log entries
        And each entry should contain status, duration, and timestamp

    Scenario: Failed step includes error details
        Given step 2 of the workflow failed
        When a user queries execution logs
        Then the log entry for step 2 should include error details
        And the error message should describe the failure reason

    Scenario: Execution log filtered by date range
        Given logs exist across multiple days
        When a user filters logs by a date range
        Then only logs within that range should be returned

    Scenario: Execution log filtered by status
        Given some steps succeeded and some failed
        When a user filters logs by status "Failed"
        Then only failed step entries should be returned

    Scenario: Execution log pagination
        Given 50 log entries exist
        When a user queries with page 1 and page size 20
        Then they should receive 20 entries
        And total count should be 50
