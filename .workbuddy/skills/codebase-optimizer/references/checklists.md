# Checklists

本文件由 codebase-optimizer Skill 在 Step 1 分析阶段加载，指导 Agent 按维度扫描代码问题。模板中的 `{占位符}` 表示需要 Agent 动态填充的内容。

## 阶段1（基础质量）：架构 → 代码质量 → 正确性 → 测试

| 维度 | 检查要点 |
|------|----------|
| 🏗️ 架构 | 模块耦合度（按目录分组分析依赖）、循环依赖（追踪import图）、分层清晰度、职责划分、接口设计、过度工程/过度抽象、上帝类/上帝函数（搜索大型文件）、全局状态管理 |
| 🧹 代码质量 | 命名规范一致性（对比代码风格配置）、重复代码（搜索相似代码块/复制粘贴迹象）、函数/方法长度（定位超长函数）、注释缺失或过时、魔法数字（搜索硬编码常量）、异常处理完整性、类型安全 |
| 🐛 正确性 | 空指针/未定义访问（追踪可能的 null/undefined 路径）、资源泄漏（文件/DB/网络连接是否关闭）、竞态条件（共享状态的并发访问）、边界条件、错误处理路径缺失、异步操作错误传递（检查.catch/reject/try-catch） |
| 🧪 测试 | 覆盖率不足的模块（对比测试文件与源码文件的比例/统计 uncovered lines）、测试粒度（检查是否有单个测试方法覆盖多个场景）、过度 Mock（检查 mock 数量是否远超真实依赖数）、断言质量（检查断言是否同时覆盖正向和异常路径）、测试数据维护方式（检查是否有共享的可变 fixture） |

## 阶段2（进阶质量）：性能 → 安全 → 工程化 → 桩代码替换进度 → 生产就绪度

| 维度 | 检查要点 |
|------|----------|
| ⚡ 性能 | 重复计算（搜索循环内不变表达式）、N+1 查询（ORM 关联加载模式）、未缓存的热点路径、大对象频繁创建、IO 密集路径同步阻塞（检查 await/async 使用）、未使用懒加载 |
| 🔒 安全 | 硬编码密钥/令牌/密码（搜索 token/secret/password 字面量）、SQL 注入（搜索拼接 SQL）、XSS（模板中的未转义输出）、认证/授权缺失、敏感日志、依赖中已知漏洞 |
| 📦 工程化 | CI/CD 配置质量（检查 .github/workflows / .gitlab-ci.yml 等 CI 配置）、构建脚本健壮性（检查 package.json scripts / Makefile / gradle 构建命令）、依赖版本锁定（检查 lockfiles / ^ 前缀使用）、lint/prettier 配置（检查配置文件是否存在且生效）、Dockerfile 优化（检查多阶段构建 / 镜像分层）、文档完整性（检查 README / CONTRIBUTING / CHANGELOG） |
| 📋 桩代码替换进度 | **蓝图 Stub 清单核对**：加载项目蓝图或架构文档中标记为"Stub/桩代码/占位实现"的组件列表，逐项核查当前实现状态。每个组件记录：预期替换阶段、实际替换状态（已替换/进行中/未开始）、当前文件路径、是否为生产阻塞项。计算替换完成率（X/Y）。对未替换组件评估生产影响：是否在启用真实实现时需要改接口/配置/部署方式。汇总到 `stub-progress-report` 段 |
| 🚀 生产就绪度 | **检查以下维度的就绪状态**：API 版本控制策略（是否存在且合理，是否有向后兼容机制）、启动时配置验证（缺失关键配置是否快速失败）、优雅降级路径（外部依赖失效时的行为是否明确）、秘密管理审计（密钥/令牌是否有非 dev 默认值守卫）、健康检查完整性（/health 端点是否覆盖关键外部依赖）、依赖版本锁定（有无已知漏洞的包）、部署检查清单（Dockerfile 多阶段构建、资源限制配置、日志采集配置、探针配置）、弹性模式（超时/重试/熔断/限流是否配置恰当、覆盖全面）、跨阶段技术债累积情况（是否有标记为"待后续阶段"但长期未处理的债务） |

## 桩代码替换进度 — 详细核查方法

Step 1 分析阶段遇到本项目时，进行以下额外扫描：

1. **读取项目 README / 蓝图文档**，提取 `Stub` / `桩代码` / `占位实现` 章节，建立完整桩代码清单。
2. **按文件系统验证每个桩代码的当前状态**：
   - 文件是否存在且内容与桩代码实现一致（返回固定值、空实现、`Task.Delay` + 固定字符串）→ `未开始`
   - 文件已替换为真实实现（调用真实 API/DB/服务）→ `已替换`
   - 文件存在但部分真实部分桩代码 → `进行中`
3. **记录替换完成率**（`已替换 / 总数`），及每个未替换组件的预计替换阶段（从蓝图文档提取）。
4. **标记生产阻塞项**：如果桩代码会阻止核心业务流程（如模型调用返回固定值、工作流引擎空转），标记为 `P0-blocking`。

