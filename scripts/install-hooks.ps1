# 一键启用 dev-agent 仓库的 git 钩子（质量门拦截）
# 用法（在仓库根目录执行）：  pwsh scripts/install-hooks.ps1
$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    git config core.hooksPath scripts/git-hooks
    Write-Host "✅ git core.hooksPath 已设为 scripts/git-hooks" -ForegroundColor Green
    Write-Host "   质量门钩子现已生效：提交 src/ 改动须带 .quality-gate.json 标记。" -ForegroundColor Cyan
    Write-Host "   仅改 docs/ 等文档、不动 src/ 的提交不受影响。" -ForegroundColor Cyan
} finally {
    Pop-Location
}
