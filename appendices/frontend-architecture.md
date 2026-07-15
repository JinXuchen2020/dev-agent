## 附录 G：前端形态选型（Web / 桌面 App / 双形态）

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **背景**：后端是 ASP.NET Core Web API，前端通过 REST API 完全解耦，因此可以用同一套后端支撑 Web、桌面 App、甚至移动端。本附录分析五种桌面 App 方案，并给出推荐架构。

<a name="g.1"></a>
### G.1 架构层面：后端不在乎前端是谁

```
┌──────────────────────────────────────────────────────────┐
│                 ASP.NET Core Web API（后端）                │
│                                                          │
│  /api/agents          /api/workflows    /api/chat         │
│  /api/tools           /api/conversations ...              │
└────────────────────────┬─────────────────────────────────┘
                         │ REST API + WebSocket（流式）
                         │ ← JWT 认证 →
    ┌────────────────────┼────────────────────┐
    │                    │                    │
    ▼                    ▼                    ▼
┌────────┐         ┌──────────┐         ┌──────────┐
│ Web    │         │ Desktop  │         │ 移动端    │
│ React  │         │ App      │         │ (未来)    │
│ (浏览器)│         │ (Tauri等) │         │          │
└────────┘         └──────────┘         └──────────┘
   蓝图默认          本附录讨论           扩展可能

↑ 三个前端调的是同一套 API，后端零改动
  换前端 = 换"皮肤"，不动"骨头"
```

<a name="g.2"></a>
### G.2 五种桌面 App 方案对比

| 方案 | 包体积 | 内存 | 性能 | 语言生态 | 代表产品 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Tauri 2.0** | ~10MB | ~80MB | ⭐⭐⭐⭐⭐ | Rust + 前端 | 开源生态新贵 |
| Electron | ~150MB | ~200MB | ⭐⭐⭐ | Node + 前端 | VS Code / Discord / Slack |
| **Photino.NET** | ~2MB | ~60MB | ⭐⭐⭐⭐ | C# + 前端 | .NET 原生轻量方案 |
| Avalonia | ~30MB | ~100MB | ⭐⭐⭐⭐ | 纯 C# | 跨平台 .NET GUI |
| MAUI | ~50MB | ~120MB | ⭐⭐⭐ | 纯 C# | 微软官方跨平台 GUI |

<a name="g.3"></a>
### G.3 方案详解

#### 方案 A：Tauri 2.0 ⭐ 首推

核心思路：用蓝图已计划的 React 写 UI，Tauri 把它打包成桌面 App，用 Rust 调原生能力。

```
┌──────────────────────────────────────────┐
│            Tauri 桌面窗口                  │
│  ┌────────────────────────────────────┐  │
│  │  你的 React 代码（和 Web 版完全一样） │  │  ← UI 层
│  │  Ant Design 组件 / React Flow 编排   │  │
│  └────────────────────────────────────┘  │
│                  ↕ Tauri IPC              │
│  ┌────────────────────────────────────┐  │
│  │  Rust 后端（原生 API 调用）           │  │  ← 原生能力层
│  │  文件系统 / 系统托盘 / 通知 / 自动更新 │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
        ↕ HTTP / WebSocket
┌──────────────────────────────────────────┐
│  ASP.NET Core Web API（你现有的后端）       │
└──────────────────────────────────────────┘
```

优点：
- ✅ React 代码 100% 复用——Web 版和桌面版同一套代码
- ✅ 包体积只有 Electron 的 1/15（10MB vs 150MB）
- ✅ 内存占用极低（80MB vs 200MB）
- ✅ Rust 后端提供原生能力（文件、系统托盘、全局快捷键）
- ✅ Tauri 2.0 已支持 Windows/macOS/Linux + 移动端
- ✅ 自动更新内置

唯一缺点：需要懂一点 Rust（大部分场景不用碰，调系统 API 才需要）。

