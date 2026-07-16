# Performance Benchmark

## Prerequisites

- Install [k6](https://k6.io/docs/get-started/installation/)
- Ensure the API is running locally (default: `http://localhost:5000`)

## Usage

```bash
# Run with default settings (target: localhost:5000)
k6 run benchmark/workflow-load-test.js

# Run against a different host
BASE_URL=http://localhost:5000 k6 run benchmark/workflow-load-test.js

# Run with higher load
k6 run --vus 50 --duration 60s benchmark/workflow-load-test.js
```

## Test Scenarios

1. **List queries** (read-heavy): GET /workflows, /agents, /execution-logs
2. **Write workload**: POST /workflows + POST /workflows/{id}/run
3. **Verification**: GET /execution-logs after each run

## Thresholds

- 95th percentile response time < 2s
- Error rate < 10%
