# 可插拔数据库架构使用指南

## 概述

本平台支持在 **SQLite** 和 **PostgreSQL** 之间无缝切换，无需修改代码。通过配置文件即可在运行时选择数据库类型。

---

## 配置方法

### 1. SQLite 配置（默认）

编辑 `src/AgentPlatform.Api/appsettings.json`：

```json
{
  "Database": {
    "Type": "sqlite"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=agent_platform.db;Cache=Shared"
  }
}
```

**特点：**
- ✅ 无需安装数据库服务器
- ✅ 开发和测试环境首选
- ✅ 数据库文件：`agent_platform.db`
- ✅ 自动创建数据库（如果不存在）

---

### 2. PostgreSQL 配置

编辑 `src/AgentPlatform.Api/appsettings.json` 或使用单独的配置文件：

```json
{
  "Database": {
    "Type": "postgresql"
  },
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=agent_platform;Username=postgres;Password=your_password"
  }
}
```

**或者使用配置文件切换：**

创建 `src/AgentPlatform.Api/appsettings.PostgreSQL.json`：

```json
{
  "Database": {
    "Type": "postgresql"
  },
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=agent_platform;Username=postgres;Password=your_password"
  }
}
```

**使用方式：**
- 命令行启动时指定配置文件：
  ```bash
  dotnet run --configuration appsettings.PostgreSQL.json
  ```

---

## 切换数据库步骤

### 从 SQLite 切换到 PostgreSQL

1. **停止应用程序**

2. **修改配置**
   ```json
   {
     "Database": {
       "Type": "postgresql"
     },
     "ConnectionStrings": {
       "PostgreSQL": "Host=localhost;Database=agent_platform;Username=postgres;Password=your_password"
     }
   }
   ```

3. **运行数据库迁移**（如果需要）
   ```bash
   cd src/AgentPlatform.Infrastructure
   dotnet ef migrations add InitPostgreSQL
   dotnet ef database update
   ```

4. **启动应用程序**
   ```bash
   cd src/AgentPlatform.Api
   dotnet run
   ```

---

### 从 PostgreSQL 切换到 SQLite

1. **停止应用程序**

2. **修改配置**
   ```json
   {
     "Database": {
       "Type": "sqlite"
     },
     "ConnectionStrings": {
       "DefaultConnection": "Data Source=agent_platform.db;Cache=Shared"
     }
   }
   ```

3. **重新启动应用程序**
   ```bash
   cd src/AgentPlatform.Api
   dotnet run
   ```

4. **（可选）删除 SQLite 数据库文件**
   ```bash
   rm agent_platform.db
   ```

---

## 数据库文件位置

### SQLite
- **开发环境**：`src/AgentPlatform.Api/agent_platform.db`
- **生产环境**：可通过环境变量修改路径

### PostgreSQL
- **连接字符串配置**：在 `ConnectionStrings:PostgreSQL` 中指定
- **默认位置**：取决于 PostgreSQL 服务器配置

---

## 技术实现

### 数据库提供程序架构

```
IDatabaseProvider 接口
├── SqliteDatabaseProvider      (SQLite 实现)
└── PostgreSQLProvider          (PostgreSQL 实现)
```

### DI 注册

在 `DependencyInjection.cs` 中：

```csharp
var dbType = configuration["Database:Type"] ?? "sqlite";
var connectionString = dbType.ToLowerInvariant() == "sqlite"
    ? configuration.GetConnectionString("DefaultConnection")
    : configuration.GetConnectionString("PostgreSQL");

services.AddDbContext<AppDbContext>(options =>
{
    if (dbType.ToLowerInvariant() == "sqlite")
    {
        options.UseSqlite(connectionString);
    }
    else if (dbType.ToLowerInvariant() == "postgresql")
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});
```

---

## 优势

### 1. 开发效率
- ✅ SQLite 开箱即用，无需安装数据库服务器
- ✅ 快速启动和停止
- ✅ 方便版本控制（数据库文件可纳入 git）

### 2. 测试环境
- ✅ 可以为每个测试创建独立的数据库文件
- ✅ 测试完成后自动清理

### 3. 生产环境
- ✅ PostgreSQL 提供生产级特性
- ✅ 并发支持、事务隔离、数据完整性
- ✅ 支持大规模数据和高并发访问

---

## 性能对比

| 特性 | SQLite | PostgreSQL |
|------|--------|------------|
| 并发访问 | 低（文件锁） | 高（多连接） |
| 数据量 | 小型应用 | 大型应用 |
| 性能 | 快（简单查询） | 稳定（复杂查询） |
| 事务支持 | 支持 | 支持且强大 |
| 数据完整性 | 支持 | 支持且更强 |

---

## 常见问题

### Q: 切换数据库后需要迁移数据吗？
**A:**
- SQLite → PostgreSQL：需要手动导出/导入数据
- PostgreSQL → SQLite：数据可能无法直接迁移（数据类型差异）

### Q: 可以同时使用两种数据库吗？
**A:** 不可以。同一时间只能使用一种数据库类型。

### Q: SQLite 数据库文件在哪里？
**A:** 默认位置为应用程序工作目录下的 `agent_platform.db`。

### Q: 生产环境应该使用哪个数据库？
**A:** 推荐使用 PostgreSQL，它提供更好的性能和可靠性。

---

## 最佳实践

1. **开发环境**：使用 SQLite，方便快速开发和测试
2. **测试环境**：使用 SQLite，每个测试用例独立的数据库文件
3. **预生产环境**：使用 PostgreSQL，模拟真实生产环境
4. **生产环境**：使用 PostgreSQL，确保稳定性和性能

---

## 监控和日志

应用程序启动时会记录使用的数据库类型：

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: AgentPlatform.Infrastructure[0]
      Database type: sqlite
```

---

## 故障排除

### SQLite 连接失败

```
错误：SQL logic error or missing database
```

**解决方案：**
1. 检查 `appsettings.json` 中的连接字符串
2. 确保应用程序有权限写入数据库文件目录
3. 验证 `Data Source` 路径是否正确

### PostgreSQL 连接失败

```
错误：connection refused
```

**解决方案：**
1. 确认 PostgreSQL 服务正在运行
2. 检查连接字符串中的主机、端口、用户名和密码
3. 验证数据库是否存在（如果没有，运行迁移）

---

## 参考资源

- [SQLite 官方文档](https://www.sqlite.org/docs.html)
- [EF Core SQLite 文档](https://docs.microsoft.com/ef/core/providers/sqlite/)
- [PostgreSQL 官方文档](https://www.postgresql.org/docs/)
- [EF Core PostgreSQL 文档](https://docs.microsoft.com/ef/core/providers/postgresql/)

---

**最后更新：** 2026-07-14