```
代码结构：
my-app/
├── src/                    ← React 代码（Web 和 Desktop 共用）
│   ├── components/
│   └── pages/
├── src-tauri/              ← Tauri 配置 + Rust 代码
│   ├── src/main.rs
│   ├── Cargo.toml
│   └── tauri.conf.json
└── package.json            ← 前端依赖
```

#### 方案 B：Electron

核心思路：把 Chromium 浏览器 + Node.js 打包成 App。

```
┌──────────────────────────────────────────┐
│            Electron 窗口                  │
│  ┌────────────────────────────────────┐  │
│  │  Chromium 浏览器内核                 │  │  ← 完整浏览器（~150MB）
│  │  渲染你的 React 代码                 │  │
│  └────────────────────────────────────┘  │
│  ┌────────────────────────────────────┐  │
│  │  Node.js 进程                        │  │  ← 完整 Node.js
│  │  文件系统 / 系统调用 / 自动更新       │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

优点：生态最成熟、文档最全、坑最少、全是 JS 不用学新语言。
缺点：包体积大（150MB+）、内存占用高（每个 Electron App 相当于开一个 Chrome）。
适合：团队不想碰 Rust，且不在乎体积（企业内部分发可接受）。

#### 方案 C：Photino.NET ⭐ .NET 原生轻量

核心思路：用 OS 自带的 WebView（Windows 用 WebView2，macOS 用 WebKit），C# 做宿主进程。

```
┌──────────────────────────────────────────┐
│          Photino 窗口（C# 进程）           │
│  ┌────────────────────────────────────┐  │
│  │  OS 原生 WebView                     │  │  ← 不打包浏览器，用系统的
│  │  Windows: WebView2                   │  │
│  │  macOS:   WKWebView                  │  │
│  │  Linux:   GTK WebKit                 │  │
│  │  渲染你的 React 代码                  │  │
│  └────────────────────────────────────┘  │
│  ┌────────────────────────────────────┐  │
│  │  C# 宿主进程                         │  │  ← 和后端同语言
│  │  文件系统 / .NET API / 原生调用       │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

优点：
- ✅ 包体积极小（~2MB）—— 不打包浏览器，用 OS 自带的
- ✅ 内存低（~60MB）
- ✅ C# 写宿主逻辑 —— 和后端同语言，零学习成本
- ✅ React 前端代码完全复用

**杀手级场景——后端内嵌**：本地桌面 App 可以不部署后端，把 Web API 直接嵌入 Photino 进程：

```
┌─────────────────────────────────┐
│ Photino 进程                     │
│  ├── ASP.NET Core Kestrel        │  ← 后端内嵌
│  ├── React UI（WebView）          │
│  └── vLLM 调用（HTTP）            │
└─────────────────────────────────┘
用户装一个 exe 就能用，不需要部署服务器！
```

缺点：生态比 Tauri/Electron 小、文档相对少。

#### 方案 D：Avalonia（纯 C# 原生 GUI）

核心思路：完全不用 Web 技术，用纯 C# 写原生 UI（类似 WPF 但跨平台）。

```csharp
// Avalonia 的 XAML（和 WPF 几乎一样）
<Window xmlns="https://github.com/avaloniaui">
    <StackPanel>
        <TextBlock Text="Agent 工作流" />
        <ListBox Items="{Binding Agents}" />
        <Button Content="启动" Command="{Binding StartCommand}" />
    </StackPanel>
</Window>
```

优点：纯 C# 一个语言搞定一切、性能最好、原生体验。
缺点：
- ❌ **不能复用 React 代码**——要全部用 XAML 重写 UI
- ❌ Ant Design / React Flow 这些组件库全用不了
- ❌ 工作流可视化编排要自己画

适合：想要极致原生体验、不在乎重写 UI、团队是 WPF 老手。

#### 方案 E：MAUI（微软官方跨平台）

微软官方的跨平台 GUI 框架（Windows/macOS/iOS/Android）。

和 Avalonia 类似，都是纯 C# 原生 UI。区别：
- MAUI 是微软官方（.NET 9 一等公民）
- Avalonia 是社区驱动（更成熟，跨平台做得更早）
- 两者都不能复用 React 代码

