using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.Workflows;

/// <summary>
/// 表示工作流聚合根。以 <see cref="WorkflowNode"/> 经 <see cref="WorkflowEdge"/> 连接而成的
/// 有向图（唯一事实来源）为底层，并保留遗留的 <see cref="WorkflowStep"/> 投影以兼容读取。
/// </summary>
public sealed class Workflow : ITenantScoped, IAggregateRoot
{
    private readonly List<WorkflowStep> _steps = [];
    private readonly List<WorkflowNode> _nodes = [];
    private readonly List<WorkflowEdge> _edges = [];
    private readonly List<IDomainEvent> _domainEvents = [];
    private bool _isDag;

    /// <summary>Gets the unique identifier of the workflow.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets or sets the display name of the workflow.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Gets a read-only list of legacy linear steps (projection of non-Start/End nodes).</summary>
    public IReadOnlyList<WorkflowStep> Steps => _steps;

    /// <summary>Gets a read-only list of graph nodes (execution source of truth).</summary>
    public IReadOnlyList<WorkflowNode> Nodes => _nodes;

    /// <summary>Gets a read-only list of graph edges.</summary>
    public IReadOnlyList<WorkflowEdge> Edges => _edges;

    /// <summary>Gets the current execution state of the workflow.</summary>
    public WorkflowState CurrentState { get; private set; }

    private readonly Dictionary<string, Guid> _agentAssignments = [];

    /// <summary>Gets a read-only dictionary mapping node names to their assigned agent IDs.</summary>
    public IReadOnlyDictionary<string, Guid> AgentAssignments => _agentAssignments;

    /// <summary>
    /// 指示工作流是否使用用户显式创作的 DAG（true），或是由 <see cref="WorkflowStep"/> 同步而来的
    /// 遗留线性链（false）。决定编排器是基于 <see cref="Nodes"/>（拓扑序）还是 <see cref="Steps"/>（顺序）执行。
    /// </summary>
    public bool IsDag => _isDag;

    /// <summary>Gets the shared context (JSON) available to all nodes in the workflow.</summary>
    public string Context { get; private set; } = null!;

    /// <summary>Gets the unique identifier of the tenant that owns this workflow.</summary>
    public Guid TenantId { get; private init; }

    /// <summary>Gets the UTC timestamp when the workflow was created.</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>Gets or sets the UTC timestamp when the workflow was last updated.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Gets the collection of domain events raised by this aggregate.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Workflow() { }

