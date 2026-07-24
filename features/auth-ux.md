# F2 · 登录与鉴权态一致性

> Feature 史诗（Tier 1），优先级 **P1**，风险 **⚠️ 高风险（auth）**。
> 分支：`feat/f2-login-auth-state`（从 `master` 新建，所有改动落此分支）。
> 状态：doing（设计已建，实现前需用户确认红线范围）。

## 1. 目标
登录凭证真实校验、401 不破坏 SPA、鉴权态前后一致。

## 2. 现状核准（2026-07-24 实测，已校正漂移）
- **`LoginPage.tsx`**：当前**无密码输入框**（仅邮箱 `admin@acme.io` 默认值，行 13）。`handleLogin` 调 `devLogin({role:'Admin', userId:email})`（行 20），成功把 JWT 写 `localStorage.auth_token`（行 21）；失败走 demo 降级 `login(undefined, email)`（行 27，无令牌）。→ B6 原「密码形同虚设」已演变为「无密码字段」。
- **`api.ts` 响应拦截器（行 40-46）**：**无任何 401 处理**，仅 `console.error` + `Promise.reject`。→ O2「整页跳转破坏 SPA」实际是「裸 reject，无跳转兜底」，SPA 内无优雅登出/跳登录。
- **`appStore.ts`**：令牌存 `localStorage`（`TOKEN_KEY='auth_token'`，行 4/29），`login/logout` 读写字面量。→ O8 XSS 风险面。
- **`api.ts getAuthToken()`（行 161-163）**：从 `localStorage.auth_token` 读 Bearer。
- **后端 `DevLoginEndpoint.cs`**：`POST /api/dev/login`，仅 `{TenantId?, Role?, UserId?}`，**无密码**；JWT Bearer 发行，无 cookie。`AuthConfiguration.cs` 仅 Bearer/API-Key 两种 scheme。
- **FE↔BE 路由漂移（待核实）**：FE `devLogin` → `POST /api/v1/auth/dev-login`（baseURL `/api/v1`），但后端映射 `/api/dev/login`。若代理无重写，dev-login 实际 404 → 永远走 demo 降级。本 feature 不修此漂移（属独立 bug），仅在 O3 处理 demo 一致性。

## 3. 验收子项与风险分级
| 子项 | 内容 | 范围 | 自主可行？ |
|------|------|------|-----------|
| **O2** | 401 不破坏 SPA：派发事件/回调，路由层用 `<Navigate to="/login">` 优雅跳登录，而非整页刷新或无兜底 | 纯前端 | ✅ 可自治 |
| **O3** | demo 路径鉴权态一致：demo 登录写占位 token 或 store 置 `demo` 标志跳过 401 跳，避免 demo 下误跳登录 | 纯前端 | ✅ 可自治 |
| **B6 (UI 层)** | 加密码输入框，明确标注「开发模式密码不参与校验」（后端 dev-login 仅校验邮箱） | 纯前端 | ✅ 可自治（仅 UI + 标注） |
| **B6 (真实验密)** | 后端真实校验密码 | **后端** | ❌ 红线：需建用户/密码存储 + 登录端点验密，属 Phase 6 后端 feature |
| **O8** | JWT `localStorage` → `httpOnly`+`SameSite` Cookie | **后端+前端** | ❌ 红线：后端发 Set-Cookie + 改 Auth handler 读 cookie + CORS credentials + SSE 改 cookie，影响面大 |

## 4. 接口契约（仅本 feature 自治子集 O2/O3/B6-UI）
无后端契约变更。前端内部：
- 新增 401 事件机制：`api.ts` 响应拦截器检测 `error.response?.status === 401` → `window.dispatchEvent(new CustomEvent('auth:unauthorized'))`；`App.tsx` 监听 → `navigate('/login', {state:{from:location}})`。
- `appStore` 新增 `isDemo: boolean`；`login(token?, email?, demo?)`；demo 模式下发占位 token（`demo.<random>`）使 `getAuthToken()` 非 null 但后端忽略/放行，或 store 置 `isDemo` 并在 401 监听中跳过跳转。推荐后者（不改请求体）。
- `LoginPage`：加密码 `Input.Password`（默认空），登录按钮提交 `{role, userId:email}`；密码框下方标注「开发演示：密码不参与校验」。

## 5. 数据模型
- `AppState` 新增 `isDemo: boolean` 字段（zustand）。

## 6. 阶段 checklist（质量门用，Phase 5 嵌入）
- [ ] O2：401 → SPA 内 `<Navigate>` 跳转，无整页刷新（e2e 加 `auth/unauthorized.spec` 验证受保护页直接访问跳登录）。
- [ ] O3：demo 登录后 `isDemo=true`，访问受保护页不误跳；真实令牌过期/无效 → 跳登录。
- [ ] B6-UI：密码框存在且标注；提交仍走 dev-login（邮箱）。
- [ ] 模型一致性：无后端契约变更，tsc + `node scripts/qa.mjs` 四闸门全绿。
- [ ] 三道质量门禁（ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer）0 open。
- [ ] 提交 `feat/f2-login-auth-state` 分支，`.quality-gate.json` cleared:true。

