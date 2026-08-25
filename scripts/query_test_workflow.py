# -*- coding: utf-8 -*-
import sqlite3, sys
sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'E:\Freelancer\AI_Projects\dev-agent\src\AgentPlatform.Api\agent_platform.db')
cur = conn.cursor()
wf = 'DC5DA840-0F89-4A53-B6FD-090651787556'

print('--- Nodes now ---')
cur.execute("SELECT Name, Type, State, ConfigJson, AssignedAgentId, ErrorDetail FROM WorkflowNode WHERE WorkflowId=?", (wf,))
for n in cur.fetchall():
    print(f"{n[0]} | {n[1]} | {n[2]} | config={n[3]} | agentId={n[4]} | err={str(n[5])[:60] if n[5] else None}")

print('--- Agents ---')
cur.execute("SELECT Id, Name, Status FROM Agents")
for a in cur.fetchall():
    print(a)

print('--- Latest log entries ---')
cur.execute("SELECT Id, Status, StartedAt FROM ExecutionLogs WHERE WorkflowId=? ORDER BY StartedAt DESC LIMIT 2", (wf,))
logs = cur.fetchall()
for lg in logs: print(lg)
if logs:
    cur.execute("SELECT StepName, Status, substr(ErrorDetail,1,120) FROM ExecutionLogEntries WHERE ExecutionLogId=? ORDER BY StepOrder DESC LIMIT 3", (logs[0][0],))
    for e in cur.fetchall(): print(e)

conn.close()
