# Testing Guide

## Test Layers

| Layer | Framework | Location | What to Test |
|-------|-----------|----------|-------------|
| Unit | xUnit + Moq | `*.Tests/` projects | Service logic, metric computations, event normalization |
| Integration | xUnit + Testcontainers | `*.IntegrationTests/` | SQL queries, API endpoints, Function triggers |
| E2E | Playwright | `e2e/` | Extension UI flows against a local dev environment |

## Running Tests

```bash
# All .NET tests
dotnet test

# Specific project
dotnet test src/Velo.Functions.Tests/

# Angular unit tests
cd src/Velo.Extension
npm test
```

## Key Test Cases for Phase 1

- `MetricsEngine.ComputeDeploymentFrequency` returns correct rate for known dataset
- `EventNormalizer.HandleBuildCompleted` correctly identifies deployments by stage name
- `ServiceHookTrigger` returns 400 for malformed payload and 200 for valid
- `DoraController.GetMetrics` returns 401 without token, 200 with valid token
- RLS: querying with `org_id = A` never returns rows from `org_id = B`

## Test Data

Use the `TestDataBuilder` pattern — build objects with sensible defaults and override only what matters for the test. Do not share mutable state between tests.