## 7. 后端实现设计（B6 真实验密 + O8 Cookie 鉴权）
> 用户已确认范围（含 B6 真实验密 + O8）。以下为后端改造设计，实现前落档。

### 7.1 新增 `User` 聚合（Domain）
- 仿 `ApiKey` 聚合：实现 `ITenantScoped` + `IAggregateRoot`，自动获得每请求租户隔离（`OnModelCreating` 全局过滤器 + `ApplyConfigurationsFromAssembly`）。
- 属性：`Id(Guid)`、`TenantId(Guid)`、`Email(string, 每租户唯一)`、`PasswordHash(string)`、`Role(string)`、`IsActive(bool)`、`CreatedAt(DateTimeUtc)`。
- 工厂 `User.Create(tenantId, email, passwordHash, role)`；口令仅经 `IPasswordHasher` 哈希后入库，绝不存明文。

### 7.2 密码哈希 `IPasswordHasher`（Infrastructure.Security）
- 用内置 `System.Security.Cryptography.Rfc2898DeriveBytes`（PBKDF2，SHA-256，迭代 ≥ 100_000），**不引新 nuget**（规避镜像 fork 风险）。
- `Hash(password)→string`（含 salt+参数，格式 `$pbkdf2$v=...$salt$hash`）；`Verify(password, hash)→bool`。

### 7.3 JWT 发行抽出 `IJwtTokenService`（Infrastructure.Security）
- 把 `DevLoginEndpoint.cs:42-52` 的密钥读取/签名/写 token 抽成共享服务，dev-login 与真实登录端点复用，单一来源。

### 7.4 真实登录端点 `POST /api/v1/auth/login`
- 入参 `{ email, password }`；`AllowAnonymous`。
- 流程：按 `tenantId`(TenantProvider) + `email` 查 `User` → 不存在/未激活/`Verify` 失败 → `401`；成功 → `IJwtTokenService.CreateToken`（claims 含 `sub/name=email, role, tenant_id`）→ **Set-Cookie `ap_access_token`**（见 7.5）→ 返回 `{ user:{id,email,role,tenantId} }`（body 也带，便于前端首屏）。
- 新增 `GET /api/v1/auth/me`：从 `HttpContext.User` claims 返回当前身份；匿名 → `401`。替代前端 `decodeJwt` 客户端解码（cookie 为 HttpOnly，JS 不可读）。

### 7.5 O8 Cookie 发行与读取
- 登录端点：`Response.Cookies.Append("ap_access_token", token, new CookieOptions { HttpOnly=true, SameSite=SameSiteMode.Lax, Secure=!IsDevelopment&&!IsQuickStart, Path="/", MaxAge=1h })`。
- `AuthConfiguration`：Smart `ForwardDefaultSelector` 增加「存在 `ap_access_token` cookie → 转 Bearer」；JwtBearer `OnMessageReceived` 从 cookie 读 `ap_access_token` 写入 `context.Token`（保留 Authorization header 兼容 API-Key/Swagger）。
- 现有 dev-login（`/api/dev/login`，`DevLoginEnabled` 门控）保留不动；FE 不再调用它（改调 `/auth/login`）。

### 7.6 CORS 改造（Security）
- `InfrastructureConfiguration.cs` 默认 policy：去掉 `AllowAnyOrigin`，改为 `WithOrigins(Cors:AllowedOrigins)` + `AllowCredentials()`；配置为空时回退 `["http://localhost:5173","https://localhost:5173"]`（dev 代理同源，cookie 可发）。
- `AllowAnyOrigin`+`AllowCredentials` 互斥，此改修复原有安全气味。

### 7.7 DB 种子（DatabaseInitializer.SeedDataAsync）
- `if (await _context.Users.CountAsync()==0)` → `AddRangeAsync(new User(DefaultTenantId, "admin@acme.io", hasher.Hash("<默认密码，写文档>"), "Admin"))` + `SaveChangesAsync`。
- 默认密码在 `docs`/设计文档明确标注，生产须改。

### 7.8 迁移
- `dotnet ef migrations add AddUserAggregate`（自动生成 + 更新 Snapshot；遵循 `#pragma warning disable IDE0161` 模板）。

