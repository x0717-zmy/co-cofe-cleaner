#requires -Version 5.1
<#
  构建脚本
    -Gui（默认）：编译图形界面版 -> bin\Co_cofe系统垃圾清理.exe
    -Cli        ：编译命令行版     -> bin\Co_cofe系统垃圾清理-命令行.exe
  图标：..\iconwork\cleaner.ico；界面里的 56px logo 用 ..\iconwork\cleaner-256.png
#>
param(
    [switch]$Cli,
    [string]$IconPath = (Join-Path $PSScriptRoot '..\iconwork\cleaner.ico'),
    [string]$LogoPath = (Join-Path $PSScriptRoot '..\iconwork\cleaner-256.png'),
    [switch]$CopyToDesktop
)

$ErrorActionPreference = 'Stop'
$guiSrc = Join-Path $PSScriptRoot 'CleanGui.cs'
$cliSrc = Join-Path $PSScriptRoot 'CleanSafely.cs'
$bin = Join-Path $PSScriptRoot 'bin'
$manifest = Join-Path $PSScriptRoot 'app.manifest'

if (-not (Test-Path -LiteralPath $IconPath)) { throw "找不到图标：$IconPath（先运行 ..\iconwork\make-icons.ps1）" }
if (-not (Test-Path -LiteralPath $bin)) { New-Item -ItemType Directory -Path $bin -Force | Out-Null }

# ---------- 找 csc.exe ----------
$cscCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw '找不到 csc.exe（需要 .NET Framework 4.x，Windows 默认自带）' }

function Build-One {
    param([string[]]$Extra)

    $common = @(
        '/nologo'
        '/platform:anycpu'
        '/optimize+'
        '/debug-'
        '/warn:4'
        '/codepage:65001'
        '/utf8output'
    )
    $args2 = $common + $Extra
    & $csc @args2
    if ($LASTEXITCODE -ne 0) { throw "编译失败，csc 退出码 $LASTEXITCODE" }
}

if (-not $Cli) {
    if (-not (Test-Path -LiteralPath $guiSrc)) { throw "找不到源码：$guiSrc" }
    if (-not (Test-Path -LiteralPath $LogoPath)) { throw "找不到 logo：$LogoPath" }

    $out = Join-Path $bin 'Co_cofe系统垃圾清理.exe'
    Write-Host "编译图形界面版 …"
    Build-One -Extra @(
        '/target:winexe'
        "/out:$out"
        "/win32icon:$IconPath"
        "/resource:$LogoPath,APP_LOGO"
        "/win32manifest:$manifest"
        $guiSrc
        '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
    )
    $exe = Get-Item -LiteralPath $out
    Write-Host ("编译成功: {0}  ({1:N0} 字节)" -f $exe.FullName, $exe.Length)
}
else {
    $out = Join-Path $bin 'Co_cofe系统垃圾清理-命令行.exe'
    Write-Host "编译命令行版 …"
    Build-One -Extra @(
        '/target:exe'
        "/out:$out"
        "/win32icon:$IconPath"
        $cliSrc
    )
    $exe = Get-Item -LiteralPath $out
    Write-Host ("编译成功: {0}  ({1:N0} 字节)" -f $exe.FullName, $exe.Length)
}

if ($CopyToDesktop) {
    $desk = [Environment]::GetFolderPath('Desktop')
    $dest = Join-Path $desk 'Co_cofe系统垃圾清理.exe'
    Copy-Item -LiteralPath $exe.FullName -Destination $dest -Force
    Write-Host "已复制到桌面: $dest"
}

return $exe.FullName
