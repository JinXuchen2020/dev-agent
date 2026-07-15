Feature: Multi-Agent Pipeline
    As a developer
    I want a 6-agent collaboration pipeline
    So that a requirement can be processed into architecture, code, tests, and docs

    Background:
        Given the AutoGen orchestration engine is initialized
        And 6 agent roles are registered: Product Manager, Architect, Developer, Tester, Tech Writer, Reviewer

    Scenario: Full pipeline produces all deliverables
        When a user submits a requirement "Create a user login API"
        Then all 6 agents should participate in the conversation
        And the pipeline should produce architecture design
        And the pipeline should produce code
        And the pipeline should produce tests
        And the pipeline should produce documentation

    Scenario: Pipeline stops when agent cannot proceed
        Given the Developer agent is unavailable
        When a user submits a requirement
        Then the pipeline should detect the missing agent
        And report "Agent Developer unavailable"
        And not produce output

    Scenario: Custom agent role participates in pipeline
        Given a user has created a custom agent role "Security Reviewer"
        When the user includes the custom role in the pipeline
        Then the custom agent should participate in the conversation
        And the pipeline output should include security review

    Scenario Outline: Pipeline handles max rounds
        When a user submits a requirement
        And the pipeline runs for <maxRounds> rounds
        Then the pipeline should terminate after <maxRounds> rounds
        And produce a stop reason indicating round limit

        Examples:
        | maxRounds |
        | 10        |
        | 50        |
