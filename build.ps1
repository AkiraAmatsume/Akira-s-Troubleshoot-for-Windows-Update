# 编译脚本：供本地与 git pre-commit 钩子调用。
# 仅做一件事：用 MSBuild 编译本工程；成功退出码 0，失败退出码 1。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File build.ps1
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

# 工程与本脚本同目录，避免依赖调用者的当前工作目录
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$project = Join-Path $projectRoot "WindowsUpdateFixer.csproj"

if (-not (Test-Path $project)) {
    Write-Host "未找到工程文件：$project" -ForegroundColor Red
    exit 1
}

# 选用 MSBuild；优先 64 位 Framework 版本（与 .NET Framework 4.8 目标匹配）
$msbuildCandidates = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
)

$msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) {
    # 回退到 PATH 中的 msbuild（如已安装 VS Build Tools）
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { $msbuild = $cmd.Source }
}

if (-not $msbuild) {
    Write-Host "未找到 MSBuild。请安装 .NET Framework 4.x 或 Visual Studio Build Tools。" -ForegroundColor Red
    exit 1
}

& $msbuild $project "/p:Configuration=$Configuration" "/t:Rebuild" "/v:quiet" "/nologo"
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Host "编译失败（退出码 $exitCode）。" -ForegroundColor Red
    exit 1
}

Write-Host "编译成功。" -ForegroundColor Green
exit 0
