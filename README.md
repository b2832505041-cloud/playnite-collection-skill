# Playnite 游戏库分类与本地化 Skill

把 Playnite 游戏库整理成**分类 + 标签**，并把游戏名、简介、发行日期、类型、厂商等元数据**本地化 / 补全**到任意目标语言（默认简体中文，支持中英互换及日/韩/繁中等）。
以可回滚的 Playnite 插件形式写入，不碰游戏安装信息与启动配置。

Organize a Playnite library into **categories + tags**, and localize/enrich game metadata (name, description, release date, genres, companies) into **any target language** (default Simplified Chinese; supports zh↔en swap, Japanese, Korean, Traditional Chinese, etc.).
Writes via a rollback-safe Playnite plugin, without touching install info or launch config.

> 这是一个 Claude/DSH Agent Skill：`SKILL.md` 是执行入口，`scripts/` 是可复用脚本。
> This is a Claude/DSH agent skill: `SKILL.md` is the execution entrypoint, `scripts/` are reusable scripts.

## 解决的问题 / What it solves

1. **分类**：把游戏按「类型 / 系列 / 特性 / 平台 / 工具软件」打成 Playnite 分类和标签。
   **Categorize** games into Playnite categories/tags by type / series / feature / platform / tool.
2. **本地化**：把游戏名换成目标语言官方名（默认取 Steam 商店对应语言区），补简介、发行日期、类型、厂商。
   **Localize**: swap names to the official target-language name (default from Steam store), enrich description/date/genres/companies.
3. **中英互换 / 任意语言**：目标语言由 `--lang` 控制（`schinese` / `tchinese` / `japanese` / `koreana` / `english`…）；目标为英文时把非英文名（中/日/韩/俄等）换成英文。
   **Language switch**: `--lang` controls the target; passing `english` converts non-Latin names (zh/ja/ko/ru…) to English.
4. **不重复**：同一作品的不同版本加版本后缀，跨平台重复加（来源）后缀。
   **No duplicates**: version suffixes for variants, (source) suffix for cross-platform duplicates.
5. **可回滚**：写入前备份，插件菜单一键撤销。
   **Rollback-safe**: backup before write; one-click revert in the plugin menu.

## 工作流程 / Workflow

```
1. 读取库        export library（Playnite 独占锁 → 优雅关闭后读副本）
2. 分类映射      build 分类.tsv（每款 → 类型/系列/特性/平台标签）
3. 元数据本地化  从 Steam 目标语言区抓官方名与简介，缺失的用搜索补齐
4. 写入          Playnite 插件按游戏 ID 精确写入
5. 校验          verify：分类覆盖、标签覆盖、无重名
```

## 快速开始 / Quick start

```powershell
# 0. 前置：Windows + Playnite + .NET Framework 4.x（系统自带 csc）+ Node.js ≥ 23.6
# Prereqs: Windows + Playnite + .NET Framework 4.x + Node.js >= 23.6

# 1. 编译并安装插件（写入 %APPDATA%\Playnite\Extensions\playnite-collection-tool\）
powershell -ExecutionPolicy Bypass -File scripts\build-plugin.ps1

# 2. 关闭 Playnite 后导出库
powershell -ExecutionPolicy Bypass -File scripts\export-playnite.ps1 -OutDir .\out

# 3. 导出 Id/来源清单
powershell -ExecutionPolicy Bypass -File scripts\collect-ids.ps1 -Mode ids -OutDir out

# 4. 抓目标语言元数据（默认简体中文；改成 english 即英文化）
node scripts\fetch-steam-meta.ts --ids out/playnite_ids.json --out out/steam_meta.json --lang schinese

# 5. 为还没目标语言名的游戏找候选并确认
node scripts\suggest-steam.ts --games out/playnite_games.json --skip-names out/steam_meta.json --out out/steam_suggest.json --lang schinese
node scripts\fetch-appdetails.ts --candidates out/steam_suggest.json --out out/confirmed.json --lang schinese

# 6. 准备人工映射表（参考 examples/）
#    map/classification.tsv、map/manual_names.json、map/version_labels.json、map/manual_ids.json

# 7. 汇总生成写入文件
node scripts\build-data.ts --map map/classification.tsv --outdir out --lang schinese

# 8. 拷进 Playnite 数据目录，重启 Playnite 自动应用
copy out\分类.tsv      "$env:APPDATA\Playnite\ExtensionsData\playnite-collection-tool\"
copy out\游戏数据.tsv  "$env:APPDATA\Playnite\ExtensionsData\playnite-collection-tool\"

# 9. 校验
powershell -ExecutionPolicy Bypass -File scripts\collect-ids.ps1 -Mode verify
```

## 目标语言 / Target language

| 语言 / Language | `--lang` |
|---|---|
| 简体中文 Simplified Chinese（默认 default） | `schinese` |
| 繁体中文 Traditional Chinese | `tchinese` |
| 日文 Japanese | `japanese` |
| 韩文 Korean | `koreana` |
| 英文 English | `english` |
| 德/法/意/西/俄 German/French/Italian/Spanish/Russian | `german` `french` `italian` `spanish` `russian` |

界面字段名（来源/完成状态/平台）由 `loc_map.json` 控制（见 [examples/loc_map.json](examples/loc_map.json)），默认简体中文，可替换成任意语言。
UI field names (source/status/platform) are controlled by `loc_map.json` (see [examples/loc_map.json](examples/loc_map.json)); default is Simplified Chinese.

## 目录 / Structure

| 路径 / Path | 说明 / Notes |
|---|---|
| `SKILL.md` | Agent 执行入口 / Agent entrypoint |
| `plugin/` | 分类/标签/元数据写入插件（C# + extension.yaml） |
| `scripts/` | 导出、抓取、生成映射、校验脚本 |
| `docs/` | 设计说明、踩坑清单 / design, pitfalls |
| `examples/` | 脱敏示例数据 / sanitized example data |

## 环境要求 / Requirements

| 依赖 / Dependency | 版本 / Version | 说明 / Notes |
|---|---|---|
| Windows | 10/11 | 插件与导出脚本依赖 Playnite |
| Playnite | 10.x（32 位） | 必须安装并导入过至少一个商店库 |
| .NET Framework | 4.x | 系统自带 csc 即可，无需 .NET SDK |
| Node.js | ≥ 23.6 | 跑数据脚本（直接运行 .ts，仅内置模块） |
| git | 可选 | 没装可跑 `scripts\setup-git.ps1` |

## 隐私说明 / Privacy

本仓库**不含任何使用者数据**：没有游戏库导出、账号 ID、Cookie/Token、本机绝对路径。
所有脚本路径通过参数传入；`examples/` 为虚构数据。使用时把 `out/`、`backup/` 排除在版本控制外（见 `.gitignore`）。

This repo contains **no user data**: no library exports, account IDs, cookies/tokens, or absolute local paths.
All paths are passed as arguments; `examples/` is fictional. Keep `out/` and `backup/` out of version control (see `.gitignore`).

## License

MIT
