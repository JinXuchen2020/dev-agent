Feature: Workflow State Machine
    As a workflow orchestrator
    I want a state machine engine with branching, retry, and rollback
    So that multi-agent workflows can handle failures gracefully

    Background:
        Given a workflow with 3 steps defined
        And the state machine engine is initialized

    Scenario: Normal flow completes all steps
        When the workflow starts
        Then step 1 should execute successfully
        And step 2 should execute successfully
        And step 3 should execute successfully
        And the workflow status should be "Completed"

    Scenario: Step failure triggers retry up to 3 times
        Given step 2 is configured to fail
        When the workflow starts
        Then step 1 should execute successfully
        And step 2 should retry up to 3 times
        And after 3 failures, step 2 should be marked as "Failed"

    Scenario: All retries exhausted triggers rollback
        Given step 2 is configured to always fail
        When the workflow starts
        Then step 2 should fail after 3 retries
        And all completed steps should be rolled back
        And the workflow status should be "RolledBack"

    Scenario: Branching skips failed branch, continues others
        Given step 2 is in a branch path
        When the branch step fails
        Then alternative branch should execute
        And the workflow should complete with the successful branch result

    Scenario: Concurrent workflow executions
        Given 2 workflows are started simultaneously
        When both workflows run
        Then they should not corrupt each other's state
        And both should produce correct results independently

    Scenario: Workflow state recovery after system restart
        Given a workflow is in "Running" state
        When the system restarts
        Then the workflow should be recovered to "Failed" state
