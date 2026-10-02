#Requires -Version 5.1
<#
.SYNOPSIS
  从 Playnite 数据库导出「游戏 Id + 原名 + 来源」，或校验整理结果。
.DESCRIPTION
  -Mode ids    导出 out/playnite_ids.json（供抓 Steam 元数据 / 生成写入数据用）
  -Mode verify 校验：分类覆盖、简介覆盖、中文名比例、重名组数
.PARAMETER Mode     ids 或 verify
.PARAMETER DbDir    Playnite 数据库目录（默认 %APPDATA%\Playnite\library）
.PARAMETER OutDir   输出目录（默认 .\out）
.PARAMETER PlaynitePath  Playnite 安装目录（自动探测）
#>
param(
  [ValidateSet("ids", "verify")] [string]$Mode = "ids",
  [string]$DbDir = "",
  [string]$OutDir = ".\out",
  [string]$PlaynitePath = "",
  [switch]$SkipShutdown
)

$ErrorActionPreference = "Stop"
if (-not $DbDir) { $DbDir = Join-Path $env:APPDATA "Playnite\library" }
if (-not $PlaynitePath) {
  $cands = @((Join-Path $env:ProgramFiles "Playnite"), (Join-Path ${env:ProgramFiles(x86)} "Playnite"), (Join-Path $env:LOCALAPPDATA "Playnite"))
  # 遍历所有盘符的常见安装位置，避免漏掉自定义路径（如 E:\Program Files\Playnite）
  foreach ($d in (Get-PSDrive -PSProvider FileSystem).Root) {
    $cands += (Join-Path $d "Playnite")
    $cands += (Join-Path $d "Program Files\Playnite")
    $cands += (Join-Path $d "Program Files (x86)\Playnite")
  }
  foreach ($c in $cands) {
    if ($c -and (Test-Path (Join-Path $c "Playnite.SDK.dll"))) { $PlaynitePath = $c; break }
  }
}
if (-not $PlaynitePath) { throw "没找到 Playnite 安装目录，请用 -PlaynitePath 指定" }

# Playnite 运行时独占锁定 games.db；优雅关闭后读取（-SkipShutdown 则要求已手动退出）
if (Get-Process Playnite.DesktopApp -ErrorAction SilentlyContinue) {
  if ($SkipShutdown) { throw "Playnite 正在运行，数据库被独占锁定。请先退出 Playnite（或去掉 -SkipShutdown 让脚本自动关闭）。" }
  $exe = Join-Path $PlaynitePath "Playnite.DesktopApp.exe"
  if (-not (Test-Path $exe)) { throw "Playnite 正在运行，但找不到 Playnite.DesktopApp.exe，请手动退出。" }
  Write-Host "关闭 Playnite ..."
  Start-Process $exe -ArgumentList "--shutdown" -ErrorAction SilentlyContinue
  for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 1
    if (-not (Get-Process Playnite.DesktopApp -ErrorAction SilentlyContinue)) { break }
  }
  if (Get-Process Playnite.DesktopApp -ErrorAction SilentlyContinue) { throw "Playnite 仍在运行，数据库被独占锁定。请先退出 Playnite。" }
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("playnite-read-" + (Get-Date -Format "yyyyMMddHHmmss"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
foreach ($f in @("games.db", "sources.db", "categories.db", "tags.db")) {
  $p = Join-Path $DbDir $f
  if (Test-Path $p) { Copy-Item $p -Destination $tmp -Force }
}
Add-Type -Path (Join-Path $PlaynitePath "LiteDB.dll")

$db = New-Object LiteDB.LiteDatabase("Filename=" + (Join-Path $tmp "games.db") + ";ReadOnly=true")
$games = @($db.GetCollection("Game").FindAll())
$smap = @{}
if (Test-Path (Join-Path $tmp "sources.db")) {
  $sd = New-Object LiteDB.LiteDatabase("Filename=" + (Join-Path $tmp "sources.db") + ";ReadOnly=true")
  foreach ($d in $sd.GetCollection("GameSource").FindAll()) { $smap["$($d["_id"].RawValue)"] = "$($d["Name"].RawValue)" }
  $sd.Dispose()
}

$rows = @()
foreach ($g in $games) {
  $src = ""; if ($g["SourceId"]) { $src = "$($smap["$($g["SourceId"].RawValue)"])" }
  $rows += [pscustomobject]@{
    Id = "$($g["_id"].RawValue)"
    Name = "$($g["Name"].RawValue)"
    Source = $src
    GameId = "$($g["GameId"].RawValue)"
    Cats = $(if ($g["CategoryIds"]) { $g["CategoryIds"].AsArray.Count } else { 0 })
    Tags = $(if ($g["TagIds"]) { $g["TagIds"].AsArray.Count } else { 0 })
    HasDesc = $(if ($g["Description"] -and "$($g["Description"].RawValue)".Length -gt 0) { 1 } else { 0 })
    HasDate = $(if ($g["ReleaseDate"] -and "$($g["ReleaseDate"].RawValue)".Length -gt 0) { 1 } else { 0 })
  }
}
$db.Dispose()

if ($Mode -eq "ids") {
  $OutDir = [System.IO.Path]::GetFullPath($OutDir)
  New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
  # 保留原始来源名（本地化前的英文名）便于脚本比对
  # 用 @() 包装，避免单条数据时 ConvertTo-Json 输出对象而非数组
  $arr = @($rows | Select-Object Name, Source, GameId)
  $json = $arr | ConvertTo-Json -Depth 3
  $file = Join-Path $OutDir "playnite_ids.json"
  [System.IO.File]::WriteAllText($file, $json, (New-Object System.Text.UTF8Encoding($false)))
  Write-Host "已导出 $($rows.Count) 条 -> $file"
} else {
  $cn = ($rows | Where-Object { $_.Name -match "[\u4e00-\u9fff]" }).Count
  $withCat = ($rows | Where-Object { $_.Cats -gt 0 }).Count
  $withTag = ($rows | Where-Object { $_.Tags -gt 0 }).Count
  $withDesc = ($rows | Where-Object { $_.HasDesc -gt 0 }).Count
  $withDate = ($rows | Where-Object { $_.HasDate -gt 0 }).Count
  $dup = $rows | Group-Object Name | Where-Object { $_.Count -gt 1 }
  Write-Host "GAMES=$($rows.Count)"
  Write-Host "有分类=$withCat  有标签=$withTag  有简介=$withDesc  有发行日期=$withDate  含中文名=$cn"
  Write-Host "重复名称组数=$($dup.Count)"
  foreach ($g in $dup) { Write-Host ("  " + $g.Count + " x [" + $g.Name + "] -> " + (($g.Group | ForEach-Object { $_.Source }) -join ",")) }
}
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
