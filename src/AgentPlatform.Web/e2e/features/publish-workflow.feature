@e2e
Feature: Publish workflow via UI and invoke its API endpoint
  As a tenant admin
  I want to publish a completed workflow from the UI
  So that I can invoke it through its API endpoint with an API key

  Background:
    Given the integration backend is reachable and I am authenticated as admin

  Scenario: Publish a completed workflow and call its API endpoint
    When I open the Workflows page
    And I publish the fixture workflow "Integration Fixture Workflow"
    Then the publish drawer shows a non-empty slug and the API endpoint text
    When I invoke the published workflow endpoint with the fixture API key
    Then no unexpected HTTP or JS errors occurred during the flow
