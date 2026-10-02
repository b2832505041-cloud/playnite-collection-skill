# Playnite 游戏库分类与中文化 Skill

把 Playnite 游戏库整理成**分类 + 标签**，并把游戏名、简介、发行日期、类型、厂商等元数据**中文化 / 补全**。
以可回滚的 Playnite 插件形式写入，不碰游戏安装信息与启动配置。

> 这是一个 Claude/DSH Agent Skill：`SKILL.md` 是给 Agent 看的执行入口，`scripts/` 是可复用脚本。

## 它解决什么问题

1. **分类**：把几百上千款游戏按「类型 / 系列 / 特性 / 平台 / 工具软件」打成 Playnite 分类和标签，方便筛选。
2. **中文化**：英文名游戏改用官方中文名（优先取 Steam 国区商店页的官方译名），补上中文简介、发行日期、类型、开发商、发行商。
3. **不重复**：同一作品的不同版本（Demo / Beta / 测试服 / 合集分集）加版本后缀，跨平台重复条目加（来源）后缀，避免出现一堆同名条目。
4. **可回滚**：写入前备份，插件菜单里一键撤销元数据改动 / 清除分类标签。

## 工作流程

```
1. 读取库      Playnite 运行时会独占锁 games.db → 先优雅关闭，再从备份副本读取
2. 分类映射    人工/规则生成 分类.tsv（每款游戏 → 类型/系列/特性/平台标签）
3. 元数据中文化 从 Steam 国区商店抓官方中文名与简介，缺失的用搜索补齐
4. 写入        Playnite 插件按游戏 ID 精确写入分类、标签、名称、日期、简介
5. 校验        重新读库确认：分类覆盖、标签覆盖、无重名
```

## 快速开始

```powershell
# 0. 前置：Windows + Playnite + .NET Framework 4.x（系统自带 csc 即可，无需 .NET SDK）

# 1. 编译并安装插件（会写入 %APPDATA%\Playnite\Extensions\playnite-collection-tool\）
powershell -ExecutionPolicy Bypass -File scripts\build-plugin.ps1

# 2. 关闭 Playnite 后，导出你的游戏库
powershell -ExecutionPolicy Bypass -File scripts\export-playnite.ps1 -OutDir .\out

# 3. 生成分类映射（示例数据在 examples/，按你的库改成规则或人工表）
node scripts\build-map.ts --input .\out\playnite_games.json --map .\map\classification.tsv

# 4. 抓 Steam 国区中文元数据（可选）
node scripts\fetch-steam-meta.ts --input .\out\playnite_ids.json --out .\out\steam_meta.json

# 5. 生成写入数据，放进 ExtensionsData，重启 Playnite 让它自动应用
```

详细的 Agent 执行步骤见 [SKILL.md](SKILL.md)，踩坑记录见 [docs/pitfalls.md](docs/pitfalls.md)。

## 环境要求

| 依赖 | 版本 | 说明 |
|---|---|---|
| Windows | 10/11 | 插件与导出脚本依赖 Playnite |
| Playnite | 10.x（32 位） | 必须安装并至少导入过一个商店库 |
| .NET Framework | 4.x | 系统自带 `csc.exe` 即可，**不需要 .NET SDK** |
| Node.js | ≥ 22.6 | 跑数据脚本（仅用内置模块，无 npm 依赖） |
| git | 可选 | 只用于版本控制；没装可跑 `scripts\setup-git.ps1` 拉便携版 |

## 从零开始（完整流程）

```powershell
# 1. 编译 + 安装插件
powershell -ExecutionPolicy Bypass -File scripts\build-plugin.ps1

# 2. 关闭 Playnite 并导出游戏库
powershell -ExecutionPolicy Bypass -File scripts\export-playnite.ps1 -OutDir out

# 3. 导出 Id/来源清单（供抓元数据用）
powershell -ExecutionPolicy Bypass -File scripts\collect-ids.ps1 -Mode ids -OutDir out

# 4. 抓 Steam 国区官方中文元数据
node scripts\fetch-steam-meta.ts --ids out/playnite_ids.json --out out/steam_meta.json

# 5. 为还没有中文名的游戏找候选，并确认
node scripts\suggest-steam.ts --games out/playnite_games.json --skip-names out/steam_meta.json --out out/steam_suggest.json
node scripts\fetch-appdetails.ts --candidates out/steam_suggest.json --out out/confirmed.json

# 6. 准备人工映射表（参考 examples/）
#    map/classification.tsv、map/manual_names.json、map/version_labels.json、map/manual_ids.json

# 7. 汇总生成两个写入文件
node scripts\build-data.ts --map map/classification.tsv --outdir out

# 8. 拷进 Playnite 数据目录，重启 Playnite 自动应用
copy out\分类.tsv      "$env:APPDATA\Playnite\ExtensionsData\playnite-collection-tool\"
copy out\游戏数据.tsv  "$env:APPDATA\Playnite\ExtensionsData\playnite-collection-tool\"

# 9. 校验结果
powershell -ExecutionPolicy Bypass -File scripts\collect-ids.ps1 -Mode verify
```

## 目录

| 路径 | 说明 |
|---|---|
| `SKILL.md` | Agent 执行入口（流程、规则、验收标准） |
| `plugin/` | 分类/标签/元数据写入插件（C# 源码 + extension.yaml） |
| `scripts/` | 导出、抓取、生成映射、校验脚本 |
| `docs/` | 设计说明、踩坑清单、字段对照 |
| `examples/` | 脱敏示例数据（演示格式，不含任何真实游戏库） |

## 隐私说明

本仓库**不含任何使用者数据**：没有游戏库导出、没有账号 ID、没有 Cookie/Token、没有本机绝对路径。
所有脚本的路径都通过参数传入；`examples/` 里是虚构的示例数据。使用时请把 `out/`、`backup/` 排除在版本控制之外（见 `.gitignore`）。

## License

MIT
