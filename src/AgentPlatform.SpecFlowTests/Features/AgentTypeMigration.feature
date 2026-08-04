Feature: AgentType Migration
    As a platform maintainer
    I want to migrate from AgentRole enum to AgentType value object
    So that agent roles become extensible and type-safe

    Background:
        Given the system is initialized with the AgentRole-to-AgentType migration

    Scenario: Create agent with new AgentType
        When a user creates an agent with role code "architecture"
        Then the agent should have an AgentType with RoleCode "architecture"
        And the agent should be retrievable by role code "architecture"

    Scenario: Migrate existing AgentRole agent
        Given an agent was created with AgentRole.Architect
        When the system migrates agent roles
        Then the agent should have an AgentType with RoleCode "architect"
        And the agent should be retrievable by role code "architect"

    Scenario: Unknown role code returns empty
        When a user queries agents by role code "nonexistent-role"
        Then the system should return an empty list
