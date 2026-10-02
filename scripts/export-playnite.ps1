#Requires -Version 5.1
<#
.SYNOPSIS
  从 Playnite 的 LiteDB 数据库导出游戏库到 JSON。
.DESCRIPTION
  Playnite 运行时会独占锁定 games.db，普通复制会失败。本脚本会：
    1) 通过 --shutdown 优雅关闭 Playnite（必要时等待）；
    2) 把 library\*.db 复制到临时目录；
    3) 用 Playnite 自带的 LiteDB.dll 只读打开并导出字段。
  导出字段：Id/Name/Source/IsInstalled/Favorite/PlaytimeHours/CompletionStatus/
            Genres/Developers/Publishers/Series/Features/Tags/Categories/
            Platforms/ReleaseDate/Added/Manual/InstallDirectory
.PARAMETER OutDir    输出目录（默认 .\out）
.PARAMETER PlaynitePath  Playnite 安装目录（默认自动探测）
.PARAMETER DbDir     Playnite 数据库目录（默认 %APPDATA%\Playnite\library）
.PARAMETER SkipShutdown  不关闭 Playnite（仅当它已经退出时使用）
#>
param(
  [string]$OutDir = ".\out",
  [string]$PlaynitePath = "",
  [string]$DbDir = "",
  [switch]$SkipShutdown
)

$ErrorActionPreference = "Stop"

function Find-Playnite {
  $cands = @(
    (Join-Path $env:ProgramFiles "Playnite"),
    (Join-Path ${env:ProgramFiles(x86)} "Playnite"),
    "C:\Playnite", "D:\Playnite", "E:\Playnite"
  )
  foreach ($c in $cands) { if ($c -and (Test-Path (Join-Path $c "Playnite.SDK.dll"))) { return $c } }
  return $null
}

if (-not $PlaynitePath) { $PlaynitePath = Find-Playnite }
if (-not $PlaynitePath) { throw "没找到 Playnite 安装目录，请用 -PlaynitePath 指定" }
if (-not $DbDir) { $DbDir = Join-Path $env:APPDATA "Playnite\library" }
$lite = Join-Path $PlaynitePath "LiteDB.dll"
if (-not (Test-Path $lite)) { throw "找不到 LiteDB.dll: $lite" }
if (-not (Test-Path $DbDir)) { throw "找不到数据库目录: $DbDir" }

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---------- 1. 关闭 Playnite ----------
if (-not $SkipShutdown) {
  $exe = Join-Path $PlaynitePath "Playnite.DesktopApp.exe"
  if (Test-Path $exe) {
    Write-Host "关闭 Playnite ..."
    Start-Process $exe -ArgumentList "--shutdown" -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 40; $i++) {
      Start-Sleep -Seconds 1
      if (-not (Get-Process Playnite.DesktopApp -ErrorAction SilentlyContinue)) { break }
    }
    $still = Get-Process Playnite.DesktopApp -ErrorAction SilentlyContinue
    if ($still) { Write-Warning "Playnite 仍在运行，请手动退出后再试（或加 -SkipShutdown）" }
  }
}
if (Get-Process Playnite.DesktopApp -ErrorAction SilentlyContinue) {
  throw "Playnite 仍在运行，数据库被独占锁定。请先退出 Playnite。"
}

