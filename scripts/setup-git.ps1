#Requires -Version 5.1
<#
.SYNOPSIS
  准备一个可用的 git（优先用系统已装的；没有就从镜像下载 MinGit 便携版）。
.DESCRIPTION
  不写系统目录、不弹 UAC。默认装到 %LOCALAPPDATA%\MinGit，也可用 -InstallDir 指定。
#>
param(
  [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "MinGit"),
  [string]$Version = "2.56.0.windows.1"
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$existing = Get-Command git -ErrorAction SilentlyContinue
if ($existing) { Write-Host "系统已有 git: $($existing.Source)"; & git --version; exit 0 }

$zipUrl = "https://registry.npmmirror.com/-/binary/git-for-windows/v$Version/MinGit-$($Version.Split('.')[0]).0-64-bit.zip"
$zipUrl = "https://registry.npmmirror.com/-/binary/git-for-windows/v$Version/MinGit-" + ($Version -replace "\.windows\.\d+$","") + "-64-bit.zip"
Write-Host "下载 $zipUrl"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$zip = Join-Path $InstallDir "mingit.zip"
Invoke-WebRequest -Uri $zipUrl -OutFile $zip -TimeoutSec 600
Expand-Archive -Path $zip -DestinationPath $InstallDir -Force
Remove-Item $zip -Force
$gitExe = Join-Path $InstallDir "cmd\git.exe"
if (-not (Test-Path $gitExe)) { throw "解压后没找到 cmd\git.exe" }
& $gitExe --version
Write-Host ""
Write-Host "已安装: $gitExe"
Write-Host "把它加入 PATH（当前会话）: $env:PATH = \"$InstallDir\cmd;$env:PATH\""
