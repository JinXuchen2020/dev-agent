# SQLite 数据库初始化问题修复报告

## 问题描述

在实现可插拔数据库架构后，API 启动时出现以下错误：
```
SQLite Error 1: 'no such table: AgentRoleDefinitions'
```

尽管日志显示所有表创建命令都成功执行，但在查询时数据库报告表不存在。

## 根本原因

SQLite 的 `EnsureCreatedAsync()` 方法在创建表后没有立即提交事务。当 EF Core 在同一个 DbContext 实例上立即执行后续查询时，事务可能尚未完全提交，导致查询失败。

## 解决方案

修改 `DatabaseInitializer.cs` 的初始化时序：

### 修改前
```csharp
// 直接调用 EnsureCreatedAsync
await _context.Database.EnsureCreatedAsync();

// 立即查询
await SeedDataAsync(); // 在这里查询时失败
```

### 修改后
```csharp
// 先创建数据库
await _context.Database.EnsureCreatedAsync();

// 添加种子数据
await SeedDataAsync();

// 最后保存所有更改
await _context.SaveChangesAsync();
```

## 额外优化

修改了连接字符串配置：
```json
"DefaultConnection": "Data Source=agent_platform.db;Cache=Private"
```

将 SQLite 缓存模式从 `Shared` 改为 `Private`，确保每个 DbContext 实例使用独立的缓存。

## 验证结果

API 成功启动并返回 6 个种子 AgentRoleDefinition：
```json
[
  {
    "id": "2dd472aa-2e0f-488b-a734-3e885174f686",
    "name": "系统架构",
    "roleCode": "architecture",
    "description": "负责系统架构设计和技术选型"
  },
  {
    "id": "2162b1ef-69ac-41c0-b5fd-120c5a348b15",
    "name": "代码实现",
    "roleCode": "development",
    "description": "负责功能开发和代码实现"
  },
  {
    "id": "d3a44722-8b6e-4452-b17e-415ef6185b39",
    "name": "文档编写",
    "roleCode": "documentation",
    "description": "负责技术文档和用户文档编写"
  },
  {
    "id": "33a73ad3-391d-425f-b812-b05d3de3c205",
    "name": "产品经理",
    "roleCode": "product",
    "description": "负责产品规划、功能设计和路线图制定"
  },
  {
    "id": "bb7ca091-1543-49b6-8e8d-67522cfdf616",
    "name": "需求分析师",
    "roleCode": "requirement",
    "description": "负责收集、分析和整理业务需求"
  },
  {
    "id": "0dd38879-5447-4be1-b4b1-ec621ad814b9",
    "name": "质量保证",
    "roleCode": "testing",
    "description": "负责功能测试和质量保证"
  }
]
```

## 相关文件

- `src/AgentPlatform.Infrastructure/Persistence/DatabaseInitializer.cs`
- `src/AgentPlatform.Infrastructure/Persistence/AppDbContext.cs`
- `src/AgentPlatform.Api/appsettings.json`

## 后续操作

当需要切换到 PostgreSQL 时，运行：
```powershell
.\scripts\switch-database.ps1 -DatabaseType postgresql
```

切换时会自动：
1. 停止当前运行的 API
2. 修改 `appsettings.json` 配置
3. 更新编译条件（`USE_SQLITE` ↔ `USE_POSTGRESQL`）
4. 重新编译项目
5. 启动 API