<a name="g.4"></a>
### G.4 决策矩阵

结合项目特点（C# 后端 + 已计划 React + 工作流可视化 + Agent 平台）：

| 项目特征 | 倾向方案 | 原因 |
| :--- | :--- | :--- |
| 后端是 C#/.NET | Photino / Avalonia | 语言优势 |
| 已计划用 React | Tauri / Photino / Electron | 可复用 React 代码 |
| 工作流可视化（React Flow） | Tauri / Photino / Electron | 必须用 Web 技术（排除 Avalonia/MAUI） |
| Agent 平台，可能需系统托盘 | Tauri / Electron | 原生支持好 |
| 可能需离线/本地部署 | Photino | 可内嵌后端 |
| 企业级产品 | Tauri / Electron | 生态成熟度重要 |

**决策结果：**

```
🥇 Tauri 2.0：   React 100% 复用 + 轻量 + 原生能力强
                 唯一代价：要学一点 Rust（很少）

🥈 Photino.NET： C# 同语言 + 极轻量 + 可内嵌后端
                 代价：生态小、文档少、踩坑自己填

🥉 Electron：    生态最成熟 + 全 JS
                 代价：体积大、内存高

❌ Avalonia/MAUI：不能复用 React + 工作流可视化要重写
```

<a name="g.5"></a>
### G.5 推荐方案：Tauri 2.0 + React（双形态发布）

```
最终架构：一套 React 代码，两种发布形态

my-platform/
├── src/                          ← React 代码（共用）
│   ├── components/               ← Ant Design 组件
│   ├── pages/
│   │   ├── WorkflowEditor/       ← React Flow 工作流编排
│   │   ├── AgentConfig/
│   │   └── ChatConsole/
│   ├── api/                      ← axios 调后端
│   └── App.tsx
│
├── src-tauri/                    ← Tauri 桌面配置（仅桌面版）
│   ├── src/main.rs
│   └── tauri.conf.json
│
├── vite.config.ts                ← Web 版构建
└── package.json

构建命令：
  npm run build           → Web 版（部署到服务器，浏览器访问）
  npm run tauri build     → 桌面版（生成 .exe / .dmg / .AppImage）

用户选择：
  - 想在线用 → 浏览器打开 https://your-platform.com
  - 想本地装 → 下载 .exe，双击运行（带系统托盘、全局快捷键、自动更新）
```

<a name="g.6"></a>
### G.6 桌面 App 独有能力清单

桌面 App 能做而 Web 做不到的事（Tauri 原生 API）：

| 能力 | 对 Agent 平台的价值 |
| :--- | :--- |
| 系统托盘（最小化到托盘，后台运行） | Agent 后台监控工作流，完成时通知 |
| 全局快捷键（随时唤起 Agent 对话窗） | 随时唤起 Agent（像 Spotlight） |
| 本地文件系统访问 | RAG 直接读本地文档，不用上传 |
| 开机自启动 | 开机即用，常驻后台 |
| 原生通知（Windows / macOS） | 工作流步骤完成、错误告警实时推送 |
| 剪贴板监听 | 自动理解用户复制的内容 |
| 离线运行（后端可内嵌） | 不依赖远程服务器，数据完全本地 |
| 自动更新 | 静默升级，用户无感 |

<a name="g.7"></a>
### G.7 与蓝图架构的关系

| 维度 | 现有蓝图 | 本附录调整 |
| :--- | :--- | :--- |
| 后端（ASP.NET Core Web API） | 保持不变 | ❌ 零改动 |
| REST API 接口 | 保持不变 | ❌ 零改动 |
| 第三章目录脚手架 `AgentPlatform.Web` | React + TypeScript + Vite | ⚠️ 扩展为支持双形态（Web + Tauri） |
| 第二章技术栈对照表「前端」 | React + Ant Design | ⚠️ 补充「桌面形态可选 Tauri 2.0」 |
| 认证（JWT） | 保持不变 | ❌ Web 和桌面共用同一套 JWT |
| WebSocket 流式 | 保持不变 | ❌ Web 和桌面共用同一套 WebSocket |

