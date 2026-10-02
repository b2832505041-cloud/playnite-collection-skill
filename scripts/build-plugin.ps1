#Requires -Version 5.1
<#
.SYNOPSIS
  编译并安装 Playnite 库整理插件。
.DESCRIPTION
  用系统自带的 csc 编译 plugin\CollectionPlugin.cs，并复制到 Playnite 的扩展目录。
  需要：Windows + .NET Framework 4.x（自带 csc）+ 已安装 Playnite。
.PARAMETER PlaynitePath
  Playnite 安装目录。默认自动探测常见位置，探测不到需要手动传。
.PARAMETER ExtensionsDir
  Playnite 扩展目录。默认 %APPDATA%\Playnite\Extensions。
#>
param(
  [string]$PlaynitePath = "",
  [string]$ExtensionsDir = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $repo "plugin\CollectionPlugin.cs"
$out  = Join-Path $repo "plugin\bin"
if (-not (Test-Path $src)) { throw "找不到插件源码: $src" }
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Find-Playnite {
  $cands = @(
    (Join-Path $env:ProgramFiles "Playnite"),
    (Join-Path ${env:ProgramFiles(x86)} "Playnite"),
    (Join-Path $env:LOCALAPPDATA "Playnite")
  )
  # 遍历所有盘符的常见安装位置，避免漏掉自定义路径（如 E:\Program Files\Playnite）
  foreach ($d in (Get-PSDrive -PSProvider FileSystem).Root) {
    $cands += (Join-Path $d "Playnite")
    $cands += (Join-Path $d "Program Files\Playnite")
    $cands += (Join-Path $d "Program Files (x86)\Playnite")
  }
  foreach ($c in $cands) {
    if ($c -and (Test-Path (Join-Path $c "Playnite.SDK.dll"))) { return $c }
  }
  $lnk = Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter "*Playnite*.lnk" -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($lnk) {
    try {
      $sh = New-Object -ComObject WScript.Shell
      $target = $sh.CreateShortcut($lnk.FullName).TargetPath
      $dir = Split-Path -Parent $target
      if (Test-Path (Join-Path $dir "Playnite.SDK.dll")) { return $dir }
    } catch { }
  }
  return $null
}

if (-not $PlaynitePath) { $PlaynitePath = Find-Playnite }
if (-not $PlaynitePath -or -not (Test-Path (Join-Path $PlaynitePath "Playnite.SDK.dll"))) {
  throw "没找到 Playnite 安装目录，请用 -PlaynitePath 指定（该目录下应有 Playnite.SDK.dll）"
}
Write-Host "Playnite: $PlaynitePath"

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "找不到 csc.exe，请安装 .NET Framework 4.x" }

# Playnite 是 32 位进程：必须 AnyCPU（x64 会 BadImageFormatException）
# 插件用到 WPF 的 MessageBoxButton，需要从 GAC 解析 WPF 程序集
function Resolve-Gac([string]$name) {
  foreach ($root in @("GAC_MSIL", "GAC_32", "GAC_64")) {
    $p = Join-Path (Join-Path $env:WINDIR "Microsoft.NET\assembly\$root") $name
    if (Test-Path $p) {
      $dll = Get-ChildItem $p -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
      if ($dll) { return $dll.FullName }
    }
  }
  return $null
}

$refs = @("Playnite.SDK.dll", "Playnite.dll", "Newtonsoft.Json.dll") | ForEach-Object { "/r:" + (Join-Path $PlaynitePath $_) }
$wpfRefs = @()
foreach ($n in @("WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml")) {
  $resolved = Resolve-Gac $n
  if ($resolved) { $wpfRefs += "/r:$resolved" } else { Write-Warning "GAC 里没有 $n（插件用到 WPF，可能编译失败）" }
}

$dll = Join-Path $out "CollectionPlugin.dll"
& $csc /nologo /target:library /platform:anycpu /langversion:5 $refs $wpfRefs "/out:$dll" $src
if ($LASTEXITCODE -ne 0) { throw "编译失败（csc 退出码 $LASTEXITCODE）" }

if (-not $ExtensionsDir) { $ExtensionsDir = Join-Path $env:APPDATA "Playnite\Extensions" }
$dest = Join-Path $ExtensionsDir "playnite-collection-tool"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $dll -Destination $dest -Force
Copy-Item (Join-Path $repo "plugin\extension.yaml") -Destination $dest -Force
$dataDir = Join-Path $env:APPDATA "Playnite\ExtensionsData\playnite-collection-tool"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

Write-Host ""
Write-Host "已安装到: $dest"
Write-Host "数据目录: $dataDir"
Write-Host "把 分类.tsv / 游戏数据.tsv 放进数据目录，然后重启 Playnite 会自动应用；"
Write-Host "也可以在主菜单 → 扩展 → Playnite Collection Tool 里手动触发。"