    private void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Clears pending domain events after dispatch.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Initializes a new workflow aggregate.</summary>
    public Workflow(Guid id, string name, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        CurrentState = WorkflowState.Pending;
        Context = "{}";
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Appends a legacy linear step (kept for backward-compatible creation paths).</summary>
    public void AddStep(WorkflowStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Sets the current execution state.</summary>
    public void SetState(WorkflowState state)
    {
        CurrentState = state;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks the workflow completed.</summary>
    public void Complete()
    {
        CurrentState = WorkflowState.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks the workflow rolled back.</summary>
    public void Rollback()
    {
        CurrentState = WorkflowState.RolledBack;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Updates the shared context JSON.</summary>
    public void UpdateContext(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        Context = context;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Assigns an agent to a named node (updates both dictionary and the matching node(s)).</summary>
    public void AssignAgent(string nodeName, Guid agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);
        _agentAssignments[nodeName] = agentId;
        foreach (var node in _nodes.Where(n => n.Name == nodeName))
            node.AssignAgent(agentId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Renames the workflow (draft edit; callers guard against Running/Paused).</summary>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Wholesale-replaces the ordered step list AND rebuilds the DAG as a linear chain
    /// (Start → LLM… → End). Agent assignments whose name survives are preserved.
    /// </summary>
    public void ReplaceSteps(IReadOnlyList<string> stepNames)
    {
        ArgumentNullException.ThrowIfNull(stepNames);

        var kept = _agentAssignments
            .Where(kv => stepNames.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        _agentAssignments.Clear();
        foreach (var kv in kept)
            _agentAssignments[kv.Key] = kv.Value;

        _steps.Clear();
        for (var i = 0; i < stepNames.Count; i++)
            _steps.Add(new WorkflowStep(Guid.NewGuid(), i, stepNames[i]));

        SyncGraphFromSteps(stepNames);
        _isDag = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 用提供的节点与边整体替换图。前端临时 id 会被映射为后端生成的 id；
    /// 结构在持久化前完成校验。
    /// </summary>
    public void ReplaceGraph(
        IReadOnlyList<(Guid TempId, StepType Type, string Name, double X, double Y, string? Config, Guid? AgentId)> nodes,
        IReadOnlyList<(Guid TempId, Guid SourceTempId, Guid TargetTempId, string? Label)> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        _nodes.Clear();
        _edges.Clear();
        _agentAssignments.Clear();
        _isDag = true;

        var map = new Dictionary<Guid, Guid>();
        foreach (var n in nodes)
        {
            var id = Guid.NewGuid();
            map[n.TempId] = id;
            _nodes.Add(new WorkflowNode(id, n.Type, n.Name, n.X, n.Y, n.Config, n.AgentId));
            if (n.AgentId.HasValue)
                _agentAssignments[n.Name] = n.AgentId.Value;
        }

        foreach (var e in edges)
        {
            if (map.TryGetValue(e.SourceTempId, out var source) && map.TryGetValue(e.TargetTempId, out var target))
                _edges.Add(new WorkflowEdge(Guid.NewGuid(), source, target, e.Label));
        }

        SyncStepsFromGraph();
        ValidateGraph();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>向图中添加节点，并重新同步遗留步骤投影。</summary>
    public void AddNode(StepType type, string name, double positionX, double positionY, string? configJson, Guid? assignedAgentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var node = new WorkflowNode(Guid.NewGuid(), type, name, positionX, positionY, configJson, assignedAgentId);
        _nodes.Add(node);
        if (assignedAgentId.HasValue)
            _agentAssignments[name] = assignedAgentId.Value;
        _isDag = true;
        SyncStepsFromGraph();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>在两个节点之间添加一条有向边。</summary>
    public void AddEdge(Guid sourceNodeId, Guid targetNodeId, string? label)
    {
        if (sourceNodeId == targetNodeId)
            throw new WorkflowGraphException("An edge cannot connect a node to itself.");
        if (_nodes.All(n => n.Id != sourceNodeId))
            throw new WorkflowGraphException("Source node does not exist.");
        if (_nodes.All(n => n.Id != targetNodeId))
            throw new WorkflowGraphException("Target node does not exist.");
        _edges.Add(new WorkflowEdge(Guid.NewGuid(), sourceNodeId, targetNodeId, label));
        _isDag = true;
        SyncStepsFromGraph();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>移除节点及其所有关联边。</summary>
    public void RemoveNode(Guid nodeId)
    {
        _nodes.RemoveAll(n => n.Id == nodeId);
        foreach (var edge in _edges.Where(e => e.SourceNodeId == nodeId || e.TargetNodeId == nodeId).ToList())
            _edges.Remove(edge);
        _isDag = true;
        SyncStepsFromGraph();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>按 id 移除一条边。</summary>
    public void RemoveEdge(Guid edgeId)
    {
        _edges.RemoveAll(e => e.Id == edgeId);
        _isDag = true;
        SyncStepsFromGraph();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>重命名节点，并迁移对应的代理分配键。</summary>
    public void RenameNode(Guid nodeId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var node = _nodes.FirstOrDefault(n => n.Id == nodeId)
            ?? throw new WorkflowGraphException("Node does not exist.");
        if (_agentAssignments.TryGetValue(node.Name, out var agentId))
        {
            _agentAssignments.Remove(node.Name);
            _agentAssignments[name] = agentId;
        }
        node.Rename(name);
        _isDag = true;
        SyncStepsFromGraph();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>更新节点的配置 JSON。</summary>
    public void SetNodeConfig(Guid nodeId, string configJson)
    {
        var node = _nodes.FirstOrDefault(n => n.Id == nodeId)
            ?? throw new WorkflowGraphException("Node does not exist.");
        node.UpdateConfig(configJson);
        _isDag = true;
        SyncStepsFromGraph();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>按 id 为节点分配代理。</summary>
    public void AssignAgentToNode(Guid nodeId, Guid agentId)
    {
        var node = _nodes.FirstOrDefault(n => n.Id == nodeId)
            ?? throw new WorkflowGraphException("Node does not exist.");
        node.AssignAgent(agentId);
        _agentAssignments[node.Name] = agentId;
        _isDag = true;
        SyncStepsFromGraph();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>指示工作流是否拥有显式 DAG（相对于仅有遗留步骤）。</summary>
    public bool HasGraph => _nodes.Count > 0;

    /// <summary>
    /// 惰性地将仅有遗留步骤的工作流提升为 DAG（Start → LLM… → End），
    /// 以便无需数据迁移即可执行旧工作流或进行拓扑排序。
    /// </summary>
    public void EnsureGraphSynced()
    {
        if (_nodes.Count == 0 && _steps.Count > 0)
            SyncGraphFromSteps(_steps.Select(s => s.StepName).ToList());
    }

    /// <summary>
    /// 以拓扑序（Kahn 算法）返回节点。若图中存在环，则抛出 <see cref="WorkflowGraphException"/>。
    /// 当不存在显式 DAG 时，回退为链式投影。
    /// </summary>
    public IReadOnlyList<WorkflowNode> GetTopologicalOrder()
    {
        var nodes = EffectiveNodes;
        var edges = EffectiveEdges;
        var indegree = nodes.ToDictionary(n => n.Id, _ => 0);
        var adj = nodes.ToDictionary(n => n.Id, _ => new List<Guid>());

        foreach (var edge in edges)
        {
            if (!indegree.ContainsKey(edge.SourceNodeId) || !indegree.ContainsKey(edge.TargetNodeId))
                continue;
            adj[edge.SourceNodeId].Add(edge.TargetNodeId);
            indegree[edge.TargetNodeId]++;
        }

        var queue = new Queue<Guid>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<WorkflowNode>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(nodes.First(n => n.Id == id));
            foreach (var next in adj[id])
            {
                indegree[next]--;
                if (indegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        if (order.Count != nodes.Count)
            throw new WorkflowGraphException("Workflow graph contains a cycle.");
        return order;
    }

    /// <summary>
    /// 校验结构完整性：恰好一个 Start、至少一个 End、无环、
    /// 从 Start 完全连通，且节点名称唯一。
    /// </summary>
    public void ValidateGraph()
    {
        var nodes = EffectiveNodes;
        var edges = EffectiveEdges;

        if (nodes.Count == 0)
            throw new WorkflowGraphException("Workflow must contain at least one node.");

        var starts = nodes.Count(n => n.Type == StepType.Start);
        var ends = nodes.Count(n => n.Type == StepType.End);
        if (starts != 1)
            throw new WorkflowGraphException($"Workflow must have exactly one Start node (found {starts}).");
        if (ends < 1)
            throw new WorkflowGraphException($"Workflow must have at least one End node (found {ends}).");

        try
        {
            GetTopologicalOrder();
        }
        catch (WorkflowGraphException ex) when (ex.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase))
        {
            throw;
        }

        var start = nodes.First(n => n.Type == StepType.Start);
        var adj = edges
            .Where(e => nodes.Any(n => n.Id == e.SourceNodeId))
            .GroupBy(e => e.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.TargetNodeId).ToList());

        var reachable = new HashSet<Guid> { start.Id };
        var q = new Queue<Guid>();
        q.Enqueue(start.Id);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (adj.TryGetValue(cur, out var nexts))
                foreach (var nx in nexts)
                    if (reachable.Add(nx))
                        q.Enqueue(nx);
        }

        if (reachable.Count != nodes.Count)
            throw new WorkflowGraphException("Workflow contains nodes unreachable from the Start node.");

        var duplicates = nodes
            .GroupBy(n => n.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new WorkflowGraphException($"Duplicate node names: {string.Join(", ", duplicates)}.");
    }

    private List<WorkflowNode>? _chainedNodes;
    private List<WorkflowEdge>? _chainedEdges;

    private IReadOnlyList<WorkflowNode> EffectiveNodes =>
        _nodes.Count > 0 ? _nodes : BuildChainView().nodes;

    private IReadOnlyList<WorkflowEdge> EffectiveEdges =>
        _edges.Count > 0 ? _edges : BuildChainView().edges;

    private (IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges) BuildChainView()
    {
        if (_chainedNodes is not null && _chainedEdges is not null)
            return (_chainedNodes, _chainedEdges);

        var nodes = new List<WorkflowNode>();
        var edges = new List<WorkflowEdge>();
        if (_steps.Count > 0)
        {
            var start = new WorkflowNode(Guid.NewGuid(), StepType.Start, "Start", 0, 0, "{}", null);
            nodes.Add(start);
            WorkflowNode prev = start;
            for (var i = 0; i < _steps.Count; i++)
            {
                var agent = _steps[i].AssignedAgentId;
                var node = new WorkflowNode(Guid.NewGuid(), StepType.LLM, _steps[i].StepName, 0, (i + 1) * 120, "{}", agent);
                nodes.Add(node);
                edges.Add(new WorkflowEdge(Guid.NewGuid(), prev.Id, node.Id, null));
                prev = node;
            }
            var end = new WorkflowNode(Guid.NewGuid(), StepType.End, "End", 0, (_steps.Count + 1) * 120, "{}", null);
            nodes.Add(end);
            edges.Add(new WorkflowEdge(Guid.NewGuid(), prev.Id, end.Id, null));
        }

        _chainedNodes = nodes;
        _chainedEdges = edges;
        return (nodes, edges);
    }

    private void SyncGraphFromSteps(IReadOnlyList<string> stepNames)
    {
        _nodes.Clear();
        _edges.Clear();
        if (stepNames.Count == 0) return;

        var start = new WorkflowNode(Guid.NewGuid(), StepType.Start, "Start", 0, 0, "{}", null);
        _nodes.Add(start);
        WorkflowNode prev = start;
        for (var i = 0; i < stepNames.Count; i++)
        {
            _agentAssignments.TryGetValue(stepNames[i], out var agentId);
            var node = new WorkflowNode(Guid.NewGuid(), StepType.LLM, stepNames[i], 0, (i + 1) * 120, "{}", agentId);
            _nodes.Add(node);
            _edges.Add(new WorkflowEdge(Guid.NewGuid(), prev.Id, node.Id, null));
            prev = node;
        }
        var end = new WorkflowNode(Guid.NewGuid(), StepType.End, "End", 0, (stepNames.Count + 1) * 120, "{}", null);
        _nodes.Add(end);
        _edges.Add(new WorkflowEdge(Guid.NewGuid(), prev.Id, end.Id, null));
    }

    /// <summary>
    /// 从当前图节点（排除 Start/End 标记）重建遗留的 <see cref="Steps"/> 投影。
    /// 在图变更后以及 DAG 执行后调用，使读取路径能在线性投影中看到节点状态。
    /// </summary>
    public void SyncStepsFromGraph()
    {
        _steps.Clear();
        var order = 0;
        foreach (var node in _nodes.Where(n => n.Type is not StepType.Start and not StepType.End))
        {
            var step = new WorkflowStep(Guid.NewGuid(), order++, node.Name);
            if (node.AssignedAgentId.HasValue)
                step.AssignAgent(node.AssignedAgentId.Value);
            _steps.Add(step);
        }
    }
}
