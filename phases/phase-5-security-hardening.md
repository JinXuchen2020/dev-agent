# 阶段五：安全加固（上线硬门槛 / launch-blocking）

> 学习目标：把平台从"无鉴权、单租户硬编码"补成"可受控上线的多租户安全底座"。
> **本阶段为 launch-blocking——任何多用户 / 对外部署前必须完成，不得与前沿特性（阶段六）并行跳过。** 蓝图 §9 铁律："安全是第一优先级，不是以后再补"，且原规划在阶段二-三，现已延期，必须在本阶段补齐。

## 学习目标

- [ ] **ASP.NET Core 认证与授权**：JWT Bearer / API-Key 网关中间件的真实接入（`UseAuthentication` / `UseAuthorization`）
- [ ] **RBAC**：Admin / Operator / Viewer 三级角色 + `[Authorize(Roles = "...")]` 端点约束
- [ ] **真实多租户隔离**：`TenantProvider` 改为 per-request 解析（JWT claim / header），激活已建好的 EF Global Query Filter
- [ ] **速率限制**：ASP.NET Core Rate Limiting（每租户 + 每 API Key）
- [ ] **Prompt 注入防护**：入站消息清洗 + 系统指令边界保护 + 外部工具输入校验
- [ ] **审计日志**：`AuditLog` 实体 + 只追加写入 + 关键操作全覆盖
- [ ] **API Key 加密**：AES-256-GCM 加密存储，明文永不落库

## 前置依赖

- [ ] 阶段四已完成并提交
- [ ] 已确认部署形态（内部受控 / 对外 SaaS）——决定 RBAC 粒度与是否启用多租户

## 任务清单

- [ ] **认证中间件**：实现 JWT Bearer 或 API-Key 网关；`Program.cs` 加 `UseAuthentication` / `UseAuthorization`；所有 Controller / 最小 API 加 `[Authorize]` 兜底。🔍 强制 `ddd-code-reviewer`：核对无匿名遗留端点（health/metrics 除外）。
- [ ] **真实多租户解析**：改 `TenantProvider.GetTenantId()` 从 `IHttpContextAccessor` / 认证主体取当前租户（不再返回 `DefaultTenantId`）；确认 `AppDbContext` 为 scoped 且 Global Query Filter 自动按请求租户生效。🔍 强制 `ddd-phase-quality-gate`：核对 DI 作用域 / 密封 / 空守卫。
- [ ] **RBAC**：`ApplicationUser` + 角色种子；敏感端点 `[Authorize(Roles = "Admin")]`；非管理员仅能操作本租户数据。
- [ ] **速率限制**：`AddRateLimiter` + 每租户 / 每 Key 策略，全局 `UseRateLimiter`，超限返回 429。
- [ ] **Prompt 注入防护**：入站用户消息清洗 + 系统提示边界标记；`SkillPackage` / 外部工具输入校验，阻断 `ignore previous instructions` 类指令。
- [ ] **审计日志**：`AuditLog` 实体（`AuditActionType` 枚举、只追加、不暴露 Delete 接口）；Repository + 写入拦截（谁 / 何时 / 调用哪个模型 / 消耗多少 token）。
- [ ] **API Key 加密**：AES-256-GCM 加密后存 PostgreSQL；密钥从环境变量 / Key Vault 读取；明文仅存内存。
- [ ] **内部上线兜底**：若完整阶段五未完，至少先落地「最小 API-Key 网关 + `TenantProvider` per-request 解析」挡住"任何人可调任意 API"，再视情况补齐 RBAC / 审计 / 加密。

## 验收标准

1. 无有效凭证调用任意 API → 401；除健康检查 / metrics 外无 `[Authorize]` 遗留匿名端点。
2. 跨租户查询被 Global Query Filter 自动拦截（不同租户 token 取不到他租户数据）。
3. 单租户并发触发 Rate Limiter 返回 429。
4. 关键操作（建 Agent / 跑工作流 / 用 Key）写入 `AuditLog`，不可删改。
5. 模型 API Key 库内为密文，明文仅存于内存 / 环境变量。
6. 注入探测类输入被清洗 / 拒绝，不污染系统提示。

▶ **设计评审关（动手前强制）**：进入本 Phase 前须已过 `blueprint-architecture-review`（见 phase-1 §0-1）。认证 / 多租户解析 / 审计属"叙事性安全能力"，合入前强制 `ddd-code-reviewer`；DI / EF / 加密存储走 `ddd-phase-quality-gate`。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性安全能力"的模块（认证中间件 / 多租户解析 / RBAC / 审计 / 密钥加密——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图 §9、依赖是否真实使用、是否真接入管道、是否留匿名后门 |
| 纯基础设施 / 结构卫生模块（仓储 / DI / EF 映射 / 配置 / CI） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名 / 接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against §9.1 / §9.2 / §9.3 / §9.5 / 阶段五验收标准"）。缺此项即视为未通过。

### Phase 5 强制范围（高风险叙事性模块）

- **认证与授权**：核对 §9.1；重点验证无匿名遗留端点、`[Authorize]` 真正生效、JWT / API-Key 校验真实接入管道。
- **真实多租户解析**：核对 §9 多租户隔离；重点验证 Global Query Filter 按请求租户生效、跨租户不可越权、`TenantProvider` 不再硬编码 `DefaultTenantId`。
- **审计 / Key 加密**：核对 §9.2 / §9.5；重点验证明文不落库、审计只追加不可删改、加密算法为 AES-256-GCM。

> 规划提示：阶段五为 launch-blocking，本 §0 要求在此阶段启动前即明确——上述安全模块合入前**必须**走 `ddd-code-reviewer`。

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：
- **完成日期**：
- **完成度**：█░░░░░░░░░ 0%

## 回顾（完成后填写）

### 做得好的

### 下次改进

### 对蓝图文档的反馈
