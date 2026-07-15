<#
.SYNOPSIS
    切换数据库类型（SQLite ↔ PostgreSQL）

.DESCRIPTION
    根据 Database:Type 配置在 SQLite 和 PostgreSQL 之间切换。
    切换后会自动重新编译并启动应用程序。

.PARAMETER DatabaseType
    数据库类型：sqlite 或 postgresql

.EXAMPLE
    .\switch-database.ps1 -DatabaseType sqlite
#>

param (
    [Parameter(Mandatory=$true)]
    [ValidateSet("sqlite", "postgresql")]
    [string]$DatabaseType
)

# 颜色输出
$Green = "`e[32m"
$Yellow = "`e[33m"
$Reset = "`e[0m"

Write-Host "${Yellow}正在切换数据库到 $DatabaseType...${Reset}" -ForegroundColor Yellow

# 1. 停止正在运行的应用程序
Write-Host "${Yellow}停止正在运行的应用程序...${Reset}" -ForegroundColor Yellow
try {
    $apiProcess = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like "*AgentPlatform.Api*" }
    if ($apiProcess) {
        Stop-Process -Id $apiProcess.Id -Force
        Write-Host "${Green}✓ 已停止旧进程${Reset}" -ForegroundColor Green
    }
} catch {
    Write-Host "${Yellow}⚠ 没有运行中的进程${Reset}" -ForegroundColor Yellow
}

# 2. 修改配置文件
$appSettingsPath = ".\src\AgentPlatform.Api\appsettings.json"

if (Test-Path $appSettingsPath) {
    Write-Host "${Yellow}更新 appsettings.json...${Reset}" -ForegroundColor Yellow

    $config = Get-Content $appSettingsPath -Raw | ConvertFrom-Json
    $config.Database.Type = $DatabaseType

    $config | ConvertTo-Json -Depth 10 | Set-Content $appSettingsPath
    Write-Host "${Green}✓ 配置已更新${Reset}" -ForegroundColor Green
} else {
    Write-Host "${Yellow}⚠ 找不到配置文件${Reset}" -ForegroundColor Yellow
    exit 1
}

# 3. 设置编译条件
$csprojPath = ".\src\AgentPlatform.Infrastructure\AgentPlatform.Infrastructure.csproj"

if (Test-Path $csprojPath) {
    Write-Host "${Yellow}设置编译条件...${Reset}" -ForegroundColor Yellow

    $content = Get-Content $csprojPath -Raw

    if ($DatabaseType -eq "postgresql") {
        # 移除 USE_SQLITE，添加 USE_POSTGRESQL
        $content = $content -replace '<DefineConstants>.*?</DefineConstants>', '<DefineConstants>USE_POSTGRESQL</DefineConstants>'
        Write-Host "${Green}✓ PostgreSQL 编译条件已设置${Reset}" -ForegroundColor Green
    } else {
        # 添加 USE_SQLITE，移除 USE_POSTGRESQL
        $content = $content -replace '<DefineConstants>.*?</DefineConstants>', '<DefineConstants>$(DefineConstants);USE_SQLITE</DefineConstants>'
        Write-Host "${Green}✓ SQLite 编译条件已设置${Reset}" -ForegroundColor Green
    }

    $content | Set-Content $csprojPath -NoNewline
} else {
    Write-Host "${Yellow}⚠ 找不到项目文件${Reset}" -ForegroundColor Yellow
    exit 1
}

# 4. 编译项目
Write-Host "${Yellow}编译项目...${Reset}" -ForegroundColor Yellow
dotnet build --configuration Release --no-incremental

if ($LASTEXITCODE -ne 0) {
    Write-Host "${Yellow}✗ 编译失败${Reset}" -ForegroundColor Yellow
    exit 1
}

Write-Host "${Green}✓ 编译成功${Reset}" -ForegroundColor Green

# 5. 启动应用程序
Write-Host "${Yellow}启动应用程序...${Reset}" -ForegroundColor Yellow
Start-Process "dotnet" -ArgumentList "run", "--project", "src\AgentPlatform.Api\AgentPlatform.Api.csproj", "--configuration", "Release"

Write-Host "${Green}✓ 应用程序已启动！${Reset}" -ForegroundColor Green
Write-Host "${Green}数据库类型：$DatabaseType${Reset}" -ForegroundColor Green
Write-Host "${Green}访问地址：http://localhost:5000${Reset}" -ForegroundColor Green
Write-Host "${Green}状态：${Green}运行中${Reset}" -ForegroundColor Green

Write-Host "`n${Green}切换完成！${Reset}" -ForegroundColor Green
