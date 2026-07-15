# 可插拔数据库架构（条件编译实现）

## 概述

本项目使用 **条件编译** 实现真正的可插拔数据库架构。通过修改项目配置，可以在编译时选择使用 SQLite 或 PostgreSQL，无需修改运行时代码。

---

## 架构设计

### 编译条件

项目使用两个编译常量来控制数据库提供程序：

```csharp
#if USE_SQLITE
    // SQLite 编译分支
#elif USE_POSTGRESQL
    // PostgreSQL 编译分支
#else
    // 默认使用 SQLite
#endif
```

### 编译常量定义

**SQLite 模式：**
```xml
<DefineConstants>$(DefineConstants);USE_SQLITE</DefineConstants>
```

**PostgreSQL 模式：**
```xml
<DefineConstants>USE_POSTGRESQL</DefineConstants>
```

---

## 使用方法

### 方法 1：使用 PowerShell 脚本（推荐）

```powershell
# 切换到 SQLite
.\scripts\switch-database.ps1 -DatabaseType sqlite

# 切换到 PostgreSQL
.\scripts\switch-database.ps1 -DatabaseType postgresql
```

**脚本功能：**
1. ✅ 停止正在运行的 API
2. ✅ 更新 `appsettings.json` 配置
3. ✅ 设置正确的编译条件
4. ✅ 重新编译项目
5. ✅ 启动应用程序

---

### 方法 2：手动切换

#### 切换到 PostgreSQL

**步骤 1：修改项目文件**

编辑 `src/AgentPlatform.Infrastructure/AgentPlatform.Infrastructure.csproj`：

```xml
<PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <DefineConstants>USE_POSTGRESQL</DefineConstants>
</PropertyGroup>
```

**步骤 2：修改配置文件**

编辑 `src/AgentPlatform.Api/appsettings.json`：

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

**步骤 3：重新编译**

```bash
dotnet build --configuration Release
```

**步骤 4：启动应用**

```bash
dotnet run --project src/AgentPlatform.Api/AgentPlatform.Api.csproj --configuration Release
```

---

#### 切换到 SQLite

**步骤 1：修改项目文件**

编辑 `src/AgentPlatform.Infrastructure/AgentPlatform.Infrastructure.csproj`：

```xml
<PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <DefineConstants>$(DefineConstants);USE_SQLITE</DefineConstants>
</PropertyGroup>
```

**步骤 2：修改配置文件**

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

**步骤 3：重新编译**

```bash
dotnet build --configuration Release
```

**步骤 4：启动应用**

```bash
dotnet run --project src/AgentPlatform.Api/AgentPlatform.Api.csproj --configuration Release
```

---

## 配置说明

### appsettings.json

```json
{
  "Database": {
    "Type": "sqlite"  // 或 "postgresql"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=agent_platform.db;Cache=Shared",  // SQLite
    "PostgreSQL": "Host=localhost;Database=agent_platform;Username=postgres;Password=your_password"  // PostgreSQL
  }
}
```

---

## 验证数据库类型

应用程序启动时会记录使用的数据库类型：

```log
info: AgentPlatform.Infrastructure.DependencyInjection[0]
      Database type: sqlite
```

或

```log
info: AgentPlatform.Infrastructure.DependencyInjection[0]
      Database type: postgresql
```

---

## 项目文件修改

### AgentPlatform.Infrastructure.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.3" />
    <!-- 其他包... -->
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- 默认使用 SQLite -->
    <DefineConstants>$(DefineConstants);USE_SQLITE</DefineConstants>
  </PropertyGroup>

</Project>
```

---

## 运行时行为

### SQLite 模式

- ✅ 只编译 SQLite 提供程序
- ✅ 运行时不需要 PostgreSQL NuGet 包
- ✅ 数据库文件：`agent_platform.db`
- ✅ 无需安装数据库服务器

### PostgreSQL 模式

- ✅ 只编译 PostgreSQL 提供程序
- ✅ 运行时不需要 SQLite NuGet 包
- ✅ 数据库服务器：需要运行 PostgreSQL
- ✅ 支持生产环境

---

## 优势

### 1. 真正的可插拔
- ✅ 通过条件编译实现
- ✅ 运行时代码无依赖
- ✅ 无需反射或动态加载

### 2. 性能优化
- ✅ SQLite 模式不包含 PostgreSQL 代码
- ✅ 编译更小更快
- ✅ 减少内存占用

### 3. 类型安全
- ✅ 编译时检查
- ✅ IDE 智能提示
- ✅ 重构友好

### 4. 版本控制
- ✅ 可以在仓库中保存两种配置
- ✅ CI/CD 流水线自动选择
- ✅ 环境特定配置

---

## 常见问题

### Q: 为什么要使用条件编译？
**A:** 条件编译比反射更高效、更安全、更易维护。

### Q: 可以在运行时切换数据库吗？
**A:** 不可以。切换数据库需要重新编译项目。

### Q: SQLite 和 PostgreSQL 代码能共存吗？
**A:** 是的，通过 `#if` 指令分别编译两个分支。

### Q: 如何验证当前使用的是哪个数据库？
**A:** 查看应用程序启动日志中的 "Database type" 消息。

### Q: 数据迁移怎么办？
**A:**
- SQLite → PostgreSQL：需要手动导出/导入数据
- PostgreSQL → SQLite：数据可能无法直接迁移（类型差异）

---

## CI/CD 集成

### GitHub Actions 示例

```yaml
name: Build

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v2

      - name: Build SQLite version
        run: |
          dotnet build --configuration Release

      - name: Build PostgreSQL version
        run: |
          # 修改项目文件使用 PostgreSQL
          sed -i 's/<DefineConstants>.*<\/DefineConstants>/<DefineConstants>USE_POSTGRESQL<\/DefineConstants>/' src/AgentPlatform.Infrastructure/AgentPlatform.Infrastructure.csproj
          dotnet build --configuration Release

      - name: Run tests
        run: dotnet test --configuration Release
```

---

## 最佳实践

1. **开发环境**：使用 SQLite，方便快速开发和测试
2. **测试环境**：使用 SQLite，每个测试用例独立的数据库文件
3. **预生产环境**：使用 PostgreSQL，模拟真实生产环境
4. **生产环境**：使用 PostgreSQL，确保稳定性和性能

---

## 故障排除

### 错误：程序集未找到

```
System.IO.FileNotFoundException: Could not load file or assembly 'Npgsql.EntityFrameworkCore.PostgreSQL'
```

**原因**：使用了 PostgreSQL 编译分支但没有安装 PostgreSQL NuGet 包。

**解决方案**：确保项目文件中定义了 `USE_POSTGRESQL` 编译常量。

---

### 错误：SQL logic error

```
System.Data.SQLite.SQLiteException: SQL logic error or missing database
```

**原因**：SQLite 数据库文件不存在或路径错误。

**解决方案**：检查 `ConnectionStrings:DefaultConnection` 配置。

---

### 错误：连接被拒绝

```
Npgsql.PostgresException: 08001: could not connect to server
```

**原因**：PostgreSQL 服务未运行或连接字符串错误。

**解决方案**：确保 PostgreSQL 服务正在运行并验证连接字符串。

---

## 参考资源

- [EF Core 条件编译文档](https://docs.microsoft.com/ef/core/miscellaneous/configuring-dbcontext)
- [C# 预处理器指令](https://docs.microsoft.com/dotnet/csharp/language-reference/preprocessor-directives)
- [SQLite vs PostgreSQL](https://www.sqlite.org/whentouse.html)

---

**最后更新：** 2026-07-14