## 8. 前端实现设计（O2/O3/B6-UI + Cookie 适配）
- `api.ts`：`axios.create({ withCredentials:true })`；**移除 Bearer 注入**（cookie 自动携带）；新增 `loginRequest(email,password)`→`POST /auth/login`、`getAuthMe()`→`GET /auth/me`。`getAuthToken`/`decodeJwt` 仅保留（decodeJwt 不再用于身份展示）。
- `appStore.ts`：**移除 localStorage 令牌读写**；`login(demo?)`：真实登录后由 `/auth/me` 回填身份；demo 模式 `login(undefined,email,true)` 置 `isDemo=true`+`isAuthenticated=true`（无 cookie）；初始化时 `getAuthMe()` 回填（401→`isAuthenticated=false`）。
- `LoginPage.tsx`：加 `Input.Password`（默认空）+ 标注「开发演示默认密码见文档」；主按钮→真实 `/auth/login`；新增「使用本地演示会话」按钮→demo。
- `App.tsx`/`AppLayout`：响应拦截器 401 → `window.dispatchEvent(new CustomEvent('auth:unauthorized'))`；`AppLayout` 监听→（非 demo）`navigate('/login')`（O2，SPA 内跳，不整页刷新）。
- `WorkflowDetailPage.tsx:86` SSE fetch：`credentials:'include'`，删 Bearer 头；`ExecutionLogDetailPage.tsx:50` EventSource 加 `withCredentials:true`。

## 9. 风险与已决策项
- **已决策（用户确认全范围）**：B6 真实验密 + O8 Cookie 均实现；demo 模式保留但仅本地、不误跳。
- **安全默认值（生产须覆盖）**：Cookie `Secure` 在 dev/QuickStart 下为 false（http 可用），prod 自动 true；`SameSite=Lax`；CORS 默认 origins 含 `localhost:5173`；默认种子密码须生产替换；`JwtSecretKey` 启动守卫已强制非 dev 默认。
- FE↔BE dev-login 路由漂移（§2）不属本 feature 修复（FE 改调 `/auth/login`，dev-login 端点保留但不再被前端调用）。
- demo 模式本质无真实鉴权，O3 仅保证「不误跳」，不等于安全。

## 10. 质量门禁记录（feature-builder Phase 5）
- **ddd-code-reviewer**：发现并修复 2 项（均 P3，非阻塞）+ 补测试。
  - 登录 JWT 未发 `ClaimTypes.Email` 声明 → 已在 `AuthEndpoints.cs` 登录 claims 增加 `new Claim(ClaimTypes.Email, user.Email)`（此前 `/auth/me` 靠 `sub` 兜底，现显式）。
  - `Pbkdf2PasswordHasher` 无单测 → 新增 `Infrastructure.Tests/Security/Pbkdf2PasswordHasherTests.cs`（5 例：哈希可验/错密码拒/篡改拒/畸形容错/盐化差异）。后端 `dotnet test` 209 passed / 0 failed。
- **ddd-phase-quality-gate**：12 类审计 **PASS**（P0=P1=P2=P3=0）。DI 注册完整（`IJwtTokenService`→Api `Program.cs:48`；`IPasswordHasher`/`IUserRepository`→Infrastructure DI:208-209）；EF 映射同步（`UserConfiguration` + `DbSet<User>`）；无休眠/死代码（`decodeJwt`/`getAuthToken` 已清理）。
  - **Waiver（P2，已知限制）**：登录按 `TenantProvider` 默认租户查询用户，非默认租户用户暂无法登录（多租户登录需租户选择 UI，属结构性决策）。接受风险：当前为单默认租户 dev 部署，不阻塞；目标阶段：后续多租户登录 feature。
  - **注意（非缺陷）**：`DatabaseInitializer` 种子默认密码 `Admin@123456` 为开发种子，生产须改；已 `LogWarning` 提示。
- **codebase-optimizer**：见下方提交记录 / `.quality-gate.json`。

## 11. 验收（F2 全范围）
- [x] B6 真实密码校验：新增 `User` 聚合 + `IPasswordHasher`(PBKDF2) + `POST /api/v1/auth/login` 验密发 Cookie。
- [x] O8 Cookie 鉴权：`ap_access_token` httpOnly+SameSite=Lax+Secure(HTTPS)；Auth handler 从 cookie 读 JWT；CORS 去 AllowAnyOrigin 改显式 origins+AllowCredentials。
- [x] O2 401 SPA 内跳转：响应拦截器派发 `auth:unauthorized` → `App.tsx` 监听 `navigate('/login')`（不整页刷新）。
- [x] O3 demo 一致性：`isDemo` 标志，demo 不误跳登录。
- [x] B6-UI：登录页加密码框 + 演示模式按钮。
- [x] 模型一致性：FE `types/index.ts` 增 `AuthUser/LoginRequest/LoginResponse` 与后端 DTO 对齐；tsc + `dotnet test` + `node qa.mjs` 全绿。
- [x] 三道质量门禁 0 open（codebaseOptimizer 见 .quality-gate.json）。