# ---------- 2. 复制数据库 ----------
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("playnite-export-" + (Get-Date -Format "yyyyMMddHHmmss"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
foreach ($f in Get-ChildItem (Join-Path $DbDir "*.db")) {
  Copy-Item $f.FullName -Destination $tmp -Force
}
Write-Host "已复制数据库到 $tmp"

# ---------- 3. 读取 ----------
Add-Type -Path $lite
$LFMAP = @{
  "Genre" = "genres.db"; "Company" = "companies.db"; "Series" = "series.db";
  "Tag" = "tags.db"; "Category" = "categories.db"; "Platform" = "platforms.db";
  "GameSource" = "sources.db"; "CompletionStatus" = "completionstatuses.db";
  "AgeRating" = "ageratings.db"; "Region" = "regions.db"; "Feature" = "features.db"
}
$lookup = @{}
foreach ($k in $LFMAP.Keys) {
  $file = Join-Path $tmp $LFMAP[$k]
  if (-not (Test-Path $file)) { continue }
  try {
    $d = New-Object LiteDB.LiteDatabase("Filename=$file;ReadOnly=true")
    foreach ($col in $d.GetCollectionNames()) {
      foreach ($doc in $d.GetCollection($col).FindAll()) {
        $idv = $doc["_id"]
        if ($null -ne $idv) { $lookup["$($idv.RawValue)"] = "$($doc["Name"].RawValue)" }
      }
    }
    $d.Dispose()
  } catch { Write-Warning "读取 $($LFMAP[$k]) 失败: $($_.Exception.Message)" }
}
Write-Host ("名称索引: " + $lookup.Count + " 条")

function Names($arr) {
  if ($null -eq $arr) { return "" }
  $parts = New-Object System.Collections.ArrayList
  foreach ($el in $arr) {
    $n = $lookup["$($el.RawValue)"]
    if ($n) { [void]$parts.Add($n) }
  }
  return ($parts -join ";")
}
function J([string]$s) {
  if ($null -eq $s) { return '""' }
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.Append('"')
  foreach ($ch in $s.ToCharArray()) {
    if ($ch -eq '"') { [void]$sb.Append('\"') }
    elseif ($ch -eq '\') { [void]$sb.Append('\\') }
    elseif ([int]$ch -eq 10) { [void]$sb.Append('\n') }
    elseif ([int]$ch -eq 13) { [void]$sb.Append('\r') }
    elseif ([int]$ch -eq 9) { [void]$sb.Append('\t') }
    elseif ([int]$ch -lt 32) { [void]$sb.Append(" ") }
    else { [void]$sb.Append($ch) }
  }
  [void]$sb.Append('"')
  return $sb.ToString()
}

$db = New-Object LiteDB.LiteDatabase("Filename=" + (Join-Path $tmp "games.db") + ";ReadOnly=true")
$games = @($db.GetCollection("Game").FindAll())
Write-Host ("游戏条目: " + $games.Count)

$arr = New-Object System.Collections.ArrayList
foreach ($g in $games) {
  $name = "$($g["Name"].RawValue)"
  $src = ""
  if ($g["SourceId"]) { $src = "$($lookup["$($g["SourceId"].RawValue)"])" }
  $status = ""
  if ($g["CompletionStatusId"]) { $status = "$($lookup["$($g["CompletionStatusId"].RawValue)"])" }
  $rel = ""
  if ($g["ReleaseDate"] -and $g["ReleaseDate"].Type -eq [LiteDB.BsonType]::DateTime) { $rel = $g["ReleaseDate"].AsDateTime.ToString("yyyy-MM-dd") }
  elseif ($g["ReleaseDate"] -and $g["ReleaseDate"].Type -eq [LiteDB.BsonType]::String) { $rel = "$($g["ReleaseDate"].RawValue)" }
  $added = ""
  if ($g["Added"] -and $g["Added"].Type -eq [LiteDB.BsonType]::DateTime) { $added = $g["Added"].AsDateTime.ToString("yyyy-MM-dd") }
  $hours = 0.0
  if ($g["Playtime"] -and $g["Playtime"].Type -eq [LiteDB.BsonType]::Int64) { $hours = [math]::Round(([double]$g["Playtime"].AsInt64) / 3600.0, 1) }
  $installed = $false; if ($g["IsInstalled"]) { $installed = $g["IsInstalled"].AsBoolean }
  $fav = $false; if ($g["Favorite"]) { $fav = $g["Favorite"].AsBoolean }
  $manual = ""; if ($g["Manual"]) { $manual = "$($g["Manual"].RawValue)" }
  $o = "{"
  $o += '"Id":' + (J "$($g["_id"].RawValue)")
  $o += ',"Name":' + (J $name)
  $o += ',"Source":' + (J $src)
  $o += ',"IsInstalled":' + $(if ($installed) { "true" } else { "false" })
  $o += ',"Favorite":' + $(if ($fav) { "true" } else { "false" })
  $o += ',"PlaytimeHours":' + $hours.ToString([System.Globalization.CultureInfo]::InvariantCulture)
  $o += ',"CompletionStatus":' + (J $status)
  $o += ',"Genres":' + (J (Names $g["GenreIds"]))
  $o += ',"Developers":' + (J (Names $g["DeveloperIds"]))
  $o += ',"Publishers":' + (J (Names $g["PublisherIds"]))
  $o += ',"Series":' + (J (Names $g["SeriesIds"]))
  $o += ',"Features":' + (J (Names $g["FeatureIds"]))
  $o += ',"Tags":' + (J (Names $g["TagIds"]))
  $o += ',"Categories":' + (J (Names $g["CategoryIds"]))
  $o += ',"Platforms":' + (J (Names $g["PlatformIds"]))
  $o += ',"ReleaseDate":' + (J $rel)
  $o += ',"Added":' + (J $added)
  $o += ',"Manual":' + (J $manual)
  $o += ',"InstallDirectory":' + (J "$($g["InstallDirectory"].RawValue)")
  $o += "}"
  [void]$arr.Add($o)
}
$db.Dispose()

$json = '{"GameCount":' + $games.Count + ',"Games":[' + ($arr -join ",") + "]}"
$outFile = Join-Path $OutDir "playnite_games.json"
[System.IO.File]::WriteAllText($outFile, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "已导出: $outFile"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "完成。请自行重新启动 Playnite。"