> **一句话总结**：由于后端是 REST API，前端技术完全解耦。首推 **Tauri 2.0 + React**——一套 React 代码同时发布 Web 版和桌面版，包体积仅 10MB，还能获得系统托盘、全局快捷键、本地文件访问等 Web 做不到的原生能力。如果想要极致轻量且全 C#，**Photino.NET** 是 .NET 原生的隐藏宝藏（甚至能把后端内嵌进桌面进程，装个 exe 就能用）。**蓝图中的 `AgentPlatform.Web` 项目从「Web 专用」升级为「双形态共用」——React 代码不变，Tauri 配置按需启用。**

<a name="g.8"></a>
### G.8 前端架构详述

> **背景**：G.5 确定了 Tauri 2.0 + React 的双形态策略，本节深入前端项目的具体架构——状态管理、API 集成、路由、权限组件和工作流编辑器设计。

#### G.8.1 状态管理（zustand）

选用 **zustand** 而非 Redux Toolkit / Jotai / Valtio：

```
┌──────────────────────────────────────────────────────────────────┐
│ 选型决策                                                                │
├──────────────────────────────────────────────────────────────────┤
│ ❌ Redux Toolkit    → 样板代码多，不适合中小型项目                         │
│ ❌ Jotai / Valtio   → 原子化方案，分散状态难追踪                          │
│ ✅ zustand          → 简洁（无 Provider）、TypeScript 友好、               │
│                       支持中间件（immer / persist / devtools）              │
└──────────────────────────────────────────────────────────────────┘
```

```typescript
// stores/workflowStore.ts
interface WorkflowState {
  agents: Agent[];
  steps: WorkflowStep[];
  connections: Connection[];
  isRunning: boolean;

  // actions
  addStep: (step: WorkflowStep) => void;
  removeStep: (stepId: string) => void;
  moveStep: (stepId: string, position: XYPosition) => void;
  setRunning: (running: boolean) => void;
  reset: () => void;
}

export const useWorkflowStore = create<WorkflowState>()(
  devtools(
    persist(
      immer((set) => ({
        agents: [],
        steps: [],
        connections: [],
        isRunning: false,

        addStep: (step) => set((state) => { state.steps.push(step); }),
        removeStep: (stepId) => set((state) => {
          state.steps = state.steps.filter((s) => s.id !== stepId);
        }),
        // ...其他 actions
      })),
      { name: "workflow-storage" }
    ),
    { name: "WorkflowStore" }
  )
);
```

**Store 分层**：

| Store | 职责 | 持久化 |
| :--- | :--- | :--- |
| `workflowStore` | 工作流编排状态（步骤列表、连线、执行状态） | 需要（localStorage） |
| `agentStore` | Agent 列表、Agent 配置表单状态 | 不需要（每次从 API 加载） |
| `chatStore` | 对话会话、消息历史、流式接收状态 | 需要 |
| `uiStore` | 侧栏展开/折叠、主题切换、面板尺寸 | 需要 |
| `authStore` | 用户信息、Token、租户 ID | 需要（安全存储） |

#### G.8.2 API 集成层（TanStack Query + axios）

```typescript
// api/client.ts —— axios 实例 + 拦截器
const apiClient = axios.create({ baseURL: import.meta.env.VITE_API_URL });

apiClient.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

apiClient.interceptors.response.use(
  (res) => res,
  (error) => {
    if (error.response?.status === 401) {
      useAuthStore.getState().logout();
      window.location.href = "/login";
    }
    return Promise.reject(error);
  }
);

// api/workflow.ts —— 工作流 API 封装
export const workflowApi = {
  list: () => apiClient.get<Workflow[]>("/api/workflows"),
  getById: (id: string) => apiClient.get<Workflow>(`/api/workflows/${id}`),
  create: (data: CreateWorkflowRequest) =>
    apiClient.post<Workflow>("/api/workflows", data),
  execute: (id: string) =>
    apiClient.post<ExecutionResult>(`/api/workflows/${id}/execute`),
  stream: (id: string) =>
    new EventSource(`${apiClient.defaults.baseURL}/api/workflows/${id}/stream`),
};
```

