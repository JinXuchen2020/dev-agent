# F2 质量门禁报告 — 登录与鉴权态一致性

- **Feature**: F2（features/auth-ux.md）
- **分支**: `feat/f2-login-auth-state`
- **日期**: 2026-07-23
- **范围**: B6 真实验密 + O8 httpOnly Cookie 鉴权 + O2(401 SPA 内跳转) + O3(demo 一致性) + B6-UI(密码框)
- **三道门禁**: `ddd-code-reviewer` → `ddd-phase-quality-gate` → `codebase-optimizer`

## 1. 实现摘要

### 后端（鉴权重构）
- 新增 `User` 聚合（`ITenantScoped`+`IAggregateRoot`，自动获每请求租户隔离）、`UserConfiguration`、`IUserRepository`/`UserRepository`。
- 新增 `IPasswordHasher`/`Pbkdf2PasswordHasher`（PBKDF2-SHA256，盐 16B，迭代 100k，固定时间比对）。
- 抽取 `IJwtTokenService`/`JwtTokenService`（HMAC-SHA256，配置驱动）。
- 新增 `AuthEndpoints`：`POST /api/v1/auth/login`（验密 → 设 `ap_access_token` httpOnly cookie）、`GET /api/v1/auth/me`（从 cookie 解析身份）、`POST /api/v1/auth/logout`（清 cookie）。
- `AuthConfiguration` Smart 策略从 cookie 读 JWT；`InfrastructureConfiguration` CORS 去 `AllowAnyOrigin` 改 `WithOrigins`+`AllowCredentials`。
- `DatabaseInitializer` 幂等种子默认用户 `admin@acme.io / Admin@123456`；新增 EF 迁移 `20260724005511_AddUserAggregate`。

### 前端（cookie 适配）
- `api.ts`：`axios.create({withCredentials:true})`；移除 Bearer 注入；401 派发 `auth:unauthorized` 事件；新增 `loginRequest`/`getAuthMe`/`logoutRequest`。
- `appStore`：去 localStorage；`bootstrapAuth()` 启动探活；`loginReal`/`loginDemo`/`logout`。
- `LoginPage`：加 `Input.Password` + 真实登录 + 演示会话按钮。
- `ProtectedRoute` 等 `authBootstrapped` 再决策；`App` 监听 `auth:unauthorized` 跳转 `/login`；SSE fetch/EventSource 加 `credentials:'include'`。

## 2. 门禁结果

### 2.1 ddd-code-reviewer（对抗式审查）— PASS
前期修复 2 项 P3：
- 登录 JWT 补 `ClaimTypes.Email`（原仅 Name/NameIdentifier）。
- 补 `Pbkdf2PasswordHasherTests`（5 例：可验/错密码拒/篡改拒/畸形容错/盐化差异）。
最终 **P0/P1/P2=0，P3=0**，后端 `dotnet test` 209 → **214 passed**。

### 2.2 ddd-phase-quality-gate（12 类结构门）— PASS
- P0/P1/P2/P3 = 0。
- 12 类审计全扫：硬编码值 0 / 死代码 0 / any 0 / XSS 0 / hook 依赖已 disable / React key 完备 / 未用依赖 0。
- 1 项 **P2 waiver**：多租户登录（按默认租户查用户，非默认租户用户暂无法登录）→ 目标后续「多租户登录」feature。

### 2.3 codebase-optimizer（七维度 + 前端专项）— PASS（Round F2-01，0 open）
- 发现 4 项（P2×3 + P3×1）全部修复：缩进修复、`User.cs` 注释、新增 5 例 `AuthEndpointsTests`、修复 Test 环境不种子默认用户的根因（`ApiContractTestFactory` 钉死 `Tenant:DefaultTenantId` + fixture 幂等种子）。
- 回归：后端 **214 passed / 0 failed**；前端 `node scripts/qa.mjs` **OVERALL PASS**（typecheck/lint/build/unit）。
- 详细见 `.codebase-optimizer/rounds/round-f2-01-report.md` 与 `final-summary-f2.md`。

## 3. 验证数据
| 项 | 结果 |
|----|------|
| 后端 `dotnet build` | 0 警告 0 错误 |
| 后端 `dotnet test` | 214 passed / 0 failed |
| 前端 `node scripts/qa.mjs` | PASS（4/4 闸门） |
| 三道门禁 | 全部 PASS |

## 4. 已知遗留（非阻断）
1. **密钥生产化**：`JwtSecretKey`/`AesEncryptionKey` 含 dev 兜底值，生产须环境变量覆盖。
2. **多租户登录**：P2 waiver（见 2.2）。
3. **种子默认密码**：`admin@acme.io / Admin@123456` 仅 `LogWarning` 提示生产修改。

## 5. 结论
F2 全范围（后端鉴权重构 + 前端 cookie 适配）实现完成，三道质量门禁全部 PASS，可进入提交与收尾。
