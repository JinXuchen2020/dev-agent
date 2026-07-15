Feature: Custom Agent Role
    As a platform user
    I want to create custom agent role types
    So that I can extend the platform with specialized agent behaviors

    Background:
        Given the agent role management system is initialized

    Scenario: Create new custom agent role
        When a user creates an agent role with:
            | Field        | Value            |
            | Name         | Security Auditor |
            | RoleCode     | security-auditor |
            | Description  | Audits code for security vulnerabilities |
            | SystemPrompt | You are a security auditor... |
        Then the role should be saved
        And the role should be queryable by role code "security-auditor"

    Scenario: Agent assigned custom role uses its system prompt
        Given a custom role "Security Auditor" exists
        When a user creates an agent with that role
        Then the agent should use the custom role's system prompt

    Scenario: List all available roles
        Given 3 custom roles exist
        When a user lists all available roles
        Then the system should return all 3 roles
        And each role should include its Name, RoleCode, and Description

    Scenario: Delete custom agent role
        Given a custom role "Security Auditor" exists
        When a user deletes the role
        Then the role should no longer be queryable
        And agents assigned that role should be unlinked

    Scenario: Create role with empty name returns validation error
        When a user creates an agent role with empty name
        Then the system should return a validation error
        And the role should not be created