**数据获取策略（TanStack Query）**：

```typescript
// hooks/useWorkflows.ts
export function useWorkflows() {
  return useQuery({
    queryKey: ["workflows"],
    queryFn: () => workflowApi.list().then((r) => r.data),
    staleTime: 30_000,           // 30s 内不重新请求
    refetchOnWindowFocus: false, // 不上线自动刷新（用户编辑中刷新会闪）
  });
}

// hooks/useExecuteWorkflow.ts
export function useExecuteWorkflow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => workflowApi.execute(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
    },
  });
}
```

#### G.8.3 路由设计（React Router v6）

```
路由树                         权限        页面组件
─────────────────────────────────────────────────────
/login                       public      LoginPage
/                            protected   DashboardLayout
├── /workflows               protected   WorkflowListPage
├── /workflows/:id/edit      protected   WorkflowEditorPage  ← React Flow
├── /workflows/:id/run       protected   WorkflowRunPage
├── /agents                  protected   AgentListPage
├── /agents/:id/config       protected   AgentConfigPage
├── /models                  protected   ModelConfigPage
├── /conversations           protected   ChatConsolePage
├── /settings                protected   SettingsPage
│   ├── /settings/profile    protected   ProfilePage
│   └── /settings/team       admin       TeamSettingsPage
└── /admin                   admin       AdminDashboardPage
    ├── /admin/tenants       super-admin TenantManagePage
    └── /admin/monitoring    admin       MonitoringDashboardPage
```

```typescript
// router.tsx
const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    path: "/",
    element: <ProtectedLayout />,        // 自动检查 token，无 token 重定向
    children: [
      { index: <Navigate to="/workflows" /> },
      {
        path: "workflows",
        children: [
          { index: <WorkflowListPage /> },
          { path: ":id/edit", element: <WorkflowEditorPage /> },
          { path: ":id/run", element: <WorkflowRunPage /> },
        ],
      },
      {
        path: "agents",
        children: [
          { index: <AgentListPage /> },
          { path: ":id/config", element: <AgentConfigPage /> },
        ],
      },
      { path: "conversations", element: <ChatConsolePage /> },
      {
        path: "admin",
        element: <AdminGuard />,          // 二次权限校验
        children: [
          { index: <AdminDashboardPage /> },
          { path: "tenants", element: <TenantManagePage /> },
        ],
      },
    ],
  },
]);
```

#### G.8.4 权限组件

```typescript
// components/CanAccess.tsx —— 基于 RBAC 的权限守卫
type Permission = "read:workflow" | "write:workflow" | "admin:tenant";

function CanAccess({ permission, children, fallback }: {
  permission: Permission;
  children: ReactNode;
  fallback?: ReactNode;
}) {
  const { user } = useAuthStore();
  const hasPermission = user?.permissions?.includes(permission);

  if (!hasPermission) return fallback ?? null;
  return <>{children}</>;
}

// 用法
<CanAccess permission="write:workflow" fallback={<Tooltip title="无编辑权限" />}>
  <Button onClick={editWorkflow}>编辑</Button>
</CanAccess>

// hooks/usePermission.ts —— 编程式权限检查
export function usePermission() {
  const { user } = useAuthStore();
  return {
    canRead: (p: Permission) => user?.permissions?.includes(p),
    canWrite: (p: Permission) => user?.permissions?.includes(`write:${p}`),
    isAdmin: () => user?.role === "admin",
    isSuperAdmin: () => user?.role === "super_admin",
  };
}
```

#### G.8.5 React Flow 工作流编辑器集成

