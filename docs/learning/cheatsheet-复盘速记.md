# 复盘速记卡（一页纸）

> 从 `00-学习导读.md` 抽出的速记版。适合打印 / 截图随身看。完整讲解回各章。

---

## 一、各章一句话

| 章 | 主题 | 一句话 |
|----|------|--------|
| 01 | DDD 实践 | 聚合根 / 值对象 / 领域事件 / 仓储在 .NET 里长什么样 |
| 02 | 整洁架构 | 依赖方向向内，Domain 零依赖，接口在 Application |
| 03 | MediatR + CQRS | Command 走 UnitOfWork 自动存盘，Query 不存 |
| 04 | EF Core 映射 | 私有构造 + 只读集合 + OwnsOne/OwnsMany + 影子属性 |
| 05 | 测试策略 | ArchTests 兜底架构，BDD 验行为，集成验真依赖 |
| 06 | 常见踩坑 | 31 个真实坑 + 诊断口诀（报错首选翻这篇） |
| 07 | 项目演进 | 为什么 Phase 1→6 是这个顺序 |
| 08 | 决策记录 | 关键选型当时的选项与理由（ADR） |
| 09 | Phase 4 学习 | 把名不副实的承诺真实落地的 5 个知识点 |
| 10 | Phase 5 学习 | 认证/多租户/RBAC/Key 加密/审计等 7 个安全知识点 + 3 个排障 |

**新手阅读顺序**：`00` → `02` → `01` → `03` → `04` → `06` → `05` → `07/08` → `09/10`

---

## 二、复盘自测（合上文档能答 = 真懂）

- **01 DDD**：聚合根为何 `private set`？值对象为何 `record`？领域事件为何不直接 `mediator.Publish`？
- **02 架构**：Domain 为何零 PackageReference？Application 为何不能引用 Infrastructure？接口定义在哪层？
- **03 MediatR**：`ICommand<T>` 标记接口解决啥？Query 为何不触发 `SaveChanges`？
- **04 EF**：只读集合为何 `UsePropertyAccessMode(Field)`？`OwnsMany` 影子主键为何必须 `ValueGeneratedOnAdd()`？值对象列名冲突怎么解？
- **05 测试**：架构测试为何放金字塔最底层？BDD 不解决哪类问题？
- **06 踩坑**：编译错 / 运行炸 / 数据写不进 / 并发不准 / 环境不对，分别先查啥？（口诀见下）
- **07 演进**：为何 Phase 1 全用 Stub？Phase 3 才做前端的原因？
- **08 决策**：模型路由为何选 Flat Priority List？领域事件为何用适配器而非直接 MediatR？
- **09 Phase4**：fail-loud 为何优于 fail-silent？分页为何必须在数据库端？token 为何不等于字符？
- **10 Phase5**：多方案认证为何用 policy scheme？多租户为何"只建 DB Query Filter"不够？`no such table` 但模型有该实体的根因？

---

## 三、报错诊断口诀（来自 06 §6.11）

```
编译报了错     → 查版本号
运行期炸了     → 查 DI 注册
数据写不进去   → 查 EF Core 映射
并发不准       → 查 lock + ConcurrentDictionary
环境不对       → 查 launch-profile + --configuration
跨天不重置     → 查 Singleton 状态重置逻辑
认证 challenge 炸 → 查默认方案 / policy scheme
Swagger 没 Authorize → 查 AddSecurityDefinition
运行时缺表     → 查 EnsureCreated/Migrate 混用
列表整页报错   → 查任一请求 400（take<1 控制器校验早于 handler clamp，见 08 §8.13）
设计决策被推翻   → 查用户真实使用（S3 单条→列表，文档决策可修订，见 08 §8.12）
角色误标自定义   → 查前端硬编码 code 与 DB 对齐（IsBuiltIn 修复，F19）
curl 中文 400   → 查 Git Bash 编码（非后端 bug，用 Python urllib 发请求）
```

---

## 四、一页纸用法

1. 接任务 → 用「一、各章一句话」定位要看的章。
2. 读时看「问题 → 解法 → 代码落点」。
3. 读完合上文档，用「二、复盘自测」自测；答不出回看。
4. **报错直接翻 `06-common-pitfalls.md` 的「按症状查因」表**（最前那张），秒定位坑号。
