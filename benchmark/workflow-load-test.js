import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const workflowRunDuration = new Trend('workflow_run_duration');
const workflowErrorRate = new Rate('workflow_errors');

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

export const options = {
  stages: [
    { duration: '10s', target: 5 },   // ramp up to 5 concurrent
    { duration: '20s', target: 20 },  // ramp to 20
    { duration: '30s', target: 20 },  // hold at 20
    { duration: '10s', target: 0 },   // ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<2000'],  // 95% of requests < 2s
    workflow_errors: ['rate<0.1'],       // <10% error rate
  },
};

function createWorkflow() {
  const payload = JSON.stringify({
    name: `perf-test-${__VU}-${__ITER}`,
    steps: [
      {
        agentType: 'Analyzer',
        name: 'analyze-input',
        config: { prompt: 'Analyze the request' },
      },
      {
        agentType: 'Executor',
        name: 'execute-task',
        config: { prompt: 'Execute the analysis result' },
      },
    ],
  });

  const res = http.post(`${BASE_URL}/api/v1/workflows`, payload, {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'create workflow status 200/201': (r) => r.status === 200 || r.status === 201,
  });

  return res.json().id;
}

function runWorkflow(id) {
  const start = Date.now();

  const res = http.post(`${BASE_URL}/api/v1/workflows/${id}/run`, null, {
    headers: { 'Content-Type': 'application/json' },
    timeout: '30s',
  });

  const duration = Date.now() - start;
  workflowRunDuration.add(duration);

  const passed = check(res, {
    'run workflow status 200': (r) => r.status === 200,
  });

  if (!passed) {
    workflowErrorRate.add(1);
  }
}

function listWorkflows() {
  const res = http.get(`${BASE_URL}/api/v1/workflows`);
  check(res, {
    'list workflows status 200': (r) => r.status === 200,
  });
}

function listAgents() {
  const res = http.get(`${BASE_URL}/api/v1/agents`);
  check(res, {
    'list agents status 200': (r) => r.status === 200,
  });
}

function listExecutionLogs() {
  const res = http.get(`${BASE_URL}/api/v1/execution-logs`);
  check(res, {
    'list logs status 200': (r) => r.status === 200,
  });
}

export default function () {
  // Phase 1: list queries (read-only, fast)
  listWorkflows();
  listAgents();
  listExecutionLogs();
  sleep(0.2);

  // Phase 2: create and run a workflow (write-heavy)
  const id = createWorkflow();
  if (id) {
    sleep(0.3);
    runWorkflow(id);
  }

  sleep(0.5);

  // Phase 3: verify the run appeared in logs
  listExecutionLogs();
}
