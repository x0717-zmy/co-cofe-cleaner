#requires -Version 5.1
<#
  Co_cofe系统垃圾清理 · 安装包构建
  两步：先编卸载程序 unins.exe，再把主程序/文档/卸载程序当资源编进安装包。

  用法：powershell -NoProfile -ExecutionPolicy Bypass -File build-installer.ps1
#>
param(
    [string]$Version = '1.3.1'
)

$ErrorActionPreference = 'Stop'

$proj    = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # 源码\安装 -> 项目根
$appDir  = Join-Path $proj '程序'
$iconDir = Join-Path $proj '源码\图标'
$outDir  = Join-Path $proj '安装包'
$build   = Join-Path $PSScriptRoot '_build'

$appExe  = Join-Path $appDir 'Co_cofe系统垃圾清理.exe'
$helpTxt = Join-Path $appDir '使用说明.txt'
$quickTxt= Join-Path $appDir '快速上手.txt'
$ico     = Join-Path $iconDir 'cleaner.ico'
$manifest= Join-Path $PSScriptRoot 'setup.manifest'
$src     = Join-Path $PSScriptRoot 'Setup.cs'
$setupExe= Join-Path $outDir ("Co_cofe系统垃圾清理_v{0}_安装版.exe" -f $Version)

foreach ($f in @($appExe, $helpTxt, $quickTxt, $ico, $manifest, $src)) {
    if (-not (Test-Path -LiteralPath $f)) { throw "缺少文件：$f" }
}
foreach ($d in @($outDir, $build)) {
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw '找不到 csc.exe' }

$common = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/debug-','/codepage:65001','/utf8output',
            "/win32manifest:$manifest", "/win32icon:$ico")

# ---------- 1. 卸载程序 ----------
$unins = Join-Path $build 'unins.exe'
Write-Host '编译卸载程序 unins.exe …'
& $csc @common "/out:$unins" $src '/main:CoCofeSetup.UninstallProgram'
if ($LASTEXITCODE -ne 0) { throw "卸载程序编译失败（$LASTEXITCODE）" }

# ---------- 2. 安装程序 ----------
Write-Host '编译安装程序 …'
& $csc @common `
    "/out:$setupExe" `
    "/resource:$appExe,APP_EXE" `
    "/resource:$helpTxt,HELP_TXT" `
    "/resource:$quickTxt,QUICK_TXT" `
    "/resource:$ico,APP_ICO" `
    "/resource:$unins,UNINS_EXE" `
    $src '/main:CoCofeSetup.SetupProgram'
if ($LASTEXITCODE -ne 0) { throw "安装程序编译失败（$LASTEXITCODE）" }

$item = Get-Item -LiteralPath $setupExe
Write-Host ("安装包: {0}  ({1:N0} 字节)" -f $item.FullName, $item.Length)
return $item.FullName