报告格式示例：
```
## 桩代码替换进度
| 组件 | 蓝图预期阶段 | 当前状态 | 文件路径 | 生产阻塞 |
|------|------------|---------|---------|---------|
| PgVectorStore | Phase 4 | ✅ 已替换 | src/.../PgVectorStore.cs | 否 |
| DockerCodeSandbox | Phase 4 | ❌ 未开始 | src/.../DockerCodeSandbox.cs | 是（P0-blocking） |
| NativeToolExecutor | Phase 2 | ⏳ 进行中 | src/.../NativeToolExecutor.cs | 否 |
```

## 生产就绪度 — 详细检查项

Step 1 分析阶段遇到本项时，逐一核查：

| 检查项 | 通过标准 | 核查方法 |
|--------|---------|---------|
| API 版本控制 | 使用 Asp.Versioning.Mvc 或等价方案，URL 含 v1/v2 | grep URL 路由、项目依赖 |
| 启动配置验证 | 缺失关键配置时启动失败（而非运行时爆炸） | 检查 Program.cs 或 startup 中的验证逻辑 |
| 优雅降级 | 外部依赖不可用时明确降级行为记录日志，不级联崩溃 | 搜索 try-catch / fallback / degrade 模式 |
| 秘密管理 | 无硬编码密钥，dev 默认值有生产守卫 | 搜索 password/secret/key 字面量，检查启动守卫 |
| 健康检查 | /health 端点覆盖所有关键外部依赖 | 检查 AddHealthChecks + 自定义检查 |
| 依赖漏洞 | dotnet list package --vulnerable 无已知漏洞 | 运行检查命令 |
| 部署配置 | Dockerfile 多阶段构建、资源限制、日志采集 | 检查 Dockerfile / docker-compose.yml |
| 弹性模式 | 外部调用有超时+重试+熔断+限流，配置可调 | 搜索 ResiliencePipeline / Polly / 超时配置 |
| 跨阶段债 | 蓝图中标记"待后续阶段"的组件已清理超 50% | 对比蓝图"阶段X范围"与实际代码 |

## 前端专项检查（React / TypeScript）

当扫描范围包含前端源码（`.ts`/`.tsx`）时，在以下维度补充前端专项检查项（与后端七维度并行，同一轮内分别产出前后端发现清单，统一去重后排期修复）：

| 维度 | 前端专项检查要点 |
|------|----------------|
| 🏗️ 架构 | 组件/页面/store/hook 目录分层是否清晰；跨目录循环 import；全局状态（Zustand/Redux store）是否过度集中；组件是否承担过多职责（上帝组件） |
| 🧹 代码质量 | `any`/`unknown` 滥用（搜 `: any`、`<any>`）；超大组件（>200 行）；列表渲染缺 `key`（`map(...)` 内无 `key` 属性）；残留 `console.log`/`debugger`；魔法字符串（硬编码路由/枚举值）；重复组件/工具函数 |
| 🐛 正确性 | XSS：`dangerouslySetInnerHTML`、直接 `innerHTML=`、未转义拼接 DOM；未捕获 Promise（`.then` 无 `.catch` / `async` 无 `try-catch`）；`useEffect` 缺清理函数（订阅/定时器/事件监听未移除）；hook 依赖缺失（`useEffect`/`useMemo`/`useCallback` 依赖数组与实际引用不一致）；空/undefined 访问（`obj.x.y` 无守卫） |
| 🧪 测试 | vitest 单测覆盖率（`npx vitest run --coverage`）；e2e（playwright）是否覆盖关键流程；是否仅测 happy path 缺异常路径；`toBeTruthy()` 等弱断言 |
| ⚡ 性能 | 列表大数组未虚拟滚动；`useMemo`/`useCallback` 误用导致重渲染；大依赖未懒加载（`React.lazy`）；effect 内重复计算；同步阻塞渲染的重计算 |
| 🔒 安全 | 前端硬编码密钥/`API Key`/`token`（env 误暴露到 `import.meta.env` 之外的字面量）；URL/query 参数注入到渲染；`dangerouslySetInnerHTML` 注入用户内容 |
| 📦 工程化 | eslint/prettier 配置存在且生效（`eslint.config.js` + `npx eslint .`）；`tsconfig` `strict: true`；`package-lock.json` 锁定且 `package.json` 无 `^`/`~` 漂移；幽灵依赖（import 但未声明）/声明未用依赖（depcheck）；CI 是否跑前端 QA |
| 🚀 生产就绪度 | 前端构建产物是否带 sourcemap 泄露；错误边界（`ErrorBoundary`）是否覆盖；环境变量区分 dev/prod；懒加载与代码分割；打包体积（`vite build` 警告） |

> 前端验证命令：优先调项目统一 QA 脚本（本仓库 `src/AgentPlatform.Web/scripts/qa.mjs` → `node scripts/qa.mjs`，可选 `--e2e`），其顺序跑 typecheck→lint→build→unit，产出 `qa-report.json`；无统一脚本时分别调 `npx tsc --noEmit` / `npx eslint .` / `npx vitest run` / `npx playwright test`。