```typescript
// pages/WorkflowEditor/index.tsx
import { ReactFlow, Background, Controls, MiniMap } from "@xyflow/react";
import "@xyflow/react/dist/style.css";

// 自定义节点类型
const nodeTypes = {
  agentNode: AgentNode,        // Agent 步骤节点
  triggerNode: TriggerNode,    // 触发节点（入口）
  conditionNode: ConditionNode,// 条件分支节点
  endNode: EndNode,            // 结束节点
};

// 自定义边
const edgeTypes = {
  successEdge: SuccessEdge,    // 成功路径（绿色）
  failureEdge: FailureEdge,    // 失败路径（红色）
  fallbackEdge: FallbackEdge,  // 回退路径（橙色虚线）
};

export function WorkflowEditor() {
  const { agents, steps, connections } = useWorkflowStore();
  const dispatch = useWorkflowStore((s) => s.actions);

  return (
    <DndProvider backend={HTML5Backend}>
      <AntDndPanel />            {/* 左侧拖拽面板 */}
      <ReactFlow
        nodes={steps}
        edges={connections}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        onNodesChange={dispatch.onNodesChange}
        onEdgesChange={dispatch.onEdgesChange}
        onConnect={dispatch.onConnect}
        fitView
      >
        <Background />
        <Controls />
        <MiniMap />
      </ReactFlow>
    </DndProvider>
  );
}

// 右侧属性面板（点击节点后展示）
function PropertyPanel() {
  const selectedNode = useWorkflowStore((s) => s.selectedNode);
  if (!selectedNode) return <Empty description="选择一个步骤查看属性" />;

  return (
    <Form>
      <Form.Item label="Agent">
        <AgentSelector />        {/* 下拉选择 Agent */}
      </Form.Item>
      <Form.Item label="超时 (s)">
        <InputNumber min={5} max={300} />
      </Form.Item>
      <Form.Item label="重试次数">
        <InputNumber min={0} max={5} />
      </Form.Item>
      {/* 条件节点额外参数 */}
      {selectedNode.type === "conditionNode" && <ConditionConfig />}
    </Form>
  );
}
```

#### G.8.6 目录结构

```
src/
├── api/                          # API 集成层
│   ├── client.ts                 # axios 实例 + 拦截器
│   ├── workflow.ts               # 工作流 API
│   ├── agent.ts                  # Agent API
│   ├── model.ts                  # 模型 API
│   ├── conversation.ts           # 对话 API
│   └── auth.ts                   # 认证 API
│
├── stores/                       # zustand 状态管理
│   ├── workflowStore.ts
│   ├── agentStore.ts
│   ├── chatStore.ts
│   ├── uiStore.ts
│   └── authStore.ts
│
├── hooks/                        # React Hooks
│   ├── useWorkflows.ts
│   ├── useAgents.ts
│   ├── useConversation.ts
│   ├── usePermission.ts
│   └── useWebSocket.ts
│
├── pages/                        # 页面组件
│   ├── LoginPage.tsx
│   ├── WorkflowListPage.tsx
│   ├── WorkflowEditor/           # React Flow 编辑器（偏大，独立目录）
│   │   ├── index.tsx
│   │   ├── nodes/                # 自定义节点
│   │   │   ├── AgentNode.tsx
│   │   │   ├── ConditionNode.tsx
│   │   │   └── TriggerNode.tsx
│   │   ├── edges/                # 自定义边
│   │   └── panels/               # 侧栏面板
│   ├── AgentConfigPage.tsx
│   ├── ChatConsolePage.tsx
│   └── SettingsPage.tsx
│
├── components/                    # 通用组件
│   ├── CanAccess.tsx              # 权限守卫
│   ├── ModelSelector.tsx          # 模型选择器
│   ├── StreamingText.tsx          # 流式文本展示
│   ├── AgentSelector.tsx          # Agent 选择器
│   └── Layout/                    # 布局组件
│       ├── AppLayout.tsx
│       ├── Sidebar.tsx
│       └── Header.tsx
│
├── router.tsx                     # 路由配置
├── App.tsx
└── main.tsx
```
