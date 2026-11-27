<#
.SYNOPSIS
    生成/修复资源管理器的"发送到 → ALTRun"快捷方式，使其指向当前的 ALTRun.exe。

.DESCRIPTION
    老版本的 SendTo 快捷方式可能指向 Delphi 版 Bin\ALTRun.exe（老 UI）。
    本脚本动态探测真正的 ALTRun.exe 路径并重建快捷方式，图标使用 exe 自身。

.PARAMETER ExePath
    可选，指定 ALTRun.exe 的完整路径。不传时自动探测：
      1) 脚本所在目录的 ALTRun.exe
      2) 脚本所在目录\ALTRun-v2\publish-selfcontained\ALTRun.exe
      3) D:\ALTRun\ALTRun.exe
      4) %LOCALAPPDATA%\ALTRun\ALTRun.exe

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File 安装发送到菜单.ps1
    powershell -ExecutionPolicy Bypass -File 安装发送到菜单.ps1 -ExePath "D:\ALTRun\ALTRun.exe"
#>
param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

function Write-Info($msg) { Write-Host "[ALTRun] $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "[ALTRun] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[ALTRun] $msg" -ForegroundColor Yellow }

# ---------- 1. 探测 exe 路径 ----------
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$candidates = @()

if ($ExePath -and (Test-Path -LiteralPath $ExePath)) {
    $candidates += (Resolve-Path -LiteralPath $ExePath).Path
}
$candidates += @(
    (Join-Path $scriptDir 'ALTRun.exe'),
    (Join-Path $scriptDir 'ALTRun-v2\publish-selfcontained\ALTRun.exe'),
    (Join-Path $scriptDir 'ALTRun-v2\publish-single\ALTRun.exe'),
    'D:\ALTRun\ALTRun.exe',
    (Join-Path $env:LOCALAPPDATA 'ALTRun\ALTRun.exe')
)

$exe = $null
foreach ($c in $candidates) {
    if ($c -and (Test-Path -LiteralPath $c)) { $exe = (Resolve-Path -LiteralPath $c).Path; break }
}
if (-not $exe) {
    Write-Warn '未找到 ALTRun.exe，请用 -ExePath 参数指定完整路径。'
    exit 1
}

# ---------- 2. 定位 SendTo 目录 ----------
$sendTo = Join-Path $env:APPDATA 'Microsoft\Windows\SendTo'
if (-not (Test-Path -LiteralPath $sendTo)) {
    New-Item -ItemType Directory -Path $sendTo -Force | Out-Null
}
$lnk = Join-Path $sendTo 'ALTRun.lnk'

# ---------- 3. 备份旧快捷方式 ----------
if (Test-Path -LiteralPath $lnk) {
    $bak = "$lnk.before-sendto-fix-$(Get-Date -Format yyyyMMdd_HHmmss)"
    Copy-Item -LiteralPath $lnk -Destination $bak -Force
    Write-Info "已备份旧快捷方式: $bak"
}

# ---------- 4. 用 WScript.Shell 创建快捷方式 ----------
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = Split-Path -Parent $exe
$shortcut.Arguments = ''           # Windows 会把右键选中的文件路径作为参数自动传入
$shortcut.IconLocation = "$exe,0"  # 用 exe 自身图标
$shortcut.Description = 'ALTRun - 添加到快捷方式列表'
$shortcut.Save()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null

Write-Ok "已创建快捷方式: $lnk"
Write-Ok "目标程序: $exe"
Write-Info '现在在任意文件上右键 → 发送到 → ALTRun 即可调用新版界面。'
